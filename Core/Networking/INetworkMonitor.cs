using System;

namespace NetStats.Core.Networking
{
    public interface INetworkMonitor : IDisposable
    {
        event EventHandler<NetworkSnapshot>? SnapshotUpdated;
        void Start();
        void Stop();
    }
}
