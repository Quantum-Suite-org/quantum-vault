using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuantumVault
{
    /// <summary>
    /// The assistant does exactly four things: search the vault, save/list reminders, do maths,
    /// and brief small talk. Everything else gets a fixed refusal.
    /// </summary>
    public static class AssistantRouter
    {
        private const string OutOfScope =
            "I can search your vault, save reminders, do quick maths and chat briefly. I can't help with anything else here.";

        private static readonly Regex Remind = new Regex(@"\bremind(er)?\b", RegexOptions.IgnoreCase);
        private static readonly Regex Due = new Regex(@"what.*\bdue\b|\bupcoming\b|\bmy reminders\b", RegexOptions.IgnoreCase);
        private static readonly Regex Search = new Regex(
            @"^\s*(search( for)?|find|look ?up|look for|where is)\s+(.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex MathLine = new Regex(
            @"^\s*(?:calc(?:ulate)?|compute|what(?:'s| is)|=)?\s*([0-9.\s+\-*/()^%]+)$", RegexOptions.IgnoreCase);

        public static async Task<object> Handle(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0) return Result("Type something and I'll help.");

            if (Remind.IsMatch(t))
            {
                var r = ReminderParser.Parse(t, DateTime.Now, out string reply);
                ReminderStore.Add(r);
                return Result(reply);
            }

            if (Due.IsMatch(t)) return Result(UpcomingText());

            var s = Search.Match(t);
            if (s.Success) return Result(SearchVault(s.Groups[3].Value.Trim()));

            var m = MathLine.Match(t);
            if (m.Success)
            {
                string expr = m.Groups[1].Value.Trim();
                bool hasOp = expr.IndexOfAny("+-*/^%".ToCharArray()) >= 0;
                if (hasOp || t.StartsWith("calc", StringComparison.OrdinalIgnoreCase))
                {
                    if (SafeMath.TryEval(expr, out double v))
                        return Result(expr + " = " + v.ToString("0.########", CultureInfo.InvariantCulture));
                    return Result("I couldn't work that out. Try something like 12 * (3 + 4).");
                }
            }

            return Result(await AssistantAgent.RunAsync(t));
        }

        private static string SearchVault(string query)
        {
            if (query.Length == 0) return "What should I search for?";

            bool unlocked = VaultInterop.NotesIsUnlocked();
            var (r, list) = VaultInterop.SearchEntries(query, -1, unlocked, 15);
            if (r != QVResult.OK) return "Search failed: " + VaultInterop.ErrorString(r);
            if (list.Count == 0) return "Nothing matched \"" + query + "\".";

            string note = unlocked ? "" : "\n(Notes are locked, so only titles were searched.)";
            return "Found " + list.Count + ":\n" +
                   string.Join("\n", list.Select(e => "• " + e.Title + "  [" + e.TypeLabel + "]")) + note;
        }

        private static string UpcomingText()
        {
            var upcoming = ReminderStore.All.Where(r => !r.Done).OrderBy(r => r.Due).Take(5).ToList();
            if (upcoming.Count == 0) return "Nothing pending.";
            return string.Join("\n", upcoming.Select(r => "• " + r.Text + " — " + r.Due.ToString("ddd d MMM HH:mm")));
        }

        private static Dictionary<string, object> Result(string reply) => new Dictionary<string, object>
        {
            { "reply", reply },
            { "reminders", ReminderStore.All.OrderBy(r => r.Done).ThenBy(r => r.Due)
                .Select(r => (object)new Dictionary<string, object>
                {
                    { "id", r.Id }, { "text", r.Text }, { "due", r.Due.ToString("o") }, { "done", r.Done }
                }).ToList() }
        };
    }

    /// <summary>Small, safe arithmetic: + - * / % ^ and parentheses. No code is ever evaluated.</summary>
    public static class SafeMath
    {
        public static bool TryEval(string s, out double value)
        {
            value = 0;
            try
            {
                var p = new Parser(s);
                value = p.Expr();
                p.End();
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                return false;
            }
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s.Replace(" ", ""); }

            private char Peek => _i < _s.Length ? _s[_i] : '\0';

            public double Expr()
            {
                double v = Term();
                while (Peek == '+' || Peek == '-')
                {
                    char op = _s[_i++];
                    double r = Term();
                    v = op == '+' ? v + r : v - r;
                }
                return v;
            }

            private double Term()
            {
                double v = Factor();
                while (Peek == '*' || Peek == '/' || Peek == '%')
                {
                    char op = _s[_i++];
                    double r = Factor();
                    if (op == '*') v *= r;
                    else if (op == '/') { if (r == 0) throw new DivideByZeroException(); v /= r; }
                    else v %= r;
                }
                return v;
            }

            private double Factor()
            {
                double b = Unary();
                if (Peek == '^') { _i++; return Math.Pow(b, Factor()); }
                return b;
            }

            private double Unary()
            {
                if (Peek == '-') { _i++; return -Factor(); }
                if (Peek == '+') { _i++; return Factor(); }
                return Primary();
            }

            private double Primary()
            {
                if (Peek == '(')
                {
                    _i++;
                    double v = Expr();
                    if (Peek != ')') throw new FormatException();
                    _i++;
                    return v;
                }
                int start = _i;
                while (char.IsDigit(Peek) || Peek == '.') _i++;
                if (start == _i) throw new FormatException();
                return double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture);
            }

            public void End()
            {
                if (_i != _s.Length) throw new FormatException();
            }
        }
    }
}





