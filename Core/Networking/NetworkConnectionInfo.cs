using System;

namespace NetStats.Core.Networking
{
    public class NetworkConnectionInfo
    {
        public int ProcessId { get; init; }
        public string ProcessName { get; init; } = string.Empty;
        public string Protocol { get; init; } = string.Empty;
        public string LocalAddress { get; init; } = string.Empty;
        public int LocalPort { get; init; }
        public string RemoteAddress { get; init; } = string.Empty;
        public int RemotePort { get; init; }
        public string State { get; init; } = string.Empty;
        public DateTime ObservedAt { get; init; }
    }
}
