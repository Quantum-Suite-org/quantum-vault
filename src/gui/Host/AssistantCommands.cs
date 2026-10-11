using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuantumVault
{
    /// <summary>Model management commands for the web UI. Chat goes through AssistantRouter ("ask").</summary>
    public static class AssistantCommands
    {
        public static readonly object Unhandled = new object();

        public static async Task<object> TryHandle(string cmd, Dictionary<string, object> a)
        {
            switch (cmd)
            {
                case "model.status":
                    return new Dictionary<string, object>
                    {
                        { "installed", ModelManager.IsInstalled() },
                        { "loaded", ChatAssistant.IsLoaded },
                        { "name", ModelManager.DisplayName },
                        { "dir", ModelManager.ModelDir }
                    };

                case "model.download":
                    await ModelManager.DownloadAsync((pct, msg) => WebBridge.Emit(new Dictionary<string, object>
                    {
                        { "event", "download" }, { "pct", pct }, { "text", msg }
                    }));
                    return true;

                default:
                    return Unhandled;
            }
        }
    }
}
