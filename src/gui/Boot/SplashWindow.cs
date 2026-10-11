using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuantumVault
{
    public sealed class SplashWindow : Window
    {
        private const double TrackWidth = 440;
        private readonly Border _fill = new Border();
        private readonly TextBlock _status = new TextBlock();

        public SplashWindow()
        {
            Title = "Quantum Vault";
            Width = 720;
            Height = 540;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BuildBackground();

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            stack.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/quantum-suite-logo.jpg", UriKind.Absolute)),
                Height = 300,
                Stretch = Stretch.Uniform,
                OpacityMask = BuildEdgeMask()
            });

            stack.Children.Add(new TextBlock
            {
                Text = "QUANTUM SUITE ORG",
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            });

            var track = new Grid
            {
                Width = TrackWidth,
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 0)
            };
            track.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(2)
            });
            _fill.CornerRadius = new CornerRadius(2);
            _fill.HorizontalAlignment = HorizontalAlignment.Left;
            _fill.Width = 0;
            _fill.Background = new LinearGradientBrush(
                Color.FromRgb(0x00, 0xC8, 0xE0), Color.FromRgb(0x7C, 0x3A, 0xED),
                new Point(0, 0), new Point(1, 0));
            track.Children.Add(_fill);
            stack.Children.Add(track);

            _status.Text = "Starting...";
            _status.FontSize = 12;
            _status.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xCF, 0xEE));
            _status.HorizontalAlignment = HorizontalAlignment.Center;
            _status.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(_status);

            Content = stack;
        }

        public void Report(double fraction, string message)
        {
            fraction = Math.Max(0, Math.Min(1, fraction));
            _fill.Width = TrackWidth * fraction;
            _status.Text = message ?? "";
        }

        private static Brush BuildBackground()
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0x06, 0x12, 0x3F), 0.0));
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x0C, 0x63), 0.55));
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x2F, 0x7A), 1.0));
            return g;
        }

        // Fades the JPG's black square corners into the gradient so only the round logo shows.
        private static Brush BuildEdgeMask()
        {
            var m = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            m.GradientStops.Add(new GradientStop(Colors.White, 0.0));
            m.GradientStops.Add(new GradientStop(Colors.White, 0.92));
            m.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
            return m;
        }
    }
}
