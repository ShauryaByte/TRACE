using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NetStats.Core.Settings;
using NetStats.UI.ViewModels;
using NetStats.UI.Views;

namespace NetStats
{
    public sealed partial class MainWindow : Window
    {
        private bool _isAlwaysOnTop = false;
        private bool _isCollapsed = false;

        private const int ExpandedWidth = 400;
        private const int ExpandedHeight = 500;
        private const int CollapsedWidth = 400;
        private const int CollapsedHeight = 126;
        private MiniSpeedWindow? _miniWindow;
        private IntPtr _hwnd;
        private SubclassProc? _subclassProc;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);

        private const int DWMWA_BORDER_COLOR = 34;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

        private const int WM_SIZE = 0x0005;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int SIZE_RESTORED = 0;
        private const int SIZE_MINIMIZED = 1;
        private const int SIZE_MAXIMIZED = 2;
        private const int SW_RESTORE = 9;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;

            public Point(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }

        public MainWindow()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                App.Log($"[FATAL MainWindow InitializeComponent] {ex}");
                throw;
            }

            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            AppWindow.Title = "TRACE";
            SetWindowText(_hwnd, "TRACE");

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            if (AppWindow.TitleBar != null)
            {
                var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                var foreground = Windows.UI.Color.FromArgb(255, 190, 195, 205);
                var hoverBg = Windows.UI.Color.FromArgb(30, 255, 255, 255);
                var hoverFg = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                var inactiveFg = Windows.UI.Color.FromArgb(255, 120, 125, 135);

                AppWindow.TitleBar.ButtonBackgroundColor = transparent;
                AppWindow.TitleBar.ButtonForegroundColor = foreground;
                AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBg;
                AppWindow.TitleBar.ButtonHoverForegroundColor = hoverFg;
                AppWindow.TitleBar.ButtonPressedBackgroundColor = hoverBg;
                AppWindow.TitleBar.ButtonPressedForegroundColor = hoverFg;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = transparent;
                AppWindow.TitleBar.ButtonInactiveForegroundColor = inactiveFg;
            }

            try
            {
                var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    AppWindow.SetIcon(iconPath);
                }
                else
                {
                    AppWindow.SetIcon("Assets/AppIcon.ico");
                }
            }
            catch { }

            // Set initial expanded size
            AppWindow.Resize(new SizeInt32(ExpandedWidth, ExpandedHeight));

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = false;
            }

            UpdateTitleBarPadding();
            UpdatePinVisualState();
            InitializeSettingsSelection();
            ((App)Application.Current).Settings.SettingsChanged += OnSettingsChanged;

            var app = (App)Application.Current;
            ApplyBackgroundTheme(app.Settings.Current.BackgroundTheme);
            if (app.CurrentAccentColor.A > 0)
            {
                UpdateDwmBorderColor(app.CurrentAccentColor);
            }

            MainDashboard.RequestToggleCollapse += (s, e) => ToggleCollapse();
            MainDashboard.RequestToggleAlwaysOnTop += (s, e) => OnPinClicked(PinButton, new RoutedEventArgs());
            MainDashboard.RequestToggleMiniOverlay += (s, e) => ToggleMiniOverlay();

            // Initialize MiniSpeedWindow with existing ViewModel and owner window handle
            _miniWindow = new MiniSpeedWindow(MainDashboard.ViewModel, _hwnd);
            _miniWindow.RequestRestoreMainWindow += (s, e) => RestoreMainWindow();

            MainDashboard.ViewModel.PropertyChanged += DashboardViewModel_PropertyChanged;
            UpdatePrivacyVisuals();

            // Update title bar padding on size and presenter changes
            AppWindow.Changed += (sender, args) =>
            {
                if (args.DidSizeChange || args.DidPresenterChange)
                {
                    UpdateTitleBarPadding();
                }
            };

            SizeChanged += (s, e) => UpdateTitleBarPadding();

            // Detect minimize / restore via Win32 subclassing for 100% reliability
            _subclassProc = new SubclassProc(WndProc);
            SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, UIntPtr.Zero);

            Closed += (s, e) =>
            {
                try
                {
                    MainDashboard.ViewModel.PropertyChanged -= DashboardViewModel_PropertyChanged;
                    _miniWindow?.HideMiniWindow();
                    _miniWindow?.Close();
                }
                catch { }
            };
        }

        private void UpdateTitleBarPadding()
        {
            try
            {
                uint dpi = GetDpiForWindow(_hwnd);
                double scale = dpi > 0 ? dpi / 96.0 : 1.0;
                double rightInsetDip = 0;

                if (AppWindow.TitleBar != null && AppWindow.TitleBar.RightInset > 0)
                {
                    rightInsetDip = AppWindow.TitleBar.RightInset / scale;
                }

                // Windows 11 caption buttons (Minimize, Maximize, Close) take ~138-144 DIPs.
                // Right padding must NEVER be less than 142 DIPs, ensuring buttons can never overlap Minimize.
                double rightPadding = Math.Max(rightInsetDip, 142);
                AppTitleBar.Padding = new Thickness(12, 0, rightPadding + 2, 0);
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
        {
            if (uMsg == WM_GETMINMAXINFO && lParam != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(hWnd);
                double scale = dpi > 0 ? dpi / 96.0 : 1.0;

                var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                int minWidth = (int)Math.Round(380 * scale);
                int minHeight = _isCollapsed ? (int)Math.Round(126 * scale) : (int)Math.Round(450 * scale);

                minMaxInfo.MinTrackSize = new Point(minWidth, minHeight);
                Marshal.StructureToPtr(minMaxInfo, lParam, false);
                return IntPtr.Zero;
            }

            if (uMsg == WM_SIZE)
            {
                int sizeType = wParam.ToInt32();

                if (sizeType == SIZE_MINIMIZED)
                {
                    DispatcherQueue.TryEnqueue(() => _miniWindow?.ShowMiniWindow());
                }
                else if (sizeType == SIZE_RESTORED || sizeType == SIZE_MAXIMIZED)
                {
                    if (!IsIconic(hWnd))
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            _miniWindow?.HideMiniWindow();
                            UpdateTitleBarPadding();
                        });
                    }
                }
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private bool _isMiniOverlayActive = false;

        private void ToggleMiniOverlay()
        {
            if (_miniWindow != null)
            {
                if (_isMiniOverlayActive)
                {
                    _miniWindow.HideMiniWindow();
                    _isMiniOverlayActive = false;
                }
                else
                {
                    _miniWindow.ShowMiniWindow();
                    _isMiniOverlayActive = true;
                }
            }
        }

        private void RestoreMainWindow()
        {
            _miniWindow?.HideMiniWindow();
            _isMiniOverlayActive = false;

            try
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Restore();
                }
                else
                {
                    ShowWindow(_hwnd, SW_RESTORE);
                }
                SetForegroundWindow(_hwnd);
                UpdateTitleBarPadding();
            }
            catch { }
        }

        private void OnPinClicked(object sender, RoutedEventArgs e)
        {
            _isAlwaysOnTop = !_isAlwaysOnTop;

            try
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsAlwaysOnTop = _isAlwaysOnTop;
                }
                else
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    SetWindowPos(hwnd, _isAlwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                }
            }
            catch { }

            UpdatePinVisualState();
        }

        private void UpdatePinVisualState()
        {
            if (_isAlwaysOnTop)
            {
                PinIcon.Glyph = "\uE840"; // PinFill
                PinIcon.Foreground = (SolidColorBrush)Application.Current.Resources["StatusLiveBrush"];
                PinActiveBackground.Opacity = 1.0;
                ToolTipService.SetToolTip(PinButton, "Always on top: On");
            }
            else
            {
                PinIcon.Glyph = "\uE718"; // PinOutline
                PinIcon.Foreground = (SolidColorBrush)Application.Current.Resources["TextMutedBrush"];
                PinActiveBackground.Opacity = 0.0;
                ToolTipService.SetToolTip(PinButton, "Always on top: Off");
            }

            MainDashboard.SetAlwaysOnTopState(_isAlwaysOnTop);
        }

        private void OnCollapseToggleClicked(object sender, RoutedEventArgs e)
        {
            ToggleCollapse();
        }

        public void UpdateDwmBorderColor(Windows.UI.Color color)
        {
            try
            {
                // Rely on subtle neutral dark window rim (0x00BBGGRR: RGB 30, 35, 40)
                // ensuring the border never becomes a distracting bright accent outline
                uint colorDarkRim = 0x0028231E;
                if (_hwnd != IntPtr.Zero)
                {
                    DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref colorDarkRim, sizeof(uint));
                }

                // Mini window must never have a visible DWM border
                _miniWindow?.SuppressDwmBorder();
            }
            catch { }
        }

        private void OnSettingsToggleClicked(object sender, RoutedEventArgs e)
        {
            if (SettingsOverlay.Visibility == Visibility.Visible)
            {
                CloseSettings();
            }
            else
            {
                OpenSettings();
            }
        }

        private void OnCloseSettingsClicked(object sender, RoutedEventArgs e)
        {
            CloseSettings();
        }

        private void OpenSettings()
        {
            if (_isCollapsed)
            {
                ToggleCollapse();
            }

            MainDashboard.Visibility = Visibility.Collapsed;
            SettingsOverlay.Visibility = Visibility.Visible;
            RefreshSettingsSelectionVisuals();
        }

        private void CloseSettings()
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            MainDashboard.Visibility = Visibility.Visible;
        }

        public void ApplyBackgroundTheme(BackgroundTheme theme)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RootWindowGrid.Background = NetStats.AppResources.BackgroundThemeHelper.CreateBackgroundBrush(theme);
            });
        }

        private void InitializeSettingsSelection()
        {
            RefreshSettingsSelectionVisuals();
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var app = (App)Application.Current;
                ApplyBackgroundTheme(app.Settings.Current.BackgroundTheme);
                RefreshSettingsSelectionVisuals();
                if (app.CurrentAccentColor.A > 0)
                {
                    UpdateDwmBorderColor(app.CurrentAccentColor);
                }
            });
        }

        private void RefreshSettingsSelectionVisuals()
        {
            var currentBg = MainDashboard.ViewModel.BackgroundTheme;
            UpdateBackgroundSwatchState(MidnightSwatch, MidnightCheck, currentBg == BackgroundTheme.Midnight);
            UpdateBackgroundSwatchState(ObsidianSwatch, ObsidianCheck, currentBg == BackgroundTheme.Obsidian);
            UpdateBackgroundSwatchState(DeepOceanSwatch, DeepOceanCheck, currentBg == BackgroundTheme.DeepOcean);
            UpdateBackgroundSwatchState(ArcticNightSwatch, ArcticNightCheck, currentBg == BackgroundTheme.ArcticNight);
            UpdateBackgroundSwatchState(IndigoNightSwatch, IndigoNightCheck, currentBg == BackgroundTheme.IndigoNight);
            UpdateBackgroundSwatchState(EmeraldNightSwatch, EmeraldNightCheck, currentBg == BackgroundTheme.EmeraldNight);
            UpdateBackgroundSwatchState(VioletNightSwatch, VioletNightCheck, currentBg == BackgroundTheme.VioletNight);
            UpdateBackgroundSwatchState(CosmicSwatch, CosmicCheck, currentBg == BackgroundTheme.Cosmic);
            UpdateBackgroundSwatchState(AuroraBackgroundSwatch, AuroraBackgroundCheck, currentBg == BackgroundTheme.Aurora);
            UpdateBackgroundSwatchState(EmberSwatch, EmberCheck, currentBg == BackgroundTheme.Ember);

            var currentTheme = MainDashboard.ViewModel.AccentTheme;
            UpdateAccentSwatchState(ElectricCyanSwatch, ElectricCyanCheck, currentTheme == AccentTheme.ElectricCyan);
            UpdateAccentSwatchState(CobaltBlueSwatch, CobaltBlueCheck, currentTheme == AccentTheme.CobaltBlue);
            UpdateAccentSwatchState(VioletSwatch, VioletCheck, currentTheme == AccentTheme.Violet);
            UpdateAccentSwatchState(MagentaSwatch, MagentaCheck, currentTheme == AccentTheme.Magenta);
            UpdateAccentSwatchState(CrimsonSwatch, CrimsonCheck, currentTheme == AccentTheme.Crimson);
            UpdateAccentSwatchState(AmberSwatch, AmberCheck, currentTheme == AccentTheme.Amber);
            UpdateAccentSwatchState(EmeraldSwatch, EmeraldCheck, currentTheme == AccentTheme.Emerald);
            UpdateAccentSwatchState(MintSwatch, MintCheck, currentTheme == AccentTheme.Mint);
            UpdateAccentSwatchState(CyberpunkSwatch, CyberpunkCheck, currentTheme == AccentTheme.Cyberpunk);
            UpdateAccentSwatchState(SunsetSwatch, SunsetCheck, currentTheme == AccentTheme.Sunset);
            UpdateAccentSwatchState(AuroraSwatch, AuroraCheck, currentTheme == AccentTheme.Aurora);

            var currentUnit = MainDashboard.ViewModel.SpeedUnit;
            UpdateUnitCardState(MegabytesUnitCard, MegabytesUnitCheck, currentUnit == SpeedUnit.MegabytesPerSecond);
            UpdateUnitCardState(MbpsUnitCard, MbpsUnitCheck, currentUnit == SpeedUnit.Mbps);

            AlwaysOnTopToggle.IsOn = _isAlwaysOnTop;
        }

        private static void UpdateBackgroundSwatchState(Button swatch, FontIcon checkIcon, bool isSelected)
        {
            var borderBrush = (Application.Current.Resources.TryGetValue("AccentBorderStrongBrush", out var b) ? b as Brush : null)
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x30, 0xC7, 0xDD));

            swatch.BorderBrush = isSelected
                ? borderBrush
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            checkIcon.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void UpdateAccentSwatchState(Button swatch, FontIcon checkIcon, bool isSelected)
        {
            var borderBrush = (Application.Current.Resources.TryGetValue("AccentBorderStrongBrush", out var b) ? b as Brush : null)
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x30, 0xC7, 0xDD));
            var surfaceBrush = (Application.Current.Resources.TryGetValue("AccentSurfaceStrongBrush", out var s) ? s as Brush : null)
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x30, 0x30, 0xC7, 0xDD));

            swatch.BorderBrush = isSelected
                ? borderBrush
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            swatch.Background = isSelected
                ? surfaceBrush
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            checkIcon.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void UpdateUnitCardState(Button card, FontIcon checkIcon, bool isSelected)
        {
            var surface = (Application.Current.Resources.TryGetValue("AccentSurfaceStrongBrush", out var s) ? s as Brush : null)
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x30, 0x30, 0xC7, 0xDD));
            var border = (Application.Current.Resources.TryGetValue("AccentBorderStrongBrush", out var b) ? b as Brush : null)
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x30, 0xC7, 0xDD));
            var defaultBg = (Application.Current.Resources.TryGetValue("AppBackgroundElevatedBrush", out var dbg) ? dbg as Brush : null)
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x14, 0x17, 0x22));
            var defaultBorder = (Application.Current.Resources.TryGetValue("AppBorderSubduedBrush", out var db) ? db as Brush : null)
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

            card.Background = isSelected ? surface : defaultBg;
            card.BorderBrush = isSelected ? border : defaultBorder;
            checkIcon.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnBackgroundCardClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string themeName } &&
                Enum.TryParse(themeName, out BackgroundTheme theme))
            {
                MainDashboard.ViewModel.SetBackgroundTheme(theme);
                ApplyBackgroundTheme(theme);
                RefreshSettingsSelectionVisuals();
            }
        }

        private void OnAccentCardClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string accentName } &&
                Enum.TryParse(accentName, out AccentTheme accentTheme))
            {
                MainDashboard.ViewModel.SetAccentTheme(accentTheme);
                RefreshSettingsSelectionVisuals();
            }
        }

        private void OnSpeedUnitCardClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string speedUnitName } &&
                Enum.TryParse(speedUnitName, out SpeedUnit speedUnit))
            {
                MainDashboard.ViewModel.SetSpeedUnit(speedUnit);
                RefreshSettingsSelectionVisuals();
            }
        }

        private void OnAlwaysOnTopToggled(object sender, RoutedEventArgs e)
        {
            if (_isAlwaysOnTop != AlwaysOnTopToggle.IsOn)
            {
                OnPinClicked(PinButton, new RoutedEventArgs());
            }
        }

        private void ToggleCollapse()
        {
            _isCollapsed = !_isCollapsed;
            MainDashboard.ViewModel.IsCollapsed = _isCollapsed;

            if (_isCollapsed)
            {
                CloseSettings();
                CollapseIcon.Glyph = "\uE70D"; // ChevronDown
                ToolTipService.SetToolTip(CollapseButton, "Expand instrument");
                AppWindow.Resize(new SizeInt32(CollapsedWidth, CollapsedHeight));
            }
            else
            {
                CollapseIcon.Glyph = "\uE70E"; // ChevronUp
                ToolTipService.SetToolTip(CollapseButton, "Collapse instrument");
                AppWindow.Resize(new SizeInt32(ExpandedWidth, ExpandedHeight));
            }
        }

        private void DashboardViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardViewModel.MicrophoneBrush) ||
                e.PropertyName == nameof(DashboardViewModel.IsMicrophoneActive) ||
                e.PropertyName == nameof(DashboardViewModel.CameraBrush) ||
                e.PropertyName == nameof(DashboardViewModel.IsCameraActive))
            {
                DispatcherQueue.TryEnqueue(UpdatePrivacyVisuals);
            }
        }

        private void UpdatePrivacyVisuals()
        {
            var vm = MainDashboard.ViewModel;
            MicIcon.Foreground = vm.MicrophoneBrush;
            CameraIcon.Foreground = vm.CameraBrush;
        }

        private void OnMicFlyoutOpening(object? sender, object? e)
        {
            var vm = MainDashboard.ViewModel;
            bool isActive = vm.IsMicrophoneActive;
            MicFlyoutStatus.Text = vm.MicrophoneStatusText;
            MicFlyoutStatus.Foreground = isActive
                ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                : (Brush)Application.Current.Resources["TextSecondaryBrush"];

            MicFlyoutAttributionLabel.Text = isActive ? "Currently using" : "Status";
            MicFlyoutAttributionText.Text = vm.MicrophoneAttribution;
        }

        private void OnCameraFlyoutOpening(object? sender, object? e)
        {
            var vm = MainDashboard.ViewModel;
            bool isActive = vm.IsCameraActive;
            CameraFlyoutStatus.Text = vm.CameraStatusText;
            CameraFlyoutStatus.Foreground = isActive
                ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                : (Brush)Application.Current.Resources["TextSecondaryBrush"];

            CameraFlyoutAttributionLabel.Text = isActive ? "Currently using" : "Status";
            CameraFlyoutAttributionText.Text = vm.CameraAttribution;
        }

        private void OnOpenMicPrivacySettingsClicked(object sender, RoutedEventArgs e)
        {
            MicFlyout.Hide();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:privacy-microphone",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void OnOpenCameraPrivacySettingsClicked(object sender, RoutedEventArgs e)
        {
            CameraFlyout.Hide();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:privacy-webcam",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private bool _isEmailCopied = false;

        private void OnCopyEmailPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isEmailCopied)
            {
                CopyEmailIcon.Foreground = (Brush)Application.Current.Resources["AccentPrimaryBrush"];
                CopyEmailLabel.Foreground = (Brush)Application.Current.Resources["AccentPrimaryBrush"];
                CopyEmailButton.BorderBrush = (Brush)Application.Current.Resources["AccentBorderBrush"];
            }
        }

        private void OnCopyEmailPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isEmailCopied)
            {
                CopyEmailIcon.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
                CopyEmailLabel.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
                CopyEmailButton.BorderBrush = (Brush)Application.Current.Resources["AppBorderSubduedBrush"];
            }
        }

        private void OnCopyEmailClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                const string email = "shauryaofficialx@gmail.com";
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                dataPackage.SetText(email);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                _isEmailCopied = true;
                CopyEmailIcon.Glyph = "\uE73E"; // CheckMark
                CopyEmailIcon.Foreground = (Brush)Application.Current.Resources["AccentPrimaryBrush"];
                CopyEmailLabel.Text = "Copied";
                CopyEmailLabel.Foreground = (Brush)Application.Current.Resources["AccentPrimaryBrush"];
                CopyEmailButton.BorderBrush = (Brush)Application.Current.Resources["AccentBorderBrush"];
                CopyEmailButton.Background = (Brush)Application.Current.Resources["AccentSurfaceBrush"];

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    _isEmailCopied = false;
                    CopyEmailIcon.Glyph = "\uE8C8"; // Copy icon
                    CopyEmailIcon.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
                    CopyEmailLabel.Text = "Copy";
                    CopyEmailLabel.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
                    CopyEmailButton.BorderBrush = (Brush)Application.Current.Resources["AppBorderSubduedBrush"];
                    CopyEmailButton.Background = (Brush)Application.Current.Resources["AppBackgroundSecondaryCardBrush"];
                };
                timer.Start();
            }
            catch { }
        }
    }
}

