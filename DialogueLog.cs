using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using StardewValley;

namespace NpcDialogueLog
{
    public class DialogueEntry
    {
        public string NpcName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Text { get; set; } = "";
        public string Date { get; set; } = "";
    }

    public static class DialogueLog
    {
        // Matches #$b#, #$e#, #$q <n> <id>#, #$r <n> <id>#, #$p <id>#, etc.
        private static readonly Regex CommandPattern = new(@"#\$[a-zA-Z][^#]*#", RegexOptions.Compiled);
        // Matches %revealtaste, %fork, %adj, %noun, %place, %name, %firstnameletter
        private static readonly Regex PercentToken = new(@"%[a-zA-Z]+", RegexOptions.Compiled);

        private static List<DialogueEntry> _entries = new();
        private static int _maxEntries = 600;

        public static IReadOnlyList<DialogueEntry> Entries => _entries;

        public static void Configure(int maxEntries)
        {
            _maxEntries = maxEntries;
        }

        public static void Load(List<DialogueEntry>? saved)
        {
            _entries = saved ?? new List<DialogueEntry>();
        }

        public static List<DialogueEntry> GetSaveData() => _entries;

        public static void Add(NPC? speaker, string rawText)
        {
            string cleaned = CleanText(rawText);
            if (string.IsNullOrWhiteSpace(cleaned))
                return;

            // Deduplicate: skip if the last entry for this NPC is identical text
            string npcKey = speaker?.Name ?? "Narrator";
            if (_entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (last.NpcName == npcKey && last.Text == cleaned)
                    return;
            }

            _entries.Add(new DialogueEntry
            {
                NpcName = npcKey,
                DisplayName = speaker?.displayName ?? npcKey,
                Text = cleaned,
                Date = $"{char.ToUpper(Game1.currentSeason[0])}{Game1.currentSeason[1..]} {Game1.dayOfMonth}, Year {Game1.year}"
            });

            // Trim to max
            if (_entries.Count > _maxEntries)
                _entries.RemoveRange(0, _entries.Count - _maxEntries);
        }

        public static void AddNarrator(string rawText)
        {
            Add(null, rawText);
        }

        private static string CleanText(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "";

            string s = raw;

            // Replace player name token
            s = s.Replace("@", Game1.player?.Name ?? "");

            // Gender switch: "male text^female text" - pick based on player gender
            if (s.Contains('^'))
            {
                string[] parts = s.Split('^');
                // 0 = male form, 1 = female form (SDV convention)
                bool isFemale = Game1.player?.IsMale == false;
                s = parts.Length >= 2 ? parts[isFemale ? 1 : 0] : parts[0];
            }

            // Strip SDV command codes like #$b#, #$e#, #$q 0 -1#, etc.
            s = CommandPattern.Replace(s, " ");

            // Strip % tokens
            s = PercentToken.Replace(s, "");

            // Collapse whitespace
            s = Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }
    }
}
