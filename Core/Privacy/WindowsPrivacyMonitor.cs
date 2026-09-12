using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace NetStats.Core.Privacy
{
    public sealed class WindowsPrivacyMonitor : IPrivacyMonitor
    {
        private const string MicrophoneRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
        private const string WebcamRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

        private static readonly HashSet<string> SystemComponents = new(StringComparer.OrdinalIgnoreCase)
        {
            "rundll32",
            "svchost",
            "audiodg",
            "dllhost",
            "dwm",
            "explorer",
            "systemsettings",
            "shellexperiencehost",
            "searchhost",
            "searchapp",
            "taskhostw",
            "windows.immersivecontrolpanel"
        };

        private readonly object _lock = new();
        private PrivacySnapshot _currentSnapshot = new();

        public PrivacySnapshot CurrentSnapshot
        {
            get
            {
                lock (_lock)
                {
                    return _currentSnapshot;
                }
            }
            private set
            {
                lock (_lock)
                {
                    _currentSnapshot = value;
                }
            }
        }

        public event EventHandler<PrivacySnapshot>? PrivacyUpdated;

        public void Start()
        {
            Poll();
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
            Stop();
        }

        public PrivacySnapshot Poll()
        {
            var micUsage = QueryDeviceUsage(MicrophoneRegistryPath);
            var camUsage = QueryDeviceUsage(WebcamRegistryPath);

            var snapshot = new PrivacySnapshot
            {
                Timestamp = DateTime.UtcNow,
                Microphone = micUsage,
                Camera = camUsage
            };

            CurrentSnapshot = snapshot;
            PrivacyUpdated?.Invoke(this, snapshot);
            return snapshot;
        }

        private static PrivacyDeviceUsage QueryDeviceUsage(string capabilityPath)
        {
            try
            {
                using var baseKey = Registry.CurrentUser.OpenSubKey(capabilityPath, writable: false);
                if (baseKey == null)
                {
                    return PrivacyDeviceUsage.Unknown();
                }

                long latestActiveStart = 0;
                string? bestAttribution = null;
                bool bestHighConfidence = false;
                bool foundAnyActive = false;

                // 1. Inspect packaged apps directly under the capability key
                var subKeyNames = baseKey.GetSubKeyNames();
                foreach (var name in subKeyNames)
                {
                    if (string.Equals(name, "NonPackaged", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        using var appKey = baseKey.OpenSubKey(name, writable: false);
                        if (appKey == null) continue;

                        if (IsSubkeyActive(appKey, out long startTime))
                        {
                            foundAnyActive = true;
                            if (startTime >= latestActiveStart)
                            {
                                latestActiveStart = startTime;
                                var (attr, highConf) = ResolvePackagedAttribution(name);
                                bestAttribution = attr;
                                bestHighConfidence = highConf;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore individual subkey read errors
                    }
                }

                // 2. Inspect NonPackaged desktop apps
                try
                {
                    using var nonPackagedKey = baseKey.OpenSubKey("NonPackaged", writable: false);
                    if (nonPackagedKey != null)
                    {
                        foreach (var desktopAppName in nonPackagedKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = nonPackagedKey.OpenSubKey(desktopAppName, writable: false);
                                if (appKey == null) continue;

                                if (IsSubkeyActive(appKey, out long startTime))
                                {
                                    foundAnyActive = true;
                                    if (startTime >= latestActiveStart)
                                    {
                                        latestActiveStart = startTime;
                                        var (attr, highConf) = ResolveNonPackagedAttribution(desktopAppName);
                                        bestAttribution = attr;
                                        bestHighConfidence = highConf;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore individual subkey read errors
                            }
                        }
                    }
                }
                catch
                {
                    // NonPackaged key might not exist or be restricted
                }

                if (foundAnyActive)
                {
                    return PrivacyDeviceUsage.Active(
                        bestAttribution ?? "Unknown",
                        bestHighConfidence
                    );
                }

                return PrivacyDeviceUsage.Inactive();
            }
            catch
            {
                return PrivacyDeviceUsage.Unknown();
            }
        }

        private static bool IsSubkeyActive(RegistryKey key, out long startTime)
        {
            startTime = 0;
            try
            {
                var rawStart = key.GetValue("LastUsedTimeStart");
                var rawStop = key.GetValue("LastUsedTimeStop");

                if (rawStart == null)
                {
                    return false;
                }

                long start = ConvertToInt64(rawStart);
                long stop = ConvertToInt64(rawStop);

                startTime = start;

                // When an app is actively using the capability:
                // LastUsedTimeStart > 0 and (LastUsedTimeStop == 0 or LastUsedTimeStart > LastUsedTimeStop)
                return start > 0 && (stop == 0 || start > stop);
            }
            catch
            {
                return false;
            }
        }

        private static long ConvertToInt64(object? value)
        {
            if (value == null) return 0;
            if (value is long l) return l;
            if (value is int i) return i;
            if (long.TryParse(value.ToString(), out var parsed)) return parsed;
            return 0;
        }

        private static (string Attribution, bool IsHighConfidence) ResolveNonPackagedAttribution(string registryKeyName)
        {
            try
            {
                // Registry subkey uses '#' as separator for paths (e.g. C:#Program Files#...#app.exe)
                string fullPath = registryKeyName.Replace('#', Path.DirectorySeparatorChar);
                string fileName = Path.GetFileNameWithoutExtension(fullPath);

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return ("Unknown", false);
                }

                if (SystemComponents.Contains(fileName))
                {
                    return ("Windows component", false);
                }

                // Known clean name mappings for popular processes if helpful
                string friendlyName = fileName switch
                {
                    "obs64" => "OBS Studio",
                    "chrome" => "Google Chrome",
                    "msedge" => "Microsoft Edge",
                    "firefox" => "Mozilla Firefox",
                    "FiveM_GTAProcess" => "FiveM",
                    "FiveM_ChromeBrowser" => "FiveM",
                    "cs2" => "Counter-Strike 2",
                    "GTA5" => "Grand Theft Auto V",
                    "GTA5_Enhanced" => "Grand Theft Auto V",
                    _ => fileName
                };

                return (friendlyName, true);
            }
            catch
            {
                return ("Windows component", false);
            }
        }

        private static (string Attribution, bool IsHighConfidence) ResolvePackagedAttribution(string packageFamilyName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(packageFamilyName))
                {
                    return ("Unknown", false);
                }

                // Remove package id suffix e.g. 5319275A.WhatsAppDesktop_cv1g1gvanyjgm -> 5319275A.WhatsAppDesktop
                int underscoreIndex = packageFamilyName.IndexOf('_');
                string baseName = underscoreIndex > 0 ? packageFamilyName[..underscoreIndex] : packageFamilyName;

                if (SystemComponents.Contains(baseName))
                {
                    return ("Windows component", false);
                }

                // Strip leading publisher ID if present (e.g. 5319275A.WhatsAppDesktop -> WhatsAppDesktop)
                int dotIndex = baseName.IndexOf('.');
                string appPart = dotIndex >= 0 && dotIndex < baseName.Length - 1 ? baseName[(dotIndex + 1)..] : baseName;

                string friendlyName = appPart switch
                {
                    "WindowsCamera" => "Camera",
                    "WhatsAppDesktop" => "WhatsApp",
                    "MSTeams" => "Microsoft Teams",
                    "ChatGPT" => "ChatGPT",
                    "XboxGamingOverlay" => "Xbox Game Bar",
                    _ => appPart
                };

                return (friendlyName, true);
            }
            catch
            {
                return ("Unknown", false);
            }
        }
    }
}
