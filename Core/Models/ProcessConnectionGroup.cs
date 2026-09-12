using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace NetStats.Core.Models
{
    public class ConnectionItem : IEquatable<ConnectionItem>
    {
        public string Protocol { get; init; } = string.Empty;
        public string RemoteEndpoint { get; init; } = string.Empty;
        public string LocalEndpoint { get; init; } = string.Empty;
        public int LocalPort { get; init; }
        public string State { get; init; } = string.Empty;
        public int ProcessId { get; init; }

        public string LocalAddressText { get; init; } = string.Empty;
        public string LocalPortText { get; init; } = string.Empty;
        public string RemoteAddressText { get; init; } = string.Empty;
        public string RemotePortText { get; init; } = string.Empty;
        public string LocalPortDisplay => string.IsNullOrEmpty(LocalPortText) ? string.Empty : $":{LocalPortText}";
        public string RemotePortDisplay => string.IsNullOrEmpty(RemotePortText) ? string.Empty : $":{RemotePortText}";

        public string Key => $"{Protocol}|{LocalEndpoint}|{RemoteEndpoint}|{State}";

        public bool Equals(ConnectionItem? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Protocol == other.Protocol &&
                   RemoteEndpoint == other.RemoteEndpoint &&
                   LocalEndpoint == other.LocalEndpoint &&
                   LocalPort == other.LocalPort &&
                   State == other.State &&
                   ProcessId == other.ProcessId;
        }

        public override bool Equals(object? obj) => Equals(obj as ConnectionItem);

        public override int GetHashCode() =>
            HashCode.Combine(Protocol, RemoteEndpoint, LocalEndpoint, LocalPort, State, ProcessId);
    }

    public class ProcessConnectionGroup : INotifyPropertyChanged
    {
        public int ProcessId { get; init; }
        public string ProcessName { get; init; } = string.Empty;
        public int ConnectionCount => Connections.Count;
        public string FormattedConnectionCount => $"{ConnectionCount} active {(ConnectionCount == 1 ? "connection" : "connections")}";
        public string FormattedHeader => ProcessId > 0 ? $"{ProcessName} (PID {ProcessId})" : ProcessName;
        public ObservableCollection<ConnectionItem> Connections { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void UpdateConnections(IList<ConnectionItem> incoming)
        {
            // If identical sequence and contents, do not touch the collection at all
            if (Connections.Count == incoming.Count)
            {
                bool identical = true;
                for (int i = 0; i < incoming.Count; i++)
                {
                    if (!Connections[i].Equals(incoming[i]))
                    {
                        identical = false;
                        break;
                    }
                }
                if (identical) return;
            }

            // Safe duplicate-tolerant list reconciliation
            var unmatchedIncoming = new List<ConnectionItem>(incoming);

            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                var current = Connections[i];
                int matchIndex = unmatchedIncoming.FindIndex(item => item.Equals(current));
                if (matchIndex >= 0)
                {
                    unmatchedIncoming.RemoveAt(matchIndex);
                }
                else
                {
                    Connections.RemoveAt(i);
                }
            }

            // Add new connections
            foreach (var newItem in unmatchedIncoming)
            {
                Connections.Add(newItem);
            }

            OnPropertyChanged(nameof(ConnectionCount));
            OnPropertyChanged(nameof(FormattedConnectionCount));
        }
    }
}


