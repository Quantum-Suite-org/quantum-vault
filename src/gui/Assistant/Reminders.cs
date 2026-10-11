using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace QuantumVault
{
    [DataContract]
    public sealed class Reminder
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public DateTime Due { get; set; }
        [DataMember] public bool Done { get; set; }
        [DataMember] public bool Notified { get; set; }

        public string Display =>
            (Done ? "✓  " : "○  ") + Text + "   ·   " + Due.ToString("ddd d MMM HH:mm");
    }

    public static class ReminderStore
    {
        private static readonly object Gate = new object();

        public static List<Reminder> All { get; private set; } = new List<Reminder>();

        private static string FilePath =>
            Path.Combine(VaultInterop.GetDataDir(), "reminders.json");

        public static void Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) { All = new List<Reminder>(); return; }
                using (var fs = File.OpenRead(path))
                {
                    var ser = new DataContractJsonSerializer(typeof(List<Reminder>));
                    All = (List<Reminder>)ser.ReadObject(fs) ?? new List<Reminder>();
                }
            }
            catch
            {
                All = new List<Reminder>();
            }
        }

        public static void Save()
        {
            lock (Gate)
            {
                try
                {
                    string path = FilePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var fs = File.Create(path))
                    {
                        new DataContractJsonSerializer(typeof(List<Reminder>)).WriteObject(fs, All);
                    }
                }
                catch { }
            }
        }

        public static void Add(Reminder r)
        {
            All.Add(r);
            Save();
        }

        public static List<Reminder> TakeDue(DateTime now)
        {
            var due = All.Where(r => !r.Done && !r.Notified && r.Due <= now).ToList();
            foreach (var r in due) r.Notified = true;
            if (due.Count > 0) Save();
            return due;
        }
    }

    public static class ReminderParser
    {
        private static readonly Regex Lead = new Regex(
            @"^\s*(please\s+)?(remind me|set (a )?reminder|reminder)\s*(to\s+)?", RegexOptions.IgnoreCase);
        private static readonly Regex AtTime = new Regex(
            @"\bat\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\b", RegexOptions.IgnoreCase);
        private static readonly Regex InSpan = new Regex(
            @"\bin\s+(\d+)\s*(minutes?|mins?|hours?|hrs?|days?)\b", RegexOptions.IgnoreCase);

        public static Reminder Parse(string input, DateTime now, out string reply)
        {
            string text = Lead.Replace(input.Trim(), "", 1);
            DateTime due = now.AddHours(1);
            bool defaulted = true;

            var span = InSpan.Match(text);
            if (span.Success)
            {
                int n = int.Parse(span.Groups[1].Value);
                string unit = span.Groups[2].Value.ToLowerInvariant();
                due = unit.StartsWith("m") ? now.AddMinutes(n)
                    : unit.StartsWith("h") ? now.AddHours(n)
                    : now.AddDays(n);
                text = text.Remove(span.Index, span.Length);
                defaulted = false;
            }
            else
            {
                DateTime day = now.Date;
                bool hasDay = false;

                if (Regex.IsMatch(text, @"\btomorrow\b", RegexOptions.IgnoreCase))
                {
                    day = now.Date.AddDays(1);
                    hasDay = true;
                    text = Regex.Replace(text, @"\btomorrow\b", "", RegexOptions.IgnoreCase);
                }
                else if (Regex.IsMatch(text, @"\btoday\b", RegexOptions.IgnoreCase))
                {
                    hasDay = true;
                    text = Regex.Replace(text, @"\btoday\b", "", RegexOptions.IgnoreCase);
                }

                var at = AtTime.Match(text);
                if (at.Success)
                {
                    int h = int.Parse(at.Groups[1].Value);
                    int m = at.Groups[2].Success ? int.Parse(at.Groups[2].Value) : 0;
                    string ap = at.Groups[3].Value.ToLowerInvariant();

                    if (ap == "pm" && h < 12) h += 12;
                    else if (ap == "am" && h == 12) h = 0;
                    else if (ap == "" && h < 7) h += 12;

                    if (h < 24 && m < 60)
                    {
                        due = day.AddHours(h).AddMinutes(m);
                        if (!hasDay && due <= now) due = due.AddDays(1);
                        text = text.Remove(at.Index, at.Length);
                        defaulted = false;
                    }
                }
                else if (hasDay)
                {
                    due = day.AddHours(9);
                    defaulted = false;
                }
            }

            text = Regex.Replace(text, @"\s{2,}", " ").Trim(' ', ',', '.', ':', '-');
            if (text.Length == 0) text = "Reminder";

            reply = "Saved \"" + text + "\" for " + due.ToString("ddd d MMM, HH:mm")
                  + (defaulted ? " (no time given, so I set 1 hour from now)" : "");

            return new Reminder { Id = Guid.NewGuid().ToString("N"), Text = text, Due = due };
        }
    }
}
