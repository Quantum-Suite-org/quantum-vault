using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace QuantumVault
{
    /// <summary>First-launch onboarding.</summary>
    public class WelcomeView : UserControl
    {
        public event RoutedEventHandler GetStarted;

        public WelcomeView()
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, MaxWidth = 480 };
            var iconBd = new Border { Width = 80, Height = 80, CornerRadius = new CornerRadius(20),
                Background = R<Brush>("QV.Brush.CyanMuted"), Margin = new Thickness(0, 0, 0, 24),
                HorizontalAlignment = HorizontalAlignment.Center };
            iconBd.Child = new PackIconMaterial { Kind = PackIconMaterialKind.Shield, Width = 40, Height = 40,
                Foreground = R<Brush>("QV.Brush.Cyan"), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(iconBd);
            stack.Children.Add(new TextBlock { Text = "Welcome to Quantum Vault", FontSize = 28,
                FontWeight = FontWeights.Light, Foreground = R<Brush>("QV.Brush.TextPrimary"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
            stack.Children.Add(new TextBlock {
                Text = "Your notes, snippets and commands â€” encrypted, local and offline. " +
                       "Notes are protected by a master password that is never stored.",
                FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                Foreground = R<Brush>("QV.Brush.TextSecondary"), Margin = new Thickness(0, 0, 0, 28) });
            var btn = new Button { Content = "Get started", Style = R<Style>("QV.Style.ButtonPrimary"),
                Padding = new Thickness(32, 12, 32, 12), HorizontalAlignment = HorizontalAlignment.Center };
            btn.Click += (s, e) => GetStarted?.Invoke(this, new RoutedEventArgs());
            stack.Children.Add(btn);
            Content = stack;
        }

        static T R<T>(string k) => (T)Application.Current.Resources[k];
    }
}


