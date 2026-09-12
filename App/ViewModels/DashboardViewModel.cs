using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NetStats.Core.Models;
using NetStats.Core.Networking;
using NetStats.Core.Privacy;
using NetStats.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace NetStats.UI.ViewModels
{
    public class DashboardViewModel : ViewModelBase, IDisposable
    {
        private double _downloadSpeed;
        private double _uploadSpeed;
        private bool _isCollapsed;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly INetworkMonitor _networkMonitor;
        private readonly IPrivacyMonitor _privacyMonitor;
        private readonly IAppSettings _settings;

        private PrivacyDeviceUsage _microphoneUsage = PrivacyDeviceUsage.Inactive();
        private PrivacyDeviceUsage _cameraUsage = PrivacyDeviceUsage.Inactive();
        private long _sessionDownloadBytes;
        private long _sessionUploadBytes;
        private string _currentSignal = "System normal";
        private DateTime _lastNewConnectionSignalTime = DateTime.MinValue;
        private readonly HashSet<string> _knownRemoteEndpoints = new(StringComparer.OrdinalIgnoreCase);
        private bool _hasBaselineEndpoints;
        private bool _everSawPerProcessMeasurement;
        private string _activityEmptyMessage = "Application rates appear as traffic is detected. Run as Administrator for deeper per-process detail.";

        public PrivacyDeviceState MicrophoneState => _microphoneUsage.State;
        public PrivacyDeviceState CameraState => _cameraUsage.State;
        public bool IsMicrophoneActive => MicrophoneState == PrivacyDeviceState.Active;
        public bool IsCameraActive => CameraState == PrivacyDeviceState.Active;

        public string MicrophoneAttribution => _microphoneUsage.Attribution;
        public string CameraAttribution => _cameraUsage.Attribution;

        public string MicrophoneStatusText => MicrophoneState switch
        {
            PrivacyDeviceState.Active => "IN USE",
            PrivacyDeviceState.Inactive => "NOT IN USE",
            _ => "UNKNOWN"
        };

        public string CameraStatusText => CameraState switch
        {
            PrivacyDeviceState.Active => "IN USE",
            PrivacyDeviceState.Inactive => "NOT IN USE",
            _ => "UNKNOWN"
        };

        public Brush MicrophoneBrush => GetDeviceBrush(IsMicrophoneActive);
        public Brush CameraBrush => GetDeviceBrush(IsCameraActive);

        public long SessionDownloadBytes
        {
            get => _sessionDownloadBytes;
            private set
            {
                if (SetProperty(ref _sessionDownloadBytes, value))
                {
                    OnPropertyChanged(nameof(SessionDownloadString));
                }
            }
        }

        public long SessionUploadBytes
        {
            get => _sessionUploadBytes;
            private set
            {
                if (SetProperty(ref _sessionUploadBytes, value))
                {
                    OnPropertyChanged(nameof(SessionUploadString));
                }
            }
        }

        public string SessionDownloadString => FormatSessionBytes(SessionDownloadBytes);
        public string SessionUploadString => FormatSessionBytes(SessionUploadBytes);

        public string CurrentSignal
        {
            get => _currentSignal;
            private set
            {
                if (SetProperty(ref _currentSignal, value))
                {
                    OnPropertyChanged(nameof(SignalIndicatorBrush));
                    OnPropertyChanged(nameof(SignalTextBrush));
                }
            }
        }

        public Brush SignalIndicatorBrush
        {
            get
            {
                if (IsCameraActive || IsMicrophoneActive || _currentSignal == "New connection")
                {
                    return GetResourceBrush("AccentPrimaryBrush", Windows.UI.Color.FromArgb(255, 48, 199, 221));
                }
                return GetResourceBrush("DownloadFlowBrush", Windows.UI.Color.FromArgb(255, 16, 185, 129));
            }
        }

        public Brush SignalTextBrush
        {
            get
            {
                if (IsCameraActive || IsMicrophoneActive || _currentSignal == "New connection")
                {
                    return GetResourceBrush("TextPrimaryBrush", Windows.UI.Color.FromArgb(255, 240, 243, 248));
                }
                return GetResourceBrush("TextMutedBrush", Windows.UI.Color.FromArgb(255, 140, 145, 155));
            }
        }

        public double DownloadSpeed
        {
            get => _downloadSpeed;
            set
            {
                if (SetProperty(ref _downloadSpeed, value))
                {
                    OnPropertyChanged(nameof(FormattedDownload));
                    OnPropertyChanged(nameof(DownloadValueString));
                    OnPropertyChanged(nameof(DownloadRatio));
                    OnPropertyChanged(nameof(UploadRatio));
                }
            }
        }

        public double UploadSpeed
        {
            get => _uploadSpeed;
            set
            {
                if (SetProperty(ref _uploadSpeed, value))
                {
                    OnPropertyChanged(nameof(FormattedUpload));
                    OnPropertyChanged(nameof(UploadValueString));
                    OnPropertyChanged(nameof(DownloadRatio));
                    OnPropertyChanged(nameof(UploadRatio));
                }
            }
        }

        public bool IsCollapsed
        {
            get => _isCollapsed;
            set
            {
                if (SetProperty(ref _isCollapsed, value))
                {
                    OnPropertyChanged(nameof(IsDashboardViewVisible));
                    OnPropertyChanged(nameof(IsInspectViewVisible));
                }
            }
        }

        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ApplyFilter();
                }
            }
        }

        public AccentTheme AccentTheme => _settings.Current.AccentTheme;
        public BackgroundTheme BackgroundTheme => _settings.Current.BackgroundTheme;
        public SpeedUnit SpeedUnit => _settings.Current.SpeedUnit;
        public string SpeedUnitLabel => SpeedUnit == NetStats.Core.Settings.SpeedUnit.Mbps ? "Mbps" : "MB/s";
        public string DownloadValueString => FormatDisplaySpeed(DownloadSpeed);
        public string UploadValueString => FormatDisplaySpeed(UploadSpeed);
        public string FormattedDownload => $"{DownloadValueString} {SpeedUnitLabel}";
        public string FormattedUpload => $"{UploadValueString} {SpeedUnitLabel}";

        public double DownloadRatio
        {
            get
            {
                var total = DownloadSpeed + UploadSpeed;
                return total > 0 ? Math.Clamp(DownloadSpeed / total, 0.05, 0.95) : 0.5;
            }
        }

        public double UploadRatio
        {
            get
            {
                var total = DownloadSpeed + UploadSpeed;
                return total > 0 ? Math.Clamp(UploadSpeed / total, 0.05, 0.95) : 0.5;
            }
        }

        private bool _isInspectActive;
        private bool _isInspectFrozen;
        private bool _isActivityView = true;
        private bool _isDetailsView;
        private NetworkSnapshot? _lastSnapshot;

        public bool IsInspectActive
        {
            get => _isInspectActive;
            set
            {
                if (SetProperty(ref _isInspectActive, value))
                {
                    OnPropertyChanged(nameof(IsDashboardViewVisible));
                    OnPropertyChanged(nameof(IsInspectViewVisible));
                    if (value && _lastSnapshot != null)
                    {
                        UpdateInspectGroups(_lastSnapshot);
                    }
                }
            }
        }

        public bool IsActivityView
        {
            get => _isActivityView;
            set
            {
                if (SetProperty(ref _isActivityView, value))
                {
                    _isDetailsView = !value;
                    OnPropertyChanged(nameof(IsDetailsView));
                    ApplyFilter();
                }
            }
        }

        public bool IsDetailsView
        {
            get => _isDetailsView;
            set
            {
                if (SetProperty(ref _isDetailsView, value))
                {
                    _isActivityView = !value;
                    OnPropertyChanged(nameof(IsActivityView));
                    ApplyFilter();
                }
            }
        }

        public bool IsInspectFrozen
        {
            get => _isInspectFrozen;
            set
            {
                if (SetProperty(ref _isInspectFrozen, value))
                {
                    OnPropertyChanged(nameof(InspectFreezeButtonLabel));
                }
            }
        }

        public string InspectFreezeButtonLabel => IsInspectFrozen ? "RESUME LIVE" : "PAUSE";


        public bool IsDashboardViewVisible => !IsCollapsed && !IsInspectActive;
        public bool IsInspectViewVisible => !IsCollapsed && IsInspectActive;

        public bool HasInspectGroups => InspectGroups.Count > 0;

        private readonly Dictionary<string, ProcessTraffic> _masterProcessList = new(StringComparer.OrdinalIgnoreCase);
        public ObservableCollection<ProcessTraffic> DashboardCurrentActivity { get; } = new();
        public bool HasDashboardActivity => DashboardCurrentActivity.Count > 0;
        public string ActivityEmptyMessage => _activityEmptyMessage;

        public ObservableCollection<ProcessTraffic> InspectActivityRows { get; } = new();
        public bool HasInspectActivity => InspectActivityRows.Count > 0;

        private string _inspectActivityEmptyTitle = string.Empty;
        private string _inspectActivityEmptyDetail = string.Empty;

        public string InspectActivityEmptyTitle
        {
            get => _inspectActivityEmptyTitle;
            private set => SetProperty(ref _inspectActivityEmptyTitle, value);
        }

        public string InspectActivityEmptyDetail
        {
            get => _inspectActivityEmptyDetail;
            private set => SetProperty(ref _inspectActivityEmptyDetail, value);
        }

        public ObservableCollection<ProcessConnectionGroup> InspectGroups { get; } = new();

        public INetworkMonitor NetworkMonitor => _networkMonitor;
        public IAppSettings Settings => _settings;

        public DashboardViewModel(IAppSettings settings)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _settings = settings;
            _settings.SettingsChanged += Settings_SettingsChanged;
            _privacyMonitor = new WindowsPrivacyMonitor();
            _networkMonitor = new SystemNetworkMonitor();
            _networkMonitor.SnapshotUpdated += NetworkMonitor_SnapshotUpdated;
            _networkMonitor.Start();
        }

        public void SetAccentTheme(AccentTheme accentTheme)
        {
            _settings.SetAccentTheme(accentTheme);
        }

        public void SetBackgroundTheme(BackgroundTheme backgroundTheme)
        {
            _settings.SetBackgroundTheme(backgroundTheme);
        }

        public void SetSpeedUnit(SpeedUnit speedUnit)
        {
            _settings.SetSpeedUnit(speedUnit);
        }

        private void Settings_SettingsChanged(object? sender, EventArgs e)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(AccentTheme));
                OnPropertyChanged(nameof(BackgroundTheme));
                OnPropertyChanged(nameof(SpeedUnit));
                OnPropertyChanged(nameof(SpeedUnitLabel));
                OnPropertyChanged(nameof(DownloadValueString));
                OnPropertyChanged(nameof(UploadValueString));
                OnPropertyChanged(nameof(FormattedDownload));
                OnPropertyChanged(nameof(FormattedUpload));
                OnPropertyChanged(nameof(MicrophoneBrush));
                OnPropertyChanged(nameof(CameraBrush));
                OnPropertyChanged(nameof(SignalIndicatorBrush));
                OnPropertyChanged(nameof(SignalTextBrush));

                foreach (var item in DashboardCurrentActivity)
                {
                    if (item.DownloadBytesPerSecond.HasValue)
                    {
                        item.FormattedDownloadRate = FormatPerProcessRate(item.DownloadBytesPerSecond.Value);
                    }
                    if (item.UploadBytesPerSecond.HasValue)
                    {
                        item.FormattedUploadRate = FormatPerProcessRate(item.UploadBytesPerSecond.Value);
                    }
                }
            });
        }

        private void NetworkMonitor_SnapshotUpdated(object? sender, NetworkSnapshot snapshot)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _lastSnapshot = snapshot;

                // Convert bytes per second to Mbps (1 Mbps = 1_000_000 bits per second)
                // bytes * 8 / 1_000_000
                DownloadSpeed = snapshot.DownloadBytesPerSecond * 8 / 1000000.0;
                UploadSpeed = snapshot.UploadBytesPerSecond * 8 / 1000000.0;

                // Session totals
                SessionDownloadBytes = snapshot.SessionDownloadBytes;
                SessionUploadBytes = snapshot.SessionUploadBytes;

                // Privacy capability monitoring
                var privacy = _privacyMonitor.Poll();
                UpdatePrivacySnapshot(privacy);

                // Minimal meaningful signal
                UpdateSignal(snapshot);

                UpdateProcessList(snapshot);

                if (IsInspectActive && !IsInspectFrozen)
                {
                    UpdateInspectGroups(snapshot);
                }
            });
        }

        private void UpdateProcessList(NetworkSnapshot snapshot)
        {
            var activeProcs = snapshot.ActiveConnections
                .Where(c => c.ProcessId != 0 && !string.IsNullOrWhiteSpace(c.ProcessName))
                .GroupBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Mark inactive state for existing processes
            foreach (var proc in _masterProcessList.Values)
            {
                proc.ConnectionCount = 0;
                proc.ConnectionState = "No active network activity";
                proc.Subtext = "No active network activity";
                proc.RemoteEndpointDisplay = null;
                proc.ProcessIds.Clear();
            }

            foreach (var group in activeProcs)
            {
                string processName = group.Key;
                if (!_masterProcessList.TryGetValue(processName, out var traffic))
                {
                    traffic = new ProcessTraffic
                    {
                        ProcessName = processName,
                        DisplayName = GetFriendlyDisplayName(processName),
                        ExecutableName = GetExecutableName(processName),
                        Monogram = GetMonogram(processName),
                        Subtext = "Connected",
                        ConnectionCount = 0
                    };
                    _masterProcessList[processName] = traffic;
                }

                traffic.DisplayName = GetFriendlyDisplayName(processName);
                traffic.ExecutableName = GetExecutableName(processName);
                traffic.ConnectionCount = group.Count();
                traffic.ProcessId = group.First().ProcessId;
                traffic.LastUpdated = DateTime.UtcNow;

                // Determine honest connection state without inferring byte direction
                bool hasEstablished = group.Any(c => string.Equals(c.State, "Established", StringComparison.OrdinalIgnoreCase));
                traffic.ConnectionState = hasEstablished ? "Connected" : "Active";
                traffic.Subtext = traffic.ConnectionState;

                // Find a relevant external remote endpoint
                var externalConn = group.FirstOrDefault(c => IsExternalEndpoint(c.RemoteAddress, c.RemotePort));
                if (externalConn != null)
                {
                    traffic.RemoteEndpointDisplay = $"{externalConn.RemoteAddress}:{externalConn.RemotePort}";
                }
                else
                {
                    var anyRemote = group.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.RemoteAddress) && c.RemoteAddress != "*" && c.RemotePort > 0);
                    traffic.RemoteEndpointDisplay = anyRemote != null ? $"{anyRemote.RemoteAddress}:{anyRemote.RemotePort}" : null;
                }

                foreach (var c in group)
                {
                    traffic.ProcessIds.Add(c.ProcessId);
                }
            }

            // Apply real per-process bandwidth measurement from OS byte counters
            var bandwidthByPid = new Dictionary<int, ProcessBandwidthSnapshot>(snapshot.PerProcessBandwidth.Count);
            foreach (var bw in snapshot.PerProcessBandwidth)
            {
                bandwidthByPid[bw.ProcessId] = bw;
            }

            foreach (var traffic in _masterProcessList.Values)
            {
                double totalDownload = 0;
                double totalUpload = 0;
                bool hasMeasurement = false;

                foreach (int pid in traffic.ProcessIds)
                {
                    if (bandwidthByPid.TryGetValue(pid, out var bw))
                    {
                        totalDownload += bw.DownloadBytesPerSecond;
                        totalUpload += bw.UploadBytesPerSecond;
                        hasMeasurement = true;
                    }
                }

                traffic.DownloadBytesPerSecond = hasMeasurement ? totalDownload : null;
                traffic.UploadBytesPerSecond = hasMeasurement ? totalUpload : null;
                traffic.MeasurementAvailability = hasMeasurement
                    ? MeasurementAvailability.Available
                    : MeasurementAvailability.Unavailable;
            }

            UpdateDashboardActivity(snapshot);
            ApplyFilter();
        }

        private void UpdateDashboardActivity(NetworkSnapshot snapshot)
        {
            // Dashboard "Current Activity" is driven ONLY by real per-process byte
            // rates from the OS telemetry (ETW primary, EStats fallback). It is NOT
            // a connection-count substitute and never estimates direction/ownership.
            // Rows are aggregated by process name (a single app = one row) and only
            // entries with measurable traffic in this interval participate, which
            // naturally applies a per-interval inactivity timeout.
            var aggregates = new Dictionary<string, AggregateBandwidth>(StringComparer.OrdinalIgnoreCase);

            foreach (var bw in snapshot.PerProcessBandwidth)
            {
                if (bw.DownloadBytesPerSecond <= 0 && bw.UploadBytesPerSecond <= 0)
                    continue;

                string key = string.IsNullOrWhiteSpace(bw.ProcessName)
                    ? $"PID {bw.ProcessId}"
                    : bw.ProcessName;

                if (!aggregates.TryGetValue(key, out var agg))
                {
                    agg = new AggregateBandwidth { ProcessId = bw.ProcessId };
                    aggregates[key] = agg;
                }

                agg.Download += bw.DownloadBytesPerSecond;
                agg.Upload += bw.UploadBytesPerSecond;
            }

            if (aggregates.Count > 0)
            {
                _everSawPerProcessMeasurement = true;
            }

            var relevant = aggregates
                .OrderByDescending(a => a.Value.Download + a.Value.Upload)
                .Take(20)
                .Select(a => ResolveActivityTraffic(a.Key, a.Value))
                .ToList();

            // In-place reconciliation to prevent UI flickering
            for (int i = DashboardCurrentActivity.Count - 1; i >= 0; i--)
            {
                if (!relevant.Contains(DashboardCurrentActivity[i]))
                {
                    DashboardCurrentActivity.RemoveAt(i);
                }
            }

            for (int i = 0; i < relevant.Count; i++)
            {
                var item = relevant[i];
                int existingIndex = DashboardCurrentActivity.IndexOf(item);
                if (existingIndex < 0)
                {
                    if (i < DashboardCurrentActivity.Count)
                    {
                        DashboardCurrentActivity.Insert(i, item);
                    }
                    else
                    {
                        DashboardCurrentActivity.Add(item);
                    }
                }
                else if (existingIndex != i && i < DashboardCurrentActivity.Count)
                {
                    DashboardCurrentActivity.Move(existingIndex, i);
                }
            }

            OnPropertyChanged(nameof(HasDashboardActivity));
            UpdateActivityEmptyMessage();
        }

        private ProcessTraffic ResolveActivityTraffic(string processName, AggregateBandwidth aggregate)
        {
            if (!_masterProcessList.TryGetValue(processName, out var traffic))
            {
                traffic = new ProcessTraffic
                {
                    ProcessName = processName,
                    DisplayName = GetFriendlyDisplayName(processName),
                    ExecutableName = GetExecutableName(processName),
                    Monogram = GetMonogram(processName),
                    Subtext = "Active",
                    ConnectionState = "Active",
                    MeasurementAvailability = MeasurementAvailability.Available
                };
                _masterProcessList[processName] = traffic;
            }

            traffic.DownloadBytesPerSecond = aggregate.Download;
            traffic.UploadBytesPerSecond = aggregate.Upload;
            traffic.MeasurementAvailability = MeasurementAvailability.Available;
            traffic.LastUpdated = DateTime.UtcNow;
            traffic.FormattedDownloadRate = FormatPerProcessRate(aggregate.Download);
            traffic.FormattedUploadRate = FormatPerProcessRate(aggregate.Upload);
            return traffic;
        }

        private static readonly bool _isElevated = CheckIsElevated();
        public static bool IsElevated => _isElevated;

        private static bool CheckIsElevated()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public string DashboardDormantStatusText => _isElevated ? "no active traffic" : "admin required for app rates";

        private string FormatPerProcessRate(double bytesPerSecond)
        {
            double mbps = bytesPerSecond * 8 / 1000000.0;
            return $"{FormatDisplaySpeed(mbps)} {SpeedUnitLabel}";
        }

        private void UpdateActivityEmptyMessage()
        {
            string message = _everSawPerProcessMeasurement
                ? "No active application traffic"
                : "Application rates appear as traffic is detected. Run as Administrator for deeper per-process detail.";

            if (_activityEmptyMessage != message)
            {
                _activityEmptyMessage = message;
                OnPropertyChanged(nameof(ActivityEmptyMessage));
            }
        }

        private sealed class AggregateBandwidth
        {
            public int ProcessId { get; set; }
            public double Download { get; set; }
            public double Upload { get; set; }
        }

        private const double MinMeaningfulActivityBytes = 8 * 1024;

        private void ApplyFilter()
        {
            string? query = _searchQuery?.Trim();
            bool hasQuery = !string.IsNullOrWhiteSpace(query);

            IEnumerable<ProcessTraffic> filtered = BuildActivityPool();

            if (!string.IsNullOrEmpty(query))
            {
                string q = query;
                filtered = filtered.Where(p =>
                    p.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.ExecutableName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessIds.Any(id => id.ToString().Contains(q)));
            }
            else
            {
                // No query: only applications with real, meaningful measured traffic.
                // Connection counts and idle processes never substitute for bandwidth.
                filtered = filtered.Where(p =>
                    p.IsMeasurementAvailable &&
                    (p.DownloadBytesPerSecond ?? 0) + (p.UploadBytesPerSecond ?? 0) >= MinMeaningfulActivityBytes);
            }

            // Highest current total traffic first; applications without traffic sink
            // to the bottom (they stay reachable through search).
            var sorted = filtered
                .OrderByDescending(p => (p.DownloadBytesPerSecond ?? 0) + (p.UploadBytesPerSecond ?? 0))
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var item in sorted)
            {
                EnsureActivityRates(item);
            }

            // In-place update to prevent UI churn
            bool sameSequence = InspectActivityRows.Count == sorted.Count;
            if (sameSequence)
            {
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (!ReferenceEquals(InspectActivityRows[i], sorted[i]))
                    {
                        sameSequence = false;
                        break;
                    }
                }
            }

            if (!sameSequence)
            {
                InspectActivityRows.Clear();
                foreach (var item in sorted)
                {
                    InspectActivityRows.Add(item);
                }
            }

            OnPropertyChanged(nameof(HasInspectActivity));
            UpdateInspectActivityState(hasQuery, query);
        }

        private List<ProcessTraffic> BuildActivityPool()
        {
            // Connection-derived entries (which already carry real per-process
            // bandwidth when available) plus bandwidth-only processes taken
            // straight from the measured snapshot. No direction or volume is
            // ever inferred from endpoints or connection counts.
            var pool = new List<ProcessTraffic>(_masterProcessList.Values);
            var seenNames = new HashSet<string>(_masterProcessList.Values.Select(t => t.ProcessName), StringComparer.OrdinalIgnoreCase);

            if (_lastSnapshot == null)
            {
                return pool;
            }

            var extra = new Dictionary<string, AggregateBandwidth>(StringComparer.OrdinalIgnoreCase);
            foreach (var bw in _lastSnapshot.PerProcessBandwidth)
            {
                if (bw.DownloadBytesPerSecond <= 0 && bw.UploadBytesPerSecond <= 0)
                {
                    continue;
                }

                string key = string.IsNullOrWhiteSpace(bw.ProcessName) ? $"PID {bw.ProcessId}" : bw.ProcessName;
                if (seenNames.Contains(key))
                {
                    continue;
                }

                if (!extra.TryGetValue(key, out var agg))
                {
                    agg = new AggregateBandwidth { ProcessId = bw.ProcessId };
                    extra[key] = agg;
                }

                agg.Download += bw.DownloadBytesPerSecond;
                agg.Upload += bw.UploadBytesPerSecond;
            }

            foreach (var entry in extra.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                pool.Add(CreateActivityTraffic(entry.Key, entry.Value));
            }

            return pool;
        }

        private static ProcessTraffic CreateActivityTraffic(string processName, AggregateBandwidth aggregate)
        {
            var traffic = new ProcessTraffic
            {
                ProcessName = processName,
                DisplayName = GetFriendlyDisplayName(processName),
                ExecutableName = GetExecutableName(processName),
                Monogram = GetMonogram(processName),
                Subtext = "Active",
                ConnectionState = "Active",
                ProcessId = aggregate.ProcessId,
                MeasurementAvailability = MeasurementAvailability.Available,
                DownloadBytesPerSecond = aggregate.Download,
                UploadBytesPerSecond = aggregate.Upload
            };
            traffic.ProcessIds.Add(aggregate.ProcessId);
            return traffic;
        }

        private void EnsureActivityRates(ProcessTraffic traffic)
        {
            traffic.FormattedDownloadRate = traffic.DownloadBytesPerSecond.HasValue
                ? FormatPerProcessRate(traffic.DownloadBytesPerSecond.Value)
                : "—";
            traffic.FormattedUploadRate = traffic.UploadBytesPerSecond.HasValue
                ? FormatPerProcessRate(traffic.UploadBytesPerSecond.Value)
                : "—";
        }

        private void UpdateInspectActivityState(bool hasQuery, string? query)
        {
            string title;
            string detail;

            if (InspectActivityRows.Count > 0)
            {
                title = string.Empty;
                detail = string.Empty;
            }
            else if (hasQuery)
            {
                title = $"No results for \u201C{query}\u201D";
                detail = "Try a process name, executable name, or PID.";
            }
            else
            {
                title = "NO ACTIVE TRAFFIC";
                detail = _isElevated
                    ? "No application bandwidth detected right now."
                    : "Run TRACE as Administrator to capture per-process rates.";
            }

            InspectActivityEmptyTitle = title;
            InspectActivityEmptyDetail = detail;
        }

        private static bool IsExternalEndpoint(string? address, int port)
        {
            if (string.IsNullOrWhiteSpace(address) || port == 0) return false;
            if (address == "0.0.0.0" || address == "::" || address == "*" || address.StartsWith("127.") || address == "::1") return false;
            return true;
        }

        private static string GetFriendlyDisplayName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return "Unknown";
            string clean = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4)
                : processName;

            return clean.ToLowerInvariant() switch
            {
                "chrome" => "Chrome",
                "msedge" => "Microsoft Edge",
                "brave" => "Brave Browser",
                "firefox" => "Firefox",
                "steam" or "steamwebhelper" => "Steam",
                "discord" => "Discord",
                "spotify" => "Spotify",
                "code" => "Visual Studio Code",
                "devenv" => "Visual Studio",
                "onedrive" => "OneDrive",
                "slack" => "Slack",
                "teams" => "Microsoft Teams",
                "explorer" => "Windows Explorer",
                "searchhost" => "Windows Search",
                "svchost" => "Service Host",
                _ => clean.Length > 1 ? char.ToUpperInvariant(clean[0]) + clean.Substring(1) : clean.ToUpperInvariant()
            };
        }

        private static string GetExecutableName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return string.Empty;
            return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName : $"{processName}.exe";
        }

        private static string GetMonogram(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return "NA";
            string clean = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4)
                : processName;
            return clean.Substring(0, Math.Min(2, clean.Length)).ToUpperInvariant();
        }

        private void UpdateInspectGroups(NetworkSnapshot snapshot)
        {
            try
            {
                // Inspect: Group strictly by ProcessId + ProcessName (per process instance)
                var rawGroups = snapshot.ActiveConnections
                    .Where(c => c.ProcessId != 0)
                    .GroupBy(c => new { c.ProcessId, c.ProcessName })
                    .Select(g => new
                    {
                        ProcessId = g.Key.ProcessId,
                        ProcessName = string.IsNullOrWhiteSpace(g.Key.ProcessName) ? "Unknown" : g.Key.ProcessName,
                        Connections = (IList<ConnectionItem>)g.Select(c =>
                        {
                            (string localAddress, string localPort) = FormatLocalParts(c.LocalAddress, c.LocalPort);
                            (string remoteAddress, string remotePort) = FormatRemoteParts(c.RemoteAddress, c.RemotePort, c.State);
                            return new ConnectionItem
                            {
                                Protocol = c.Protocol,
                                RemoteEndpoint = FormatRemoteEndpoint(c.RemoteAddress, c.RemotePort, c.State),
                                LocalPort = c.LocalPort,
                                LocalEndpoint = $"{localAddress}:{localPort}",
                                State = string.Equals(c.Protocol, "UDP", StringComparison.OrdinalIgnoreCase)
                                    ? "--"
                                    : (string.IsNullOrWhiteSpace(c.State) ? "--" : c.State),
                                ProcessId = c.ProcessId,
                                LocalAddressText = localAddress,
                                LocalPortText = localPort,
                                RemoteAddressText = remoteAddress,
                                RemotePortText = remotePort
                            };
                        }).ToList()
                    })
                    .OrderByDescending(g => g.Connections.Count)
                    .ThenBy(g => g.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(g => g.ProcessId)
                    .ToList();

                var incomingGroupKeys = new HashSet<(int, string)>(rawGroups.Select(g => (g.ProcessId, g.ProcessName)));

                // Remove groups that are no longer active
                for (int i = InspectGroups.Count - 1; i >= 0; i--)
                {
                    var current = InspectGroups[i];
                    if (!incomingGroupKeys.Contains((current.ProcessId, current.ProcessName)))
                    {
                        InspectGroups.RemoveAt(i);
                    }
                }

                // Differential update for remaining groups and insert new ones
                for (int i = 0; i < rawGroups.Count; i++)
                {
                    var incoming = rawGroups[i];
                    var existing = InspectGroups.FirstOrDefault(g => g.ProcessId == incoming.ProcessId && g.ProcessName == incoming.ProcessName);

                    if (existing != null)
                    {
                        // In-place reconciliation of connection items (does NOT destroy XAML TextBlocks or clear selection!)
                        existing.UpdateConnections(incoming.Connections);

                        int currentIndex = InspectGroups.IndexOf(existing);
                        if (currentIndex != i && i < InspectGroups.Count)
                        {
                            InspectGroups.Move(currentIndex, i);
                        }
                    }
                    else
                    {
                        var newGroup = new ProcessConnectionGroup
                        {
                            ProcessId = incoming.ProcessId,
                            ProcessName = incoming.ProcessName
                        };
                        newGroup.UpdateConnections(incoming.Connections);

                        if (i < InspectGroups.Count)
                        {
                            InspectGroups.Insert(i, newGroup);
                        }
                        else
                        {
                            InspectGroups.Add(newGroup);
                        }
                    }
                }

                OnPropertyChanged(nameof(HasInspectGroups));
            }
            catch (Exception ex)
            {
                App.Log($"Inspect Error: {ex.Message}\n{ex.StackTrace}");
            }
        }



        private static string FormatRemoteEndpoint(string remoteAddress, int remotePort, string state)
        {
            if (string.IsNullOrWhiteSpace(remoteAddress) ||
                remoteAddress == "0.0.0.0" ||
                remoteAddress == "*" ||
                remoteAddress == "::" ||
                remotePort == 0)
            {
                return "*:*";
            }
            return $"{remoteAddress}:{remotePort}";
        }

        private static (string Address, string Port) FormatLocalParts(string? address, int port)
        {
            string addr = string.IsNullOrWhiteSpace(address) ? "*" : address;
            return (addr, port.ToString());
        }

        private static (string Address, string Port) FormatRemoteParts(string? address, int port, string state)
        {
            if (string.IsNullOrWhiteSpace(address) ||
                address == "0.0.0.0" ||
                address == "*" ||
                address == "::" ||
                port == 0)
            {
                return ("*", "*");
            }
            return (address, port.ToString());
        }

        public void OpenInspect()
        {
            IsInspectActive = true;
            IsActivityView = true;
        }

        public void CloseInspect()
        {
            IsInspectActive = false;
        }

        public void Dispose()
        {
            _settings.SettingsChanged -= Settings_SettingsChanged;
            if (_networkMonitor != null)
            {
                _networkMonitor.SnapshotUpdated -= NetworkMonitor_SnapshotUpdated;
                _networkMonitor.Stop();
                _networkMonitor.Dispose();
            }
            _privacyMonitor?.Dispose();
        }

        private void UpdatePrivacySnapshot(PrivacySnapshot privacy)
        {
            bool micChanged = _microphoneUsage.State != privacy.Microphone.State ||
                              _microphoneUsage.Attribution != privacy.Microphone.Attribution;
            bool camChanged = _cameraUsage.State != privacy.Camera.State ||
                              _cameraUsage.Attribution != privacy.Camera.Attribution;

            _microphoneUsage = privacy.Microphone;
            _cameraUsage = privacy.Camera;

            if (micChanged)
            {
                OnPropertyChanged(nameof(MicrophoneState));
                OnPropertyChanged(nameof(IsMicrophoneActive));
                OnPropertyChanged(nameof(MicrophoneAttribution));
                OnPropertyChanged(nameof(MicrophoneStatusText));
                OnPropertyChanged(nameof(MicrophoneBrush));
            }

            if (camChanged)
            {
                OnPropertyChanged(nameof(CameraState));
                OnPropertyChanged(nameof(IsCameraActive));
                OnPropertyChanged(nameof(CameraAttribution));
                OnPropertyChanged(nameof(CameraStatusText));
                OnPropertyChanged(nameof(CameraBrush));
            }
        }

        private void UpdateSignal(NetworkSnapshot snapshot)
        {
            // Debounced new connection detection
            var currentExternalEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var conn in snapshot.ActiveConnections)
            {
                if (string.IsNullOrWhiteSpace(conn.RemoteAddress) ||
                    conn.RemoteAddress == "0.0.0.0" ||
                    conn.RemoteAddress == "::" ||
                    conn.RemoteAddress == "*" ||
                    conn.RemoteAddress.StartsWith("127.") ||
                    conn.RemotePort == 0)
                {
                    continue;
                }

                string key = $"{conn.RemoteAddress}:{conn.RemotePort}";
                currentExternalEndpoints.Add(key);
            }

            if (!_hasBaselineEndpoints)
            {
                _knownRemoteEndpoints.UnionWith(currentExternalEndpoints);
                _hasBaselineEndpoints = true;
            }
            else
            {
                bool hasNew = false;
                foreach (var ep in currentExternalEndpoints)
                {
                    if (_knownRemoteEndpoints.Add(ep))
                    {
                        hasNew = true;
                    }
                }

                if (hasNew && (DateTime.UtcNow - _lastNewConnectionSignalTime).TotalSeconds > 8)
                {
                    _lastNewConnectionSignalTime = DateTime.UtcNow;
                }
            }

            // Signal priority
            if (IsCameraActive)
            {
                CurrentSignal = "Camera in use";
            }
            else if (IsMicrophoneActive)
            {
                CurrentSignal = "Microphone in use";
            }
            else if ((DateTime.UtcNow - _lastNewConnectionSignalTime).TotalSeconds < 6)
            {
                CurrentSignal = "New connection";
            }
            else
            {
                CurrentSignal = "System normal";
            }
        }

        private static Brush GetResourceBrush(string resourceKey, Windows.UI.Color fallbackColor)
        {
            if (Application.Current?.Resources.TryGetValue(resourceKey, out var val) == true && val is Brush brush)
            {
                return brush;
            }
            return new SolidColorBrush(fallbackColor);
        }

        private static Brush GetDeviceBrush(bool isActive)
        {
            if (isActive)
            {
                return GetResourceBrush("AccentPrimaryBrush", Windows.UI.Color.FromArgb(255, 48, 199, 221));
            }
            return GetResourceBrush("TextMutedBrush", Windows.UI.Color.FromArgb(255, 120, 125, 135));
        }

        private static string FormatSessionBytes(long bytes)
        {
            if (bytes <= 0) return "0.0 MB";
            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024.0:0.0} KB";
            }
            if (bytes < 1024L * 1024 * 1024)
            {
                return $"{bytes / (1024.0 * 1024.0):0.0} MB";
            }
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.00} GB";
        }

        private string FormatDisplaySpeed(double speedInMbps)
        {
            double displayValue = SpeedUnit == NetStats.Core.Settings.SpeedUnit.Mbps ? speedInMbps : speedInMbps / 8.0;
            return displayValue.ToString(SpeedUnit == NetStats.Core.Settings.SpeedUnit.Mbps ? "0.0" : "0.00");
        }
    }
}
