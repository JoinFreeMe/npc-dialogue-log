using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace NpcDialogueLog
{
    public class ModConfig
    {
        public KeybindList OpenLogKey { get; set; } = KeybindList.Parse("L");
        public int MaxEntries { get; set; } = 10000;
        public bool LogNarratorDialogue { get; set; } = false;
        public bool ShowDateInLog { get; set; } = true;
        public bool UseInternalNames { get; set; } = false;
    }
}
