using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NetStats.UI.ViewModels;

namespace NetStats.UI.Views
{
    public sealed partial class DashboardView : Page
    {
        private enum ResponsiveTier
        {
            Compact,
            Standard,
            Large
        }

        private ResponsiveTier? _responsiveTier;

        public DashboardViewModel ViewModel { get; }

        public event EventHandler? RequestToggleCollapse;
        public event EventHandler? RequestOpenDetails;
        public event EventHandler? RequestToggleAlwaysOnTop;
        public event EventHandler? RequestToggleMiniOverlay;

        public DashboardView()
        {
            ViewModel = new DashboardViewModel(((NetStats.App)Application.Current).Settings);
            InitializeComponent();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void OnDashboardLoaded(object sender, RoutedEventArgs e)
        {
            ApplyResponsiveLayout(force: true);
        }

        private void OnDashboardSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout(force: false);
        }

        private void ApplyResponsiveLayout(bool force)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            double standardBreakpoint = GetResourceDouble("ResponsiveStandardBreakpoint");
            double largeBreakpoint = GetResourceDouble("ResponsiveLargeBreakpoint");
            ResponsiveTier tier = (ActualWidth >= largeBreakpoint || ActualHeight >= 630)
                ? ResponsiveTier.Large
                : (ActualWidth >= standardBreakpoint || ActualHeight >= 550)
                    ? ResponsiveTier.Standard
                    : ResponsiveTier.Compact;

            if (!force && _responsiveTier == tier)
            {
                return;
            }

            bool tierChanged = _responsiveTier.HasValue && _responsiveTier.Value != tier;
            _responsiveTier = tier;

            // Integrated Quick Controls Rail: Reveal on wider windows (>=480px),
            // while preserving full width for Active Applications on compact windows (<480px).
            if (ActualWidth >= 480)
            {
                ActivityColumn.Width = new GridLength(1, GridUnitType.Star);
                QuickControlsColumn.Width = new GridLength(160);
                QuickControlsModule.Visibility = Visibility.Visible;
            }
            else
            {
                ActivityColumn.Width = new GridLength(1, GridUnitType.Star);
                QuickControlsColumn.Width = new GridLength(0);
                QuickControlsModule.Visibility = Visibility.Collapsed;
            }

            if (tier == ResponsiveTier.Large)
            {
                ExpandedRoot.Padding = new Thickness(14, 10, 14, 10);
                HeroSignalField.Height = GetResourceDouble("LargeHeroSignalHeight");
                DownloadValueText.FontSize = GetResourceDouble("LargeHeroValueSize");
                UploadValueText.FontSize = GetResourceDouble("LargeHeroValueSize");
                DownloadUnitText.FontSize = 13;
                UploadUnitText.FontSize = 13;

                InspectRoot.Padding = new Thickness(14, 10, 14, 10);
                InspectTitle.FontSize = 13;
                InspectSubtitle.FontSize = 9;
            }
            else if (tier == ResponsiveTier.Standard)
            {
                ExpandedRoot.Padding = new Thickness(10, 6, 10, 6);
                HeroSignalField.Height = GetResourceDouble("StandardHeroSignalHeight");
                DownloadValueText.FontSize = GetResourceDouble("StandardHeroValueSize");
                UploadValueText.FontSize = GetResourceDouble("StandardHeroValueSize");
                DownloadUnitText.FontSize = 11;
                UploadUnitText.FontSize = 11;

                InspectRoot.Padding = new Thickness(10, 6, 10, 6);
                InspectTitle.FontSize = 12;
                InspectSubtitle.FontSize = 9;
            }
            else
            {
                ExpandedRoot.Padding = new Thickness(8, 4, 8, 4);
                HeroSignalField.Height = GetResourceDouble("CompactHeroSignalHeight");
                DownloadValueText.FontSize = GetResourceDouble("CompactHeroValueSize");
                UploadValueText.FontSize = GetResourceDouble("CompactHeroValueSize");
                DownloadUnitText.FontSize = 10;
                UploadUnitText.FontSize = 10;

                InspectRoot.Padding = new Thickness(8, 4, 8, 4);
                InspectTitle.FontSize = 11;
                InspectSubtitle.FontSize = 8;
            }

            if (tierChanged)
            {
                AnimateHeroSettle();
            }
        }

        private static double GetResourceDouble(string key) =>
            (double)Application.Current.Resources[key];

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardViewModel.HasInspectGroups))
            {
                DispatcherQueue.TryEnqueue(() => ApplyResponsiveLayout(force: true));
            }
            else if (e.PropertyName == nameof(DashboardViewModel.DownloadSpeed))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (DownloadAtmosphereBloom != null)
                    {
                        double speedMb = ViewModel.DownloadSpeed / (1024.0 * 1024.0);
                        DownloadAtmosphereBloom.Opacity = Math.Min(0.85, 0.55 + (speedMb / 15.0) * 0.30);
                    }
                });
            }
            else if (e.PropertyName == nameof(DashboardViewModel.UploadSpeed))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (UploadAtmosphereBloom != null)
                    {
                        double speedMb = ViewModel.UploadSpeed / (1024.0 * 1024.0);
                        UploadAtmosphereBloom.Opacity = Math.Min(0.85, 0.55 + (speedMb / 8.0) * 0.30);
                    }
                });
            }
        }

        private void AnimateHeroSettle()
        {
            var animation = new DoubleAnimation
            {
                From = 0.94,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, ExpandedRoot);
            Storyboard.SetTargetProperty(animation, "Opacity");
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        private void OnDetailsClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenInspect();
            RequestOpenDetails?.Invoke(this, EventArgs.Empty);
            UpdateTabVisuals();
        }

        private void OnBackClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.CloseInspect();
        }

        private void OnActivityTabClicked(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsActivityView)
            {
                ViewModel.IsActivityView = true;
                UpdateTabVisuals();
            }
        }

        private void OnDetailsTabClicked(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsDetailsView)
            {
                ViewModel.IsDetailsView = true;
                UpdateTabVisuals();
            }
        }

        private void UpdateTabVisuals()
        {
            bool isActivity = ViewModel.IsActivityView;
            var activeSurface = (Application.Current.Resources.TryGetValue("AccentSurfaceStrongBrush", out var s) ? s as Brush : null)
                ?? (Brush)Application.Current.Resources["AccentPrimaryBrush"];
            var transparent = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

            ActivityTabLabel.Foreground = isActivity
                ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                : (Brush)Application.Current.Resources["TextMutedBrush"];
            ActivityTabIndicator.Background = isActivity ? activeSurface : transparent;

            DetailsTabLabel.Foreground = !isActivity
                ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                : (Brush)Application.Current.Resources["TextMutedBrush"];
            DetailsTabIndicator.Background = !isActivity ? activeSurface : transparent;
        }

        private void OnExpandClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.IsCollapsed = false;
            RequestToggleCollapse?.Invoke(this, EventArgs.Empty);
        }

        private void OnCollapsedCardTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ViewModel.IsCollapsed = false;
            RequestToggleCollapse?.Invoke(this, EventArgs.Empty);
        }

        private void OnToggleFreezeClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.IsInspectFrozen = !ViewModel.IsInspectFrozen;
        }

        private void OnCopyEndpointClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string endpoint && !string.IsNullOrWhiteSpace(endpoint))
            {
                try
                {
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                    dataPackage.SetText(endpoint);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                    if (button.Content is FontIcon icon)
                    {
                        icon.Glyph = "\uE73E"; // CheckMark
                        var origForeground = icon.Foreground;
                        if (Application.Current.Resources.TryGetValue("AccentPrimaryBrush", out var accent) && accent is Brush ab)
                        {
                            icon.Foreground = ab;
                        }
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                        timer.Tick += (s, args) =>
                        {
                            timer.Stop();
                            icon.Glyph = "\uE8C8"; // Copy
                            icon.Foreground = origForeground;
                        };
                        timer.Start();
                    }
                }
                catch { }
            }
        }

        public void SetAlwaysOnTopState(bool isAlwaysOnTop)
        {
            if (QuickPinIcon != null && QuickPinStatusText != null && QuickPinStatusBadge != null)
            {
                if (isAlwaysOnTop)
                {
                    QuickPinIcon.Glyph = "\uE840"; // PinFill
                    QuickPinIcon.Foreground = (Brush)Application.Current.Resources["StatusLiveBrush"];
                    QuickPinStatusText.Text = "ON";
                    QuickPinStatusText.Foreground = (Brush)Application.Current.Resources["StatusLiveBrush"];
                    QuickPinStatusBadge.Background = (Brush)Application.Current.Resources["PinActiveSurfaceBrush"];
                }
                else
                {
                    QuickPinIcon.Glyph = "\uE718"; // PinOutline
                    QuickPinIcon.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
                    QuickPinStatusText.Text = "OFF";
                    QuickPinStatusText.Foreground = (Brush)Application.Current.Resources["TextMutedBrush"];
                    QuickPinStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                }
            }
        }

        private void OnQuickPinClicked(object sender, RoutedEventArgs e)
        {
            RequestToggleAlwaysOnTop?.Invoke(this, EventArgs.Empty);
        }

        private void OnQuickMiniClicked(object sender, RoutedEventArgs e)
        {
            RequestToggleMiniOverlay?.Invoke(this, EventArgs.Empty);
        }
    }
}
