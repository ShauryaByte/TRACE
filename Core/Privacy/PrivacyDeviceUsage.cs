using System;

namespace NetStats.Core.Privacy
{
    public sealed record PrivacyDeviceUsage
    {
        public PrivacyDeviceState State { get; init; } = PrivacyDeviceState.Inactive;
        public string Attribution { get; init; } = "No current activity";
        public bool IsHighConfidence { get; init; } = true;
        public DateTime LastObservation { get; init; } = DateTime.UtcNow;

        public static PrivacyDeviceUsage Inactive() => new()
        {
            State = PrivacyDeviceState.Inactive,
            Attribution = "No current activity",
            IsHighConfidence = true,
            LastObservation = DateTime.UtcNow
        };

        public static PrivacyDeviceUsage Active(string attribution, bool isHighConfidence) => new()
        {
            State = PrivacyDeviceState.Active,
            Attribution = attribution,
            IsHighConfidence = isHighConfidence,
            LastObservation = DateTime.UtcNow
        };

        public static PrivacyDeviceUsage Unknown(string? attribution = null) => new()
        {
            State = PrivacyDeviceState.Unknown,
            Attribution = string.IsNullOrWhiteSpace(attribution) ? "Unknown" : attribution,
            IsHighConfidence = false,
            LastObservation = DateTime.UtcNow
        };
    }
}
