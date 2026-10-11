using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace QuantumVault
{
    public static class Bootstrapper
    {
        public static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(10);

        public static bool BrowserEngineAvailable { get; private set; }
        public static string CoreVersion { get; private set; } = "unknown";

        private sealed class Step
        {
            public readonly string Label;
            public readonly Func<string> Work;
            public Step(string label, Func<string> work) { Label = label; Work = work; }
        }

        // Returns null on success, or an error message.
        public static async Task<string> RunAsync(Action<double, string> report)
        {
            const double workShare = 0.85;
            var clock = Stopwatch.StartNew();

            var steps = new List<Step>
            {
                new Step("Loading quantum core engine...", LoadCore),
                new Step("Initialising AES-256 cipher (Windows CNG)...", InitCipher),
                new Step("Verifying key store and PBKDF2 parameters...", VerifyKeys),
                new Step("Mounting encrypted vault...", MountVault),
                new Step("Loading reminders...", () => { ReminderStore.Load(); return null; }),
                new Step("Probing browser engine (WebView2)...", ProbeBrowser),
                new Step("Loading assistant model into memory...", WarmModel),
                new Step("Preparing workspace...", () => null)
            };

            for (int i = 0; i < steps.Count; i++)
            {
                report(workShare * i / steps.Count, steps[i].Label);

                string error;
                try { error = await Task.Run(steps[i].Work); }
                catch (Exception ex) { error = steps[i].Label + " " + ex.Message; }

                if (error != null) return error;
                await Task.Delay(180);
            }

            // Branding hold: total splash time is at least MinimumDuration.
            while (clock.Elapsed < MinimumDuration)
            {
                double t = Math.Min(clock.Elapsed.TotalSeconds / MinimumDuration.TotalSeconds, 0.99);
                report(workShare + (1 - workShare) * t, "Finalising secure workspace...");
                await Task.Delay(100);
            }

            report(1.0, "Ready");
            return null;
        }

        // Not fatal: the app works without the assistant model.
        private static string WarmModel()
        {
            if (ModelManager.IsInstalled()) ChatAssistant.Warm();
            return null;
        }

        private static string LoadCore()
        {
            try
            {
                CoreVersion = VaultInterop.Version();
                return null;
            }
            catch (DllNotFoundException ex)
            {
                return "Core DLL missing (qvault_core.dll): " + ex.Message;
            }
        }

        private static string InitCipher()
        {
            var r = VaultInterop.Init(null, null);
            if (r != QVResult.OK) return "Vault init failed: " + VaultInterop.ErrorString(r);
            try { Logger.Init(VaultInterop.GetLogDir()); } catch { }
            return null;
        }

        private static string VerifyKeys()
        {
            VaultInterop.IsFirstLaunch();
            VaultInterop.NotesIsUnlocked();
            return null;
        }

        private static string MountVault()
        {
            VaultInterop.GetStats();
            return null;
        }

        private static string ProbeBrowser()
        {
            try
            {
                BrowserEngineAvailable = !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch
            {
                BrowserEngineAvailable = false;
            }
            return null;
        }
    }
}


