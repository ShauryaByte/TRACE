using System;

namespace NetStats.Core.Networking
{
    /// <summary>
    /// Real per-process byte rate measured from OS network telemetry.
    /// Values are in bytes per second. Direction comes from actual byte accounting
    /// (DownloadBytesPerSecond = bytes received, UploadBytesPerSecond = bytes sent),
    /// never inferred from endpoints or process identity.
    /// </summary>
    public sealed record ProcessBandwidthSnapshot(
        int ProcessId,
        string ProcessName,
        double DownloadBytesPerSecond,
        double UploadBytesPerSecond,
        DateTime LastUpdated);
}
