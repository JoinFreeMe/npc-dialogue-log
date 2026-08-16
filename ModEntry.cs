using System;
using System.Collections.Generic;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace NpcDialogueLog
{
    public class ModEntry : Mod
    {
        private ModConfig _config = null!;
        private static IMonitor? _monitor;
        internal static string ModVersion = "";
        internal static string ModFolderPath = "";

        public override void Entry(IModHelper helper)
        {
            _config = helper.ReadConfig<ModConfig>();
            _monitor = Monitor;
            ModVersion = ModManifest.Version.ToString();
            ModFolderPath = helper.DirectoryPath;

            // Migrate old default (600) to new default (10000)
            if (_config.MaxEntries == 600)
            {
                _config.MaxEntries = 10000;
                helper.WriteConfig(_config);
            }

            DialogueLog.Configure(_config.MaxEntries);
            _narratorEnabled = _config.LogNarratorDialogue;
            _overheadEnabled = _config.LogOverheadText;
            UseInternalNames = _config.UseInternalNames;
            NewestFirst = _config.NewestFirst;
            ShowExpressionInLog = _config.ShowExpressionInLog;
            SaveSortOrder = v =>
            {
                _config.NewestFirst = v;
                Helper.WriteConfig(_config);
            };

            // Harmony patches
            var harmony = new Harmony(ModManifest.UniqueID);
            // Constructor postfix: captures page 0 when a dialogue box first opens
            harmony.Patch(
                original: AccessTools.Constructor(typeof(DialogueBox), new[] { typeof(Dialogue) }),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(DialogueBox_Dialogue_Postfix))
            );
            harmony.Patch(
                original: AccessTools.Constructor(typeof(DialogueBox), new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(DialogueBox_String_Postfix))
            );
            // receiveLeftClick prefix: captures the current page just before the player advances,
            // covering pages 1, 2, 3 … (page 0 is already caught by the constructor postfix above)
            harmony.Patch(
                original: AccessTools.Method(typeof(DialogueBox), nameof(DialogueBox.receiveLeftClick),
                    new[] { typeof(int), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(DialogueBox_ReceiveLeftClick_Prefix))
            );
            // Overhead text bubbles (e.g. "Hi @!", festival shouts) - opt-in via config
            harmony.Patch(
                original: AccessTools.Method(typeof(NPC), nameof(NPC.showTextAboveHead)),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(NPC_ShowTextAboveHead_Postfix))
            );

            // Events
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayEnding  += OnDayEnding;
            helper.Events.Input.ButtonsChanged += OnButtonsChanged;
        }

        // ── Harmony postfixes ──────────────────────────────────────────────────

        [HarmonyPostfix]
        static void DialogueBox_Dialogue_Postfix(Dialogue dialogue)
        {
            try
            {
                if (dialogue == null) return;
                // Log page 0 - subsequent pages are caught by DialogueBox_ReceiveLeftClick_Prefix
                string? text = dialogue.getCurrentDialogue();
                if (!string.IsNullOrEmpty(text))
                    DialogueLog.Add(dialogue.speaker, text, dialogue.getPortraitIndex());
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[NpcDialogueLog] Error in Dialogue ctor patch: {ex.Message}", LogLevel.Warn);
            }
        }

        [HarmonyPrefix]
        static void DialogueBox_ReceiveLeftClick_Prefix(DialogueBox __instance)
        {
            try
            {
                // Grab the Dialogue object attached to this box
                var charDialogue = AccessTools.Field(typeof(DialogueBox), "characterDialogue")
                    ?.GetValue(__instance) as Dialogue;
                if (charDialogue == null) return;

                // getCurrentDialogue() returns the page currently on screen, before the click advances it
                string? text = charDialogue.getCurrentDialogue();
                if (!string.IsNullOrEmpty(text))
                    DialogueLog.Add(charDialogue.speaker, text, charDialogue.getPortraitIndex());
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[NpcDialogueLog] Error in receiveLeftClick patch: {ex.Message}", LogLevel.Warn);
            }
        }

        [HarmonyPostfix]
        static void NPC_ShowTextAboveHead_Postfix(NPC __instance, string text)
        {
            try
            {
                if (!_overheadEnabled) return;
                if (__instance == null || string.IsNullOrWhiteSpace(text)) return;
                DialogueLog.Add(__instance, text);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[NpcDialogueLog] Error in showTextAboveHead patch: {ex.Message}", LogLevel.Warn);
            }
        }

        [HarmonyPostfix]
        static void DialogueBox_String_Postfix(string dialogue)
        {
            try
            {
                // Only log narrator dialogue if the option is enabled.
                // We need the config - access via static field isn't ideal, but ModConfig
                // is small and this fires infrequently.
                if (!_narratorEnabled) return;
                if (!string.IsNullOrWhiteSpace(dialogue))
                    DialogueLog.AddNarrator(dialogue);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[NpcDialogueLog] Error in string Dialogue patch: {ex.Message}", LogLevel.Warn);
            }
        }

        // Set by Entry() after config load so static postfix can read it
        private static bool _narratorEnabled = false;
        private static bool _overheadEnabled = false;

        // Read by DialogueLogMenu.DisplayOf() to choose internal vs localized NPC names
        internal static bool UseInternalNames = false;

        // The menu's sort button flips this and calls SaveSortOrder so it persists
        internal static bool NewestFirst = true;
        internal static Action<bool>? SaveSortOrder;

        // Read by DialogueLogMenu to decide whether to name the expression in each entry
        internal static bool ShowExpressionInLog = true;

        // ── SMAPI events ───────────────────────────────────────────────────────

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm is null) return;

            gmcm.Register(
                mod: ModManifest,
                reset: () => _config = new ModConfig(),
                save: () =>
                {
                    Helper.WriteConfig(_config);
                    _narratorEnabled = _config.LogNarratorDialogue;
                    _overheadEnabled = _config.LogOverheadText;
                    UseInternalNames = _config.UseInternalNames;
                    NewestFirst = _config.NewestFirst;
                    ShowExpressionInLog = _config.ShowExpressionInLog;
                    DialogueLog.Configure(_config.MaxEntries);
                }
            );

            gmcm.AddKeybindList(
                mod: ModManifest,
                getValue: () => _config.OpenLogKey,
                setValue: v => _config.OpenLogKey = v,
                name: () => "Open Log Key",
                tooltip: () => "Keybind to open the dialogue log."
            );

            gmcm.AddNumberOption(
                mod: ModManifest,
                getValue: () => _config.MaxEntries,
                setValue: v => _config.MaxEntries = v,
                name: () => "Max Entries",
                tooltip: () => "Maximum number of dialogue entries kept in the log. Older entries are dropped once the limit is reached.",
                // Max must reach the config default, or saving here silently trims the log
                min: 100,
                max: 10000,
                interval: 100
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                getValue: () => _config.LogNarratorDialogue,
                setValue: v => _config.LogNarratorDialogue = v,
                name: () => "Log Narrator Dialogue",
                tooltip: () => "Also record narrator / story text (not spoken by an NPC)."
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                getValue: () => _config.LogOverheadText,
                setValue: v => _config.LogOverheadText = v,
                name: () => "Log Overhead Text",
                tooltip: () => "Also record short text bubbles shown above NPC heads (e.g. \"Hi @!\", festival shouts). Off by default - these fire frequently."
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                getValue: () => _config.ShowDateInLog,
                setValue: v => _config.ShowDateInLog = v,
                name: () => "Show Date in Log",
                tooltip: () => "Display the in-game date next to each log entry."
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                getValue: () => _config.ShowExpressionInLog,
                setValue: v => _config.ShowExpressionInLog = v,
                name: () => "Name Expressions",
                tooltip: () => "Also write the expression (Happy, Sad, Angry...) next to each logged line as text. The portrait always shows it either way."
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                getValue: () => _config.NewestFirst,
                setValue: v => _config.NewestFirst = v,
                name: () => "Newest Entries First",
                tooltip: () => "Show the most recent dialogue at the top. Turn off to read oldest to newest. Can also be toggled with the sort button in the log."
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                getValue: () => _config.UseInternalNames,
                setValue: v => _config.UseInternalNames = v,
                name: () => "Use Internal NPC Names",
                tooltip: () => "Show internal English NPC names (e.g. \"Abigail\") instead of localized display names. Affects sidebar, headers, search, and the A-Z letter index."
            );
        }

        // One log file per player, per save.
        private string LogPath =>
            $"logs/{Game1.uniqueIDForThisGame:X}-{Game1.player.UniqueMultiplayerID:X}.json";

        private bool _logUnreadable;

        private List<DialogueEntry>? ReadLog()
        {
            _logUnreadable = false;
            try
            {
                return Helper.Data.ReadJsonFile<List<DialogueEntry>>(LogPath);
            }
            catch (Exception ex)
            {
                _logUnreadable = true;
                Monitor.Log($"Couldn't read the dialogue log, starting empty. {ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            var saved = ReadLog();

            // Import logs from earlier versions, which stored them in the save.
            // Skipped when a log file exists but can't be read, so a damaged file
            // is never replaced with an older copy.
            if (saved is null && !_logUnreadable && Context.IsMainPlayer)
            {
                saved = Helper.Data.ReadSaveData<List<DialogueEntry>>("dialogue-log");
                if (saved is not null)
                    Helper.Data.WriteJsonFile(LogPath, saved);
            }

            DialogueLog.Load(saved);
            _narratorEnabled = _config.LogNarratorDialogue;
            _overheadEnabled = _config.LogOverheadText;
            UseInternalNames = _config.UseInternalNames;
            NewestFirst = _config.NewestFirst;
            ShowExpressionInLog = _config.ShowExpressionInLog;
        }

        // DayEnding is raised for every player; Saving isn't.
        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            Helper.Data.WriteJsonFile(LogPath, DialogueLog.GetSaveData());
        }

        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!_config.OpenLogKey.JustPressed()) return;

            if (Game1.activeClickableMenu is DialogueLogMenu openLog)
            {
                openLog.exitThisMenu();
                return;
            }

            var previous = Game1.activeClickableMenu;
            Game1.activeClickableMenu = new DialogueLogMenu(onClose: () =>
            {
                Game1.activeClickableMenu = previous;
            });
        }
    }
}
