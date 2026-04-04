using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace NpcDialogueLog
{
    public class DialogueLogMenu : IClickableMenu
    {
        // Layout constants (pixels)
        private const int Padding      = 32;
        private const int BorderInset  = 20;
        private const int TitleHeight  = 52;
        private const int ChipH        = 36;
        private const int ChipGap      = 4;
        private const int ChipPad      = 10;
        private const int SbWidth      = 20;
        private const int LineH        = 24; // line height for Game1.smallFont
        private const int EntryPadTop  = 8;
        private const int EntryPadBot  = 10;
        private const int HeaderGap    = 6;  // gap between header line and first text line

        private readonly List<DialogueEntry> _allEntries;
        private List<DialogueEntry>          _filtered  = new();
        private List<string>                 _npcNames  = new();

        private string? _activeFilter = null;
        private int     _scrollOffset = 0;

        private List<(Rectangle rect, string key)> _chips = new();
        private int       _chipAreaHeight = 0;
        private int       _chipsBottom    = 0;
        private Rectangle _entryArea;

        // Per-entry layout cache - rebuilt whenever filter or layout changes
        private List<int>          _entryOffsets = new(); // Y offset from _entryArea.Y
        private List<int>          _entryHeights = new(); // pixel height of each entry
        private List<List<string>> _entryLines   = new(); // wrapped lines per entry
        private int                _totalHeight  = 0;

        // NPC name → display name, built once from _allEntries
        private readonly Dictionary<string, string> _displayNames = new();

        private ClickableTextureComponent _closeButton = null!;

        public DialogueLogMenu() : base(0, 0, 0, 0)
        {
            _allEntries = DialogueLog.Entries.ToList();
            _allEntries.Reverse(); // newest first

            _npcNames = _allEntries
                .Select(e => e.NpcName)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            foreach (var e in _allEntries)
                _displayNames.TryAdd(e.NpcName, e.DisplayName);

            ApplyFilter();
            RebuildLayout();
        }

        private void RebuildLayout()
        {
            int vw = Game1.uiViewport.Width;
            int vh = Game1.uiViewport.Height;

            width  = Math.Min((int)(vw * 0.65f), 960);
            height = Math.Min((int)(vh * 0.72f), 720);

            xPositionOnScreen = (vw - width)  / 2;
            yPositionOnScreen = (vh - height) / 2;

            BuildChips();

            _chipsBottom = yPositionOnScreen + _chipAreaHeight + ChipGap * 2;
            int entryTop = _chipsBottom + Padding;
            _entryArea = new Rectangle(
                xPositionOnScreen + Padding,
                entryTop,
                width - Padding * 2 - SbWidth - 4,
                yPositionOnScreen + height - Padding - entryTop
            );

            _closeButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - 36, yPositionOnScreen - 8, 48, 48),
                Game1.mouseCursors,
                new Rectangle(337, 494, 12, 12),
                4f
            );

            ComputeEntryHeights();
            int maxScroll = Math.Max(0, _totalHeight - _entryArea.Height);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            RebuildLayout();
        }

        // ── Filter ────────────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            _filtered = _activeFilter == null
                ? _allEntries.ToList()
                : _allEntries.Where(e => e.NpcName == _activeFilter).ToList();
            _scrollOffset = 0;
            ComputeEntryHeights();
        }

        // ── Entry height cache ────────────────────────────────────────────────────

        private void ComputeEntryHeights()
        {
            _entryOffsets.Clear();
            _entryHeights.Clear();
            _entryLines.Clear();

            // _entryArea may not be initialised yet on first call from ApplyFilter()
            // before RebuildLayout() runs - skip safely and let RebuildLayout call us again
            if (_entryArea.Width <= 0) return;

            int offset = 0;
            int textMaxW = _entryArea.Width - 16;

            foreach (var entry in _filtered)
            {
                var lines = WrapText(entry.Text, textMaxW, Game1.smallFont);
                int h = EntryPadTop + LineH + HeaderGap + lines.Count * LineH + EntryPadBot;
                _entryOffsets.Add(offset);
                _entryHeights.Add(h);
                _entryLines.Add(lines);
                offset += h;
            }

            _totalHeight = offset;
        }

        // ── Chip layout ───────────────────────────────────────────────────────────

        private void BuildChips()
        {
            _chips.Clear();
            int startX = xPositionOnScreen + Padding;
            int maxX   = xPositionOnScreen + width - Padding;
            int x = startX;
            int y = yPositionOnScreen + TitleHeight + ChipGap * 4;

            PlaceChip("All", "");
            foreach (string name in _npcNames)
            {
                string label = _displayNames.TryGetValue(name, out var dn) ? dn : name;
                PlaceChip(label, name);
            }

            _chipAreaHeight = (y + ChipH) - yPositionOnScreen;

            void PlaceChip(string label, string key)
            {
                int w = (int)Game1.smallFont.MeasureString(label).X + ChipPad * 2;
                if (x + w > maxX && x > startX)
                {
                    x = startX;
                    y += ChipH + ChipGap;
                }
                _chips.Add((new Rectangle(x, y, w, ChipH), key));
                x += w + 6;
            }
        }

        // ── Draw ──────────────────────────────────────────────────────────────────

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect,
                Game1.graphics.GraphicsDevice.Viewport.Bounds,
                Color.Black * 0.5f);

            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                xPositionOnScreen, yPositionOnScreen, width, height, Color.White, 1f, false);

            string  title   = "Dialogue Log";
            Vector2 titleSz = Game1.dialogueFont.MeasureString(title);
            float   titleY  = yPositionOnScreen + BorderInset +
                              Math.Max(0f, (TitleHeight - BorderInset - titleSz.Y) / 2f);
            b.DrawString(Game1.dialogueFont, title,
                new Vector2(xPositionOnScreen + (width - titleSz.X) / 2f, titleY),
                Game1.textColor);

            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(xPositionOnScreen + Padding, _chipsBottom, width - Padding * 2, 2),
                Color.Black * 0.15f);

            foreach (var (rect, key) in _chips)
            {
                bool active = key == "" ? _activeFilter == null : _activeFilter == key;
                b.Draw(Game1.fadeToBlackRect, rect, active ? Color.Black * 0.5f : Color.Black * 0.22f);

                string label = key == "" ? "All"
                    : (_displayNames.TryGetValue(key, out var dn) ? dn : key);
                Vector2 labelSz = Game1.smallFont.MeasureString(label);
                b.DrawString(Game1.smallFont, label,
                    new Vector2(
                        rect.X + (rect.Width  - labelSz.X) / 2f,
                        rect.Y + (rect.Height - labelSz.Y) / 2f),
                    active ? Color.White : Game1.textColor);
            }

            Rectangle prevScissor = b.GraphicsDevice.ScissorRectangle;
            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                null, new RasterizerState { ScissorTestEnable = true }, null, Matrix.Identity);
            b.GraphicsDevice.ScissorRectangle = _entryArea;

            DrawEntries(b);

            b.End();
            b.GraphicsDevice.ScissorRectangle = prevScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                null, null, null, Matrix.Identity);

            DrawScrollBar(b);

            if (_filtered.Count == 0)
            {
                string  msg   = _activeFilter == null ? "No dialogue recorded yet." : "No dialogue from this NPC yet.";
                Vector2 msgSz = Game1.smallFont.MeasureString(msg);
                b.DrawString(Game1.smallFont, msg,
                    new Vector2(
                        _entryArea.X + (_entryArea.Width  - msgSz.X) / 2f,
                        _entryArea.Y + (_entryArea.Height - msgSz.Y) / 2f),
                    Game1.textColor * 0.6f);
            }

            _closeButton.draw(b);
            drawMouse(b);
        }

        private void DrawEntries(SpriteBatch b)
        {
            int textMaxW = _entryArea.Width - 16;

            for (int i = 0; i < _filtered.Count; i++)
            {
                int entryY = _entryArea.Y + _entryOffsets[i] - _scrollOffset;
                int entryH = _entryHeights[i];

                if (entryY + entryH < _entryArea.Y) continue;
                if (entryY > _entryArea.Bottom)     break;

                var entry = _filtered[i];

                if (i % 2 == 0)
                    b.Draw(Game1.fadeToBlackRect,
                        new Rectangle(_entryArea.X, entryY, _entryArea.Width, entryH - 2),
                        Color.Black * 0.07f);

                // Header: NPC name + date
                string header = entry.DisplayName;
                if (!string.IsNullOrEmpty(entry.Date))
                    header += $"  •  {entry.Date}";
                b.DrawString(Game1.smallFont, header,
                    new Vector2(_entryArea.X + 8, entryY + EntryPadTop),
                    new Color(101, 55, 0));

                // Wrapped dialogue text (pre-computed in ComputeEntryHeights)
                var lines = _entryLines[i];
                int textY = entryY + EntryPadTop + LineH + HeaderGap;
                foreach (string line in lines)
                {
                    b.DrawString(Game1.smallFont, line,
                        new Vector2(_entryArea.X + 8, textY),
                        Game1.textColor);
                    textY += LineH;
                }
            }
        }

        private void DrawScrollBar(SpriteBatch b)
        {
            if (_totalHeight <= _entryArea.Height) return;

            int trackX = _entryArea.Right + 4;
            int trackH = _entryArea.Height;

            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(trackX, _entryArea.Y, SbWidth - 4, trackH),
                Color.Black * 0.2f);

            float ratio   = (float)_entryArea.Height / _totalHeight;
            int   thumbH  = Math.Max(20, (int)(trackH * ratio));
            float scrollR = (float)_scrollOffset / (_totalHeight - _entryArea.Height);
            int   thumbY  = _entryArea.Y + (int)((trackH - thumbH) * scrollR);

            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(trackX, thumbY, SbWidth - 4, thumbH),
                Color.Gray);
        }

        // ── Input ─────────────────────────────────────────────────────────────────

        public override void receiveScrollWheelAction(int direction)
        {
            int maxScroll = Math.Max(0, _totalHeight - _entryArea.Height);
            _scrollOffset = Math.Clamp(_scrollOffset - direction / 3, 0, maxScroll);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (_closeButton.containsPoint(x, y))
            {
                exitThisMenu();
                return;
            }

            foreach (var (rect, key) in _chips)
            {
                if (rect.Contains(x, y))
                {
                    _activeFilter = key == "" ? null : key;
                    ApplyFilter();
                    if (playSound) Game1.playSound("smallSelect");
                    return;
                }
            }
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Escape || key == Keys.E)
                exitThisMenu();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static List<string> WrapText(string text, int maxWidth, SpriteFont font)
        {
            var lines = new List<string>();
            var words = text.Split(' ');
            var current = new StringBuilder();

            foreach (string word in words)
            {
                string test = current.Length == 0 ? word : current + " " + word;
                if (font.MeasureString(test).X > maxWidth && current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append(word);
                }
                else
                {
                    if (current.Length > 0) current.Append(' ');
                    current.Append(word);
                }
            }

            if (current.Length > 0) lines.Add(current.ToString());
            if (lines.Count == 0)   lines.Add("");
            return lines;
        }
    }
}
