using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace NetStats.Core.Settings
{
    public sealed class JsonAppSettings : IAppSettings
    {
        private const string FileName = "settings.json";
        private readonly string _settingsPath;

        public AppSettings Current { get; }
        public event EventHandler? SettingsChanged;

        public JsonAppSettings()
        {
            string folderPath;
            try
            {
                folderPath = ApplicationData.Current.LocalFolder.Path;
            }
            catch
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                folderPath = Path.Combine(localAppData, "TRACE");
                Directory.CreateDirectory(folderPath);
            }

            _settingsPath = Path.Combine(folderPath, FileName);
            Current = Load();
        }

        public void SetAccentTheme(AccentTheme accentTheme)
        {
            if (Current.AccentTheme == accentTheme)
            {
                return;
            }

            Current.AccentTheme = accentTheme;
            SaveAndNotify();
        }

        public void SetBackgroundTheme(BackgroundTheme backgroundTheme)
        {
            if (Current.BackgroundTheme == backgroundTheme)
            {
                return;
            }

            Current.BackgroundTheme = backgroundTheme;
            SaveAndNotify();
        }

        public void SetSpeedUnit(SpeedUnit speedUnit)
        {
            if (Current.SpeedUnit == speedUnit)
            {
                return;
            }

            Current.SpeedUnit = speedUnit;
            SaveAndNotify();
        }

        public void SetMiniWindowPosition(int x, int y)
        {
            if (Current.MiniWindowX == x && Current.MiniWindowY == y)
            {
                return;
            }

            Current.MiniWindowX = x;
            Current.MiniWindowY = y;
            SaveAndNotify();
        }

        private AppSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new AppSettings();
                }

                return Normalize(JsonSerializer.Deserialize(File.ReadAllText(_settingsPath), AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings());
            }
            catch (JsonException)
            {
                return new AppSettings();
            }
            catch (IOException)
            {
                return new AppSettings();
            }
        }

        private void SaveAndNotify()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                string temporaryPath = _settingsPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Current, AppSettingsJsonContext.Default.AppSettings));
                File.Move(temporaryPath, _settingsPath, true);
            }
            catch (IOException)
            {
                // Preferences remain active for this session if local storage is temporarily unavailable.
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static AppSettings Normalize(AppSettings settings)
        {
            if (!Enum.IsDefined(settings.AccentTheme))
            {
                settings.AccentTheme = AccentTheme.ElectricCyan;
            }

            if (!Enum.IsDefined(settings.BackgroundTheme))
            {
                settings.BackgroundTheme = BackgroundTheme.Midnight;
            }

            if (!Enum.IsDefined(settings.SpeedUnit))
            {
                settings.SpeedUnit = SpeedUnit.Mbps;
            }

            if (!settings.MiniWindowX.HasValue || !settings.MiniWindowY.HasValue)
            {
                settings.MiniWindowX = null;
                settings.MiniWindowY = null;
            }

            return settings;
        }
    }

    [JsonSerializable(typeof(AppSettings))]
    internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
    {
    }
}
