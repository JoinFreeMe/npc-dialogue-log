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

        public override void Entry(IModHelper helper)
        {
            _config = helper.ReadConfig<ModConfig>();
            _monitor = Monitor;

            DialogueLog.Configure(_config.MaxEntries);

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

            // Events
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.Saving     += OnSaving;
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
                    DialogueLog.Add(dialogue.speaker, text);
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
                    DialogueLog.Add(charDialogue.speaker, text);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[NpcDialogueLog] Error in receiveLeftClick patch: {ex.Message}", LogLevel.Warn);
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
                tooltip: () => "Maximum number of dialogue entries kept in the log.",
                min: 10,
                max: 2000
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
                getValue: () => _config.ShowDateInLog,
                setValue: v => _config.ShowDateInLog = v,
                name: () => "Show Date in Log",
                tooltip: () => "Display the in-game date next to each log entry."
            );
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            var saved = Helper.Data.ReadSaveData<List<DialogueEntry>>("dialogue-log");
            DialogueLog.Load(saved);
            _narratorEnabled = _config.LogNarratorDialogue;
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            Helper.Data.WriteSaveData("dialogue-log", DialogueLog.GetSaveData());
        }

        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (!Context.IsPlayerFree) return;
            if (_config.OpenLogKey.JustPressed())
                Game1.activeClickableMenu = new DialogueLogMenu();
        }
    }
}
