using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace QuantumVault
{
    // ── NavIconButton ────────────────────────────────────────────────────────
    public class NavIconButton : Button
    {
        public static readonly DependencyProperty NavIconProperty =
            DependencyProperty.Register("NavIcon", typeof(PackIconMaterialKind),
                typeof(NavIconButton), new PropertyMetadata(PackIconMaterialKind.Circle, OnChanged));
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register("IsActive", typeof(bool),
                typeof(NavIconButton), new PropertyMetadata(false, OnChanged));

        public PackIconMaterialKind NavIcon { get => (PackIconMaterialKind)GetValue(NavIconProperty); set => SetValue(NavIconProperty, value); }
        public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

        private Border  _bg;
        private Border  _accent;
        private PackIconMaterial _ico;

        public NavIconButton()
        {
            Width  = 56;
            Height = 44;
            Margin = new Thickness(0, 1, 0, 1);
            Background    = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            Cursor = System.Windows.Input.Cursors.Hand;
            FocusVisualStyle = null;
            Loaded += (s, e) => Build();
        }

        private void Build()
        {
            _accent = new Border
            {
                Width  = 3,
                Background   = R<Brush>("QV.Brush.Cyan"),
                CornerRadius = new CornerRadius(0, 2, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility   = Visibility.Hidden
            };

            _ico = new PackIconMaterial
            {
                Kind   = NavIcon,
                Width  = 18,
                Height = 18,
                Foreground = R<Brush>("QV.Brush.TextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };

            _bg = new Border
            {
                Width        = 40,
                Height       = 36,
                CornerRadius = new CornerRadius(7),
                Background   = Brushes.Transparent,
                Child        = _ico
            };

            var grid = new Grid();
            grid.Children.Add(_accent);
            grid.Children.Add(_bg);
            Content = grid;

            if (ToolTip is string tip)
                ToolTip = new ToolTip { Content = tip };

            MouseEnter += (s, e) => { if (!IsActive) _bg.Background = R<Brush>("QV.Brush.SidebarHover"); };
            MouseLeave += (s, e) => { if (!IsActive) _bg.Background = Brushes.Transparent; };
            Apply();
        }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => (d as NavIconButton)?.Apply();

        private void Apply()
        {
            if (_ico != null) _ico.Kind = NavIcon;
            if (_bg   == null) return;
            if (IsActive)
            {
                _bg.Background   = R<Brush>("QV.Brush.SidebarActive");
                _ico.Foreground  = R<Brush>("QV.Brush.Cyan");
                _accent.Visibility = Visibility.Visible;
            }
            else
            {
                _bg.Background   = Brushes.Transparent;
                _ico.Foreground  = R<Brush>("QV.Brush.TextMuted");
                _accent.Visibility = Visibility.Hidden;
            }
        }

        static T R<T>(string k) => (T)Application.Current.Resources[k];
    }

    // ── NavLabelButton ───────────────────────────────────────────────────────
    public class NavLabelButton : Button
    {
        public static readonly DependencyProperty NavIconProperty =
            DependencyProperty.Register("NavIcon", typeof(PackIconMaterialKind),
                typeof(NavLabelButton), new PropertyMetadata(PackIconMaterialKind.Circle, OnChanged));
        public static readonly DependencyProperty NavLabelProperty =
            DependencyProperty.Register("NavLabel", typeof(string),
                typeof(NavLabelButton), new PropertyMetadata("", OnChanged));
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register("IsActive", typeof(bool),
                typeof(NavLabelButton), new PropertyMetadata(false, OnChanged));

        public PackIconMaterialKind NavIcon { get => (PackIconMaterialKind)GetValue(NavIconProperty); set => SetValue(NavIconProperty, value); }
        public string NavLabel { get => (string)GetValue(NavLabelProperty); set => SetValue(NavLabelProperty, value); }
        public bool   IsActive { get => (bool)GetValue(IsActiveProperty);   set => SetValue(IsActiveProperty, value); }

        private Border    _bg;
        private PackIconMaterial _ico;
        private TextBlock _lbl;
        private Border    _accent;

        public NavLabelButton()
        {
            HorizontalAlignment        = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Margin          = new Thickness(0, 1, 0, 1);
            Background      = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            Cursor          = System.Windows.Input.Cursors.Hand;
            FocusVisualStyle = null;
            Loaded += (s, e) => Build();
        }

        private void Build()
        {
            _accent = new Border
            {
                Width  = 3,
                Background   = R<Brush>("QV.Brush.Cyan"),
                CornerRadius = new CornerRadius(0, 2, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility   = Visibility.Hidden
            };

            _ico = new PackIconMaterial
            {
                Kind      = NavIcon,
                Width     = 15,
                Height    = 15,
                Foreground = R<Brush>("QV.Brush.TextMuted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin    = new Thickness(0, 0, 10, 0)
            };

            _lbl = new TextBlock
            {
                Text      = NavLabel,
                FontSize  = 13,
                Foreground = R<Brush>("QV.Brush.TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(12, 8, 12, 8)
            };
            row.Children.Add(_ico);
            row.Children.Add(_lbl);

            _bg = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background   = Brushes.Transparent,
                Child        = row
            };

            var grid = new Grid();
            grid.Children.Add(_bg);
            grid.Children.Add(_accent);
            Content = grid;

            MouseEnter += (s, e) => { if (!IsActive) _bg.Background = R<Brush>("QV.Brush.SidebarHover"); };
            MouseLeave += (s, e) => { if (!IsActive) _bg.Background = Brushes.Transparent; };
            Apply();
        }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => (d as NavLabelButton)?.Apply();

        private void Apply()
        {
            if (_ico != null) _ico.Kind   = NavIcon;
            if (_lbl != null) _lbl.Text   = NavLabel;
            if (_bg  == null) return;
            if (IsActive)
            {
                _bg.Background     = R<Brush>("QV.Brush.SidebarActive");
                _lbl.Foreground    = R<Brush>("QV.Brush.Cyan");
                _ico.Foreground    = R<Brush>("QV.Brush.Cyan");
                _accent.Visibility = Visibility.Visible;
            }
            else
            {
                _bg.Background     = Brushes.Transparent;
                _lbl.Foreground    = R<Brush>("QV.Brush.TextSecondary");
                _ico.Foreground    = R<Brush>("QV.Brush.TextMuted");
                _accent.Visibility = Visibility.Hidden;
            }
        }

        static T R<T>(string k) => (T)Application.Current.Resources[k];
    }
}
