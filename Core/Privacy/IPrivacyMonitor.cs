using System;

namespace NetStats.Core.Privacy
{
    public interface IPrivacyMonitor : IDisposable
    {
        PrivacySnapshot CurrentSnapshot { get; }
        event EventHandler<PrivacySnapshot>? PrivacyUpdated;
        void Start();
        void Stop();
        PrivacySnapshot Poll();
    }
}
