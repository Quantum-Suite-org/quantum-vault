using System;
using System.IO;

namespace QuantumVault
{
    public static class Logger
    {
        private static string _logPath;
        private static readonly object _lock = new object();

        public static void Init(string logDir)
        {
            try
            {
                Directory.CreateDirectory(logDir);
                _logPath = Path.Combine(logDir, "qvault_gui.log");
            }
            catch { /* non-fatal */ }
        }

        public static void Info(string msg)  => Write("INFO",  msg);
        public static void Warn(string msg)  => Write("WARN",  msg);
        public static void Error(string msg) => Write("ERROR", msg);

        private static void Write(string level, string msg)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}";
                System.Diagnostics.Debug.WriteLine(line);
                if (_logPath == null) return;
                lock (_lock)
                    File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch { /* swallow logging errors */ }
        }
    }
}
