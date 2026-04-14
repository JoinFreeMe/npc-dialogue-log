# NPC Dialogue Log

A Stardew Valley mod that records every line of NPC dialogue spoken to you and lets you review it at any time.

Ever clicked through an NPC's dialogue too fast and wished you could read it again? Press **L** to open the log. Browse everything you've been told, or click a character's name to filter to just their lines. The log saves with your farm so nothing is ever lost between sessions.

## Features

- Logs every line of NPC dialogue as it happens
- Filter by character using the buttons at the top
- Scrollable list, newest entries first
- Persists across sessions - saved per farm file
- Works at any window size or resolution, including while resizing
- Multiplayer friendly - each player logs their own dialogue
- Works with modded NPCs (Stardew Valley Expanded, Ridgeside Village, etc.)
- Configurable via [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) or config.json

## Requirements

- **Stardew Valley** 1.6 or later
- **SMAPI** 4.0 or later - [download here](https://smapi.io)

## Installation

1. Install [SMAPI](https://smapi.io) if you haven't already
2. Download the latest release from the [Releases](../../releases) page
3. Unzip into your `Stardew Valley/Mods/` folder
4. Launch the game through SMAPI

## Usage

Press **L** (configurable) at any time to open the dialogue log. Click an NPC's name at the top to filter to just their conversations. Scroll to browse older entries.

## Configuration

A `config.json` is created on first launch. You can also configure in-game with [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098).

| Option | Default | Description |
|---|---|---|
| `OpenLogKey` | `L` | Key to open the log |
| `MaxEntries` | `600` | Maximum entries kept |
| `LogNarratorDialogue` | `false` | Also log dialogue with no NPC speaker |
| `ShowDateInLog` | `true` | Show the in-game date next to each entry |

## Compatibility

- Works with **Stardew Valley Expanded** and other NPC expansion mods
- Works with **Canon-Friendly Dialogue Expansion** and other dialogue mods
- Multiplayer and split-screen compatible
- No known mod conflicts

## Building from Source

Requires .NET 6 SDK and Stardew Valley + SMAPI installed.

```bash
dotnet build
```

The mod will be automatically deployed to your `Stardew Valley/Mods/` folder.

## License

[MIT](LICENSE)

## Links

- [CurseForge](https://www.curseforge.com/stardewvalley/mods/npc-dialogue-log)
- [Discord](https://discord.com/invite/aCE6HqfCHj)
