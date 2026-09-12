using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NetStats.UI.Controls
{
    public sealed partial class FlowVisualizer : UserControl
    {
        public static readonly DependencyProperty DownloadSpeedProperty =
            DependencyProperty.Register(nameof(DownloadSpeed), typeof(double), typeof(FlowVisualizer), new PropertyMetadata(0.0, OnSpeedChanged));

        public static readonly DependencyProperty UploadSpeedProperty =
            DependencyProperty.Register(nameof(UploadSpeed), typeof(double), typeof(FlowVisualizer), new PropertyMetadata(0.0, OnSpeedChanged));

        public double DownloadSpeed
        {
            get => (double)GetValue(DownloadSpeedProperty);
            set => SetValue(DownloadSpeedProperty, value);
        }

        public double UploadSpeed
        {
            get => (double)GetValue(UploadSpeedProperty);
            set => SetValue(UploadSpeedProperty, value);
        }

        private const int MaxHistory = 36;
        private readonly List<double> _downloadHistory = new();
        private readonly List<double> _uploadHistory = new();
        private double _smoothedMax = 40.0;

        public FlowVisualizer()
        {
            InitializeComponent();

            for (int i = 0; i < MaxHistory; i++)
            {
                _downloadHistory.Add(0);
                _uploadHistory.Add(0);
            }
        }

        private static void OnSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FlowVisualizer visualizer)
            {
                visualizer.UpdateHistory();
            }
        }

        private void UpdateHistory()
        {
            _downloadHistory.Add(DownloadSpeed);
            if (_downloadHistory.Count > MaxHistory) _downloadHistory.RemoveAt(0);

            _uploadHistory.Add(UploadSpeed);
            if (_uploadHistory.Count > MaxHistory) _uploadHistory.RemoveAt(0);

            Redraw();
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Redraw();
        }

        private void Redraw()
        {
            var width = ActualWidth;
            var height = ActualHeight;

            if (width <= 0 || height < 15.0) return;

            var rawMax = Math.Max(25.0, _downloadHistory.Concat(_uploadHistory).Max());
            // Smooth out vertical scale changes for a calm, controlled instrument feel
            _smoothedMax = _smoothedMax * 0.88 + rawMax * 0.12;
            var effectiveMax = Math.Max(15.0, _smoothedMax);

            // Render Download Streams (green)
            RenderStream(
                _downloadHistory,
                effectiveMax,
                width,
                height,
                DownloadPolyline,
                DownloadAreaPolygon,
                DownloadBeacon,
                DownloadBeaconAura
            );

            // Render Upload Streams (blue)
            RenderStream(
                _uploadHistory,
                effectiveMax,
                width,
                height,
                UploadPolyline,
                UploadAreaPolygon,
                UploadBeacon,
                UploadBeaconAura
            );
        }

        private void RenderStream(
            List<double> history,
            double maxVal,
            double width,
            double height,
            Microsoft.UI.Xaml.Shapes.Polyline linePolyline,
            Microsoft.UI.Xaml.Shapes.Polygon areaPolygon,
            Microsoft.UI.Xaml.Shapes.Ellipse beacon,
            Microsoft.UI.Xaml.Shapes.Ellipse beaconAura)
        {
            if (history.Count < 2) return;

            double topMargin = 7.0;
            double bottomMargin = 5.0;
            if (height <= (topMargin + bottomMargin) || width <= 0) return;

            linePolyline.Points.Clear();
            areaPolygon.Points.Clear();

            int count = history.Count;
            var stepX = width / (count - 1);
            var smoothedPoints = new PointCollection();

            double usableHeight = Math.Max(1.0, height - topMargin - bottomMargin);
            double maxY = Math.Max(topMargin, height - bottomMargin);
            double minY = Math.Min(topMargin, maxY);

            double ComputeY(double value)
            {
                double normalized = Math.Clamp(value / maxVal, 0.0, 1.0);
                double y = maxY - (normalized * usableHeight);
                return Math.Clamp(y, minY, maxY);
            }

            double startY = ComputeY(history[0]);
            smoothedPoints.Add(new Point(0, startY));

            for (int i = 1; i < count; i++)
            {
                double x = i * stepX;
                double y = ComputeY(history[i]);

                double prevX = (i - 1) * stepX;
                double prevY = ComputeY(history[i - 1]);

                double cp1X = prevX + (stepX / 2.0);
                double cp1Y = prevY;
                double cp2X = x - (stepX / 2.0);
                double cp2Y = y;

                for (int step = 1; step <= 4; step++)
                {
                    double t = step / 4.0;
                    double omt = 1.0 - t;

                    double px = (omt * omt * omt * prevX) + (3 * omt * omt * t * cp1X) + (3 * omt * t * t * cp2X) + (t * t * t * x);
                    double py = (omt * omt * omt * prevY) + (3 * omt * omt * t * cp1Y) + (3 * omt * t * t * cp2Y) + (t * t * t * y);

                    smoothedPoints.Add(new Point(px, py));
                }
            }

            // Apply smoothed signal line points
            foreach (var pt in smoothedPoints)
            {
                linePolyline.Points.Add(pt);
                areaPolygon.Points.Add(pt);
            }

            // Close the area polygon at baseline
            if (smoothedPoints.Count > 0)
            {
                var lastPt = smoothedPoints.Last();
                var firstPt = smoothedPoints.First();
                
                areaPolygon.Points.Add(new Point(lastPt.X, height));
                areaPolygon.Points.Add(new Point(firstPt.X, height));
            }

            // Update Leading Beacon & Idle State
            if (smoothedPoints.Count > 0)
            {
                var lastPoint = smoothedPoints.Last();
                
                Canvas.SetLeft(beacon, lastPoint.X - (beacon.Width / 2));
                Canvas.SetTop(beacon, lastPoint.Y - (beacon.Height / 2));
                
                Canvas.SetLeft(beaconAura, lastPoint.X - (beaconAura.Width / 2));
                Canvas.SetTop(beaconAura, lastPoint.Y - (beaconAura.Height / 2));
                
                bool isActive = history.Last() > 0.8;
                beacon.Opacity = isActive ? 1.0 : 0.0;
                beaconAura.Opacity = isActive ? 0.35 : 0.0;
                areaPolygon.Opacity = isActive ? 1.0 : 0.2;
            }
        }
    }
}
