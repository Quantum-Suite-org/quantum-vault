using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace QuantumVault
{
    /// <summary>Downloads and locates the local assistant model. Files live in %APPDATA%\...\QuantumVault\models.</summary>
    public static class ModelManager
    {
        public const string DisplayName = "Qwen2.5-0.5B-Instruct (int4, ONNX GenAI)";

        // Hugging Face folder holding a complete ONNX Runtime GenAI bundle for Qwen2.5-0.5B-Instruct.
        public const string BaseUrl =
            "https://huggingface.co/webai-community/ai-models/resolve/main/Qwen2.5-0.5B-Instruct/onnx-webgpu/";

        private static readonly string[] Files =
        {
            "genai_config.json",
            "model.onnx",
            "model.onnx.data",
            "tokenizer.json",
            "tokenizer_config.json",
            "added_tokens.json",
            "special_tokens_map.json",
            "vocab.json",
            "merges.txt"
        };

        private const int ContextLength = 16384;

        public static string ModelDir =>
            Path.Combine(VaultInterop.GetDataDir(), "models", "qwen2.5-0.5b-instruct-int4");

        public static bool IsInstalled()
        {
            foreach (var f in Files)
                if (!File.Exists(Path.Combine(ModelDir, f))) return false;
            return true;
        }

        public static async Task DownloadAsync(Action<int, string> progress)
        {
            Directory.CreateDirectory(ModelDir);
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("QuantumVault/1.0");

                for (int i = 0; i < Files.Length; i++)
                {
                    string name = Files[i];
                    string target = Path.Combine(ModelDir, name);
                    if (File.Exists(target)) continue;

                    using (var resp = await http.GetAsync(BaseUrl + name, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!resp.IsSuccessStatusCode)
                            throw new Exception(name + ": HTTP " + (int)resp.StatusCode +
                                                " (check ModelManager.BaseUrl / file list)");

                        long total = resp.Content.Headers.ContentLength ?? -1;
                        string part = target + ".part";

                        using (var src = await resp.Content.ReadAsStreamAsync())
                        using (var dst = File.Create(part))
                        {
                            var buf = new byte[1 << 16];
                            long done = 0;
                            int n;
                            while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                            {
                                await dst.WriteAsync(buf, 0, n);
                                done += n;
                                if (total > 0)
                                {
                                    double fileFrac = (double)done / total;
                                    int pct = (int)(100.0 * (i + fileFrac) / Files.Length);
                                    progress(pct, "Downloading " + name + "...");
                                }
                            }
                        }
                        File.Move(part, target);
                    }
                }
            }

            FixConfig(Path.Combine(ModelDir, "genai_config.json"));
            progress(100, "Model ready");
        }

        // The WebGPU bundle needs two changes for the CPU runtime:
        //  - provider list emptied (no WebGPU on CPU)
        //  - context_length set (the runtime refuses to start without it)
        private static void FixConfig(string configPath)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = ser.DeserializeObject(File.ReadAllText(configPath)) as Dictionary<string, object>;
            if (root == null || !(root["model"] is Dictionary<string, object> model)) return;

            model["context_length"] = ContextLength;
            if (model["decoder"] is Dictionary<string, object> decoder &&
                decoder["session_options"] is Dictionary<string, object> session)
            {
                session["provider_options"] = new object[0];
            }
            File.WriteAllText(configPath, ser.Serialize(root));
        }
    }
}

