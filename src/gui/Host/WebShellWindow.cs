using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MahApps.Metro.IconPacks;

namespace QuantumVault
{
    /// <summary>Default UI: custom WPF title bar + WebView2 hosting the web app in web\.</summary>
    public sealed class WebShellWindow : Window
    {
        /// <summary>Raised once when the web UI cannot run. App switches to the classic WPF window.</summary>
        public event Action<string> Failed;

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private readonly WebView2 _web = new WebView2();
        private readonly WebBridge _bridge = new WebBridge();
        private readonly DispatcherTimer _reminderTimer = new DispatcherTimer();
        private readonly string _webDir;
        private Button _maxBtn;
        private int _reloads;
        private bool _failedRaised;

        public WebShellWindow()
        {
            _webDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");

            Title = "Quantum Vault";
            Width = 1220;
            Height = 790;
            MinWidth = 980;
            MinHeight = 620;
            WindowStyle = WindowStyle.None;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x07, 0x0B, 0x14));

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 40,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            var dock = new DockPanel();
            var bar = BuildTitleBar();
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(_web);
            Content = dock;

            StateChanged += (s, e) =>
            {
                if (_maxBtn?.Content is PackIconMaterial ico)
                    ico.Kind = WindowState == WindowState.Maximized
                        ? PackIconMaterialKind.WindowRestore
                        : PackIconMaterialKind.WindowMaximize;
            };

            _reminderTimer.Interval = TimeSpan.FromSeconds(15);
            _reminderTimer.Tick += (s, e) =>
            {
                var due = ReminderStore.TakeDue(DateTime.Now);
                if (due.Count > 0)
                    Post(new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "event", "reminder" },
                        { "text", string.Join(", ", due.Select(x => x.Text)) }
                    });
            };

            // Marshal events from background work onto the UI thread before posting to the web view.
            WebBridge.Emit = p => Dispatcher.BeginInvoke(new Action(() => Post(p)));

            Loaded += async (s, e) => await InitAsync();
        }

        private async Task InitAsync()
        {
            if (!File.Exists(Path.Combine(_webDir, "index.html")))
            {
                Fail("web UI files are missing (web\\index.html)");
                return;
            }

            try
            {
                string userData = Path.Combine(VaultInterop.GetDataDir(), "webview");
                var env = await CoreWebView2Environment.CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(env);

                var core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.SetVirtualHostNameToFolderMapping("qv.local", _webDir,
                    CoreWebView2HostResourceAccessKind.DenyCors);

                core.WebMessageReceived += OnWebMessage;
                core.ProcessFailed += OnProcessFailed;

                core.Navigate("https://qv.local/index.html");
                _reminderTimer.Start();
            }
            catch (Exception ex)
            {
                Fail("WebView2 could not start: " + ex.Message);
            }
        }

        private async void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string reply = await _bridge.HandleAsync(e.WebMessageAsJson);
                _web.CoreWebView2.PostWebMessageAsJson(reply);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Quantum Vault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            // One reload for a crashed renderer; anything else (or repeated crashes) switches to the classic UI.
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited || _reloads >= 1)
            {
                Fail("web engine crashed (" + e.ProcessFailedKind + ")");
                return;
            }
            _reloads++;
            _web.Reload();
        }

        private void Post(System.Collections.Generic.Dictionary<string, object> payload)
        {
            _web.CoreWebView2?.PostWebMessageAsJson(Json.Serialize(payload));
        }

        private void Fail(string reason)
        {
            if (_failedRaised) return;
            _failedRaised = true;
            _reminderTimer.Stop();
            Failed?.Invoke(reason);
        }

        // ── Custom title bar ────────────────────────────────────────────────
        private UIElement BuildTitleBar()
        {
            var g = new Grid { Height = 40, Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x12, 0x20)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new PackIconMaterial
            {
                Kind = PackIconMaterialKind.ShieldLock,
                Width = 15, Height = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0xE0)),
                Margin = new Thickness(0, 0, 8, 0)
            });
            left.Children.Add(new TextBlock
            {
                Text = "Quantum Vault",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xFF))
            });
            WindowChrome.SetIsHitTestVisibleInChrome(left, false);

            var caps = new StackPanel { Orientation = Orientation.Horizontal };
            caps.Children.Add(CapButton(PackIconMaterialKind.WindowMinimize, () => WindowState = WindowState.Minimized, false));
            _maxBtn = CapButton(PackIconMaterialKind.WindowMaximize,
                () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, false);
            caps.Children.Add(_maxBtn);
            caps.Children.Add(CapButton(PackIconMaterialKind.WindowClose, () => Close(), true));

            Grid.SetColumn(caps, 1);
            g.Children.Add(left);
            g.Children.Add(caps);
            return g;
        }

        private static Button CapButton(PackIconMaterialKind kind, Action action, bool isClose)
        {
            var grey = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xC7));
            var btn = new Button
            {
                Width = 46,
                Height = 40,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = new PackIconMaterial { Kind = kind, Width = 11, Height = 11, Foreground = grey }
            };
            WindowChrome.SetIsHitTestVisibleInChrome(btn, true);
            btn.Click += (s, e) => action();
            btn.MouseEnter += (s, e) => btn.Background = isClose
                ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                : new SolidColorBrush(Color.FromRgb(0x16, 0x22, 0x3A));
            btn.MouseLeave += (s, e) => btn.Background = Brushes.Transparent;
            return btn;
        }
    }
}

