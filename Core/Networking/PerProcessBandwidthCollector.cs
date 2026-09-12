using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace NetStats.Core.Networking
{
    /// <summary>
    /// Measures real per-process TCP byte rates using the Windows IP Helper API
    /// GetPerTcpConnectionEStats. Provides actual byte counters from the OS network
    /// stack — not inferred, estimated, or fabricated values.
    ///
    /// How it works:
    ///   1. Each second, enumerates all TCP connections via GetExtendedTcpTable.
    ///   2. For each connection, calls SetPerTcpConnectionEStats to enable byte
    ///      counting (once, when the connection first appears).
    ///   3. Calls GetPerTcpConnectionEStats to read cumulative DataBytesIn and
    ///      DataBytesOut from the OS.
    ///   4. Computes per-connection deltas from the previous sample.
    ///   5. Aggregates deltas per PID to produce per-process byte rates.
    ///
    /// Direction comes from actual kernel byte accounting:
    ///   - DataBytesIn  = bytes received by the process (download)
    ///   - DataBytesOut = bytes sent by the process (upload)
    ///
    /// Requirements:
    ///   - Requires SeSystemProfilePrivilege or Administrator rights.
    ///   - If privileges insufficient, IsAvailable becomes false and all
    ///     subsequent calls return empty results. No data is fabricated.
    ///
    /// Limitations:
    ///   - TCP only. UDP/QUIC byte counts are unavailable through this API.
    ///   - Processes with only UDP connections will report MeasurementAvailability.Unavailable.
    ///   - First sample for each connection is a baseline (no rate reported).
    /// </summary>
    internal sealed class PerProcessBandwidthCollector : IDisposable
    {
        #region Win32

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable, ref int dwOutBufLen, bool sort,
            int ipVersion, int tblClass, uint reserved = 0);

        [DllImport("iphlpapi.dll")]
        private static extern uint SetPerTcpConnectionEStats(
            IntPtr row, uint version, IntPtr statistics,
            uint statisticsSize, uint offset);

        [DllImport("iphlpapi.dll")]
        private static extern uint GetPerTcpConnectionEStats(
            IntPtr row, uint version, IntPtr data, uint dataSize,
            IntPtr restart, IntPtr srtt, uint srttSize,
            IntPtr buffs, uint buffsSize);

        private const int AfInet = 2;
        private const int TcpTableOwnerPidAll = 5;
        private const uint NoError = 0;
        private const uint ErrorInsufficientBuffer = 122;
        private const uint StatusAccessDenied = 0xC0000022;

        [StructLayout(LayoutKind.Sequential)]
        private struct TcpRow
        {
            public uint State;
            public uint LocalAddr;
            public uint LocalPort;
            public uint RemoteAddr;
            public uint RemotePort;
            public uint OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TcpEstatsDataRw
        {
            public byte CollectData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TcpEstatsDataRod
        {
            public ulong DataBytesOut;
            public ulong DataSegsOut;
            public ulong DataBytesIn;
            public ulong DataSegsIn;
        }

        #endregion

        private readonly Dictionary<string, (long BytesIn, long BytesOut)> _previousCounters = new();
        private readonly HashSet<string> _enabledConnections = new();
        private bool _privilegeDenied;

        public bool IsAvailable { get; private set; }

        /// <summary>
        /// Reads per-process TCP byte counters and computes byte rates.
        /// Call once per sampling interval (typically every 1 second).
        ///
        /// <paramref name="intervalSeconds"/> is the caller's shared measurement
        /// interval — the identical value the system monitor uses for its NIC
        /// rates — which keeps EStats rates directly comparable with system-wide
        /// rates.
        ///
        /// Returns an empty list when measurement is unavailable, or when the
        /// caller signals a rebaseline by passing <see cref="double.NaN"/>
        /// (first sample or a sleep/resume gap): the connection counters are
        /// re-baselined so the next measurement spans the new window.
        /// </summary>
        public IReadOnlyList<ProcessBandwidthSnapshot> CollectAndCompute(double intervalSeconds)
        {
            if (_privilegeDenied)
                return Array.Empty<ProcessBandwidthSnapshot>();

            // Rebaseline (first sample, sleep/resume gap, counter reset):
            // store current counters as the new baseline and return no rates.
            if (double.IsNaN(intervalSeconds))
            {
                BaselineCurrentConnections();
                return Array.Empty<ProcessBandwidthSnapshot>();
            }

            // Defensive guard against a non-NaN but unusable interval.
            if (!double.IsFinite(intervalSeconds) || intervalSeconds <= 0 || intervalSeconds > 5.0)
            {
                BaselineCurrentConnections();
                return Array.Empty<ProcessBandwidthSnapshot>();
            }

            return ComputeRates(intervalSeconds);
        }

        private void BaselineCurrentConnections()
        {
            var rows = GetTcpConnectionRows();
            _previousCounters.Clear();
            _enabledConnections.Clear();

            foreach (var row in rows)
            {
                string key = GetConnectionKey(row);
                _enabledConnections.Add(key);
                EnableStatsForConnection(row);

                if (TryReadByteCounters(row, out long bytesIn, out long bytesOut))
                {
                    _previousCounters[key] = (bytesIn, bytesOut);
                }
            }
        }

        private IReadOnlyList<ProcessBandwidthSnapshot> ComputeRates(double elapsedSeconds)
        {
            var rows = GetTcpConnectionRows();
            var processDownloads = new Dictionary<int, long>();
            var processUploads = new Dictionary<int, long>();
            var currentKeys = new HashSet<string>();

            foreach (var row in rows)
            {
                string key = GetConnectionKey(row);
                currentKeys.Add(key);

                if (_enabledConnections.Add(key))
                {
                    EnableStatsForConnection(row);
                }

                if (TryReadByteCounters(row, out long bytesIn, out long bytesOut))
                {
                    if (_previousCounters.TryGetValue(key, out var prev))
                    {
                        long deltaIn = bytesIn - prev.BytesIn;
                        long deltaOut = bytesOut - prev.BytesOut;

                        if (deltaIn >= 0 && deltaOut >= 0)
                        {
                            int pid = (int)row.OwningPid;
                            AddToDict(processDownloads, pid, deltaIn);
                            AddToDict(processUploads, pid, deltaOut);
                        }
                    }

                    _previousCounters[key] = (bytesIn, bytesOut);
                }
            }

            foreach (var key in _previousCounters.Keys.Where(k => !currentKeys.Contains(k)).ToList())
                _previousCounters.Remove(key);
            _enabledConnections.IntersectWith(currentKeys);

            var results = new List<ProcessBandwidthSnapshot>(processDownloads.Count);
            DateTime nowUtc = DateTime.UtcNow;
            foreach (var kv in processDownloads)
            {
                results.Add(new ProcessBandwidthSnapshot(
                    kv.Key,
                    ResolveProcessName(kv.Key),
                    kv.Value / elapsedSeconds,
                    processUploads.TryGetValue(kv.Key, out var ul) ? ul / elapsedSeconds : 0,
                    nowUtc));
            }

            return results;
        }

        private static string ResolveProcessName(int pid)
        {
            if (pid == 0) return "Idle";
            if (pid == 4) return "System";

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                return process.ProcessName;
            }
            catch
            {
                return "Unknown";
            }
        }

        private void EnableStatsForConnection(TcpRow row)
        {
            if (_privilegeDenied) return;

            int rowSize = Marshal.SizeOf<TcpRow>();
            IntPtr rowPtr = Marshal.AllocHGlobal(rowSize);
            try
            {
                Marshal.StructureToPtr(row, rowPtr, false);

                var rw = new TcpEstatsDataRw { CollectData = 1 };
                int rwSize = Marshal.SizeOf<TcpEstatsDataRw>();
                IntPtr rwPtr = Marshal.AllocHGlobal(rwSize);
                try
                {
                    Marshal.StructureToPtr(rw, rwPtr, false);
                    uint status = SetPerTcpConnectionEStats(rowPtr, 0, rwPtr, (uint)rwSize, 0);

                    if (status == StatusAccessDenied)
                    {
                        _privilegeDenied = true;
                        IsAvailable = false;
                    }
                    else if (status == NoError)
                    {
                        IsAvailable = true;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(rwPtr);
                }
            }
            catch
            {
                _privilegeDenied = true;
                IsAvailable = false;
            }
            finally
            {
                Marshal.FreeHGlobal(rowPtr);
            }
        }

        private bool TryReadByteCounters(TcpRow row, out long bytesIn, out long bytesOut)
        {
            bytesIn = 0;
            bytesOut = 0;

            int rowSize = Marshal.SizeOf<TcpRow>();
            IntPtr rowPtr = Marshal.AllocHGlobal(rowSize);
            try
            {
                Marshal.StructureToPtr(row, rowPtr, false);

                int rodSize = Marshal.SizeOf<TcpEstatsDataRod>();
                IntPtr rodPtr = Marshal.AllocHGlobal(rodSize);
                try
                {
                    uint status = GetPerTcpConnectionEStats(
                        rowPtr, 0, rodPtr, (uint)rodSize,
                        IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, 0);

                    if (status == NoError)
                    {
                        var rod = Marshal.PtrToStructure<TcpEstatsDataRod>(rodPtr);
                        bytesIn = (long)rod.DataBytesIn;
                        bytesOut = (long)rod.DataBytesOut;
                        return true;
                    }
                    return false;
                }
                finally
                {
                    Marshal.FreeHGlobal(rodPtr);
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(rowPtr);
            }
        }

        private static List<TcpRow> GetTcpConnectionRows()
        {
            var rows = new List<TcpRow>();
            int bufferSize = 0;
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableOwnerPidAll);

            if (result != ErrorInsufficientBuffer)
                return rows;

            IntPtr tablePtr = Marshal.AllocHGlobal(bufferSize);
            try
            {
                result = GetExtendedTcpTable(tablePtr, ref bufferSize, true, AfInet, TcpTableOwnerPidAll);
                if (result == NoError)
                {
                    int rowCount = Marshal.ReadInt32(tablePtr);
                    IntPtr rowPtr = tablePtr + 4;
                    int rowSize = Marshal.SizeOf<TcpRow>();

                    for (int i = 0; i < rowCount; i++)
                    {
                        rows.Add(Marshal.PtrToStructure<TcpRow>(rowPtr));
                        rowPtr += rowSize;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tablePtr);
            }
            return rows;
        }

        private static string GetConnectionKey(TcpRow row)
        {
            return $"{row.LocalAddr}:{row.LocalPort}:{row.RemoteAddr}:{row.RemotePort}";
        }

        private static void AddToDict(Dictionary<int, long> dict, int key, long value)
        {
            dict.TryGetValue(key, out long existing);
            dict[key] = existing + value;
        }

        public void Dispose()
        {
        }
    }
}
