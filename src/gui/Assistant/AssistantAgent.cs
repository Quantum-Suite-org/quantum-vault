using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuantumVault
{
    /// <summary>
    /// The assistant agent. The local model may call tools by replying with one line:
    ///   TOOL: name | argument
    /// The tool result is fed back and the model answers. At most MaxSteps tool calls per question.
    /// </summary>
    public static class AssistantAgent
    {
        private const int MaxSteps = 4;
        private const int AnswerTokens = 400;
        private static readonly HttpClient Http = CreateHttp();
        private static readonly Regex ToolLine = new Regex(@"TOOL:\s*([a-z_]+)\s*\|\s*(.+)", RegexOptions.IgnoreCase);

        public static async Task<string> RunAsync(string question)
        {
            if (!ModelManager.IsInstalled())
                return "The assistant model isn't downloaded yet. Open the Assistant page and click Download model.";

            var convo = new StringBuilder();
            convo.Append("<|im_start|>system\n").Append(SystemPrompt()).Append("<|im_end|>\n");
            convo.Append("<|im_start|>user\n").Append(question).Append("<|im_end|>\n");

            string reply = "";
            for (int step = 0; step <= MaxSteps; step++)
            {
                convo.Append("<|im_start|>assistant\n");
                reply = Clean(await ChatAssistant.GenerateAsync(convo.ToString(), AnswerTokens));

                var m = ToolLine.Match(reply);
                if (!m.Success || step == MaxSteps) break;

                string tool = m.Groups[1].Value.ToLowerInvariant();
                string arg = m.Groups[2].Value.Trim();
                string result = await RunToolAsync(tool, arg);

                convo.Append(reply).Append("<|im_end|>\n");
                convo.Append("<|im_start|>user\nTOOL RESULT (").Append(tool).Append("):\n")
                     .Append(result)
                     .Append("\nNow answer the user in plain language. Do not repeat the tool line.<|im_end|>\n");
            }

            // If the model still only produced a tool line, say so rather than show raw syntax.
            if (ToolLine.IsMatch(reply)) return "I couldn't finish looking that up. Try asking again more simply.";
            return reply.Length == 0 ? "I don't have an answer for that." : reply;
        }

        // ── Tools ───────────────────────────────────────────────────────────
        private static async Task<string> RunToolAsync(string tool, string arg)
        {
            try
            {
                switch (tool)
                {
                    case "search_vault": return SearchVault(arg);
                    case "search_web":   return await SearchWebAsync(arg);
                    case "fetch_page":   return await FetchPageAsync(arg);
                    case "add_reminder":
                    {
                        var r = ReminderParser.Parse(arg, DateTime.Now, out string reply);
                        ReminderStore.Add(r);
                        return reply;
                    }
                    case "list_reminders": return UpcomingReminders();
                    case "calculate":
                        return SafeMath.TryEval(arg, out double v)
                            ? arg + " = " + v.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)
                            : "Could not calculate that expression.";
                    default:
                        return "Unknown tool \"" + tool + "\". Use one of: search_vault, search_web, fetch_page, add_reminder, list_reminders, calculate.";
                }
            }
            catch (Exception ex)
            {
                return "The tool failed: " + ex.Message;
            }
        }

        private static string SearchVault(string words)
        {
            bool unlocked = VaultInterop.NotesIsUnlocked();
            var (r, list) = VaultInterop.SearchEntries(words, -1, unlocked, 10);
            if (r != QVResult.OK) return "Vault search failed.";
            if (list.Count == 0) return "No vault entries matched.";

            var lines = new List<string>();
            foreach (var item in list)
            {
                bool hidden = item.Type == QVEntryType.Note && (!unlocked || item.IsDpapi);
                if (hidden) { lines.Add("- " + item.Title + " [" + item.TypeLabel + "] (content not shared)"); continue; }

                var (gr, full) = VaultInterop.GetEntry(item.Id);
                string body = gr == QVResult.OK && full != null ? Clip(full.Content, 500) : "";
                lines.Add("- " + item.Title + " [" + item.TypeLabel + "]\n" + body);
            }
            return string.Join("\n", lines);
        }

        private static string UpcomingReminders()
        {
            var upcoming = ReminderStore.All.Where(r => !r.Done).OrderBy(r => r.Due).Take(8).ToList();
            if (upcoming.Count == 0) return "No pending reminders.";
            return string.Join("\n", upcoming.Select(r => "- " + r.Text + " at " + r.Due.ToString("ddd d MMM HH:mm")));
        }

        private static async Task<string> SearchWebAsync(string query)
        {
            string html = await Http.GetStringAsync("https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query));
            var titles = Regex.Matches(html, "class=\"result__a\"[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var snippets = Regex.Matches(html, "class=\"result__snippet\"[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var lines = new List<string>();
            for (int i = 0; i < Math.Min(5, titles.Count); i++)
            {
                string title = Plain(titles[i].Groups[1].Value);
                string snip = i < snippets.Count ? Plain(snippets[i].Groups[1].Value) : "";
                lines.Add((i + 1) + ". " + title + (snip.Length > 0 ? " - " + snip : ""));
            }
            return lines.Count == 0 ? "No web results found." : string.Join("\n", lines);
        }

        private static async Task<string> FetchPageAsync(string url)
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            string html = await Http.GetStringAsync(url);
            string text = Regex.Replace(html, @"<(script|style|noscript)[^>]*>.*?</\1>", " ",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Plain(text);
            text = Regex.Replace(text, @"\s{2,}", " ");
            return Clip(text, 2000);
        }

        // ── Prompt ──────────────────────────────────────────────────────────
        private static string SystemPrompt()
        {
            var (_, s) = VaultInterop.GetStats();
            bool unlocked = VaultInterop.NotesIsUnlocked();
            string state = "The vault has " + s.NoteCount + " notes, " + s.SnippetCount + " snippets and " +
                           s.CommandCount + " commands. Notes are " + (unlocked ? "unlocked" : "locked") + ".";

            return
"You are the built-in assistant of Quantum Vault, a Windows app that keeps notes, snippets and commands " +
"encrypted on this PC. The app works offline. You run locally as Qwen2.5-0.5B-Instruct.\n\n" +
"APP LAYOUT\n" +
"- Dashboard: counts and recent entries, with an Add New button.\n" +
"- Vault: a list of entries (All, Notes, Snippets, Commands, Favorites), search, and an editor. Notes need the master " +
"password and stay locked until the user unlocks them. Notes with 'Windows lock' turned on can only be read by this " +
"Windows account and are never shared with you.\n" +
"- Assistant: the chat where you answer questions.\n" +
"- Browser: loads a web page as plain text. Scripts do not run.\n" +
"- Security: encryption and vault health.\n" +
"- Settings: change the master password, export, import and backup.\n" +
"- Reminders: saved on this PC; a popup appears when one is due.\n\n" +
"TOOLS\n" +
"To use a tool, reply with exactly one line and nothing else:\n" +
"TOOL: <name> | <argument>\n" +
"- search_vault | words          find entries in the vault\n" +
"- search_web | query           search the internet (titles and snippets)\n" +
"- fetch_page | url             read the text of a web page\n" +
"- add_reminder | text          save a reminder, e.g. call mom tomorrow at 5pm\n" +
"- list_reminders | -           list upcoming reminders\n" +
"- calculate | expression       do arithmetic\n" +
"Examples:\n" +
"User: what is the capital of Peru? -> TOOL: search_web | capital of Peru\n" +
"User: do I have notes about taxes? -> TOOL: search_vault | taxes\n" +
"User: what is 12 * (3 + 4)? -> TOOL: calculate | 12 * (3 + 4)\n" +
"After a tool result arrives, answer the user in plain language.\n\n" +
"LIMITS\n" +
"You cannot delete, edit or unlock entries, read locked notes, or change settings. " +
"You cannot see anything outside this app except through search_web and fetch_page.\n\n" +
"CURRENT STATE\n" + state + "\n\n" +
"RULES\n" +
"- Be concise and friendly.\n" +
"- Never invent vault contents. If unsure, use a tool.\n" +
"- Say so when you are not sure of something.\n" +
"- If asked what you can do, describe the layout and tools above in plain words.";
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private static string Clean(string s) =>
            (s ?? "").Replace("<|im_end|>", "").Replace("<|im_start|>", "").Trim();

        private static string Plain(string s) =>
            WebUtility.HtmlDecode(Regex.Replace(s ?? "", "<[^>]+>", " ")).Trim();

        private static string Clip(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max) + "…";

        private static HttpClient CreateHttp()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) QuantumVault/1.0");
            return c;
        }
    }
}
