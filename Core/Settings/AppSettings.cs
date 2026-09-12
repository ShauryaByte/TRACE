using System;

namespace NetStats.Core.Settings
{
    public enum AccentTheme
    {
        ElectricCyan,
        CobaltBlue,
        Violet,
        Magenta,
        Crimson,
        Amber,
        Emerald,
        Mint,
        Cyberpunk,
        Sunset,
        Aurora
    }


    public enum BackgroundTheme
    {
        Midnight,
        Obsidian,
        DeepOcean,
        ArcticNight,
        IndigoNight,
        EmeraldNight,
        VioletNight,
        Cosmic,
        Aurora,
        Ember
    }

    public enum SpeedUnit
    {
        Mbps,
        MegabytesPerSecond
    }

    public sealed class AppSettings
    {
        public AccentTheme AccentTheme { get; set; } = AccentTheme.ElectricCyan;
        public BackgroundTheme BackgroundTheme { get; set; } = BackgroundTheme.Midnight;
        public SpeedUnit SpeedUnit { get; set; } = SpeedUnit.Mbps;
        public int? MiniWindowX { get; set; }
        public int? MiniWindowY { get; set; }
    }

    public interface IAppSettings
    {
        AppSettings Current { get; }
        event EventHandler? SettingsChanged;
        void SetAccentTheme(AccentTheme accentTheme);
        void SetBackgroundTheme(BackgroundTheme backgroundTheme);
        void SetSpeedUnit(SpeedUnit speedUnit);
        void SetMiniWindowPosition(int x, int y);
    }
}
