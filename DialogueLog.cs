using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        /// Portrait the NPC was showing, matching NPC.portrait_*_index.
        /// -1 means not recorded: narrator text, or an entry logged before 1.7.0.
        public int PortraitIndex { get; set; } = -1;
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

        public static void Add(NPC? speaker, string rawText, int portraitIndex = -1)
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
                Date = $"{char.ToUpper(Game1.currentSeason[0])}{Game1.currentSeason[1..]} {Game1.dayOfMonth}, Year {Game1.year}",
                PortraitIndex = portraitIndex
            });

            // Trim to max
            if (_entries.Count > _maxEntries)
                _entries.RemoveRange(0, _entries.Count - _maxEntries);
        }

        public static void AddNarrator(string rawText)
        {
            Add(null, rawText);
        }

        /// Friendly name for a portrait index, or null if nothing was recorded.
        public static string? ExpressionName(int portraitIndex) => portraitIndex switch
        {
            < 0                            => null,
            NPC.portrait_neutral_index     => "Neutral",
            NPC.portrait_happy_index       => "Happy",
            NPC.portrait_sad_index         => "Sad",
            NPC.portrait_custom_index      => "Unique",
            NPC.portrait_blush_index       => "Love",
            NPC.portrait_angry_index       => "Angry",
            _                              => $"Portrait {portraitIndex}"
        };

        public static string ExportAsText()
        {
            var sb = new StringBuilder();
            foreach (var e in _entries)
            {
                sb.Append(e.DisplayName);
                if (!string.IsNullOrEmpty(e.Date))
                    sb.Append("  •  ").Append(e.Date);
                if (ExpressionName(e.PortraitIndex) is string expression)
                    sb.Append("  •  ").Append(expression);
                sb.AppendLine();
                sb.AppendLine(e.Text);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// Export shape - readers get the expression name, not the raw portrait index.
        private sealed class ExportedEntry
        {
            public string NpcName { get; init; } = "";
            public string DisplayName { get; init; } = "";
            public string Text { get; init; } = "";
            public string Date { get; init; } = "";
            public string? Expression { get; init; }
        }

        public static string ExportAsJson()
        {
            var exported = _entries.Select(e => new ExportedEntry
            {
                NpcName = e.NpcName,
                DisplayName = e.DisplayName,
                Text = e.Text,
                Date = e.Date,
                Expression = ExpressionName(e.PortraitIndex)
            });

            return JsonSerializer.Serialize(exported, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                // Default escaping turns apostrophes into ', kanji into \uXXXX, etc.
                // Relaxed escaping keeps these readable while still escaping the JSON-required set.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
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
