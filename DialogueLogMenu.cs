using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        // ── Layout constants ──────────────────────────────────────────────────
        private const int Padding      = 20;
        private const int SidebarW     = 200;
        private const int LetterColW   = 22;
        private const int NpcRowH      = 40;
        private const int PortraitSm   = 32;
        private const int PortraitXs   = 24;  // tiny portrait in dialogue entries
        private const int PortraitLg   = 48;
        private const int HeaderH      = 56;
        private const int SearchH      = 28;
        private const int SbWidth      = 16;
        private const int LineH        = 24;
        private const int EntryPadTop  = 8;
        private const int EntryPadBot  = 10;
        private const int HeaderGap    = 6;
        private const int DivW         = 2;
        private const int EntryPortOff = 30; // horizontal offset for entry text after portrait

        // ── Data ──────────────────────────────────────────────────────────────
        private readonly List<DialogueEntry>            _allEntries;
        private List<DialogueEntry>                     _filtered = new();
        private readonly List<string>                   _npcNames;
        private readonly Dictionary<string, string>     _displayNames = new();
        private readonly Dictionary<string, Texture2D?> _portraits    = new();

        // ── State ─────────────────────────────────────────────────────────────
        private string? _selectedNpc       = null;   // null = "All"
        private string  _sidebarSearch     = "";      // filters NPC names in sidebar
        private string  _textSearch        = "";      // filters dialogue text in entries
        private bool    _sidebarSearchActive = false;
        private bool    _textSearchActive    = false;
        private char?   _activeLetter      = null;
        private int     _sidebarScroll     = 0;
        private int     _entryScroll       = 0;

        // Scrollbar drag state
        private bool    _draggingEntryBar    = false;
        private bool    _draggingSidebarBar  = false;
        private int     _dragStartY          = 0;
        private int     _dragStartScroll     = 0;

        // ── Layout regions ────────────────────────────────────────────────────
        private Rectangle _sidebarRect;
        private Rectangle _letterCol;
        private Rectangle _sidebarSearchRect;
        private Rectangle _npcListRect;
        private Rectangle _headerRect;
        private Rectangle _textSearchRect;
        private Rectangle _entryRect;

        // ── Computed layout data ──────────────────────────────────────────────
        private List<(Rectangle rect, char letter, bool hasNpcs)> _letterSlots = new();
        private List<string>       _visibleNpcs   = new();
        private int                _npcTotalH     = 0;
        private List<int>          _entryOffsets   = new();
        private List<int>          _entryHeights   = new();
        private List<List<string>> _entryLines     = new();
        private int                _entriesTotalH  = 0;

        // Pre-computed line wrapping for ALL entries (indexed same as _allEntries)
        private List<List<string>> _allWrapped     = new();
        private List<int>          _allHeights     = new();
        private int                _lastWrapWidth  = 0;
        private Dictionary<DialogueEntry, int> _entryIndex = new();

        private ClickableTextureComponent _closeBtn = null!;
        private Rectangle _discordRect;  // clickable area for "Discord" word only
        private const string DiscordUrl = "https://discord.com/invite/aCE6HqfCHj";
        private const string FooterPrefix = "Join us on ";
        private const string FooterLink = "Discord";
        private const float FooterScale = 0.7f;

        // ── Constructor ───────────────────────────────────────────────────────

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

            foreach (string n in _npcNames)
            {
                try   { _portraits[n] = Game1.content.Load<Texture2D>($"Portraits/{n}"); }
                catch { _portraits[n] = null; }
            }

            RebuildLayout();    // sets _entryRect so wrap width is known
            PrecomputeWrapping();
            ApplyFilter();

            Game1.game1.Window.TextInput += OnTextInput;
        }

        /// Pre-compute line wrapping for every entry once. O(n) on open, then
        /// ComputeEntryHeights becomes a cheap index lookup.
        private void PrecomputeWrapping()
        {
            int maxW = _entryRect.Width > 0 ? _entryRect.Width - EntryPortOff - 12 : 600;
            _lastWrapWidth = maxW;
            _allWrapped.Clear();
            _allHeights.Clear();
            _entryIndex.Clear();

            for (int i = 0; i < _allEntries.Count; i++)
            {
                var entry = _allEntries[i];
                var lines = WrapText(entry.Text, maxW, Game1.smallFont);
                int h = EntryPadTop + LineH + HeaderGap + lines.Count * LineH + EntryPadBot;
                _allWrapped.Add(lines);
                _allHeights.Add(h);
                _entryIndex[entry] = i;
            }
        }

        // ── Layout ────────────────────────────────────────────────────────────

        private void RebuildLayout()
        {
            int vw = Game1.uiViewport.Width;
            int vh = Game1.uiViewport.Height;

            width  = Math.Min((int)(vw * 0.78f), 1100);
            height = Math.Min((int)(vh * 0.82f), 800);
            xPositionOnScreen = (vw - width) / 2;
            yPositionOnScreen = (vh - height) / 2;

            int inX = xPositionOnScreen + Padding;
            int inY = yPositionOnScreen + Padding;
            int inW = width  - Padding * 2;
            int footerH = 28; // space for divider + discord link + padding
            int inH = height - Padding * 2 - footerH;

            // Discord link - only the "Discord" word is clickable
            Vector2 prefixSz = Game1.smallFont.MeasureString(FooterPrefix) * FooterScale;
            Vector2 linkSz = Game1.smallFont.MeasureString(FooterLink) * FooterScale;
            float fullW = prefixSz.X + linkSz.X;
            float footerX = xPositionOnScreen + (width - fullW) / 2f;
            int footerY = yPositionOnScreen + height - Padding - footerH + (footerH - (int)linkSz.Y) / 2;
            _discordRect = new Rectangle(
                (int)(footerX + prefixSz.X), footerY,
                (int)linkSz.X + 2, (int)linkSz.Y);

            // Sidebar (left)
            _sidebarRect = new Rectangle(inX, inY, SidebarW, inH);
            _letterCol   = new Rectangle(inX, inY, LetterColW, inH);

            int npcX = _letterCol.Right + 4;
            int npcW = SidebarW - LetterColW - 4;

            // Sidebar: "All" row takes NpcRowH, then search bar, then NPC list
            int searchTop = inY + NpcRowH + 2;
            _sidebarSearchRect = new Rectangle(npcX, searchTop, npcW, SearchH);
            int listTop = _sidebarSearchRect.Bottom + 4;
            _npcListRect = new Rectangle(npcX, listTop, npcW, inY + inH - listTop);

            BuildLetterSlots();

            // Right panel
            int rpX = _sidebarRect.Right + DivW + 12;
            int rpW = inW - SidebarW - DivW - 12;

            _headerRect = new Rectangle(rpX, inY, rpW, HeaderH);

            // Text search bar - compact, right-aligned in header area
            int tsW = Math.Min(220, rpW / 3);
            _textSearchRect = new Rectangle(
                _headerRect.Right - tsW - 4,
                _headerRect.Y + (HeaderH - SearchH) / 2,
                tsW, SearchH);

            int eTop = _headerRect.Bottom + 8;
            _entryRect = new Rectangle(rpX, eTop, rpW - SbWidth - 4, inY + inH - eTop);

            _closeBtn = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - 36, yPositionOnScreen - 8, 48, 48),
                Game1.mouseCursors, new Rectangle(337, 494, 12, 12), 4f);

            BuildNpcList();
            ComputeEntryHeights();
            ClampScrolls();
        }

        private void BuildLetterSlots()
        {
            _letterSlots.Clear();
            var used = new HashSet<char>();
            foreach (string n in _npcNames)
            {
                string d = DisplayOf(n);
                if (d.Length > 0) used.Add(char.ToUpper(d[0]));
            }

            // Span the full sidebar height evenly across 26 letters
            int totalH = _letterCol.Height - 4;
            int lh = totalH / 26;
            int remainder = totalH - (lh * 26);
            int y = _letterCol.Y + 2;
            for (char c = 'A'; c <= 'Z'; c++)
            {
                // Distribute remainder pixels across the first few letters
                int h = lh + (c - 'A' < remainder ? 1 : 0);
                _letterSlots.Add((new Rectangle(_letterCol.X, y, LetterColW, h), c, used.Contains(c)));
                y += h;
            }
        }

        private void BuildNpcList()
        {
            _visibleNpcs.Clear();
            foreach (string n in _npcNames)
            {
                string d = DisplayOf(n);

                if (_activeLetter.HasValue &&
                    (d.Length == 0 || char.ToUpper(d[0]) != _activeLetter.Value))
                    continue;

                if (_sidebarSearch.Length > 0 &&
                    !d.Contains(_sidebarSearch, StringComparison.OrdinalIgnoreCase))
                    continue;

                _visibleNpcs.Add(n);
            }
            _npcTotalH = _visibleNpcs.Count * NpcRowH;
        }

        // ── Filtering ─────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            _filtered = _selectedNpc == null
                ? _allEntries.ToList()
                : _allEntries.Where(e => e.NpcName == _selectedNpc).ToList();

            if (_textSearch.Length > 0)
                _filtered = _filtered
                    .Where(e => e.Text.Contains(_textSearch, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            _entryScroll = 0;
            ComputeEntryHeights();
        }

        // ── Entry height cache ────────────────────────────────────────────────

        private void ComputeEntryHeights()
        {
            _entryOffsets.Clear();
            _entryHeights.Clear();
            _entryLines.Clear();

            if (_entryRect.Width <= 0) return;

            // Check if wrap width changed (window resize) - recompute if so
            int maxW = _entryRect.Width - EntryPortOff - 12;
            if (maxW != _lastWrapWidth && _allWrapped.Count > 0)
                PrecomputeWrapping();

            // Use pre-computed wrapping via O(1) dictionary lookup
            int off = 0;
            foreach (var entry in _filtered)
            {
                if (_entryIndex.TryGetValue(entry, out int idx) && idx < _allWrapped.Count)
                {
                    _entryLines.Add(_allWrapped[idx]);
                    _entryHeights.Add(_allHeights[idx]);
                }
                else
                {
                    // Fallback for safety
                    var lines = WrapText(entry.Text, maxW, Game1.smallFont);
                    int h = EntryPadTop + LineH + HeaderGap + lines.Count * LineH + EntryPadBot;
                    _entryLines.Add(lines);
                    _entryHeights.Add(h);
                }
                _entryOffsets.Add(off);
                off += _entryHeights[^1];
            }
            _entriesTotalH = off;
        }

        private void ClampScrolls()
        {
            _sidebarScroll = Math.Clamp(_sidebarScroll, 0,
                Math.Max(0, _npcTotalH - _npcListRect.Height));
            _entryScroll = Math.Clamp(_entryScroll, 0,
                Math.Max(0, _entriesTotalH - _entryRect.Height));
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
            => RebuildLayout();

        // ── Draw ──────────────────────────────────────────────────────────────

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect,
                Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                xPositionOnScreen, yPositionOnScreen, width, height, Color.White, 1f, false);

            DrawSidebar(b);

            // Vertical divider - full height including footer
            int divTop = _sidebarRect.Y;
            int divBot = yPositionOnScreen + height - Padding;
            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(_sidebarRect.Right + 6, divTop, DivW, divBot - divTop),
                Color.Black * 0.15f);

            DrawRightPanel(b);

            // ── Footer divider (full width, always visible) ──
            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(xPositionOnScreen + Padding, _discordRect.Y - 6, width - Padding * 2, 2),
                Color.Black * 0.12f);

            // ── Discord footer ──

            int mx = Game1.getMouseX(), my = Game1.getMouseY();
            bool hovering = _discordRect.Contains(mx, my);

            // "Join us on " (static, gray)
            Vector2 prefSz = Game1.smallFont.MeasureString(FooterPrefix) * FooterScale;
            float prefX = _discordRect.X - prefSz.X;
            b.DrawString(Game1.smallFont, FooterPrefix,
                new Vector2(prefX, _discordRect.Y),
                Game1.textColor * 0.35f, 0f, Vector2.Zero, FooterScale, SpriteEffects.None, 0f);

            // "Discord" (clickable, blue on hover)
            b.DrawString(Game1.smallFont, FooterLink,
                new Vector2(_discordRect.X, _discordRect.Y),
                hovering ? new Color(88, 101, 242) : Game1.textColor * 0.45f,
                0f, Vector2.Zero, FooterScale, SpriteEffects.None, 0f);

            // "By RipZ" (bottom left)
            b.DrawString(Game1.smallFont, "By RipZ",
                new Vector2(xPositionOnScreen + Padding + 4, _discordRect.Y),
                Game1.textColor * 0.4f, 0f, Vector2.Zero, FooterScale, SpriteEffects.None, 0f);

            // Version number (bottom right, dark)
            string ver = $"v{ModEntry.ModVersion}";
            Vector2 verSz = Game1.smallFont.MeasureString(ver) * FooterScale;
            b.DrawString(Game1.smallFont, ver,
                new Vector2(xPositionOnScreen + width - Padding - verSz.X,
                            _discordRect.Y),
                Game1.textColor * 0.7f, 0f, Vector2.Zero, FooterScale, SpriteEffects.None, 0f);

            _closeBtn.draw(b);
            drawMouse(b);
        }

        private void DrawSidebar(SpriteBatch b)
        {
            // ── A-Z letter strip (bold, full-height) ──
            foreach (var (r, c, has) in _letterSlots)
            {
                bool active = _activeLetter == c;
                if (active)
                    b.Draw(Game1.fadeToBlackRect, r, Color.Black * 0.45f);

                string s = c.ToString();
                // Use dialogueFont for bolder letters, scale to fit
                Vector2 sz = Game1.dialogueFont.MeasureString(s);
                float sc = Math.Min((float)r.Width * 0.9f / sz.X, r.Height * 0.85f / sz.Y);
                Color lc = active ? Color.White
                    : (has ? Game1.textColor : Game1.textColor * 0.25f);
                b.DrawString(Game1.dialogueFont, s,
                    new Vector2(r.X + (r.Width - sz.X * sc) / 2f,
                                r.Y + (r.Height - sz.Y * sc) / 2f),
                    lc, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
            }

            // ── "All" row (above search, not scrolled) ──
            int allY = _sidebarRect.Y;
            bool allSel = _selectedNpc == null;
            if (allSel)
                b.Draw(Game1.fadeToBlackRect,
                    new Rectangle(_npcListRect.X, allY, _npcListRect.Width, NpcRowH),
                    Color.Black * 0.3f);
            b.DrawString(Game1.smallFont, "All",
                new Vector2(_npcListRect.X + 8,
                            allY + (NpcRowH - Game1.smallFont.LineSpacing) / 2f),
                allSel ? Color.White : Game1.textColor);

            // ── Sidebar search bar ──
            b.Draw(Game1.fadeToBlackRect, _sidebarSearchRect,
                _sidebarSearchActive ? Color.Black * 0.22f : Color.Black * 0.12f);
            if (_sidebarSearchActive)
                b.Draw(Game1.fadeToBlackRect,
                    new Rectangle(_sidebarSearchRect.X, _sidebarSearchRect.Bottom - 2,
                                  _sidebarSearchRect.Width, 2),
                    new Color(101, 55, 0) * 0.6f);

            string sbDisplay = _sidebarSearch.Length > 0
                ? _sidebarSearch
                : (_sidebarSearchActive ? "" : "Filter...");
            Color sbColor = _sidebarSearch.Length > 0
                ? Game1.textColor : Game1.textColor * 0.4f;
            b.DrawString(Game1.smallFont,
                sbDisplay + (_sidebarSearchActive ? "|" : ""),
                new Vector2(_sidebarSearchRect.X + 6,
                            _sidebarSearchRect.Y + (_sidebarSearchRect.Height - Game1.smallFont.LineSpacing) / 2f),
                sbColor);

            // ── NPC list (scissored) ──
            var prevScissor = b.GraphicsDevice.ScissorRectangle;
            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                null, new RasterizerState { ScissorTestEnable = true }, null, Matrix.Identity);
            b.GraphicsDevice.ScissorRectangle = _npcListRect;

            for (int i = 0; i < _visibleNpcs.Count; i++)
            {
                string name = _visibleNpcs[i];
                string display = DisplayOf(name);
                int rowY = _npcListRect.Y + i * NpcRowH - _sidebarScroll;

                if (rowY + NpcRowH < _npcListRect.Y) continue;
                if (rowY > _npcListRect.Bottom) break;

                bool sel = _selectedNpc == name;
                if (sel)
                    b.Draw(Game1.fadeToBlackRect,
                        new Rectangle(_npcListRect.X, rowY, _npcListRect.Width, NpcRowH),
                        Color.Black * 0.3f);

                if (_portraits.TryGetValue(name, out var tex) && tex != null)
                    b.Draw(tex,
                        new Rectangle(_npcListRect.X + 4,
                                      rowY + (NpcRowH - PortraitSm) / 2,
                                      PortraitSm, PortraitSm),
                        new Rectangle(0, 0, 64, 64), Color.White);

                b.DrawString(Game1.smallFont, display,
                    new Vector2(_npcListRect.X + PortraitSm + 12,
                                rowY + (NpcRowH - Game1.smallFont.LineSpacing) / 2f),
                    sel ? Color.White : Game1.textColor);
            }

            b.End();
            b.GraphicsDevice.ScissorRectangle = prevScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                null, null, null, Matrix.Identity);

            // ── Sidebar scrollbar ──
            DrawSidebarScrollBar(b);
        }

        private void DrawSidebarScrollBar(SpriteBatch b)
        {
            if (_npcTotalH <= _npcListRect.Height) return;

            int trackX = _npcListRect.Right - SbWidth + 2;
            int trackH = _npcListRect.Height;

            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(trackX, _npcListRect.Y, SbWidth - 4, trackH),
                Color.Black * 0.15f);

            float ratio = (float)_npcListRect.Height / _npcTotalH;
            int thumbH = Math.Max(20, (int)(trackH * ratio));
            int maxScroll = _npcTotalH - _npcListRect.Height;
            float scrollRatio = maxScroll > 0 ? (float)_sidebarScroll / maxScroll : 0f;
            int thumbY = _npcListRect.Y + (int)((trackH - thumbH) * scrollRatio);

            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(trackX, thumbY, SbWidth - 4, thumbH),
                _draggingSidebarBar ? Color.LightGray : Color.Gray);
        }

        private void DrawRightPanel(SpriteBatch b)
        {
            // ── Header: portrait + NPC name ──
            string headerName = _selectedNpc == null
                ? "All Dialogue"
                : DisplayOf(_selectedNpc);

            Texture2D? headerPort = null;
            if (_selectedNpc != null)
                _portraits.TryGetValue(_selectedNpc, out headerPort);

            int textX = _headerRect.X + 8;
            if (headerPort != null)
            {
                b.Draw(headerPort,
                    new Rectangle(_headerRect.X + 4,
                                  _headerRect.Y + (HeaderH - PortraitLg) / 2,
                                  PortraitLg, PortraitLg),
                    new Rectangle(0, 0, 64, 64), Color.White);
                textX = _headerRect.X + PortraitLg + 12;
            }

            Vector2 nameSz = Game1.dialogueFont.MeasureString(headerName);
            float nameScale = Math.Min(1f, HeaderH * 0.8f / nameSz.Y);
            b.DrawString(Game1.dialogueFont, headerName,
                new Vector2(textX, _headerRect.Y + (HeaderH - nameSz.Y * nameScale) / 2f),
                Game1.textColor, 0f, Vector2.Zero, nameScale, SpriteEffects.None, 0f);

            // ── Text search (compact, right side of header) ──
            b.Draw(Game1.fadeToBlackRect, _textSearchRect,
                _textSearchActive ? Color.Black * 0.22f : Color.Black * 0.12f);
            if (_textSearchActive)
                b.Draw(Game1.fadeToBlackRect,
                    new Rectangle(_textSearchRect.X, _textSearchRect.Bottom - 2,
                                  _textSearchRect.Width, 2),
                    new Color(101, 55, 0) * 0.6f);

            string tsDisplay = _textSearch.Length > 0
                ? _textSearch
                : (_textSearchActive ? "" : "Search dialogue...");
            Color tsColor = _textSearch.Length > 0
                ? Game1.textColor : Game1.textColor * 0.4f;
            b.DrawString(Game1.smallFont,
                tsDisplay + (_textSearchActive ? "|" : ""),
                new Vector2(_textSearchRect.X + 6,
                            _textSearchRect.Y + (_textSearchRect.Height - Game1.smallFont.LineSpacing) / 2f),
                tsColor);

            // Entry count (between name and search)
            string countTxt = $"{_filtered.Count} entries";
            Vector2 countSz = Game1.smallFont.MeasureString(countTxt);
            float countX = _textSearchRect.X - countSz.X - 12;
            if (countX > textX + nameSz.X * nameScale + 8)
                b.DrawString(Game1.smallFont, countTxt,
                    new Vector2(countX, _headerRect.Y + (HeaderH - countSz.Y) / 2f),
                    Game1.textColor * 0.45f);

            // Separator below header
            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(_headerRect.X, _headerRect.Bottom + 2, _headerRect.Width, 2),
                Color.Black * 0.12f);

            // ── Dialogue entries (scissored) ──
            var prevScissor = b.GraphicsDevice.ScissorRectangle;
            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                null, new RasterizerState { ScissorTestEnable = true }, null, Matrix.Identity);
            b.GraphicsDevice.ScissorRectangle = _entryRect;

            DrawEntries(b);

            b.End();
            b.GraphicsDevice.ScissorRectangle = prevScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                null, null, null, Matrix.Identity);

            DrawScrollBar(b);

            if (_filtered.Count == 0)
            {
                string msg = _textSearch.Length > 0
                    ? "No matching dialogue found."
                    : (_selectedNpc == null
                        ? "No dialogue recorded yet."
                        : "No dialogue from this NPC yet.");
                Vector2 msgSz = Game1.smallFont.MeasureString(msg);
                b.DrawString(Game1.smallFont, msg,
                    new Vector2(_entryRect.X + (_entryRect.Width - msgSz.X) / 2f,
                                _entryRect.Y + (_entryRect.Height - msgSz.Y) / 2f),
                    Game1.textColor * 0.6f);
            }
        }

        private void DrawEntries(SpriteBatch b)
        {
            for (int i = 0; i < _filtered.Count; i++)
            {
                int entryY = _entryRect.Y + _entryOffsets[i] - _entryScroll;
                int entryH = _entryHeights[i];

                if (entryY + entryH < _entryRect.Y) continue;
                if (entryY > _entryRect.Bottom) break;

                var entry = _filtered[i];

                if (i % 2 == 0)
                    b.Draw(Game1.fadeToBlackRect,
                        new Rectangle(_entryRect.X, entryY, _entryRect.Width, entryH - 2),
                        Color.Black * 0.07f);

                // NPC portrait icon in entry
                int contentX = _entryRect.X + 8;
                if (_portraits.TryGetValue(entry.NpcName, out var tex) && tex != null)
                {
                    b.Draw(tex,
                        new Rectangle(contentX, entryY + EntryPadTop, PortraitXs, PortraitXs),
                        new Rectangle(0, 0, 64, 64), Color.White);
                }
                int textLeft = contentX + EntryPortOff;

                // Header: NPC name + date
                string header = entry.DisplayName;
                if (!string.IsNullOrEmpty(entry.Date))
                    header += $"  \u2022  {entry.Date}";
                b.DrawString(Game1.smallFont, header,
                    new Vector2(textLeft, entryY + EntryPadTop),
                    new Color(101, 55, 0));

                // Wrapped dialogue text
                int textY = entryY + EntryPadTop + LineH + HeaderGap;
                foreach (string line in _entryLines[i])
                {
                    b.DrawString(Game1.smallFont, line,
                        new Vector2(textLeft, textY), Game1.textColor);
                    textY += LineH;
                }
            }
        }

        private void DrawScrollBar(SpriteBatch b)
        {
            if (_entriesTotalH <= _entryRect.Height) return;

            int trackX = _entryRect.Right + 4;
            int trackH = _entryRect.Height;

            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(trackX, _entryRect.Y, SbWidth - 4, trackH),
                Color.Black * 0.2f);

            float ratio = (float)_entryRect.Height / _entriesTotalH;
            int thumbH  = Math.Max(20, (int)(trackH * ratio));
            float scrollRatio = (float)_entryScroll / (_entriesTotalH - _entryRect.Height);
            int thumbY  = _entryRect.Y + (int)((trackH - thumbH) * scrollRatio);

            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(trackX, thumbY, SbWidth - 4, thumbH),
                _draggingEntryBar ? Color.LightGray : Color.Gray);
        }

        // ── Scrollbar hit rects (for click-drag) ─────────────────────────────

        private Rectangle GetEntryThumbRect()
        {
            if (_entriesTotalH <= _entryRect.Height) return Rectangle.Empty;
            int trackX = _entryRect.Right + 4;
            float ratio = (float)_entryRect.Height / _entriesTotalH;
            int thumbH = Math.Max(20, (int)(_entryRect.Height * ratio));
            int maxScroll = _entriesTotalH - _entryRect.Height;
            float sr = maxScroll > 0 ? (float)_entryScroll / maxScroll : 0f;
            int thumbY = _entryRect.Y + (int)((_entryRect.Height - thumbH) * sr);
            return new Rectangle(trackX, thumbY, SbWidth - 4, thumbH);
        }

        private Rectangle GetEntryTrackRect()
        {
            if (_entriesTotalH <= _entryRect.Height) return Rectangle.Empty;
            return new Rectangle(_entryRect.Right + 4, _entryRect.Y, SbWidth - 4, _entryRect.Height);
        }

        private Rectangle GetSidebarThumbRect()
        {
            if (_npcTotalH <= _npcListRect.Height) return Rectangle.Empty;
            int trackX = _npcListRect.Right - SbWidth + 2;
            float ratio = (float)_npcListRect.Height / _npcTotalH;
            int thumbH = Math.Max(20, (int)(_npcListRect.Height * ratio));
            int maxScroll = _npcTotalH - _npcListRect.Height;
            float sr = maxScroll > 0 ? (float)_sidebarScroll / maxScroll : 0f;
            int thumbY = _npcListRect.Y + (int)((_npcListRect.Height - thumbH) * sr);
            return new Rectangle(trackX, thumbY, SbWidth - 4, thumbH);
        }

        private Rectangle GetSidebarTrackRect()
        {
            if (_npcTotalH <= _npcListRect.Height) return Rectangle.Empty;
            return new Rectangle(_npcListRect.Right - SbWidth + 2, _npcListRect.Y, SbWidth - 4, _npcListRect.Height);
        }

        // ── Input ─────────────────────────────────────────────────────────────

        public override void receiveScrollWheelAction(int direction)
        {
            int mx = Game1.getMouseX(), my = Game1.getMouseY();

            if (_npcListRect.Contains(mx, my) || _letterCol.Contains(mx, my))
            {
                int max = Math.Max(0, _npcTotalH - _npcListRect.Height);
                _sidebarScroll = Math.Clamp(_sidebarScroll - direction / 3, 0, max);
            }
            else
            {
                int max = Math.Max(0, _entriesTotalH - _entryRect.Height);
                _entryScroll = Math.Clamp(_entryScroll - direction / 3, 0, max);
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (_closeBtn.containsPoint(x, y))
            {
                exitThisMenu();
                return;
            }

            // Discord link
            if (_discordRect.Contains(x, y))
            {
                try { Process.Start(new ProcessStartInfo(DiscordUrl) { UseShellExecute = true }); }
                catch { /* silently fail if browser can't open */ }
                if (playSound) Game1.playSound("bigSelect");
                return;
            }

            // Entry scrollbar drag start
            var entryThumb = GetEntryThumbRect();
            var entryTrack = GetEntryTrackRect();
            if (entryThumb.Contains(x, y))
            {
                _draggingEntryBar = true;
                _dragStartY = y;
                _dragStartScroll = _entryScroll;
                return;
            }
            if (entryTrack.Contains(x, y))
            {
                // Click on track - jump to that position
                int maxScroll = _entriesTotalH - _entryRect.Height;
                float clickRatio = (float)(y - _entryRect.Y) / _entryRect.Height;
                _entryScroll = Math.Clamp((int)(clickRatio * maxScroll), 0, maxScroll);
                _draggingEntryBar = true;
                _dragStartY = y;
                _dragStartScroll = _entryScroll;
                return;
            }

            // Sidebar scrollbar drag start
            var sbThumb = GetSidebarThumbRect();
            var sbTrack = GetSidebarTrackRect();
            if (sbThumb.Contains(x, y))
            {
                _draggingSidebarBar = true;
                _dragStartY = y;
                _dragStartScroll = _sidebarScroll;
                return;
            }
            if (sbTrack.Contains(x, y))
            {
                int maxScroll = _npcTotalH - _npcListRect.Height;
                float clickRatio = (float)(y - _npcListRect.Y) / _npcListRect.Height;
                _sidebarScroll = Math.Clamp((int)(clickRatio * maxScroll), 0, maxScroll);
                _draggingSidebarBar = true;
                _dragStartY = y;
                _dragStartScroll = _sidebarScroll;
                return;
            }

            // Sidebar search click
            if (_sidebarSearchRect.Contains(x, y))
            {
                _sidebarSearchActive = true;
                _textSearchActive = false;
                if (playSound) Game1.playSound("smallSelect");
                return;
            }

            // Text search click
            if (_textSearchRect.Contains(x, y))
            {
                _textSearchActive = true;
                _sidebarSearchActive = false;
                if (playSound) Game1.playSound("smallSelect");
                return;
            }

            // Click elsewhere deactivates search
            _sidebarSearchActive = false;
            _textSearchActive = false;

            // A-Z letter strip
            foreach (var (r, c, _) in _letterSlots)
            {
                if (r.Contains(x, y))
                {
                    _activeLetter = _activeLetter == c ? null : c;
                    _sidebarScroll = 0;
                    BuildNpcList();
                    ClampScrolls();
                    if (playSound) Game1.playSound("smallSelect");
                    return;
                }
            }

            // "All" row (fixed position above search)
            int allY = _sidebarRect.Y;
            Rectangle allRect = new Rectangle(_npcListRect.X, allY, _npcListRect.Width, NpcRowH);
            if (allRect.Contains(x, y))
            {
                _selectedNpc = null;
                ApplyFilter();
                ClampScrolls();
                if (playSound) Game1.playSound("smallSelect");
                return;
            }

            // NPC list clicks
            if (_npcListRect.Contains(x, y))
            {
                for (int i = 0; i < _visibleNpcs.Count; i++)
                {
                    int rowY = _npcListRect.Y + i * NpcRowH - _sidebarScroll;
                    if (rowY + NpcRowH < _npcListRect.Y || rowY > _npcListRect.Bottom) continue;
                    if (y >= rowY && y < rowY + NpcRowH)
                    {
                        _selectedNpc = _visibleNpcs[i];
                        ApplyFilter();
                        ClampScrolls();
                        if (playSound) Game1.playSound("smallSelect");
                        return;
                    }
                }
            }
        }

        public override void leftClickHeld(int x, int y)
        {
            if (_draggingEntryBar)
            {
                int delta = y - _dragStartY;
                int trackH = _entryRect.Height;
                float ratio = (float)_entriesTotalH / trackH;
                int maxScroll = Math.Max(0, _entriesTotalH - _entryRect.Height);
                _entryScroll = Math.Clamp(_dragStartScroll + (int)(delta * ratio), 0, maxScroll);
            }
            else if (_draggingSidebarBar)
            {
                int delta = y - _dragStartY;
                int trackH = _npcListRect.Height;
                float ratio = (float)_npcTotalH / trackH;
                int maxScroll = Math.Max(0, _npcTotalH - _npcListRect.Height);
                _sidebarScroll = Math.Clamp(_dragStartScroll + (int)(delta * ratio), 0, maxScroll);
            }
        }

        public override void releaseLeftClick(int x, int y)
        {
            _draggingEntryBar = false;
            _draggingSidebarBar = false;
        }

        public override void receiveKeyPress(Keys key)
        {
            if (_sidebarSearchActive || _textSearchActive)
            {
                if (key == Keys.Escape)
                {
                    if (_sidebarSearchActive)
                    {
                        _sidebarSearchActive = false;
                        _sidebarSearch = "";
                        BuildNpcList();
                        ClampScrolls();
                    }
                    if (_textSearchActive)
                    {
                        _textSearchActive = false;
                        _textSearch = "";
                        ApplyFilter();
                        ClampScrolls();
                    }
                }
                return; // suppress all keys while typing
            }

            if (key == Keys.Escape || key == Keys.E)
                exitThisMenu();
        }

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (_sidebarSearchActive)
            {
                if (e.Key == Keys.Back)
                {
                    if (_sidebarSearch.Length > 0)
                        _sidebarSearch = _sidebarSearch[..^1];
                }
                else if (e.Key == Keys.Enter || e.Key == Keys.Escape)
                    return;
                else if (!char.IsControl(e.Character))
                    _sidebarSearch += e.Character;

                BuildNpcList();
                ClampScrolls();
            }
            else if (_textSearchActive)
            {
                if (e.Key == Keys.Back)
                {
                    if (_textSearch.Length > 0)
                        _textSearch = _textSearch[..^1];
                }
                else if (e.Key == Keys.Enter || e.Key == Keys.Escape)
                    return;
                else if (!char.IsControl(e.Character))
                    _textSearch += e.Character;

                ApplyFilter();
                ClampScrolls();
            }
        }

        protected override void cleanupBeforeExit()
        {
            Game1.game1.Window.TextInput -= OnTextInput;
            base.cleanupBeforeExit();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string DisplayOf(string npcName)
            => _displayNames.TryGetValue(npcName, out var dn) ? dn : npcName;

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
