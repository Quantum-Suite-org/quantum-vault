using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MahApps.Metro.IconPacks;

namespace QuantumVault
{
    /// <summary>Dashboard view — matches reference "Main Dashboard" screen.</summary>
    public class DashboardView : UserControl
    {
        public event RoutedEventHandler NewEntryRequested;
        public event EventHandler<NavigateEventArgs> NavigateRequested;

        private TextBlock _tbTotal, _tbLogins, _tbNotes, _tbItems;
        private StackPanel _recentPanel;

        public DashboardView() { Build(); }

        private void Build()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root   = new Grid { Margin = new Thickness(28, 24, 28, 24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── Row 0: Header ──────────────────────────────────────────────
            var header = new Grid { Margin = new Thickness(0, 0, 0, 24) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hdrLeft = new StackPanel();
            hdrLeft.Children.Add(new TextBlock
            {
                Text       = "Main Dashboard",
                FontSize   = 26, FontWeight = FontWeights.Light,
                Foreground = R<Brush>("QV.Brush.TextPrimary")
            });
            hdrLeft.Children.Add(new TextBlock
            {
                Text       = "Welcome back · Your vault is secure",
                FontSize   = 13,
                Foreground = R<Brush>("QV.Brush.TextSecondary"),
                Margin     = new Thickness(0, 4, 0, 0)
            });

            var addBtn = MakePrimaryButton("+ Add New", () => NewEntryRequested?.Invoke(this, new RoutedEventArgs()));
            Grid.SetColumn(addBtn, 1);

            header.Children.Add(hdrLeft);
            header.Children.Add(addBtn);
            Grid.SetRow(header, 0);

            // ── Row 1: Metric cards ────────────────────────────────────────
            var cards = new Grid { Margin = new Thickness(0, 0, 0, 24) };
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.ColumnDefinitions.Add(new ColumnDefinition());

            var c0 = MakeMetricCard("Vault", "Shield", R<Brush>("QV.Brush.Cyan"),
                () => NavigateRequested?.Invoke(this, new NavigateEventArgs("vault")));
            var c1 = MakeMetricCard("Notes", "NoteTextOutline", R<Brush>("QV.Brush.PurpleBright"),
                () => NavigateRequested?.Invoke(this, new NavigateEventArgs("notes")));
            var c2 = MakeMetricCard("Snippets", "CodeBraces", R<Brush>("QV.Brush.Green"),
                () => NavigateRequested?.Invoke(this, new NavigateEventArgs("snippets")));
            var c3 = MakeMetricCard("Commands", "Console", R<Brush>("QV.Brush.Warning"),
                () => NavigateRequested?.Invoke(this, new NavigateEventArgs("commands")));

            // Bind counts
            _tbTotal  = FindMetricLabel(c0);
            _tbLogins = FindMetricLabel(c1);
            _tbNotes  = FindMetricLabel(c2);
            _tbItems  = FindMetricLabel(c3);

            Grid.SetColumn(c1, 1); Grid.SetColumn(c2, 2); Grid.SetColumn(c3, 3);
            cards.Children.Add(c0); cards.Children.Add(c1);
            cards.Children.Add(c2); cards.Children.Add(c3);
            Grid.SetRow(cards, 1);

            // ── Row 2: Lower split ─────────────────────────────────────────
            var lower = new Grid();
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(lower, 2);

            // Recent activity panel
            var recentCard = MakeCard("Recent Activity");
            _recentPanel   = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var recentScroll = new ScrollViewer
            {
                Content = _recentPanel,
                MaxHeight = 380,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            ((StackPanel)((Border)recentCard).Child).Children.Add(recentScroll);
            Grid.SetColumn(recentCard, 0);

            // Quick actions panel
            var actCard     = MakeCard("Quick Actions");
            var actStack    = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            actStack.Children.Add(MakeActionCard("🔒", "Add New",
                "Create a note, snippet or command",
                R<Brush>("QV.Brush.CyanMuted"), R<Brush>("QV.Brush.Cyan"),
                () => NewEntryRequested?.Invoke(this, new RoutedEventArgs())));
            actStack.Children.Add(MakeActionCard("🛡", "Security Center",
                "View vault health and alerts",
                R<Brush>("QV.Brush.PurpleMuted"), R<Brush>("QV.Brush.PurpleBright"),
                () => NavigateRequested?.Invoke(this, new NavigateEventArgs("security"))));
            actStack.Children.Add(MakeActionCard("⚙", "Settings",
                "Manage vault, backup and export",
                new SolidColorBrush(Color.FromRgb(0x1A, 0x25, 0x10)), R<Brush>("QV.Brush.Green"),
                () => NavigateRequested?.Invoke(this, new NavigateEventArgs("settings"))));
            ((StackPanel)((Border)actCard).Child).Children.Add(actStack);
            Grid.SetColumn(actCard, 1);

            lower.Children.Add(recentCard);
            lower.Children.Add(actCard);

            root.Children.Add(header);
            root.Children.Add(cards);
            root.Children.Add(lower);

            scroll.Content = root;
            Content = scroll;
        }

        public void Refresh()
        {
            var (_, stats) = VaultInterop.GetStats();
            if (_tbTotal  != null) _tbTotal.Text  = stats.TotalEntries.ToString();
            if (_tbLogins != null) _tbLogins.Text = stats.NoteCount.ToString();
            if (_tbNotes  != null) _tbNotes.Text  = stats.SnippetCount.ToString();
            if (_tbItems  != null) _tbItems.Text  = stats.CommandCount.ToString();

            if (_recentPanel == null) return;
            _recentPanel.Children.Clear();
            var (r, recent) = VaultInterop.RecentEntries(12);
            if (r == QVResult.OK)
            {
                foreach (var e in recent)
                    _recentPanel.Children.Add(MakeRecentRow(e));
            }
            if (_recentPanel.Children.Count == 0)
            {
                _recentPanel.Children.Add(new TextBlock
                {
                    Text = "No entries yet. Create your first entry!",
                    FontSize = 13, Foreground = R<Brush>("QV.Brush.TextMuted"),
                    Margin = new Thickness(0, 16, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
        }

        // ── Builders ───────────────────────────────────────────────────────
        private Border MakeMetricCard(string title, string iconKind, Brush accent, Action click)
        {
            var bd = new Border
            {
                Background      = R<Brush>("QV.Brush.BgCard"),
                BorderBrush     = R<Brush>("QV.Brush.BorderCard"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(10),
                Padding         = new Thickness(18, 16, 18, 16),
                Margin          = new Thickness(0, 0, 12, 0),
                Cursor          = System.Windows.Input.Cursors.Hand
            };

            var iconBd = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(40,
                    ((SolidColorBrush)accent).Color.R,
                    ((SolidColorBrush)accent).Color.G,
                    ((SolidColorBrush)accent).Color.B)),
                Margin = new Thickness(0, 0, 0, 12)
            };

            if (Enum.TryParse<PackIconMaterialKind>(iconKind, out var kind))
                iconBd.Child = new PackIconMaterial { Kind = kind, Width = 20, Height = 20,
                    Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center };

            var countTb = new TextBlock
            {
                Text = "0", FontSize = 28, FontWeight = FontWeights.Light,
                Foreground = R<Brush>("QV.Brush.TextPrimary"), Margin = new Thickness(0, 0, 0, 4)
            };
            var titleTb = new TextBlock
            {
                Text = title, FontSize = 12,
                Foreground = R<Brush>("QV.Brush.TextSecondary")
            };

            var stack = new StackPanel();
            stack.Children.Add(iconBd);
            stack.Children.Add(countTb);
            stack.Children.Add(titleTb);
            bd.Child = stack;

            // Store count tb for later update
            bd.Tag = countTb;

            bd.MouseEnter += (s, e) => bd.Background = R<Brush>("QV.Brush.BgCardHover");
            bd.MouseLeave += (s, e) => bd.Background = R<Brush>("QV.Brush.BgCard");
            bd.MouseLeftButtonUp += (s, e) => click?.Invoke();
            return bd;
        }

        private TextBlock FindMetricLabel(Border card) => card.Tag as TextBlock;

        private Border MakeCard(string title)
        {
            var bd = new Border
            {
                Background      = R<Brush>("QV.Brush.BgCard"),
                BorderBrush     = R<Brush>("QV.Brush.BorderCard"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(10),
                Padding         = new Thickness(20),
                Margin          = new Thickness(0, 0, 12, 0)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = R<Brush>("QV.Brush.TextPrimary")
            });
            bd.Child = sp;
            return bd;
        }

        private Border MakeRecentRow(VaultEntry e)
        {
            var bd = new Border
            {
                Padding = new Thickness(10, 10, 10, 10),
                Margin  = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(7),
                Cursor  = System.Windows.Input.Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var (iconKind, iconColor) = e.Type switch
            {
                QVEntryType.Note    => ("NoteTextOutline", R<Brush>("QV.Brush.PurpleBright")),
                QVEntryType.Snippet => ("CodeBraces",      R<Brush>("QV.Brush.Cyan")),
                _                   => ("Console",          R<Brush>("QV.Brush.Green"))
            };

            var iconBd = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(30,
                    ((SolidColorBrush)iconColor).Color.R,
                    ((SolidColorBrush)iconColor).Color.G,
                    ((SolidColorBrush)iconColor).Color.B)),
                Margin = new Thickness(0, 0, 12, 0)
            };
            if (Enum.TryParse<PackIconMaterialKind>(iconKind, out var kind))
                iconBd.Child = new PackIconMaterial { Kind = kind, Width = 15, Height = 15,
                    Foreground = iconColor,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center };

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = e.Title, FontSize = 13, Foreground = R<Brush>("QV.Brush.TextPrimary"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textStack.Children.Add(new TextBlock
            {
                Text = e.TypeLabel, FontSize = 10, Foreground = iconColor, Margin = new Thickness(0, 2, 0, 0)
            });

            var timeTb = new TextBlock
            {
                Text = FriendlyTime(e.UpdatedAt), FontSize = 10,
                Foreground = R<Brush>("QV.Brush.TextMuted"),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(iconBd,    0);
            Grid.SetColumn(textStack, 1);
            Grid.SetColumn(timeTb,    2);
            grid.Children.Add(iconBd);
            grid.Children.Add(textStack);
            grid.Children.Add(timeTb);
            bd.Child = grid;

            bd.MouseEnter += (s, ev) => bd.Background = R<Brush>("QV.Brush.BgElevated");
            bd.MouseLeave += (s, ev) => bd.Background = Brushes.Transparent;
            return bd;
        }

        private Border MakeActionCard(string emoji, string title, string desc,
            Brush bgBrush, Brush accentBrush, Action click)
        {
            var bd = new Border
            {
                Background      = bgBrush,
                BorderBrush     = new SolidColorBrush(Color.FromArgb(60,
                    ((SolidColorBrush)accentBrush).Color.R,
                    ((SolidColorBrush)accentBrush).Color.G,
                    ((SolidColorBrush)accentBrush).Color.B)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(14, 12, 14, 12),
                Margin          = new Thickness(0, 0, 0, 8),
                Cursor          = System.Windows.Input.Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = accentBrush
            });
            textStack.Children.Add(new TextBlock
            {
                Text = desc, FontSize = 11, Foreground = R<Brush>("QV.Brush.TextSecondary"),
                Margin = new Thickness(0, 2, 0, 0)
            });

            var arrow = new TextBlock
            {
                Text = "→", FontSize = 18, Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(textStack, 0);
            Grid.SetColumn(arrow, 1);
            grid.Children.Add(textStack);
            grid.Children.Add(arrow);
            bd.Child = grid;

            bd.MouseEnter += (s, e) => bd.Opacity = 0.8;
            bd.MouseLeave += (s, e) => bd.Opacity = 1.0;
            bd.MouseLeftButtonUp += (s, e) => click?.Invoke();
            return bd;
        }

        private Button MakePrimaryButton(string text, Action click)
        {
            var btn = new Button
            {
                Content = text,
                Style   = R<Style>("QV.Style.ButtonPrimary"),
                Padding = new Thickness(20, 10, 20, 10),
                VerticalAlignment = VerticalAlignment.Center
            };
            btn.Click += (s, e) => click?.Invoke();
            return btn;
        }

        private static string FriendlyTime(DateTime dt)
        {
            var d = DateTime.Now - dt;
            if (d.TotalMinutes < 1)  return "just now";
            if (d.TotalHours   < 1)  return $"{(int)d.TotalMinutes}m ago";
            if (d.TotalDays    < 1)  return $"{(int)d.TotalHours}h ago";
            if (d.TotalDays    < 7)  return $"{(int)d.TotalDays}d ago";
            return dt.ToString("dd MMM yyyy");
        }

        static T R<T>(string k) => (T)Application.Current.Resources[k];
    }
}
