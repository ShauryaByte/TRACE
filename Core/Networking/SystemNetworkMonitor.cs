using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetStats.Core.Networking
{
    public class SystemNetworkMonitor : INetworkMonitor
    {
        private sealed record InterfaceCounters(long BytesReceived, long BytesSent);

        public event EventHandler<NetworkSnapshot>? SnapshotUpdated;

        private readonly Dictionary<string, InterfaceCounters> _previousInterfaceCounters = new(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _processNameCache = new();

        public SystemNetworkMonitor()
        {
            // Primary: ETW kernel network provider (TCP + UDP, requires Administrator).
            var etwCollector = new KernelNetworkEtwCollector();

            if (etwCollector.IsAvailable)
            {
                _perProcessCollector = etwCollector;
                _fallbackCollector = null;
            }
            else
            {
                // Fallback: EStats TCP-only collector (requires SeSystemProfilePrivilege).
                _perProcessCollector = etwCollector;
                _fallbackCollector = new PerProcessBandwidthCollector();

                System.Diagnostics.Debug.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [Monitor] ETW collector unavailable; EStats fallback active.");
            }
        }
        private readonly IProcessBandwidthCollector _perProcessCollector;
        private readonly PerProcessBandwidthCollector? _fallbackCollector;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _samplingTask;
        private long _previousSampleTimestamp;
        private bool _hasBaseline;
        private long _sessionDownloadBytes;
        private long _sessionUploadBytes;

        // Maximum plausible single-interface rate: 10 Gbps in bytes/sec.
        // Anything above this from a single NIC in a 1-second interval is a counter discontinuity.
        private const double MaxPlausibleBytesPerSecondPerInterface = 10_000_000_000.0 / 8.0; // ~1.25 GB/s

        // If elapsed time between samples exceeds this, treat as sleep/resume and rebaseline.
        private const double MaxSampleIntervalSeconds = 5.0;

        public void Start()
        {
            if (_samplingTask != null)
            {
                return;
            }

            _previousInterfaceCounters.Clear();
            _previousSampleTimestamp = 0;
            _hasBaseline = false;
            _cancellationTokenSource = new CancellationTokenSource();
            _samplingTask = Task.Run(() => SamplingLoopAsync(_cancellationTokenSource.Token));
        }

        public void Stop()
        {
            if (_cancellationTokenSource == null)
            {
                return;
            }

            _cancellationTokenSource.Cancel();
            try
            {
                _samplingTask?.Wait();
            }
            catch (AggregateException)
            {
            }

            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            _samplingTask = null;
        }

        private IReadOnlyList<ProcessBandwidthSnapshot> CollectPerProcessBandwidth(double intervalSeconds)
        {
            if (_fallbackCollector != null && !_perProcessCollector.IsAvailable)
            {
                return _fallbackCollector.CollectAndCompute(intervalSeconds);
            }

            return _perProcessCollector.CollectAndCompute(intervalSeconds);
        }

        private bool IsPerProcessCollectorAvailable()
        {
            if (_fallbackCollector != null && !_perProcessCollector.IsAvailable)
            {
                return _fallbackCollector.IsAvailable;
            }

            return _perProcessCollector.IsAvailable;
        }

        public void Dispose()
        {
            Stop();
            _perProcessCollector.Dispose();
            _fallbackCollector?.Dispose();
        }

        private async Task SamplingLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    SnapshotUpdated?.Invoke(this, CaptureSnapshot());
                }
                catch (Exception)
                {
                    // A transient adapter or connection-table failure must not stop telemetry.
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private NetworkSnapshot CaptureSnapshot()
        {
            long timestamp = Stopwatch.GetTimestamp();
            var currentCounters = CaptureEligibleInterfaceCounters();
            double downloadBytesPerSecond = 0;
            double uploadBytesPerSecond = 0;

            double elapsedSeconds = _hasBaseline
                ? (timestamp - _previousSampleTimestamp) / (double)Stopwatch.Frequency
                : double.NaN;

            // Sleep/resume guard + first sample: rebaseline the system-wide window.
            bool rebaseline = !_hasBaseline ||
                !double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0 || elapsedSeconds > MaxSampleIntervalSeconds;

            // Align the per-process and system-wide measurement windows:
            // drain the ETW/EStats counters at the SAME instant (and on the SAME
            // thread) the NIC counters above were just read, and divide by the
            // SAME elapsed interval. Previously the per-process counters were
            // drained at the END of this method — after interface enumeration and
            // connection-table resolution — so its 1s window was shifted relative
            // to the NIC window by a variable multi-millisecond offset. With bursty
            // traffic that skew made a process appear to exceed the system total
            // for the same nominal interval.
            var perProcess = CollectPerProcessBandwidth(rebaseline ? double.NaN : elapsedSeconds);

            if (!rebaseline)
            {
                foreach (var (interfaceId, current) in currentCounters)
                {
                    if (!_previousInterfaceCounters.TryGetValue(interfaceId, out var previous))
                    {
                        // New adapter appeared: baseline it, do NOT count its cumulative counters as traffic.
                        continue;
                    }

                    // Counter decrease = counter reset or adapter reinitialization. Rebaseline this interface.
                    if (current.BytesReceived < previous.BytesReceived || current.BytesSent < previous.BytesSent)
                    {
                        continue;
                    }

                    long deltaRecv = current.BytesReceived - previous.BytesReceived;
                    long deltaSent = current.BytesSent - previous.BytesSent;

                    // Per-interface discontinuity guard: if a single interface reports an implausible rate,
                    // it's a counter discontinuity, not actual traffic.
                    double ifRecvRate = deltaRecv / elapsedSeconds;
                    double ifSentRate = deltaSent / elapsedSeconds;

                    if (ifRecvRate > MaxPlausibleBytesPerSecondPerInterface || ifSentRate > MaxPlausibleBytesPerSecondPerInterface)
                    {
                        // Discontinuity detected on this interface. Skip it this interval (will rebaseline next cycle).
                        continue;
                    }

                    if (deltaRecv > 0)
                    {
                        _sessionDownloadBytes += deltaRecv;
                        downloadBytesPerSecond += ifRecvRate;
                    }

                    if (deltaSent > 0)
                    {
                        _sessionUploadBytes += deltaSent;
                        uploadBytesPerSecond += ifSentRate;
                    }
                }
            }

            // Store current counters as baseline for next sample.
            // Interfaces that disappeared are implicitly removed (we rebuild the dictionary).
            _previousInterfaceCounters.Clear();
            foreach (var (interfaceId, counters) in currentCounters)
            {
                _previousInterfaceCounters[interfaceId] = counters;
            }

            _previousSampleTimestamp = timestamp;
            _hasBaseline = true;

            return new NetworkSnapshot
            {
                Timestamp = DateTime.UtcNow,
                DownloadBytesPerSecond = rebaseline ? 0 : (IsValidRate(downloadBytesPerSecond) ? downloadBytesPerSecond : 0),
                UploadBytesPerSecond = rebaseline ? 0 : (IsValidRate(uploadBytesPerSecond) ? uploadBytesPerSecond : 0),
                SessionDownloadBytes = _sessionDownloadBytes,
                SessionUploadBytes = _sessionUploadBytes,
                ActiveConnections = ResolveActiveConnections(),
                PerProcessBandwidth = perProcess
            };
        }

        private static Dictionary<string, InterfaceCounters> CaptureEligibleInterfaceCounters()
        {
            var counters = new Dictionary<string, InterfaceCounters>(StringComparer.Ordinal);

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!IsEligibleTrafficInterface(networkInterface))
                {
                    continue;
                }

                try
                {
                    var statistics = networkInterface.GetIPv4Statistics();
                    counters[networkInterface.Id] = new InterfaceCounters(statistics.BytesReceived, statistics.BytesSent);
                }
                catch (NetworkInformationException)
                {
                    // Some adapters briefly become unavailable while Windows is refreshing their state.
                }
            }

            return counters;
        }

        private static bool IsEligibleTrafficInterface(NetworkInterface networkInterface)
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                return false;

            // Exclude fundamentally non-traffic interface types
            if (networkInterface.NetworkInterfaceType is
                NetworkInterfaceType.Loopback or
                NetworkInterfaceType.Tunnel or
                NetworkInterfaceType.Ppp)
            {
                return false;
            }

            // Filter virtual/duplicate adapters that mirror physical adapter traffic.
            // These cause double-counting of the same bytes.
            string name = networkInterface.Name ?? string.Empty;
            string desc = networkInterface.Description ?? string.Empty;

            if (IsVirtualOrDuplicateAdapter(name) || IsVirtualOrDuplicateAdapter(desc))
            {
                return false;
            }

            // CRITICAL: Only count interfaces that have a non-link-local default gateway.
            // This excludes filter driver sub-interfaces (WFP, QoS, Npcap, etc.) which
            // report IDENTICAL counters to their parent adapter but are separate
            // NetworkInterface objects. Without this check, the same physical traffic
            // is counted N times (once per filter driver), producing wildly inflated rates.
            // It also correctly excludes WAN Miniport adapters and internal virtual switches.
            try
            {
                var ipProps = networkInterface.GetIPProperties();
                bool hasGateway = ipProps.GatewayAddresses
                    .Any(g => g.Address != null && !g.Address.IsIPv6LinkLocal);
                if (!hasGateway)
                    return false;
            }
            catch (NetworkInformationException)
            {
                return false;
            }

            return true;
        }

        private static bool IsVirtualOrDuplicateAdapter(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string lower = text.ToLowerInvariant();

            // Hyper-V virtual switches mirror physical adapter traffic
            if (lower.Contains("hyper-v"))
                return true;
            if (lower.Contains("vethernet"))
                return true;

            // Docker/container networking (NAT or bridged, mirrors host traffic)
            if (lower.Contains("docker"))
                return true;

            // WSL virtual adapter
            if (lower.Contains("wsl"))
                return true;

            // VMware host-only and NAT adapters (not bridged, but often idle noise)
            if (lower.Contains("vmware"))
                return true;
            if (lower.Contains("vmnet"))
                return true;

            // VirtualBox host-only
            if (lower.Contains("virtualbox"))
                return true;
            if (lower.Contains("vboxnet"))
                return true;

            // Windows tunnel adapters
            if (lower.Contains("teredo"))
                return true;
            if (lower.Contains("isatap"))
                return true;
            if (lower.Contains("6to4"))
                return true;

            // WAN Miniport virtual adapters (IP, IPv6, Network Monitor, SSTP, IKEv2, etc.)
            if (lower.Contains("wan miniport"))
                return true;

            // Microsoft Wi-Fi Direct virtual adapter
            if (lower.Contains("wi-fi direct"))
                return true;
            if (lower.Contains("microsoft hosted"))
                return true;

            // Bluetooth PAN
            if (lower.Contains("bluetooth"))
                return true;

            return false;
        }

        private List<NetworkConnectionInfo> ResolveActiveConnections()
        {
            var rawConnections = ConnectionHelper.GetActiveConnections();
            var resolvedConnections = new List<NetworkConnectionInfo>(rawConnections.Count);
            var currentPids = new HashSet<int>();

            foreach (var connection in rawConnections)
            {
                string processName;
                if (connection.ProcessId == 0)
                {
                    processName = "System Idle Process";
                }
                else if (connection.ProcessId == 4)
                {
                    processName = "System";
                }
                else if (!_processNameCache.TryGetValue(connection.ProcessId, out processName!))
                {
                    try
                    {
                        using var process = Process.GetProcessById(connection.ProcessId);
                        processName = process.ProcessName;
                        _processNameCache[connection.ProcessId] = processName;
                    }
                    catch
                    {
                        processName = "Unknown";
                    }
                }

                currentPids.Add(connection.ProcessId);
                resolvedConnections.Add(new NetworkConnectionInfo
                {
                    ProcessId = connection.ProcessId,
                    ProcessName = processName,
                    Protocol = connection.Protocol,
                    LocalAddress = connection.LocalAddress,
                    LocalPort = connection.LocalPort,
                    RemoteAddress = connection.RemoteAddress,
                    RemotePort = connection.RemotePort,
                    State = connection.State,
                    ObservedAt = connection.ObservedAt
                });
            }

            foreach (int processId in _processNameCache.Keys.Where(processId => !currentPids.Contains(processId)).ToArray())
            {
                _processNameCache.Remove(processId);
            }

            return resolvedConnections;
        }

        private static bool IsValidRate(double value)
        {
            return double.IsFinite(value) && value >= 0;
        }
    }
}
