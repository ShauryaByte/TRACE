using System;
using System.Collections.Generic;

namespace NetStats.Core.Networking
{
    /// <summary>
    /// Provides per-process byte rate measurements from OS network telemetry.
    /// Implementations must report actual measured bytes, never inferred or fabricated values.
    /// </summary>
    internal interface IProcessBandwidthCollector : IDisposable
    {
        /// <summary>
        /// True if the collector successfully initialized and can provide measurements.
        /// False if privileges were insufficient or the session failed to start.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// True if any ETW events were lost during the last collection interval,
        /// which may cause undercounting for that interval.
        /// </summary>
        bool EventsWereLost { get; }

        /// <summary>
        /// Reads accumulated byte counters, computes per-process byte rates, and
        /// resets internal state for the next interval. Call once per sampling
        /// interval (typically every 1 second).
        ///
        /// <paramref name="intervalSeconds"/> is the elapsed time of the current
        /// measurement window as measured by the caller (the system monitor). It
        /// MUST be the identical value used to compute the system-wide NIC rates,
        /// so per-process rates are directly comparable to system-wide rates.
        ///
        /// Pass <see cref="double.NaN"/> to signal a rebaseline (first sample or a
        /// sleep/resume gap): the collector drains accumulated state and returns an
        /// empty result without computing any rates. The rate computation of the
        /// NEXT call then spans from this baseline to the next drain, aligned with
        /// the caller's NIC window.
        /// </summary>
        IReadOnlyList<ProcessBandwidthSnapshot> CollectAndCompute(double intervalSeconds);
    }
}