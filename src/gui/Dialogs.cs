using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.Controls;

namespace QuantumVault
{
    internal static class DlgUtil
    {
        public static T R<T>(string k) => (T)Application.Current.Resources[k];

        public static void Setup(MetroWindow w, string title, double width, double height)
        {
            w.Title = title; w.Width = width; w.Height = height;
            w.ResizeMode = ResizeMode.NoResize;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.Background = R<Brush>("QV.Brush.BgBase");
            w.GlowBrush = R<Brush>("QV.Brush.Cyan");
            w.BorderThickness = new Thickness(1);
            w.ShowInTaskbar = false;
        }

        public static TextBlock Label(string text) => new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = R<Brush>("QV.Brush.TextMuted"), Margin = new Thickness(0, 10, 0, 4)
        };

        public static PasswordBox Pw() => new PasswordBox
        {
            Padding = new Thickness(8, 7, 8, 7), FontSize = 13,
            Background = R<Brush>("QV.Brush.BgInput"),
            Foreground = R<Brush>("QV.Brush.TextPrimary"),
            BorderBrush = R<Brush>("QV.Brush.BorderCard"),
            CaretBrush = R<Brush>("QV.Brush.Cyan")
        };

        public static StackPanel Buttons(Button ok, Button cancel)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            cancel.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(cancel); sp.Children.Add(ok);
            return sp;
        }

        public static Button Primary(string t) => new Button { Content = t,
            Style = R<Style>("QV.Style.ButtonPrimary"), Padding = new Thickness(20, 8, 20, 8), IsDefault = true };
        public static Button Ghost(string t) => new Button { Content = t,
            Style = R<Style>("QV.Style.ButtonGhost"), Padding = new Thickness(16, 8, 16, 8), IsCancel = true };
    }

    /// <summary>Choose entry type.</summary>
    public class NewEntryDialog : MetroWindow
    {
        public QVEntryType SelectedType { get; private set; } = QVEntryType.Snippet;

        public NewEntryDialog()
        {
            DlgUtil.Setup(this, "New Entry", 420, 330);
            var sp = new StackPanel { Margin = new Thickness(24, 16, 24, 20) };
            sp.Children.Add(new TextBlock { Text = "What do you want to create?", FontSize = 16,
                Foreground = DlgUtil.R<Brush>("QV.Brush.TextPrimary"), Margin = new Thickness(0, 0, 0, 14) });
            sp.Children.Add(Choice("Note", "Encrypted with your master password", QVEntryType.Note));
            sp.Children.Add(Choice("Snippet", "Reusable code or text", QVEntryType.Snippet));
            sp.Children.Add(Choice("Command", "Shell command you run often", QVEntryType.Command));
            Content = sp;
        }

        private Button Choice(string title, string desc, QVEntryType type)
        {
            var inner = new StackPanel();
            inner.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = DlgUtil.R<Brush>("QV.Brush.TextPrimary") });
            inner.Children.Add(new TextBlock { Text = desc, FontSize = 11,
                Foreground = DlgUtil.R<Brush>("QV.Brush.TextSecondary"), Margin = new Thickness(0, 2, 0, 0) });
            var b = new Button { Content = inner, Style = DlgUtil.R<Style>("QV.Style.ButtonGhost"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) };
            b.Click += (s, e) => { SelectedType = type; DialogResult = true; };
            return b;
        }
    }

    /// <summary>Set (first time) or enter the notes master password.</summary>
    public class PasswordDialog : MetroWindow
    {
        public string Password => _pw.Password;
        private readonly PasswordBox _pw = DlgUtil.Pw();

        public PasswordDialog()
        {
            DlgUtil.Setup(this, "Notes password", 400, 250);
            var sp = new StackPanel { Margin = new Thickness(24, 16, 24, 20) };
            sp.Children.Add(new TextBlock { Text = "Enter your master password", FontSize = 15,
                Foreground = DlgUtil.R<Brush>("QV.Brush.TextPrimary") });
            sp.Children.Add(new TextBlock { Text = "Required to open notes. It is never stored.",
                FontSize = 11, Foreground = DlgUtil.R<Brush>("QV.Brush.TextSecondary"),
                Margin = new Thickness(0, 4, 0, 0) });
            sp.Children.Add(DlgUtil.Label("PASSWORD"));
            sp.Children.Add(_pw);

            var ok = DlgUtil.Primary("Unlock");
            ok.Click += (s, e) =>
            {
                if (_pw.Password.Length == 0) return;
                DialogResult = true;
            };
            sp.Children.Add(DlgUtil.Buttons(ok, DlgUtil.Ghost("Cancel")));
            Content = sp;
            Loaded += (s, e) => _pw.Focus();
        }
    }

    /// <summary>Change master password.</summary>
    public class ChangePasswordDialog : MetroWindow
    {
        public string OldPassword => _old.Password;
        public string NewPassword => _new.Password;
        private readonly PasswordBox _old = DlgUtil.Pw(), _new = DlgUtil.Pw(), _conf = DlgUtil.Pw();
        private readonly TextBlock _err = new TextBlock { FontSize = 11, Margin = new Thickness(0, 8, 0, 0),
            Foreground = DlgUtil.R<Brush>("QV.Brush.Danger") };

        public ChangePasswordDialog()
        {
            DlgUtil.Setup(this, "Change password", 420, 380);
            var sp = new StackPanel { Margin = new Thickness(24, 12, 24, 20) };
            sp.Children.Add(DlgUtil.Label("CURRENT PASSWORD")); sp.Children.Add(_old);
            sp.Children.Add(DlgUtil.Label("NEW PASSWORD"));     sp.Children.Add(_new);
            sp.Children.Add(DlgUtil.Label("CONFIRM NEW"));      sp.Children.Add(_conf);
            sp.Children.Add(_err);

            var ok = DlgUtil.Primary("Change");
            ok.Click += (s, e) =>
            {
                if (_new.Password.Length < 8) { _err.Text = "New password must be at least 8 characters."; return; }
                if (_new.Password != _conf.Password) { _err.Text = "Passwords do not match."; return; }
                DialogResult = true;
            };
            sp.Children.Add(DlgUtil.Buttons(ok, DlgUtil.Ghost("Cancel")));
            Content = sp;
            Loaded += (s, e) => _old.Focus();
        }
    }
}
