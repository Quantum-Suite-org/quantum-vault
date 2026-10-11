using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace QuantumVault
{
    /// <summary>Settings — password, export/import/backup, data paths, about.</summary>
    public class SettingsView : UserControl
    {
        public event RoutedEventHandler ChangePasswordRequested;
        public event RoutedEventHandler ExportRequested;
        public event RoutedEventHandler ImportRequested;
        public event RoutedEventHandler BackupRequested;
        public event RoutedEventHandler CloseRequested;

        public SettingsView()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24), MaxWidth = 720,
                HorizontalAlignment = HorizontalAlignment.Left };

            root.Children.Add(new TextBlock { Text = "Settings", FontSize = 26, FontWeight = FontWeights.Light,
                Foreground = R<Brush>("QV.Brush.TextPrimary"), Margin = new Thickness(0, 0, 0, 20) });

            root.Children.Add(Section("Master password",
                "Protects your notes. It is never stored.",
                Btn("Change password", () => ChangePasswordRequested?.Invoke(this, new RoutedEventArgs()))));

            var dataBtns = new StackPanel { Orientation = Orientation.Horizontal };
            dataBtns.Children.Add(Btn("Export…", () => ExportRequested?.Invoke(this, new RoutedEventArgs())));
            dataBtns.Children.Add(Btn("Import…", () => ImportRequested?.Invoke(this, new RoutedEventArgs())));
            dataBtns.Children.Add(Btn("Backup now", () => BackupRequested?.Invoke(this, new RoutedEventArgs())));
            root.Children.Add(Section("Data", "Export to JSON, import from JSON, or create a backup copy.", dataBtns));

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Quantum-suite-org", "QuantumVault");
            var pathBox = new TextBox { Text = dir, IsReadOnly = true, Style = R<Style>("QV.Style.TextBox"),
                FontSize = 12 };
            var openBtn = Btn("Open folder", () =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { }
            });
            var pathStack = new StackPanel();
            pathStack.Children.Add(pathBox);
            pathStack.Children.Add(openBtn);
            root.Children.Add(Section("Storage location", "Where your encrypted vault lives.", pathStack));

            root.Children.Add(Section("About",
                "Quantum Vault — local, offline-first secure notes, snippets and commands.\nCore version: " + SafeVersion(),
                null));

            scroll.Content = root;
            Content = scroll;
        }

        public void Refresh() { }

        private static string SafeVersion()
        {
            try { return VaultInterop.Version(); } catch { return "unknown"; }
        }

        private Border Section(string title, string desc, UIElement body)
        {
            var bd = new Border
            {
                Background = R<Brush>("QV.Brush.BgCard"),
                BorderBrush = R<Brush>("QV.Brush.BorderCard"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = R<Brush>("QV.Brush.TextPrimary") });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = R<Brush>("QV.Brush.TextSecondary"), Margin = new Thickness(0, 4, 0, body != null ? 12 : 0) });
            if (body != null) sp.Children.Add(body);
            bd.Child = sp;
            return bd;
        }

        private Button Btn(string text, Action a)
        {
            var b = new Button { Content = text, Style = R<Style>("QV.Style.ButtonGhost"),
                Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 6, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (s, e) => a();
            return b;
        }

        static T R<T>(string k) => (T)Application.Current.Resources[k];
    }
}


