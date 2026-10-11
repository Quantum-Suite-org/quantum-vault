using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace QuantumVault
{
    public partial class App : Application
    {
        private Window _shell;
        private bool _fellBack;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += OnDispatcherUnhandled;

            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/QVPremium.xaml", UriKind.Absolute)
            });

            bool forceLegacy = e.Args.Any(a => string.Equals(a, "--legacy", StringComparison.OrdinalIgnoreCase));

            var splash = new SplashWindow();
            splash.Show();

            string error = await Bootstrapper.RunAsync((p, m) => splash.Report(p, m));
            if (error != null)
            {
                splash.Close();
                MessageBox.Show(error, "Quantum Vault", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            Window win;
            string notice = null;

            if (forceLegacy)
            {
                win = CreateLegacy();
            }
            else if (false)
            {
                win = CreateLegacy();
                notice = "The WebView2 runtime is not installed, so the classic UI is being used.\n" +
                         "Install the Microsoft Edge WebView2 Runtime to get the default interface.";
            }
            else
            {
                var web = new WebShellWindow();
                web.Failed += reason => SwitchToLegacy(reason);
                win = web;
                _shell = web;
            }

            win.Show();
            MainWindow = win;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            splash.Close();

            if (notice != null)
                MessageBox.Show(notice, "Quantum Vault", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Automatic switch from the web UI to the classic WPF window.
        private void SwitchToLegacy(string reason)
        {
            if (_fellBack) return;
            _fellBack = true;

            var legacy = CreateLegacy();
            legacy.Show();
            MainWindow = legacy;
            _shell?.Close();
            _shell = legacy;

            MessageBox.Show(
                "The web interface could not run (" + reason + ").\n" +
                "Switched to the classic UI for this session.",
                "Quantum Vault", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Last-resort safety net: an unexpected exception in the web shell switches to the classic UI.
        private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (_fellBack || !(_shell is WebShellWindow)) return;

            e.Handled = true;
            SwitchToLegacy("unexpected error: " + e.Exception.Message);
        }

        private static Window CreateLegacy() => new global::QuantumVault.MainWindow();
    }
}

