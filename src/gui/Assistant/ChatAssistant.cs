using System;
using System.Threading.Tasks;

namespace QuantumVault
{
    /// <summary>Local model. Loaded once into memory at startup.</summary>
    public static class ChatAssistant
    {
        private static readonly LocalLlm Llm = new LocalLlm();
        private static readonly object Gate = new object();

        public static string LastError { get; private set; }
        public static bool IsLoaded => Llm.IsLoaded;

        /// <summary>Loads the model into memory now so the first reply is fast. Never throws.</summary>
        public static void Warm()
        {
            try
            {
                lock (Gate)
                {
                    if (!Llm.IsLoaded) Llm.Load(ModelManager.ModelDir);
                }
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        /// <summary>Runs one generation on a worker thread. The prompt is a full ChatML conversation.</summary>
        public static Task<string> GenerateAsync(string prompt, int maxNewTokens) =>
            Task.Run(() =>
            {
                lock (Gate)
                {
                    if (!Llm.IsLoaded) Llm.Load(ModelManager.ModelDir);
                    return Llm.Generate(prompt, maxNewTokens, null);
                }
            });
    }
}
