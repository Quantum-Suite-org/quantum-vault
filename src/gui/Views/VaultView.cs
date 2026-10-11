using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace QuantumVault
{
    /// <summary>Vault view — entry list + editor pane. Matches reference "Vault Screen".</summary>
    public class VaultView : UserControl
    {
        public event RoutedEventHandler SaveRequested;
        public event RoutedEventHandler DeleteRequested;
        public event RoutedEventHandler DuplicateRequested;
        public event RoutedEventHandler NewEntryRequested;

        private ObservableCollection<VaultEntry> _entries = new ObservableCollection<VaultEntry>();
        private ListBox    _list;
        private TextBlock  _headerTb, _countTb;
        private Grid       _editorPane;
        private Border     _emptyPane;
        private string     _currentSection = "vault";
        private string     _currentId      = null;
        private bool       _dirty          = false;

        // Editor fields
        private TextBox    _titleBox, _contentBox, _tagsBox, _descBox, _langBox;
        private CheckBox   _dpapiCheck;
        private TextBlock  _typeBadge, _updatedTb, _charCountTb, _savedTb;
        private Button     _favBtn, _copyBtn;
        private Border     _copyBtnBd, _dpapiBd;

        public VaultView() { Build(); }

        // ── Build ──────────────────────────────────────────────────────────
        private void Build()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: list pane
            var listPane = BuildListPane();
            Grid.SetColumn(listPane, 0);

            // Right: editor / empty
            var rightGrid = new Grid();
            _emptyPane = BuildEmptyPane();
            _editorPane = BuildEditorPane();
            _editorPane.Visibility = Visibility.Collapsed;
            rightGrid.Children.Add(_emptyPane);
            rightGrid.Children.Add(_editorPane);
            Grid.SetColumn(rightGrid, 1);

            root.Children.Add(listPane);
            root.Children.Add(rightGrid);
            Content = root;
        }

        private UIElement BuildListPane()
        {
            var bd = new Border
            {
                Background      = R<Brush>("QV.Brush.BgSurface"),
                BorderBrush     = R<Brush>("QV.Brush.BorderSubtle"),
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var dockPanel = new DockPanel { LastChildFill = true };

            // Header
            var hdr = new Border
            {
                Background = R<Brush>("QV.Brush.BgSurface"),
                Padding    = new Thickness(16, 14, 16, 10),
                BorderBrush = R<Brush>("QV.Brush.BorderSubtle"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            DockPanel.SetDock(hdr, Dock.Top);
            var hdrGrid = new Grid();
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _headerTb = new TextBlock
            {
                Text = "All Entries", FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = R<Brush>("QV.Brush.TextPrimary"), VerticalAlignment = VerticalAlignment.Center
            };
            _countTb = new TextBlock
            {
                FontSize = 11, Foreground = R<Brush>("QV.Brush.TextMuted"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_countTb, 1);
            hdrGrid.Children.Add(_headerTb);
            hdrGrid.Children.Add(_countTb);
            hdr.Child = hdrGrid;

            // New button
            var newBar = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = R<Brush>("QV.Brush.BorderSubtle"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            DockPanel.SetDock(newBar, Dock.Top);
            var newBtn = new Button
            {
                Content = "+ New Entry",
                Style   = R<Style>("QV.Style.ButtonPrimary"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0, 9, 0, 9),
                FontSize = 13
            };
            newBtn.Click += (s, e) => NewEntryRequested?.Invoke(this, new RoutedEventArgs());
            newBar.Child = newBtn;

            // List
            _list = new ListBox
            {
                Background    = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemsSource   = _entries,
                ItemContainerStyle = R<Style>("QV.Style.ListBoxItem")
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            VirtualizingStackPanel.SetIsVirtualizing(_list, true);
            _list.SelectionChanged += List_SelectionChanged;
            _list.ItemTemplate = BuildEntryTemplate();

            dockPanel.Children.Add(hdr);
            dockPanel.Children.Add(newBar);
            dockPanel.Children.Add(_list);
            bd.Child = dockPanel;
            return bd;
        }

        private DataTemplate BuildEntryTemplate()
        {
            return new DataTemplate(typeof(VaultEntry))
            {
                VisualTree = BuildEntryItemFactory()
            };
        }

        private FrameworkElementFactory BuildEntryItemFactory()
        {
            // We use a custom control for the item
            var factory = new FrameworkElementFactory(typeof(VaultEntryRow));
            factory.SetBinding(VaultEntryRow.EntryProperty,
                new System.Windows.Data.Binding());
            return factory;
        }

        private Border BuildEmptyPane()
        {
            var bd = new Border { Background = R<Brush>("QV.Brush.BgBase") };
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            var iconBd = new Border
            {
                Width = 64, Height = 64, CornerRadius = new CornerRadius(16),
                Background = R<Brush>("QV.Brush.CyanMuted"),
                Margin = new Thickness(0, 0, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            iconBd.Child = new PackIconMaterial
            {
                Kind = PackIconMaterialKind.Shield, Width = 32, Height = 32,
                Foreground = R<Brush>("QV.Brush.Cyan"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            stack.Children.Add(iconBd);
            stack.Children.Add(new TextBlock
            {
                Text = "Select an entry", FontSize = 20, FontWeight = FontWeights.Light,
                Foreground = R<Brush>("QV.Brush.TextSecondary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Choose an item from the list or create a new entry.",
                FontSize = 13, Foreground = R<Brush>("QV.Brush.TextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320, Margin = new Thickness(0, 0, 0, 24)
            });
            // Keyboard hints
            var hints = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var (k, d) in new[] { ("Ctrl+N","New"), ("Ctrl+F","Search"), ("Ctrl+S","Save"), ("Ctrl+,","Settings") })
            {
                hints.Children.Add(new Border
                {
                    BorderBrush = R<Brush>("QV.Brush.BorderSubtle"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding  = new Thickness(8, 4, 8, 4),
                    Margin   = new Thickness(4),
                    Child    = new TextBlock
                    {
                        Text = $"{k}  {d}", FontSize = 10,
                        Foreground = R<Brush>("QV.Brush.TextMuted"),
                        FontFamily = R<FontFamily>("QV.Font.Mono")
                    }
                });
            }
            stack.Children.Add(hints);
            bd.Child = stack;
            return bd;
        }

        private Grid BuildEditorPane()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // toolbar
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // title
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // meta
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // statusbar

            // Toolbar
            var toolbar = new Border
            {
                Background = R<Brush>("QV.Brush.BgSurface"),
                BorderBrush = R<Brush>("QV.Brush.BorderSubtle"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var tbGrid = new Grid();
            tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftTools = new StackPanel { Orientation = Orientation.Horizontal };
            _typeBadge = new TextBlock
            {
                FontSize = 10, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var typeBadgeBd = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background   = R<Brush>("QV.Brush.CyanMuted"),
                Padding      = new Thickness(8, 3, 8, 3),
                Margin       = new Thickness(0, 0, 12, 0),
                Child        = _typeBadge
            };
            _savedTb = new TextBlock
            {
                Text = "● Saved", FontSize = 11,
                Foreground = R<Brush>("QV.Brush.Green"),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            leftTools.Children.Add(typeBadgeBd);
            leftTools.Children.Add(_savedTb);

            var rightTools = new StackPanel { Orientation = Orientation.Horizontal };

            _copyBtnBd = new Border { Visibility = Visibility.Collapsed };
            _copyBtn = MakeToolBtn("⎘ Copy", R<Brush>("QV.Brush.TextSecondary"), () => DoCopy());
            _copyBtnBd.Child = _copyBtn;

            _favBtn = MakeToolBtn("☆", R<Brush>("QV.Brush.Warning"), () => DoFavorite());
            var dupBtn  = MakeToolBtn("⧉ Duplicate", R<Brush>("QV.Brush.TextSecondary"),
                () => { SaveCurrentEntry(); DuplicateRequested?.Invoke(this, new RoutedEventArgs()); });
            var saveBtn = new Button
            {
                Content = "Save", Style = R<Style>("QV.Style.ButtonPrimary"),
                Padding = new Thickness(16, 7, 16, 7), FontSize = 12,
                Margin  = new Thickness(8, 0, 0, 0)
            };
            saveBtn.Click += (s, e) => SaveCurrentEntry();
            var delBtn = new Button
            {
                Content = "Delete", Style = R<Style>("QV.Style.ButtonDanger"),
                Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
                Margin  = new Thickness(6, 0, 0, 0)
            };
            delBtn.Click += (s, e) => ConfirmDelete();

            rightTools.Children.Add(_copyBtnBd);
            rightTools.Children.Add(_favBtn);
            rightTools.Children.Add(dupBtn);
            rightTools.Children.Add(saveBtn);
            rightTools.Children.Add(delBtn);
            Grid.SetColumn(rightTools, 1);

            tbGrid.Children.Add(leftTools);
            tbGrid.Children.Add(rightTools);
            toolbar.Child = tbGrid;
            Grid.SetRow(toolbar, 0);

            // Title row
            var titleBd = new Border
            {
                Padding = new Thickness(20, 16, 20, 12),
                BorderBrush = R<Brush>("QV.Brush.BorderSubtle"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            _titleBox = new TextBox
            {
                FontSize = 22, FontWeight = FontWeights.Light,
                Foreground = R<Brush>("QV.Brush.TextPrimary"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CaretBrush = R<Brush>("QV.Brush.Cyan"),
                SelectionBrush = R<Brush>("QV.Brush.CyanMuted"),
                FocusVisualStyle = null
            };
            _titleBox.TextChanged += (s, e) => MarkDirty();
            titleBd.Child = _titleBox;
            Grid.SetRow(titleBd, 1);

            // Meta row
            var metaBd = new Border
            {
                Background = R<Brush>("QV.Brush.BgSurface"),
                Padding = new Thickness(20, 10, 20, 10),
                BorderBrush = R<Brush>("QV.Brush.BorderSubtle"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var metaGrid = new Grid();
            for (int i = 0; i < 4; i++)
                metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _tagsBox = MakeMetaBox("tags (comma separated)");
            _descBox = MakeMetaBox("short description");
            _langBox = MakeMetaBox("language (e.g. python)");

            PlaceMetaLabel("TAGS",        0, 0, metaGrid);
            PlaceMetaLabel("DESCRIPTION", 1, 0, metaGrid);
            PlaceMetaLabel("LANGUAGE",    2, 0, metaGrid);

            Grid.SetColumn(_tagsBox, 0); Grid.SetRow(_tagsBox, 1);
            Grid.SetColumn(_descBox, 1); Grid.SetRow(_descBox, 1);
            Grid.SetColumn(_langBox, 2); Grid.SetRow(_langBox, 1);

            _dpapiBd = new Border
            {
                Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            var dpapiStack = new StackPanel();
            dpapiStack.Children.Add(new TextBlock
            {
                Text = "DPAPI LOCK", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = R<Brush>("QV.Brush.TextMuted"), Margin = new Thickness(0, 0, 0, 4)
            });
            _dpapiCheck = new CheckBox
            {
                Style = R<Style>("QV.Style.Toggle"),
                Content = new TextBlock { Text = "Windows lock", FontSize = 11,
                    Foreground = R<Brush>("QV.Brush.TextSecondary") },
                ToolTip = "Bind this note to your Windows account (DPAPI)"
            };
            _dpapiCheck.Checked   += (s, e) => MarkDirty();
            _dpapiCheck.Unchecked += (s, e) => MarkDirty();
            dpapiStack.Children.Add(_dpapiCheck);
            _dpapiBd.Child = dpapiStack;
            Grid.SetColumn(_dpapiBd, 3); Grid.SetRow(_dpapiBd, 0); Grid.SetRowSpan(_dpapiBd, 2);

            metaGrid.Children.Add(_tagsBox);
            metaGrid.Children.Add(_descBox);
            metaGrid.Children.Add(_langBox);
            metaGrid.Children.Add(_dpapiBd);
            metaBd.Child = metaGrid;
            Grid.SetRow(metaBd, 2);

            // Content
            var contentBd = new Border { Padding = new Thickness(20) };
            _contentBox = new TextBox
            {
                Style = R<Style>("QV.Style.TextBox"),
                AcceptsReturn = true, AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = 14
            };
            _contentBox.TextChanged += (s, e) =>
            {
                MarkDirty();
                if (_charCountTb != null)
                    _charCountTb.Text = $"{_contentBox.Text.Length} chars";
            };
            contentBd.Child = _contentBox;
            Grid.SetRow(contentBd, 3);

            // Status bar
            var statusBd = new Border
            {
                Background = R<Brush>("QV.Brush.BgSurface"),
                BorderBrush = R<Brush>("QV.Brush.BorderSubtle"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 7, 20, 7)
            };
            var statusGrid = new Grid();
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _updatedTb = new TextBlock
            {
                FontSize = 11, Foreground = R<Brush>("QV.Brush.TextMuted"),
                VerticalAlignment = VerticalAlignment.Center
            };
            _charCountTb = new TextBlock
            {
                FontSize = 11, Foreground = R<Brush>("QV.Brush.TextMuted"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_charCountTb, 1);
            statusGrid.Children.Add(_updatedTb);
            statusGrid.Children.Add(_charCountTb);
            statusBd.Child = statusGrid;
            Grid.SetRow(statusBd, 4);

            grid.Children.Add(toolbar);
            grid.Children.Add(titleBd);
            grid.Children.Add(metaBd);
            grid.Children.Add(contentBd);
            grid.Children.Add(statusBd);
            return grid;
        }

        // ── Public API ─────────────────────────────────────────────────────
        public void LoadSection(string section)
        {
            _currentSection = section;
            _headerTb.Text = section switch
            {
                "notes"     => "Notes",
                "snippets"  => "Snippets",
                "commands"  => "Commands",
                "favorites" => "Favorites",
                "recent"    => "Recently Edited",
                _           => "All Entries"
            };

            int typeFilter = section switch
            {
                "notes"    => (int)QVEntryType.Note,
                "snippets" => (int)QVEntryType.Snippet,
                "commands" => (int)QVEntryType.Command,
                _          => -1
            };
            bool favsOnly = section == "favorites";

            List<VaultEntry> results;
            if (section == "recent")
            {
                var (r, l) = VaultInterop.RecentEntries(50);
                results = r == QVResult.OK ? l : new List<VaultEntry>();
            }
            else
            {
                var (r, l) = VaultInterop.ListEntries(typeFilter, favsOnly);
                results = r == QVResult.OK ? l : new List<VaultEntry>();
            }

            _entries.Clear();
            foreach (var e in results) _entries.Add(e);
            _countTb.Text = $"{results.Count}";

            // Re-select current if still in list
            if (_currentId != null)
            {
                var sel = _entries.FirstOrDefault(e => e.Id == _currentId);
                if (sel != null) _list.SelectedItem = sel;
            }
        }

        public void LoadSearch(string query)
        {
            _headerTb.Text = $"Search: {query}";
            var (r, results) = VaultInterop.SearchEntries(query, -1, true, 200);
            _entries.Clear();
            foreach (var e in results) _entries.Add(e);
            _countTb.Text = $"{results.Count}";
        }

        public void SelectEntry(string id)
        {
            var e = _entries.FirstOrDefault(x => x.Id == id);
            if (e != null) _list.SelectedItem = e;
            else OpenEntryById(id);
        }

        public void FocusTitle() => _titleBox?.Focus();

        public void TriggerSave()    => SaveCurrentEntry();
        public void TriggerDelete()  => ConfirmDelete();
        public void TriggerDuplicate()
        {
            if (_currentId == null) return;
            SaveCurrentEntry();
            var (r, newId) = VaultInterop.DuplicateEntry(_currentId);
            if (r != QVResult.OK) return;
            LoadSection(_currentSection);
            SelectEntry(newId);
        }

        // ── Selection ──────────────────────────────────────────────────────
        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_list.SelectedItem is VaultEntry entry)
                OpenEntryById(entry.Id);
        }

        private void OpenEntryById(string id)
        {
            if (_dirty && _currentId != null) SaveCurrentEntry();

            if (!VaultInterop.NotesIsUnlocked())
            {
                var (chk, chkEntry) = VaultInterop.GetEntry(id);
                if (chk == QVResult.OK && chkEntry.Type == QVEntryType.Note)
                {
                    var mw = Window.GetWindow(this) as MainWindow;
                    if (mw != null && !mw.PromptNotesPassword()) return;
                }
            }

            var (r, entry) = VaultInterop.GetEntry(id);
            if (r != QVResult.OK) return;

            _currentId = id;
            _dirty     = false;
            PopulateEditor(entry);
            _emptyPane.Visibility  = Visibility.Collapsed;
            _editorPane.Visibility = Visibility.Visible;
        }

        private void PopulateEditor(VaultEntry e)
        {
            _titleBox.Text    = e.Title       ?? "";
            _contentBox.Text  = e.Content     ?? "";
            _tagsBox.Text     = e.Tags        ?? "";
            _descBox.Text     = e.Description ?? "";
            _langBox.Text     = e.Language    ?? "";
            _dpapiCheck.IsChecked = e.IsDpapi;

            _typeBadge.Text = e.TypeLabel.ToUpperInvariant();
            var (badgeBg, badgeFg) = e.Type switch
            {
                QVEntryType.Note    => (R<Brush>("QV.Brush.PurpleMuted"), R<Brush>("QV.Brush.PurpleBright")),
                QVEntryType.Snippet => (R<Brush>("QV.Brush.CyanMuted"),   R<Brush>("QV.Brush.Cyan")),
                _                   => (new SolidColorBrush(Color.FromRgb(0x06,0x4E,0x3B)), R<Brush>("QV.Brush.Green"))
            };

            var badgeBd = (Border)((StackPanel)((Border)_typeBadge.Parent).Parent)?.Children[0]
                          ?? (Border)_typeBadge.Parent;
            if (_typeBadge.Parent is Border bd2) { bd2.Background = badgeBg; }
            _typeBadge.Foreground = badgeFg;

            _favBtn.Content = e.IsFavorite ? "★" : "☆";
            _copyBtnBd.Visibility = (e.Type == QVEntryType.Snippet || e.Type == QVEntryType.Command)
                ? Visibility.Visible : Visibility.Collapsed;
            _dpapiBd.Visibility = e.Type == QVEntryType.Note
                ? Visibility.Visible : Visibility.Collapsed;

            if (e.Type != QVEntryType.Note)
            {
                _contentBox.FontFamily = R<FontFamily>("QV.Font.Mono");
                _contentBox.FontSize   = 12;
                _contentBox.Background = R<Brush>("QV.Brush.BgInput");
            }
            else
            {
                _contentBox.FontFamily = R<FontFamily>("QV.Font.UI");
                _contentBox.FontSize   = 14;
                _contentBox.Background = Brushes.Transparent;
            }

            _updatedTb.Text  = "Updated " + FriendlyTime(e.UpdatedAt);
            _charCountTb.Text = $"{_contentBox.Text.Length} chars";
            _savedTb.Visibility = Visibility.Collapsed;
        }

        private void SaveCurrentEntry()
        {
            if (_currentId == null) return;
            var title = _titleBox.Text.Trim();
            if (string.IsNullOrEmpty(title)) { _titleBox.Text = "Untitled"; title = "Untitled"; }

            var r = VaultInterop.UpdateEntry(
                _currentId, title,
                _contentBox.Text,
                _tagsBox.Text.Trim(),
                _descBox.Text.Trim(),
                _langBox.Text.Trim(),
                _dpapiCheck.IsChecked == true ? 1 : 0);

            if (r != QVResult.OK) return;
            _dirty = false;
            _savedTb.Visibility = Visibility.Visible;

            // Refresh list item
            var idx = _entries.IndexOf(_entries.FirstOrDefault(e => e.Id == _currentId));
            if (idx >= 0)
            {
                var (_, fresh) = VaultInterop.GetEntry(_currentId);
                if (fresh != null) { _entries[idx] = fresh; _list.SelectedIndex = idx; }
            }
            SaveRequested?.Invoke(this, new RoutedEventArgs());

            var timer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, e) => { _savedTb.Visibility = Visibility.Collapsed; timer.Stop(); };
            timer.Start();
        }

        private void ConfirmDelete()
        {
            if (_currentId == null) return;
            var (_, e) = VaultInterop.GetEntry(_currentId);
            var r = MessageBox.Show($"Delete \"{e?.Title}\"? This cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            VaultInterop.DeleteEntry(_currentId);
            _currentId = null; _dirty = false;
            _emptyPane.Visibility  = Visibility.Visible;
            _editorPane.Visibility = Visibility.Collapsed;
            LoadSection(_currentSection);
            DeleteRequested?.Invoke(this, new RoutedEventArgs());
        }

        private void DoCopy()
        {
            if (!string.IsNullOrEmpty(_contentBox.Text))
                Clipboard.SetText(_contentBox.Text);
        }

        private void DoFavorite()
        {
            if (_currentId == null) return;
            var (_, e) = VaultInterop.GetEntry(_currentId);
            if (e == null) return;
            VaultInterop.SetFavorite(_currentId, !e.IsFavorite);
            _favBtn.Content = !e.IsFavorite ? "★" : "☆";
        }

        private void MarkDirty() => _dirty = true;

        // ── Helpers ────────────────────────────────────────────────────────
        private TextBox MakeMetaBox(string hint)
        {
            var tb = new TextBox
            {
                Style = R<Style>("QV.Style.TextBox"),
                FontSize = 12, Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(8, 5, 8, 5), AcceptsReturn = false
            };
            tb.GotFocus  += (s, e) => { if (tb.Text == hint) tb.Text = ""; };
            tb.LostFocus += (s, e) => { if (string.IsNullOrEmpty(tb.Text)) tb.Text = ""; };
            tb.TextChanged += (s, e) => MarkDirty();
            return tb;
        }

        private void PlaceMetaLabel(string text, int col, int row, Grid grid)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = R<Brush>("QV.Brush.TextMuted"), Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetColumn(tb, col); Grid.SetRow(tb, row);
            grid.Children.Add(tb);
        }

        private Button MakeToolBtn(string label, Brush fg, Action action)
        {
            var btn = new Button
            {
                Content = label, Style = R<Style>("QV.Style.ButtonGhost"),
                Padding = new Thickness(10, 5, 10, 5), FontSize = 12,
                Foreground = fg, Margin = new Thickness(4, 0, 0, 0)
            };
            btn.Click += (s, e) => action();
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

    // ── VaultEntryRow ──────────────────────────────────────────────────────
    public class VaultEntryRow : UserControl
    {
        public static readonly DependencyProperty EntryProperty =
            DependencyProperty.Register("Entry", typeof(VaultEntry), typeof(VaultEntryRow),
                new PropertyMetadata(null, (d, e) => ((VaultEntryRow)d).Populate()));

        public VaultEntry Entry { get => (VaultEntry)GetValue(EntryProperty); set => SetValue(EntryProperty, value); }

        private TextBlock _title, _meta, _type;
        private TextBlock _fav;
        private Border    _iconBd;
        private PackIconMaterial _icon;

        public VaultEntryRow()
        {
            var grid = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _iconBd = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center
            };
            _icon = new PackIconMaterial
            {
                Width = 16, Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            _iconBd.Child = _icon;

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _title = new TextBlock
            {
                FontSize = 13, Foreground = R<Brush>("QV.Brush.TextPrimary"),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190
            };
            var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            _type = new TextBlock { FontSize = 10 };
            _meta = new TextBlock
            {
                FontSize = 10, Foreground = R<Brush>("QV.Brush.TextMuted"),
                Margin = new Thickness(6, 0, 0, 0)
            };
            bottomRow.Children.Add(_type);
            bottomRow.Children.Add(_meta);
            textStack.Children.Add(_title);
            textStack.Children.Add(bottomRow);

            _fav = new TextBlock
            {
                Text = "★", FontSize = 12,
                Foreground = R<Brush>("QV.Brush.Warning"),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            Grid.SetColumn(_iconBd,    0);
            Grid.SetColumn(textStack,  1);
            Grid.SetColumn(_fav,       2);
            grid.Children.Add(_iconBd);
            grid.Children.Add(textStack);
            grid.Children.Add(_fav);
            Content = grid;
        }

        private void Populate()
        {
            var e = Entry; if (e == null) return;
            _title.Text = e.Title;
            _fav.Visibility = e.IsFavorite ? Visibility.Visible : Visibility.Collapsed;

            var (kind, fg, bg) = e.Type switch
            {
                QVEntryType.Note    => (PackIconMaterialKind.NoteTextOutline,
                    R<Brush>("QV.Brush.PurpleBright"), R<Brush>("QV.Brush.PurpleMuted")),
                QVEntryType.Snippet => (PackIconMaterialKind.CodeBraces,
                    R<Brush>("QV.Brush.Cyan"), R<Brush>("QV.Brush.CyanMuted")),
                _                   => (PackIconMaterialKind.Console,
                    R<Brush>("QV.Brush.Green"),
                    new SolidColorBrush(Color.FromRgb(0x06,0x4E,0x3B)) as Brush)
            };
            _icon.Kind        = kind;
            _icon.Foreground  = fg;
            _iconBd.Background = bg;
            _type.Text        = e.TypeLabel;
            _type.Foreground  = fg;

            var diff = DateTime.Now - e.UpdatedAt;
            _meta.Text = diff.TotalMinutes < 1 ? "just now"
                       : diff.TotalHours   < 1 ? $"{(int)diff.TotalMinutes}m ago"
                       : diff.TotalDays    < 1 ? $"{(int)diff.TotalHours}h ago"
                       : $"{(int)diff.TotalDays}d ago";
        }

        static T R<T>(string k) => (T)Application.Current.Resources[k];
    }
}

