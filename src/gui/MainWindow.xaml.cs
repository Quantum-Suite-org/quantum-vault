using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MahApps.Metro.Controls;

namespace QuantumVault
{
    public partial class MainWindow : MetroWindow
    {
        private ObservableCollection<VaultEntry> _entries = new ObservableCollection<VaultEntry>();
        private string  _currentSection = "dashboard";
        private string  _currentEditId  = null;
        private bool    _isDirty        = false;
        private DispatcherTimer _searchDebounce;

        // Commands
        public ICommand NewEntryCommand     { get; }
        public ICommand FocusSearchCommand  { get; }
        public ICommand SaveCommand         { get; }
        public ICommand DeleteCommand       { get; }
        public ICommand DuplicateCommand    { get; }
        public ICommand OpenSettingsCommand { get; }

        // All nav buttons for active-state management
        private NavIconButton[]  _iconBtns;
        private NavLabelButton[] _labelBtns;

        public MainWindow()
        {
            NewEntryCommand     = new RelayCommand(_ => NewEntry());
            FocusSearchCommand  = new RelayCommand(_ => { TbSearch.Focus(); TbSearch.SelectAll(); });
            SaveCommand         = new RelayCommand(_ => SaveCurrentEntry(), _ => _isDirty);
            DeleteCommand       = new RelayCommand(_ => DeleteCurrentEntry(), _ => _currentEditId != null);
            DuplicateCommand    = new RelayCommand(_ => DuplicateCurrentEntry(), _ => _currentEditId != null);
            OpenSettingsCommand = new RelayCommand(_ => Navigate("settings"));

            InitializeComponent();
            DataContext = this;

            _iconBtns  = new[] { IcoDash, IcoVault, IcoNotes, IcoSnippets, IcoCommands, IcoSecurity, IcoFavorites, IcoSettings };
            _labelBtns = new[] { LbDash, LbVault, LbNotes, LbSnippets, LbCommands, LbFavorites, LbRecent, LbSecurity };

            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _searchDebounce.Tick += (s, e) => { _searchDebounce.Stop(); RefreshCurrentView(); };

            Logger.Init(VaultInterop.GetLogDir());

            if (VaultInterop.IsFirstLaunch())
                Navigate("welcome");
            else
                Navigate("dashboard");

            UpdateStatusBar();
        }

        // ── Navigation ─────────────────────────────────────────────────────
        private void Navigate(string section)
        {
            _currentSection = section;

            // Hide all views
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewVault.Visibility     = Visibility.Collapsed;
            ViewSecurity.Visibility  = Visibility.Collapsed;
            ViewSettings.Visibility  = Visibility.Collapsed;
            ViewWelcome.Visibility   = Visibility.Collapsed;

            // Show target
            switch (section)
            {
                case "dashboard":
                    ViewDashboard.Refresh(); ViewDashboard.Visibility = Visibility.Visible; break;
                case "vault":
                case "notes": case "snippets": case "commands":
                case "favorites": case "recent": case "all":
                    ViewVault.LoadSection(section); ViewVault.Visibility = Visibility.Visible; break;
                case "security":
                    ViewSecurity.Refresh(); ViewSecurity.Visibility = Visibility.Visible; break;
                case "settings":
                    ViewSettings.Refresh(); ViewSettings.Visibility = Visibility.Visible; break;
                case "welcome":
                    ViewWelcome.Visibility = Visibility.Visible; break;
            }

            // Sync active states
            string iconTag  = section is "notes" or "snippets" or "commands" or "favorites" or "recent" or "all" ? "vault" : section;
            string labelTag = section;

            foreach (var b in _iconBtns)  b.IsActive = (b.Tag as string) == iconTag;
            foreach (var b in _labelBtns) b.IsActive = (b.Tag as string) == labelTag;

            UpdateStatusBar();
        }

        private void NavIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is NavIconButton btn) Navigate(btn.Tag as string ?? "dashboard");
        }

        private void NavLabel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is NavLabelButton btn) Navigate(btn.Tag as string ?? "dashboard");
        }

        // ── Search ──────────────────────────────────────────────────────────
        private void TbSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            TbSearchHint.Visibility = string.IsNullOrEmpty(TbSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void TbSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { TbSearch.Text = ""; }
            else if (e.Key == Key.Enter) { _searchDebounce.Stop(); RefreshCurrentView(); }
        }

        private void RefreshCurrentView()
        {
            string q = TbSearch.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(q))
            {
                ViewVault.LoadSearch(q);
                ViewVault.Visibility = Visibility.Visible;
                ViewDashboard.Visibility = ViewSecurity.Visibility =
                ViewSettings.Visibility  = ViewWelcome.Visibility   = Visibility.Collapsed;
                foreach (var b in _iconBtns)  b.IsActive = (b.Tag as string) == "vault";
                foreach (var b in _labelBtns) b.IsActive = false;
            }
            else if (_currentSection == "vault" || _currentSection == "notes" ||
                     _currentSection == "snippets" || _currentSection == "commands" ||
                     _currentSection == "favorites" || _currentSection == "recent")
            {
                ViewVault.LoadSection(_currentSection);
            }
            else if (_currentSection == "dashboard")
            {
                ViewDashboard.Refresh();
            }
        }

        // ── Entry operations ────────────────────────────────────────────────
        private void NewEntry()
        {
            var dlg = new NewEntryDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var type = dlg.SelectedType;
            if (type == QVEntryType.Note && !VaultInterop.NotesIsUnlocked())
                if (!PromptNotesPassword()) return;

            var (result, newId) = VaultInterop.CreateEntry(type, "Untitled", "", "", "", "", false);
            if (result != QVResult.OK) { ShowError("Create failed: " + VaultInterop.ErrorString(result)); return; }

            Navigate("vault");
            ViewVault.SelectEntry(newId);
            ViewVault.FocusTitle();
            _currentEditId = newId;
            _isDirty = true;
            UpdateStatusBar();
        }

        private void SaveCurrentEntry()
        {
            ViewVault.TriggerSave();
            UpdateStatusBar();
        }

        private void DeleteCurrentEntry()
        {
            ViewVault.TriggerDelete();
            _currentEditId = null; _isDirty = false;
            UpdateStatusBar();
        }

        private void DuplicateCurrentEntry()
        {
            ViewVault.TriggerDuplicate();
            UpdateStatusBar();
        }

        // ── Password ────────────────────────────────────────────────────────
        internal bool PromptNotesPassword()
        {
            var dlg = new PasswordDialog { Owner = this };
            if (dlg.ShowDialog() != true) return false;
            var r = VaultInterop.NotesSetPassword(dlg.Password);
            if (r == QVResult.OK) { UpdateStatusBar(); return true; }
            ShowError(r == QVResult.BadPassword ? "Incorrect password." : VaultInterop.ErrorString(r));
            return false;
        }

        // ── Status bar ──────────────────────────────────────────────────────
        internal void UpdateStatusBar()
        {
            var (_, stats) = VaultInterop.GetStats();
            TbStatTotal.Text  = $"{stats.TotalEntries} entries  ·  {stats.NoteCount}N  {stats.SnippetCount}S  {stats.CommandCount}C";
            TbStatLocked.Text = VaultInterop.NotesIsUnlocked() ? "Notes unlocked" : "Notes locked";
        }

        // ── Event relay from views ──────────────────────────────────────────
        private void Dashboard_NewEntry(object s, RoutedEventArgs e)  => NewEntry();
        private void Dashboard_Navigate(object s, NavigateEventArgs e) => Navigate(e.Section);
        private void Vault_Save(object s, RoutedEventArgs e)           => UpdateStatusBar();
        private void Vault_Delete(object s, RoutedEventArgs e)         => UpdateStatusBar();
        private void Vault_Duplicate(object s, RoutedEventArgs e)      => UpdateStatusBar();
        private void Vault_NewEntry(object s, RoutedEventArgs e)       => NewEntry();
        private void Settings_Close(object s, RoutedEventArgs e)       => Navigate(_currentSection == "settings" ? "dashboard" : _currentSection);
        private void Welcome_GetStarted(object s, RoutedEventArgs e)   => Navigate("dashboard");
        private void NotifBtn_Click(object s, RoutedEventArgs e)       { }
        private void ProfileBtn_Click(object s, RoutedEventArgs e)     => Navigate("settings");

        private void ShowError(string msg) =>
            MessageBox.Show(msg, "Quantum Vault", MessageBoxButton.OK, MessageBoxImage.Error);

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isDirty)
            {
                var r = MessageBox.Show("Unsaved changes. Save before closing?",
                    "Quantum Vault", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes) SaveCurrentEntry();
                else if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            }
            base.OnClosing(e);
        }
    }

    // ── NavigateEventArgs ───────────────────────────────────────────────────
    public class NavigateEventArgs : RoutedEventArgs
    {
        public string Section { get; }
        public NavigateEventArgs(string section) : base() { Section = section; }
    }
}
