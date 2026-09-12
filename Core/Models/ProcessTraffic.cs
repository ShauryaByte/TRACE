using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NetStats.Core.Models
{
    public enum MeasurementAvailability
    {
        Unavailable,
        Available
    }

    public class ProcessTraffic : INotifyPropertyChanged
    {
        private int _connectionCount;
        private string _connectionState = "Connected";
        private string? _remoteEndpointDisplay;
        private MeasurementAvailability _measurementAvailability = MeasurementAvailability.Unavailable;
        private double? _downloadBytesPerSecond;
        private double? _uploadBytesPerSecond;
        private string _formattedDownloadRate = string.Empty;
        private string _formattedUploadRate = string.Empty;

        private string _displayName = string.Empty;

        public required string ProcessName { get; set; }

        public string DisplayName
        {
            get => string.IsNullOrWhiteSpace(_displayName) ? ProcessName : _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ExecutableName { get; set; } = string.Empty;
        public string? ExecutablePath { get; set; }
        public required string Monogram { get; set; }
        public required string Subtext { get; set; }
        public int ProcessId { get; set; }
        public HashSet<int> ProcessIds { get; } = new();

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public MeasurementAvailability MeasurementAvailability
        {
            get => _measurementAvailability;
            set
            {
                if (_measurementAvailability != value)
                {
                    _measurementAvailability = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsMeasurementAvailable));
                }
            }
        }

        public bool IsMeasurementAvailable => _measurementAvailability == MeasurementAvailability.Available;
        public bool IsActiveTraffic => (_downloadBytesPerSecond ?? 0) > 10240 || (_uploadBytesPerSecond ?? 0) > 10240;

        public double? DownloadBytesPerSecond
        {
            get => _downloadBytesPerSecond;
            set
            {
                if (_downloadBytesPerSecond != value)
                {
                    _downloadBytesPerSecond = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsActiveTraffic));
                }
            }
        }

        public double? UploadBytesPerSecond
        {
            get => _uploadBytesPerSecond;
            set
            {
                if (_uploadBytesPerSecond != value)
                {
                    _uploadBytesPerSecond = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsActiveTraffic));
                }
            }
        }

        // Presentation strings (value + unit) computed by the ViewModel each
        // snapshot so live dashboard rows track the selected speed unit.
        public string FormattedDownloadRate
        {
            get => _formattedDownloadRate;
            set
            {
                if (_formattedDownloadRate != value)
                {
                    _formattedDownloadRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FormattedUploadRate
        {
            get => _formattedUploadRate;
            set
            {
                if (_formattedUploadRate != value)
                {
                    _formattedUploadRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ConnectionState
        {
            get => _connectionState;
            set
            {
                if (_connectionState != value)
                {
                    _connectionState = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? RemoteEndpointDisplay
        {
            get => _remoteEndpointDisplay;
            set
            {
                if (_remoteEndpointDisplay != value)
                {
                    _remoteEndpointDisplay = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasRemoteEndpoint));
                }
            }
        }

        public bool HasRemoteEndpoint => !string.IsNullOrWhiteSpace(_remoteEndpointDisplay);

        public int ConnectionCount
        {
            get => _connectionCount;
            set
            {
                if (_connectionCount != value)
                {
                    _connectionCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedConnectionCount));
                    OnPropertyChanged(nameof(HasActiveConnections));
                }
            }
        }

        public bool HasActiveConnections => ConnectionCount > 0;

        public string FormattedConnectionCount => $"{ConnectionCount} active {(ConnectionCount == 1 ? "connection" : "connections")}";

        public string FormattedInspectSubtitle
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(ExecutableName))
                    parts.Add(ExecutableName);
                if (ProcessId > 0)
                    parts.Add($"PID {ProcessId}");
                if (ConnectionCount > 0)
                    parts.Add($"{ConnectionCount} {(ConnectionCount == 1 ? "connection" : "connections")}");

                return parts.Count > 0 ? string.Join("   ·   ", parts) : string.Empty;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

