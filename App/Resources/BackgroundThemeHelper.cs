using System;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using NetStats.Core.Settings;

namespace NetStats.AppResources
{
    public static class BackgroundThemeHelper
    {
        public static Brush CreateBackgroundBrush(BackgroundTheme theme)
        {
            return theme switch
            {
                BackgroundTheme.Obsidian => new SolidColorBrush(Color.FromArgb(0xA0, 0x05, 0x06, 0x08)),
                BackgroundTheme.DeepOcean => CreateGradient(
                    Color.FromArgb(0x94, 0x08, 0x1C, 0x30),
                    Color.FromArgb(0x75, 0x05, 0x0F, 0x1C)),
                BackgroundTheme.ArcticNight => CreateGradient(
                    Color.FromArgb(0x8C, 0x0F, 0x22, 0x32),
                    Color.FromArgb(0x70, 0x09, 0x15, 0x22)),
                BackgroundTheme.IndigoNight => CreateGradient(
                    Color.FromArgb(0x94, 0x16, 0x1A, 0x46),
                    Color.FromArgb(0x75, 0x0B, 0x0D, 0x26)),
                BackgroundTheme.EmeraldNight => CreateGradient(
                    Color.FromArgb(0x94, 0x08, 0x28, 0x1B),
                    Color.FromArgb(0x75, 0x04, 0x14, 0x0D)),
                BackgroundTheme.VioletNight => CreateGradient(
                    Color.FromArgb(0x94, 0x26, 0x0E, 0x3C),
                    Color.FromArgb(0x75, 0x0F, 0x06, 0x19)),
                BackgroundTheme.Cosmic => CreateGradient(
                    Color.FromArgb(0x98, 0x30, 0x0F, 0x38),
                    Color.FromArgb(0x75, 0x0E, 0x12, 0x2C)),
                BackgroundTheme.Aurora => CreateGradient(
                    Color.FromArgb(0x8C, 0x06, 0x32, 0x34),
                    Color.FromArgb(0x70, 0x04, 0x1B, 0x17)),
                BackgroundTheme.Ember => CreateGradient(
                    Color.FromArgb(0x94, 0x36, 0x10, 0x16),
                    Color.FromArgb(0x75, 0x16, 0x07, 0x0B)),
                _ => new SolidColorBrush(Color.FromArgb(0x88, 0x0A, 0x0D, 0x15)) // Midnight default
            };
        }

        private static LinearGradientBrush CreateGradient(Color startColor, Color endColor)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop { Color = startColor, Offset = 0.0 });
            brush.GradientStops.Add(new GradientStop { Color = endColor, Offset = 1.0 });
            return brush;
        }
    }
}
