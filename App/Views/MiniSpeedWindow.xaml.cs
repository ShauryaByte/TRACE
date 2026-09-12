using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NetStats.UI.ViewModels;

namespace NetStats.UI.Views
{
    public sealed partial class MiniSpeedWindow : Window
    {
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        public event EventHandler? RequestRestoreMainWindow;

        private readonly DashboardViewModel _viewModel;
        private readonly IntPtr _hwnd;
        private readonly IntPtr _ownerHwnd;

        public IntPtr GetHwnd() => _hwnd;

        private bool _isMiniVisible;
        private bool _isPointerPressed;
        private bool _isDragging;
        private POINT _pointerDownPosition;
        private RECT _dragStartBounds;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        // DWM APIs for border and shadow control
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        private const uint SPI_GETWORKAREA = 0x0030;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_CXDRAG = 68;
        private const int SM_CYDRAG = 69;
        private const int GWL_EXSTYLE = -20;
        private const int GWLP_HWNDPARENT = -8;
        private const long WS_EX_TOOLWINDOW = 0x00000080L;
        private const long WS_EX_APPWINDOW = 0x00040000L;
        private const long WS_EX_TOPMOST = 0x00000008L;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint MONITOR_DEFAULTTONULL = 0;
        private const uint MONITOR_DEFAULTTOPRIMARY = 1;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new(-1);

        // DWM attribute constants
        private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const uint DWMWA_BORDER_COLOR = 34;
        private const uint DWMWCP_ROUND = 2;
        private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

        public MiniSpeedWindow(DashboardViewModel viewModel, IntPtr ownerHwnd = default)
        {
            _viewModel = viewModel;
            _ownerHwnd = ownerHwnd;
            InitializeComponent();
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            try
            {
                AppWindow.IsShownInSwitchers = false;
            }
            catch
            {
            }

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
                presenter.IsAlwaysOnTop = true;
            }

            ApplyBorderlessStyles();

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_isMiniVisible ||
                (e.PropertyName != nameof(DashboardViewModel.DownloadSpeed) &&
                 e.PropertyName != nameof(DashboardViewModel.UploadSpeed) &&
                 e.PropertyName != nameof(DashboardViewModel.SpeedUnitLabel) &&
                 e.PropertyName != nameof(DashboardViewModel.DownloadValueString) &&
                 e.PropertyName != nameof(DashboardViewModel.UploadValueString) &&
                 e.PropertyName != nameof(DashboardViewModel.MicrophoneBrush) &&
                 e.PropertyName != nameof(DashboardViewModel.CameraBrush) &&
                 e.PropertyName != nameof(DashboardViewModel.IsMicrophoneActive) &&
                 e.PropertyName != nameof(DashboardViewModel.IsCameraActive)))
            {
                return;
            }

            UpdateTelemetryText();
        }

        public void ShowMiniWindow()
        {
            if (_isMiniVisible)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                return;
            }

            _isMiniVisible = true;
            UpdateTelemetryText();
            bool savedValid = PositionAtSavedLocation();
            if (!savedValid)
            {
                PositionAdjacentToTaskbar();
            }

            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            ApplyBorderlessStyles();
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public void HideMiniWindow()
        {
            if (!_isMiniVisible)
            {
                return;
            }

            _isMiniVisible = false;
            ShowWindow(_hwnd, SW_HIDE);
        }

        /// <summary>
        /// Update the DWM border color. Called from MainWindow when accent changes.
        /// For the mini window, we always suppress the border by matching the dark background.
        /// </summary>
        public void SuppressDwmBorder()
        {
            ApplyBorderlessStyles();
        }

        private void ApplyBorderlessStyles()
        {
            if (_hwnd == IntPtr.Zero) return;
            try
            {
                const int GWL_STYLE = -16;
                long style = GetWindowLongPtr(_hwnd, GWL_STYLE).ToInt64();
                // Strip WS_CAPTION (0x00C00000), WS_BORDER (0x00800000), WS_DLGFRAME (0x00400000), WS_THICKFRAME (0x00040000)
                style &= ~(0x00C00000L | 0x00800000L | 0x00400000L | 0x00040000L);
                SetWindowLongPtr(_hwnd, GWL_STYLE, new IntPtr(style));

                const int GWL_EXSTYLE = -20;
                long exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
                exStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                // Strip WS_EX_APPWINDOW and all 3D window edge / dialog frame styles:
                // WS_EX_WINDOWEDGE (0x00000100), WS_EX_CLIENTEDGE (0x00000200), WS_EX_DLGMODALFRAME (0x00000001), WS_EX_STATICEDGE (0x00020000)
                exStyle &= ~(WS_EX_APPWINDOW | 0x00000100L | 0x00000200L | 0x00000001L | 0x00020000L);
                SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

                const uint SWP_FRAMECHANGED = 0x0020;
                const uint SWP_NOZORDER = 0x0004;
                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

                ApplyDwmBorderSuppression();
            }
            catch { }
        }

        private void ApplyDwmBorderSuppression()
        {
            if (_hwnd == IntPtr.Zero) return;
            try
            {
                // 1. Force the DWM window border color to exactly match the dark client background
                // (0x00181311 = COLORREF for #111318). This seamlessly eliminates any visible white/light/cyan outline.
                uint darkBorderColor = 0x00181311;
                DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref darkBorderColor, sizeof(uint));

                // 2. Ask DWM for native rounded window corners
                uint cornerPref = DWMWCP_ROUND;
                DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(uint));
            }
            catch { }
        }

        private void UpdateTelemetryText()
        {
            DownloadValueText.Text = _viewModel.DownloadValueString;
            UploadValueText.Text = _viewModel.UploadValueString;
            SharedUnitText.Text = _viewModel.SpeedUnitLabel;
            MiniMicIcon.Foreground = _viewModel.MicrophoneBrush;
            MiniCameraIcon.Foreground = _viewModel.CameraBrush;

            if (_isMiniVisible && _hwnd != IntPtr.Zero)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        private bool PositionAtSavedLocation()
        {
            int? savedX = _viewModel.Settings.Current.MiniWindowX;
            int? savedY = _viewModel.Settings.Current.MiniWindowY;
            if (!savedX.HasValue || !savedY.HasValue)
            {
                return false;
            }

            var (width, height) = GetWindowSizePixels();
            var savedBounds = new RECT { Left = savedX.Value, Top = savedY.Value, Right = savedX.Value + width, Bottom = savedY.Value + height };
            IntPtr monitor = MonitorFromRect(ref savedBounds, MONITOR_DEFAULTTONULL);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            int x = savedX.Value;
            int y = savedY.Value;

            // Keep the restored window inside the target monitor's work area so it is
            // never placed underneath the taskbar or off-screen. The user's saved spot
            // is honored whenever it is still valid.
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            bool gotInfo = GetMonitorInfo(monitor, ref monitorInfo);
            if (gotInfo)
            {
                int minX = monitorInfo.rcWork.Left;
                int maxX = monitorInfo.rcWork.Right - width;
                int minY = monitorInfo.rcWork.Top;
                int maxY = monitorInfo.rcWork.Bottom - height;

                if (maxX >= minX)
                {
                    x = Math.Clamp(x, minX, maxX);
                }
                if (maxY >= minY)
                {
                    y = Math.Clamp(y, minY, maxY);
                }
            }

            try { AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height)); } catch { }
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            return true;
        }

        private void PositionAdjacentToTaskbar()
        {
            try
            {
                IntPtr targetHwnd = _ownerHwnd != IntPtr.Zero ? _ownerHwnd : _hwnd;
                IntPtr monitor = MonitorFromWindow(targetHwnd, MONITOR_DEFAULTTONEAREST);

                RECT workArea;
                RECT monitorRect;

                var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
                {
                    workArea = monitorInfo.rcWork;
                    monitorRect = monitorInfo.rcMonitor;
                }
                else
                {
                    SystemParametersInfo(SPI_GETWORKAREA, 0, out workArea, 0);
                    int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                    int screenHeight = GetSystemMetrics(SM_CYSCREEN);
                    monitorRect = new RECT { Left = 0, Top = 0, Right = screenWidth, Bottom = screenHeight };
                }

                uint dpi = GetDpiForWindow(_hwnd);
                if (dpi == 0 && _ownerHwnd != IntPtr.Zero)
                {
                    dpi = GetDpiForWindow(_ownerHwnd);
                }
                float scale = (dpi == 0 ? 96 : dpi) / 96.0f;
                var (windowWidth, windowHeight) = GetWindowSizePixels();

                // Minimal gap: approximately 0-2 logical pixels above the bottom edge of the usable work area
                int minimalGapY = Math.Max(1, (int)Math.Round(1.0f * scale));
                int marginX = (int)Math.Round(16.0f * scale);

                // Conceptual taskbar-adjacent calculation:
                // miniBottom = workArea.Bottom - minimalGap
                // miniTop = miniBottom - actualMiniHeight
                int x = workArea.Right - windowWidth - marginX;
                int y = workArea.Bottom - windowHeight - minimalGapY;

                // Adapt for non-standard taskbar docks (top, left, right)
                if (workArea.Top > monitorRect.Top)
                {
                    y = workArea.Top + minimalGapY;
                }

                if (workArea.Left > monitorRect.Left)
                {
                    x = workArea.Left + minimalGapY;
                }
                else if (workArea.Right < monitorRect.Right)
                {
                    x = workArea.Right - windowWidth - minimalGapY;
                }

                x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - windowWidth));
                y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - windowHeight));

                try { AppWindow.Resize(new Windows.Graphics.SizeInt32(windowWidth, windowHeight)); } catch { }
                SetWindowPos(_hwnd, HWND_TOPMOST, x, y, windowWidth, windowHeight, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch
            {
            }
        }

        private (int Width, int Height) GetWindowSizePixels()
        {
            uint dpi = GetDpiForWindow(_hwnd);
            if (dpi == 0 && _ownerHwnd != IntPtr.Zero)
            {
                dpi = GetDpiForWindow(_ownerHwnd);
            }
            float scale = (dpi == 0 ? 96 : dpi) / 96.0f;
            return ((int)Math.Round(204 * scale), (int)Math.Round(36 * scale));
        }

        private void OnWindowPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            POINT pointerDownPosition = default;
            RECT dragStartBounds = default;
            if (!GetCursorPos(out pointerDownPosition) || !GetWindowRect(_hwnd, out dragStartBounds))
            {
                return;
            }

            _pointerDownPosition = pointerDownPosition;
            _dragStartBounds = dragStartBounds;
            _isPointerPressed = true;
            _isDragging = false;
            RootBorder.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnWindowPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            POINT currentPosition = default;
            if (!_isPointerPressed || !GetCursorPos(out currentPosition))
            {
                return;
            }

            int deltaX = currentPosition.X - _pointerDownPosition.X;
            int deltaY = currentPosition.Y - _pointerDownPosition.Y;
            if (!_isDragging)
            {
                int thresholdX = Math.Max(2, GetSystemMetrics(SM_CXDRAG) / 2);
                int thresholdY = Math.Max(2, GetSystemMetrics(SM_CYDRAG) / 2);
                if (Math.Abs(deltaX) < thresholdX && Math.Abs(deltaY) < thresholdY)
                {
                    return;
                }

                _isDragging = true;
            }

            SetWindowPos(_hwnd, HWND_TOPMOST, _dragStartBounds.Left + deltaX, _dragStartBounds.Top + deltaY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            e.Handled = true;
        }

        private void OnWindowPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPointerPressed)
            {
                return;
            }

            bool wasDragging = _isDragging;
            _isPointerPressed = false;
            _isDragging = false;
            RootBorder.ReleasePointerCapture(e.Pointer);

            if (wasDragging && GetWindowRect(_hwnd, out RECT bounds))
            {
                _viewModel.Settings.SetMiniWindowPosition(bounds.Left, bounds.Top);
            }
            else if (!wasDragging)
            {
                RequestRestoreMainWindow?.Invoke(this, EventArgs.Empty);
            }

            e.Handled = true;
        }

        private void OnWindowPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isPointerPressed = false;
            _isDragging = false;
        }
    }
}
