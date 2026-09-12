using System;

namespace NetStats.Core.Privacy
{
    public sealed class PrivacySnapshot
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public PrivacyDeviceUsage Microphone { get; init; } = PrivacyDeviceUsage.Inactive();
        public PrivacyDeviceUsage Camera { get; init; } = PrivacyDeviceUsage.Inactive();
    }
}
