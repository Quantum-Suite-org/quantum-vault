using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using Microsoft.Win32;

namespace QuantumVault
{
    /// <summary>Answers requests from the web UI. Each request is {id, cmd, args}; each reply is {id, ok, data | code+error}.</summary>
    public sealed class WebBridge
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly HttpClient Http = CreateHttp();

        // Set by the window: sends events (tokens, download progress) to the web UI. Must run on the UI thread.
        public static Action<Dictionary<string, object>> Emit = _ => { };

        internal sealed class BridgeError : Exception
        {
            public readonly string Code;
            public BridgeError(string code, string message) : base(message) { Code = code; }
        }

        public async Task<string> HandleAsync(string raw)
        {
            string id = null;
            try
            {
                var msg = Json.Deserialize<Dictionary<string, object>>(raw);
                id = Str(msg, "id");
                string cmd = Str(msg, "cmd");
                var args = msg.ContainsKey("args") && msg["args"] is Dictionary<string, object> d
                    ? d : new Dictionary<string, object>();

                object data = await Dispatch(cmd, args);
                return Json.Serialize(D("id", id, "ok", true, "data", data));
            }
            catch (BridgeError be)
            {
                return Json.Serialize(D("id", id, "ok", false, "code", be.Code, "error", be.Message));
            }
            catch (Exception ex)
            {
                return Json.Serialize(D("id", id, "ok", false, "code", "error", "error", ex.Message));
            }
        }

        private static async Task<object> Dispatch(string cmd, Dictionary<string, object> a)
        {
            switch (cmd)
            {
                case "version":
                    return D("core", Bootstrapper.CoreVersion,
                             "browser", Bootstrapper.BrowserEngineAvailable,
                             "dataDir", VaultInterop.GetDataDir());

                case "stats":
                {
                    var (r, s) = VaultInterop.GetStats();
                    Check(r);
                    return D("total", s.TotalEntries, "notes", s.NoteCount,
                             "snippets", s.SnippetCount, "commands", s.CommandCount,
                             "unlocked", VaultInterop.NotesIsUnlocked());
                }

                case "recent":
                {
                    var (r, list) = VaultInterop.RecentEntries(IntArg(a, "limit", 12));
                    Check(r);
                    return list.Select(Dto).ToList();
                }

                case "list":
                {
                    string section = Str(a, "section");
                    int type = section == "notes" ? (int)QVEntryType.Note
                             : section == "snippets" ? (int)QVEntryType.Snippet
                             : section == "commands" ? (int)QVEntryType.Command
                             : -1;
                    var (r, list) = VaultInterop.ListEntries(type, section == "favorites");
                    Check(r);
                    return list.Select(Dto).ToList();
                }

                case "search":
                {
                    var (r, list) = VaultInterop.SearchEntries(Str(a, "q"), -1, true, 200);
                    Check(r);
                    return list.Select(Dto).ToList();
                }

                case "get":
                {
                    var (r, e) = VaultInterop.GetEntry(Str(a, "id"));
                    Check(r);
                    RequireUnlockedFor(e);
                    return Dto(e);
                }

                case "create":
                {
                    if (!Enum.TryParse(Str(a, "type"), true, out QVEntryType type))
                        throw new BridgeError("error", "Unknown entry type.");
                    if (type == QVEntryType.Note && !VaultInterop.NotesIsUnlocked())
                        throw new BridgeError("locked", "Notes are locked.");
                    var (r, id) = VaultInterop.CreateEntry(type, "Untitled", "", "", "", "", false);
                    Check(r);
                    return id;
                }

                case "update":
                {
                    string title = Str(a, "title");
                    if (title.Trim().Length == 0) title = "Untitled";
                    Check(VaultInterop.UpdateEntry(Str(a, "id"), title, Str(a, "content"), Str(a, "tags"),
                                                   Str(a, "description"), Str(a, "language"),
                                                   Bool(a, "dpapi") ? 1 : 0));
                    return true;
                }

                case "delete":
                    Check(VaultInterop.DeleteEntry(Str(a, "id")));
                    return true;

                case "duplicate":
                {
                    var (r, nid) = VaultInterop.DuplicateEntry(Str(a, "id"));
                    Check(r);
                    return nid;
                }

                case "favorite":
                    Check(VaultInterop.SetFavorite(Str(a, "id"), Bool(a, "value")));
                    return true;

                case "unlock":
                {
                    var r = VaultInterop.NotesSetPassword(Str(a, "password"));
                    if (r == QVResult.BadPassword) throw new BridgeError("badpassword", "Incorrect password.");
                    Check(r);
                    return true;
                }

                case "changePassword":
                    Check(VaultInterop.NotesChangePassword(Str(a, "oldPassword"), Str(a, "newPassword")));
                    return true;

                case "export":
                    return DoExport();

                case "import":
                    return DoImport();

                case "backup":
                {
                    var (r, path) = VaultInterop.Backup();
                    Check(r);
                    return path;
                }

                case "reminders":
                    return Reminders();

                case "ask":
                    return await AssistantRouter.Handle(Str(a, "text"));

                case "doneReminder":
                {
                    var rem = ReminderStore.All.FirstOrDefault(x => x.Id == Str(a, "id"));
                    if (rem != null) { rem.Done = !rem.Done; ReminderStore.Save(); }
                    return Reminders();
                }

                case "clearDone":
                    ReminderStore.All.RemoveAll(x => x.Done);
                    ReminderStore.Save();
                    return Reminders();

                case "fetch":
                    return await FetchText(Str(a, "url"));

                case "openExternal":
                    Process.Start(Normalize(Str(a, "url")));
                    return true;

                default:
                    var extra = await AssistantCommands.TryHandle(cmd, a);
                    if (extra != AssistantCommands.Unhandled) return extra;
                    throw new BridgeError("error", "Unknown command: " + cmd);
            }
        }

        // ── Reminders ───────────────────────────────────────────────────────
        private static object Reminders() =>
            ReminderStore.All.OrderBy(r => r.Done).ThenBy(r => r.Due)
                .Select(r => D("id", r.Id, "text", r.Text, "due", r.Due.ToString("o"), "done", r.Done))
                .ToList();


        // ── Import / export dialogs ─────────────────────────────────────────
        private static object DoExport()
        {
            var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "quantum-vault-export.json" };
            if (dlg.ShowDialog() != true) return null;
            Check(VaultInterop.ExportJson(dlg.FileName, true));
            return dlg.FileName;
        }

        private static object DoImport()
        {
            var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return null;
            var (r, imported, skipped, errors) = VaultInterop.ImportJson(dlg.FileName, true);
            Check(r);
            return D("imported", imported, "skipped", skipped, "errors", errors);
        }

        // ── Browser tool (text mode; page scripts are not executed) ─────────
        private static async Task<object> FetchText(string input)
        {
            string url = Normalize(input);
            string html = await Http.GetStringAsync(url);
            return D("url", url, "text", HtmlToText(html));
        }

        private static string Normalize(string input)
        {
            string t = (input ?? "").Trim();
            if (t.Length == 0) return "https://duckduckgo.com/";
            if (t.Contains(" ") || !t.Contains("."))
                return "https://duckduckgo.com/html/?q=" + Uri.EscapeDataString(t);
            if (!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                t = "https://" + t;
            return t;
        }

        private static string HtmlToText(string html)
        {
            string s = Regex.Replace(html, @"<(script|style|noscript)[^>]*>.*?</\1>", " ",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            s = Regex.Replace(s, @"<br\s*/?>|</(p|div|li|h[1-6]|tr)>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", " ");
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"[ \t]+", " ");
            s = Regex.Replace(s, @"\n\s*\n+", "\n\n");
            return s.Trim();
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private static void Check(QVResult r)
        {
            if (r != QVResult.OK) throw new BridgeError("error", VaultInterop.ErrorString(r));
        }

        private static void RequireUnlockedFor(VaultEntry e)
        {
            if (e != null && e.Type == QVEntryType.Note && !VaultInterop.NotesIsUnlocked())
                throw new BridgeError("locked", "Notes are locked.");
        }

        private static Dictionary<string, object> Dto(VaultEntry e) =>
            D("id", e.Id, "title", e.Title, "content", e.Content, "tags", e.Tags,
              "description", e.Description, "language", e.Language, "dpapi", e.IsDpapi,
              "favorite", e.IsFavorite, "type", e.Type.ToString().ToLowerInvariant(),
              "typeLabel", e.TypeLabel, "updated", e.UpdatedAt.ToString("o"));

        private static Dictionary<string, object> D(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        private static string Str(Dictionary<string, object> a, string k) =>
            a.ContainsKey(k) && a[k] != null ? a[k].ToString() : "";

        private static bool Bool(Dictionary<string, object> a, string k) =>
            a.ContainsKey(k) && a[k] is bool b && b;

        private static int IntArg(Dictionary<string, object> a, string k, int fallback) =>
            a.ContainsKey(k) && int.TryParse(a[k]?.ToString(), out int v) ? v : fallback;

        private static HttpClient CreateHttp()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("QuantumVault/1.0 (text browser)");
            return c;
        }
    }
}





