using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuantumVault
{
    /// <summary>Security Center — encryption, notes lock, backup status.</summary>
    public class SecurityView : UserControl
    {
        private StackPanel _list;

        public SecurityView()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
            root.Children.Add(new TextBlock
            {
                Text = "Security Center", FontSize = 26, FontWeight = FontWeights.Light,
                Foreground = R<Brush>("QV.Brush.TextPrimary")
            });
            root.Children.Add(new TextBlock
            {
                Text = "Vault health, encryption status and recommendations",
                FontSize = 13, Foreground = R<Brush>("QV.Brush.TextSecondary"),
                Margin = new Thickness(0, 4, 0, 24)
            });
            _list = new StackPanel();
            root.Children.Add(_list);
            scroll.Content = root;
            Content = scroll;
        }

        public void Refresh()
        {
            _list.Children.Clear();
            bool unlocked = VaultInterop.NotesIsUnlocked();
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Quantum-suite-org", "QuantumVault");
            bool keyExists = File.Exists(Path.Combine(dataDir, "vault.key"));
            bool hasBackup = Directory.Exists(Path.Combine(dataDir, "backups")) &&
                             Directory.GetFiles(Path.Combine(dataDir, "backups")).Length > 0;

            _list.Children.Add(Row("Encryption", "AES-256-CBC (Windows CNG)", true));
            _list.Children.Add(Row("Key derivation", "PBKDF2-SHA256, 200,000 iterations (notes)", true));
            _list.Children.Add(Row("Snippet/command key", keyExists ? "vault.key present" : "vault.key not created yet", keyExists));
            _list.Children.Add(Row("Notes lock", unlocked ? "Unlocked for this session" : "Locked", !unlocked));
            _list.Children.Add(Row("Backups", hasBackup ? "At least one backup found" : "No backups yet — create one in Settings", hasBackup));
            _list.Children.Add(Row("Network", "Fully offline. No data leaves this PC.", true));
        }

        private Border Row(string title, string detail, bool ok)
        {
            var accent = ok ? R<Brush>("QV.Brush.Green") : R<Brush>("QV.Brush.Warning");
            var bd = new Border
            {
                Background = R<Brush>("QV.Brush.BgCard"),
                BorderBrush = R<Brush>("QV.Brush.BorderCard"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = accent,
                Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = R<Brush>("QV.Brush.TextPrimary") });
            sp.Children.Add(new TextBlock { Text = detail, FontSize = 12, Margin = new Thickness(0, 2, 0, 0),
                Foreground = R<Brush>("QV.Brush.TextSecondary") });
            Grid.SetColumn(sp, 1);
            g.Children.Add(dot); g.Children.Add(sp);
            bd.Child = g;
            return bd;
        }

        static T R<T>(string k) => (T)Application.Current.Resources[k];
    }
}
