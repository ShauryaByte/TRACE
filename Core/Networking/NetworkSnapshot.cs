using System;
using System.Collections.Generic;

namespace NetStats.Core.Networking
{
    public class NetworkSnapshot
    {
        public DateTime Timestamp { get; init; }

        /// <summary>
        /// System-wide receive rate (bytes/sec) from the interface layer
        /// (GetIPv4Statistics.BytesReceived on gateway-eligible adapters).
        /// This counts IP octets INCLUDING IP/TCP headers and 802.11 framing,
        /// so it is structurally larger than the sum of per-process rates.
        /// </summary>
        public double DownloadBytesPerSecond { get; init; }

        /// <summary>
        /// System-wide transmit rate (bytes/sec) from the interface layer.
        /// Includes TCP/IP headers and 802.11 framing, and counts acknowledged
        /// segments (ACKs) that carry no application payload, so it is always
        /// larger than the sum of per-process upload data.
        /// </summary>
        public double UploadBytesPerSecond { get; init; }
        public long SessionDownloadBytes { get; init; }
        public long SessionUploadBytes { get; init; }
        public IReadOnlyList<NetworkConnectionInfo> ActiveConnections { get; init; } = Array.Empty<NetworkConnectionInfo>();

        /// <summary>
        /// Per-process byte rates from the kernel ETW network provider (TcpIp/UdpIp
        /// send/recv, IPv4 + IPv6). Each entry is the measured transport PAYLOAD
        /// bytes (no IP/TCP/UDP headers, no link framing) attributed to a process.
        /// UDP and non-socket-mapped kernel traffic is never assigned to a process;
        /// genuinely unattributable bytes are simply not listed here rather than
        /// being fabricated onto any PID. Because of the layer difference above,
        /// Sum(PerProcessBandwidth) is not expected to equal DownloadBytesPerSecond +
        /// UploadBytesPerSecond.
        /// </summary>
        public IReadOnlyList<ProcessBandwidthSnapshot> PerProcessBandwidth { get; init; } = Array.Empty<ProcessBandwidthSnapshot>();
    }
}
