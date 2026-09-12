using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using NetStats.Core.Settings;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NetStats;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    public IAppSettings Settings { get; }
    
    public static void Log(string message)
    {
        try
        {
            string line = $"[{DateTime.UtcNow:o}] {message}\n";
            System.Diagnostics.Debug.WriteLine(line);
            Console.WriteLine(line);
            try
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var traceDir = System.IO.Path.Combine(baseDir, "TRACE");
                var oldDir = System.IO.Path.Combine(baseDir, "NetStats");
                if (System.IO.Directory.Exists(oldDir) && !System.IO.Directory.Exists(traceDir))
                {
                    try { System.IO.Directory.CreateDirectory(traceDir); } catch { }
                    var oldLog = System.IO.Path.Combine(oldDir, "app_activity.log");
                    var newLog = System.IO.Path.Combine(traceDir, "app_activity.log");
                    if (System.IO.File.Exists(oldLog) && !System.IO.File.Exists(newLog))
                    {
                        try { System.IO.File.Copy(oldLog, newLog, false); } catch { }
                    }
                }
                else
                {
                    System.IO.Directory.CreateDirectory(traceDir);
                }
                System.IO.File.AppendAllText(System.IO.Path.Combine(traceDir, "app_activity.log"), line);
            }
            catch { }
        }
        catch { }
    }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        Log("App constructor starting");
        UnhandledException += (s, e) =>
        {
            Log($"[FATAL UNHANDLED WinUI] {e.Message} | {e.Exception}");
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log($"[FATAL UNHANDLED AppDomain] {e.ExceptionObject}");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log($"[FATAL UNHANDLED TaskScheduler] {e.Exception}");
        };

        try
        {
            InitializeComponent();
            Log("App InitializeComponent finished");
        }
        catch (Exception ex)
        {
            Log($"[FATAL App InitializeComponent] {ex}");
            throw;
        }

        try
        {
            Settings = new JsonAppSettings();
            Settings.SettingsChanged += (_, _) =>
            {
                ApplyAccentTheme();
                ApplyBackgroundTheme();
            };
            Log("App constructor completed");
        }
        catch (Exception ex)
        {
            Log($"[FATAL Settings initialization] {ex}");
            throw;
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            Log("OnLaunched starting");
            _window = new MainWindow();
            Log("MainWindow created");
            ApplyAccentTheme();
            ApplyBackgroundTheme();
            Log("ApplyAccentTheme and ApplyBackgroundTheme done");
            _window.Activate();
            Log("MainWindow activated");
        }
        catch (Exception ex)
        {
            Log($"[FATAL OnLaunched] {ex}");
            throw;
        }
    }

    public void ApplyBackgroundTheme()
    {
        try
        {
            if (_window is MainWindow mw)
            {
                mw.ApplyBackgroundTheme(Settings.Current.BackgroundTheme);
            }
        }
        catch (Exception ex)
        {
            Log($"[ApplyBackgroundTheme] {ex}");
        }
    }

    public Windows.UI.Color CurrentAccentColor { get; private set; }

    private void ApplyAccentTheme()
    {
        try
        {
            var palette = Settings.Current.AccentTheme switch
            {
                AccentTheme.CobaltBlue => CreateAccentPalette(59, 130, 246, 29, 78, 216, 30, 58, 138, 191, 219, 254),
                AccentTheme.Violet => CreateAccentPalette(168, 85, 247, 126, 34, 206, 88, 28, 135, 233, 213, 255),
                AccentTheme.Magenta => CreateAccentPalette(236, 72, 153, 190, 24, 93, 131, 24, 67, 251, 207, 232),
                AccentTheme.Crimson => CreateAccentPalette(239, 68, 68, 185, 28, 28, 127, 29, 29, 254, 202, 202),
                AccentTheme.Amber => CreateAccentPalette(245, 158, 11, 180, 83, 9, 120, 53, 15, 254, 240, 138),
                AccentTheme.Emerald => CreateAccentPalette(16, 185, 129, 4, 120, 87, 6, 78, 59, 167, 243, 208),
                AccentTheme.Mint => CreateAccentPalette(20, 184, 166, 15, 118, 110, 19, 78, 74, 153, 246, 228),
                AccentTheme.Cyberpunk => CreateAccentPalette(255, 42, 133, 0, 245, 255, 112, 16, 80, 255, 180, 225),
                AccentTheme.Sunset => CreateAccentPalette(255, 94, 58, 255, 42, 109, 128, 30, 20, 255, 205, 175),
                AccentTheme.Aurora => CreateAccentPalette(0, 255, 135, 96, 239, 255, 20, 100, 60, 185, 255, 225),
                _ => CreateAccentPalette(48, 199, 221, 22, 138, 162, 23, 57, 67, 157, 236, 247)
            };

            CurrentAccentColor = palette.Primary;

            // Set brushes immediately to ensure all XAML views reflect theme changes synchronously
            SetDesignBrushImmediately("AccentPrimaryBrush", palette.Primary);
            SetDesignBrushImmediately("AccentSecondaryBrush", palette.Secondary);
            SetDesignBrushImmediately("AccentMutedBrush", palette.Muted);
            SetDesignBrushImmediately("AccentSurfaceBrush", palette.Surface);
            SetDesignBrushImmediately("AccentSurfaceStrongBrush", palette.SurfaceStrong);
            SetDesignBrushImmediately("AccentBorderBrush", palette.Border);
            SetDesignBrushImmediately("AccentBorderStrongBrush", palette.BorderStrong);
            SetDesignBrushImmediately("AccentGlowBrush", palette.Glow);
            SetDesignBrushImmediately("AccentTextBrush", palette.Text);
            SetDesignBrushImmediately("AccentHoverBrush", palette.Hover);
            SetDesignBrushImmediately("AccentPressedBrush", palette.Pressed);
            SetDesignBrushImmediately("AccentFocusBrush", palette.Focus);
            SetDesignBrushImmediately("StatusLiveBrush", palette.Primary);
            SetDesignBrushImmediately("PinActiveSurfaceBrush", palette.SurfaceStrong);
            SetDesignBrushImmediately("PinActiveBorderBrush", palette.BorderStrong);

            if (_window is MainWindow mw)
            {
                mw.UpdateDwmBorderColor(palette.Primary);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ApplyAccentTheme] {ex}");
        }
    }

    private static AccentPalette CreateAccentPalette(
        byte primaryR,
        byte primaryG,
        byte primaryB,
        byte secondaryR,
        byte secondaryG,
        byte secondaryB,
        byte mutedR,
        byte mutedG,
        byte mutedB,
        byte textR,
        byte textG,
        byte textB)
    {
        var primary = Windows.UI.Color.FromArgb(255, primaryR, primaryG, primaryB);
        var secondary = Windows.UI.Color.FromArgb(255, secondaryR, secondaryG, secondaryB);
        return new AccentPalette(
            primary,
            secondary,
            Windows.UI.Color.FromArgb(255, mutedR, mutedG, mutedB),
            WithAlpha(primary, 30),
            WithAlpha(primary, 48),
            WithAlpha(primary, 82),
            WithAlpha(primary, 138),
            WithAlpha(primary, 61),
            Windows.UI.Color.FromArgb(255, textR, textG, textB),
            WithAlpha(primary, 50),
            WithAlpha(primary, 82),
            WithAlpha(primary, 184));
    }

    private static Windows.UI.Color WithAlpha(Windows.UI.Color color, byte alpha) =>
        Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);

    private void SetDesignBrushImmediately(string resourceKey, Windows.UI.Color color)
    {
        GetDesignBrush(resourceKey).Color = color;
    }

    private SolidColorBrush GetDesignBrush(string resourceKey)
    {
        foreach (var dictionary in Resources.MergedDictionaries)
        {
            if (dictionary.TryGetValue(resourceKey, out var resource) && resource is SolidColorBrush brush)
            {
                return brush;
            }
        }

        throw new InvalidOperationException($"Missing application brush resource: {resourceKey}");
    }

    private readonly record struct AccentPalette(
        Windows.UI.Color Primary,
        Windows.UI.Color Secondary,
        Windows.UI.Color Muted,
        Windows.UI.Color Surface,
        Windows.UI.Color SurfaceStrong,
        Windows.UI.Color Border,
        Windows.UI.Color BorderStrong,
        Windows.UI.Color Glow,
        Windows.UI.Color Text,
        Windows.UI.Color Hover,
        Windows.UI.Color Pressed,
        Windows.UI.Color Focus);
}
