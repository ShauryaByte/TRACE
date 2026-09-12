using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace NetStats.Core.Networking
{
    internal static class ConnectionHelper
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, uint reserved = 0);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, uint reserved = 0);

        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const int UDP_TABLE_OWNER_PID = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint remoteAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] remotePort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint owningPid;
        }

        public static List<NetworkConnectionInfo> GetActiveConnections()
        {
            var connections = new List<NetworkConnectionInfo>();
            connections.AddRange(GetTcpConnections());
            connections.AddRange(GetUdpConnections());
            return connections;
        }

        private static List<NetworkConnectionInfo> GetTcpConnections()
        {
            var connections = new List<NetworkConnectionInfo>();
            int bufferSize = 0;
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL);
            
            if (result != 122) // ERROR_INSUFFICIENT_BUFFER
                return connections;

            IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
            try
            {
                result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL);
                if (result == 0)
                {
                    int rowCount = Marshal.ReadInt32(tcpTablePtr);
                    IntPtr rowPtr = tcpTablePtr + 4;

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                        
                        var localPort = BitConverter.ToUInt16(new byte[] { row.localPort[1], row.localPort[0] }, 0);
                        var remotePort = BitConverter.ToUInt16(new byte[] { row.remotePort[1], row.remotePort[0] }, 0);
                        
                        connections.Add(new NetworkConnectionInfo
                        {
                            Protocol = "TCP",
                            LocalAddress = new IPAddress(row.localAddr).ToString(),
                            LocalPort = localPort,
                            RemoteAddress = new IPAddress(row.remoteAddr).ToString(),
                            RemotePort = remotePort,
                            State = ((TcpState)row.state).ToString(),
                            ProcessId = (int)row.owningPid,
                            ObservedAt = DateTime.UtcNow
                        });

                        rowPtr += Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tcpTablePtr);
            }
            return connections;
        }

        private static List<NetworkConnectionInfo> GetUdpConnections()
        {
            var connections = new List<NetworkConnectionInfo>();
            int bufferSize = 0;
            uint result = GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, UDP_TABLE_OWNER_PID);
            
            if (result != 122) // ERROR_INSUFFICIENT_BUFFER
                return connections;

            IntPtr udpTablePtr = Marshal.AllocHGlobal(bufferSize);
            try
            {
                result = GetExtendedUdpTable(udpTablePtr, ref bufferSize, true, AF_INET, UDP_TABLE_OWNER_PID);
                if (result == 0)
                {
                    int rowCount = Marshal.ReadInt32(udpTablePtr);
                    IntPtr rowPtr = udpTablePtr + 4;

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                        
                        var localPort = BitConverter.ToUInt16(new byte[] { row.localPort[1], row.localPort[0] }, 0);
                        
                        connections.Add(new NetworkConnectionInfo
                        {
                            Protocol = "UDP",
                            LocalAddress = new IPAddress(row.localAddr).ToString(),
                            LocalPort = localPort,
                            State = "None",
                            ProcessId = (int)row.owningPid,
                            ObservedAt = DateTime.UtcNow
                        });

                        rowPtr += Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(udpTablePtr);
            }
            return connections;
        }
        
        public enum TcpState
        {
            Closed = 1,
            Listen = 2,
            SynSent = 3,
            SynRcvd = 4,
            Established = 5,
            FinWait1 = 6,
            FinWait2 = 7,
            CloseWait = 8,
            Closing = 9,
            LastAck = 10,
            TimeWait = 11,
            DeleteTcb = 12
        }
    }
}
