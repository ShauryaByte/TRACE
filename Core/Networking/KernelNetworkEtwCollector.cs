using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace NetStats.Core.Networking
{
    /// <summary>
    /// Measures real per-process byte rates using Windows kernel ETW events
    /// from the Microsoft-Windows-Kernel-Network provider.
    ///
    /// Covers TCP send/recv and UDP send/recv with per-PID attribution directly
    /// from the kernel network stack — the same mechanism Windows uses internally.
    ///
    /// How it works:
    ///   1. Starts an ETW session that enables the Kernel Network provider
    ///      (TcpIp + UdpIp send/recv events, IPv4 and IPv6).
    ///   2. As network packets traverse the kernel stack, ETW fires events with
    ///      the owning process PID and byte count for each packet.
    ///   3. Events are accumulated per-PID in real time by the ETW processing thread.
    ///   4. CollectAndCompute(intervalSeconds) reads the accumulated byte counters,
    ///      divides by the caller's shared interval to produce bytes/sec, and resets
    ///      for the next interval. The interval, window boundaries, and rebaseline
    ///      timing are all owned by the caller (SystemNetworkMonitor) so ETW rates
    ///      are directly comparable with the system-wide NIC rates.
    ///
    /// Requirements:
    ///   - Requires Administrator privileges to enable kernel ETW logging.
    ///   - If not running elevated, IsAvailable becomes false.
    ///   - The main TRACE UI does NOT require elevation — this collector
    ///     gracefully degrades when privileges are insufficient.
    ///
    /// Covers:
    ///   - TCP send and receive (IPv4 + IPv6)
    ///   - UDP send and receive (IPv4 + IPv6)
    ///   - Every process on the system with active network I/O
    ///
    /// PID reuse handling:
    ///   - Tracks process start time (via Process.GetProcessById) to detect when
    ///     a PID has been recycled. Counters for stale PIDs are discarded.
    ///
    /// Sleep/resume handling:
    ///   - The caller signals a rebaseline (first sample or unreasonable gap) by
    ///     passing double.NaN as the interval; the collector then drains its state
    ///     and returns zero rates (no fabrication).
    /// </summary>
    internal sealed class KernelNetworkEtwCollector : IProcessBandwidthCollector
    {
        private const string SessionName = "NetStatsKernelNetwork";
        private const double MaxElapsedBeforeResetSeconds = 5.0;

        private TraceEventSession? _session;
        private Thread? _processingThread;
        private volatile bool _disposed;
        private volatile bool _eventsWereLost;

        // Accumulated byte counters indexed by PID.
        // Modified by the ETW processing thread, read by CollectAndCompute on the sampling thread.
        // Swapped atomically so no bytes are lost between intervals.
        private ConcurrentDictionary<int, long> _downloadBytes = new();
        private ConcurrentDictionary<int, long> _uploadBytes = new();

        // Last cumulative event-loss counter reported by the ETW source.
        private long _lastEventsLost;

        // Process start times to detect PID reuse.
        private readonly ConcurrentDictionary<int, DateTime> _processStartTimes = new();

        private int _sessionFailed;

        public bool IsAvailable { get; private set; }
        public bool EventsWereLost => _eventsWereLost;

        public KernelNetworkEtwCollector()
        {
            StartSession();
        }

        private void StartSession()
        {
            if (Interlocked.CompareExchange(ref _sessionFailed, 1, 0) != 0)
                return;

            try
            {
                _session = new TraceEventSession(SessionName);

                // Enable kernel network events (TCP send/recv + UDP send/recv).
                // This requires Administrator privileges.
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                _session.Source.Kernel.TcpIpSend += OnTcpIpSend;
                _session.Source.Kernel.TcpIpRecv += OnTcpIpRecv;
                _session.Source.Kernel.UdpIpSend += OnUdpIpSend;
                _session.Source.Kernel.UdpIpRecv += OnUdpIpRecv;
                _session.Source.Kernel.TcpIpSendIPV6 += OnTcpIpSendV6;
                _session.Source.Kernel.TcpIpRecvIPV6 += OnTcpIpRecvV6;
                _session.Source.Kernel.UdpIpSendIPV6 += OnUdpIpSendV6;
                _session.Source.Kernel.UdpIpRecvIPV6 += OnUdpIpRecvV6;

                // Run the ETW processing loop on a dedicated background thread.
                // TraceEventSource.Process() blocks until the session is disposed.
                _processingThread = new Thread(() =>
                {
                    try
                    {
                        _session.Source.Process();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected when the session is disposed during shutdown.
                    }
                    catch (Exception)
                    {
                        // ETW processing thread must not crash the application.
                    }
                })
                {
                    IsBackground = true,
                    Name = "ETW-KernelNetwork-Processor",
                    Priority = ThreadPriority.AboveNormal
                };
                _processingThread.Start();

                // Wait briefly to confirm the session started successfully.
                // If the thread exits immediately, the session failed (likely privilege issue).
                Thread.Sleep(200);
                if (_processingThread.IsAlive)
                {
                    IsAvailable = true;
                    System.Diagnostics.Debug.WriteLine("[KernelETW] ETW kernel network session started successfully.");
                }
                else
                {
                    IsAvailable = false;
                    Interlocked.Exchange(ref _sessionFailed, 1);
                    System.Diagnostics.Debug.WriteLine("[KernelETW] ETW kernel network session failed (thread exited).");
                }
            }
            catch (UnauthorizedAccessException)
            {
                IsAvailable = false;
                Interlocked.Exchange(ref _sessionFailed, 1);
                System.Diagnostics.Debug.WriteLine("[KernelETW] ETW kernel network session requires Administrator privileges.");
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                IsAvailable = false;
                Interlocked.Exchange(ref _sessionFailed, 1);
                System.Diagnostics.Debug.WriteLine("[KernelETW] ETW kernel network session failed (COM error).");
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                Interlocked.Exchange(ref _sessionFailed, 1);
                System.Diagnostics.Debug.WriteLine($"[KernelETW] ETW kernel network session failed: {ex.Message}");
            }
        }

        #region ETW Event Handlers

        // Send events = bytes transmitted by the owning process (UPLOAD).
        // Recv events = bytes received by the owning process (DOWNLOAD).

        private void OnTcpIpSend(TcpIpSendTraceData data) => Accumulate(data.ProcessID, data.size, _uploadBytes);

        private void OnTcpIpRecv(TcpIpTraceData data) => Accumulate(data.ProcessID, data.size, _downloadBytes);

        private void OnUdpIpSend(UdpIpTraceData data) => Accumulate(data.ProcessID, data.size, _uploadBytes);

        private void OnUdpIpRecv(UdpIpTraceData data) => Accumulate(data.ProcessID, data.size, _downloadBytes);

        // IPv6 variants: note the TraceEvent type names (TcpIpV6* and the
        // upstream typo "UpdIpV6*").
        private void OnTcpIpSendV6(TcpIpV6SendTraceData data) => Accumulate(data.ProcessID, data.size, _uploadBytes);

        private void OnTcpIpRecvV6(TcpIpV6TraceData data) => Accumulate(data.ProcessID, data.size, _downloadBytes);

        private void OnUdpIpSendV6(UpdIpV6TraceData data) => Accumulate(data.ProcessID, data.size, _uploadBytes);

        private void OnUdpIpRecvV6(UpdIpV6TraceData data) => Accumulate(data.ProcessID, data.size, _downloadBytes);

        private void Accumulate(int pid, int size, ConcurrentDictionary<int, long> counter)
        {
            if (pid <= 0 || size <= 0)
                return;

            counter.AddOrUpdate(pid, size, (_, existing) => existing + size);
            EnsureProcessTracked(pid);
        }

        #endregion

        private void EnsureProcessTracked(int pid)
        {
            if (_processStartTimes.ContainsKey(pid))
                return;

            try
            {
                using var process = Process.GetProcessById(pid);
                _processStartTimes[pid] = process.StartTime.ToUniversalTime();
            }
            catch
            {
                // Process may have exited between the ETW event and our lookup.
                // Use DateTime.MinValue as a sentinel — the PID is still valid for
                // this interval even if we can't confirm its start time.
                _processStartTimes[pid] = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Reads accumulated byte counters, computes per-process byte rates,
        /// and resets internal state for the next interval.
        ///
        /// <paramref name="intervalSeconds"/> is the caller's shared measurement
        /// interval — the identical value the system monitor uses for its NIC
        /// rates. Draining happens at the same instant the NIC counters are read,
        /// so the ETW window and the NIC window share the same time boundaries.
        /// </summary>
        public IReadOnlyList<ProcessBandwidthSnapshot> CollectAndCompute(double intervalSeconds)
        {
            if (_disposed || !IsAvailable)
                return Array.Empty<ProcessBandwidthSnapshot>();

            // Rebaseline sentinel (first sample, sleep/resume gap, or counter reset):
            // the caller is starting a fresh measurement window right now. Drain
            // accumulated state; the NEXT call measures over [now, next drain],
            // aligned with the caller's NIC window.
            if (double.IsNaN(intervalSeconds))
            {
                DrainCounters();
                return Array.Empty<ProcessBandwidthSnapshot>();
            }

            // Defensive guard against a non-NaN but unusable interval.
            if (!double.IsFinite(intervalSeconds) || intervalSeconds <= 0 || intervalSeconds > MaxElapsedBeforeResetSeconds)
            {
                DrainCounters();
                return Array.Empty<ProcessBandwidthSnapshot>();
            }

            // Detect dropped events since the last interval (undercounting indicator).
            // EventsWereLost reflects only the interval just measured.
            try
            {
                if (_session != null)
                {
                    long lostNow = _session.Source.EventsLost;
                    _eventsWereLost = lostNow > _lastEventsLost;
                    _lastEventsLost = lostNow;
                }
                else
                {
                    _eventsWereLost = false;
                }
            }
            catch
            {
                _eventsWereLost = false;
            }

            // Atomically swap the accumulated counters for fresh dictionaries.
            // Events that arrive during the swap go into the new dictionary and are
            // accounted for in the next interval — nothing is lost.
            var downloads = Interlocked.Exchange(ref _downloadBytes, new ConcurrentDictionary<int, long>());
            var uploads = Interlocked.Exchange(ref _uploadBytes, new ConcurrentDictionary<int, long>());

            // Detect PID reuse: discard counters from PIDs whose process start time
            // has changed since we first recorded it.
            var activePids = new HashSet<int>(downloads.Keys);
            activePids.UnionWith(uploads.Keys);

            var results = new List<ProcessBandwidthSnapshot>(activePids.Count);
            DateTime nowUtc = DateTime.UtcNow;

            foreach (int pid in activePids)
            {
                // Validate PID is still alive with same start time.
                if (!IsPidStillValid(pid))
                    continue;

                long dlBytes = downloads.TryGetValue(pid, out var dl) ? dl : 0;
                long ulBytes = uploads.TryGetValue(pid, out var ul) ? ul : 0;

                // Skip PIDs with zero traffic in this interval.
                if (dlBytes == 0 && ulBytes == 0)
                    continue;

                string processName = ResolveProcessName(pid);

                results.Add(new ProcessBandwidthSnapshot(
                    pid,
                    processName,
                    dlBytes / intervalSeconds,
                    ulBytes / intervalSeconds,
                    nowUtc));
            }

            return results;
        }

        private bool IsPidStillValid(int pid)
        {
            if (!_processStartTimes.TryGetValue(pid, out var recordedStartTime))
                return true; // Not tracked — assume valid for this interval.

            try
            {
                using var process = Process.GetProcessById(pid);
                return process.StartTime.ToUniversalTime() == recordedStartTime ||
                       recordedStartTime == DateTime.MinValue;
            }
            catch
            {
                // Process exited — discard its counters (PID may have been reused).
                _processStartTimes.TryRemove(pid, out _);
                return false;
            }
        }

        private string ResolveProcessName(int pid)
        {
            if (pid == 0) return "Idle";
            if (pid == 4) return "System";

            try
            {
                using var process = Process.GetProcessById(pid);
                return process.ProcessName;
            }
            catch
            {
                return "Unknown";
            }
        }

        private void DrainCounters()
        {
            Interlocked.Exchange(ref _downloadBytes, new ConcurrentDictionary<int, long>());
            Interlocked.Exchange(ref _uploadBytes, new ConcurrentDictionary<int, long>());
            _eventsWereLost = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _session?.Dispose();
            }
            catch
            {
                // Best-effort cleanup.
            }

            try
            {
                _processingThread?.Join(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Best-effort join.
            }

            _session = null;
        }
    }
}
