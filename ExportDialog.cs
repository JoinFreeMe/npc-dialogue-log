using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace NpcDialogueLog
{
    public class ExportDialog : IClickableMenu
    {
        // The menu texture-box border eats ~24-28px on each side; Padding is the inner inset.
        private const int Padding   = 36;
        private const int RowH      = 36;
        private const int BtnW      = 110;
        private const int InputH    = 36;

        private string _filename;
        private string _ext = ".txt";
        private bool _inputActive = false;

        private Rectangle _inputRect;
        private Rectangle _extRect;
        private Rectangle _txtBtn;
        private Rectangle _jsonBtn;
        private Rectangle _saveBtn;
        private ClickableTextureComponent _closeBtn = null!;

        private string? _toast;
        private double _toastUntil;

        public bool IsClosed { get; private set; }

        public ExportDialog() : base(0, 0, 0, 0)
        {
            _filename = DateTime.Now.ToString("dd-MM-yyyy");
            RebuildLayout();
            Game1.game1.Window.TextInput += OnTextInput;
        }

        private void RebuildLayout()
        {
            int vw = Game1.uiViewport.Width;
            int vh = Game1.uiViewport.Height;

            width  = 560;
            height = 240;
            xPositionOnScreen = (vw - width) / 2;
            yPositionOnScreen = (vh - height) / 2;

            int inX = xPositionOnScreen + Padding;
            int inY = yPositionOnScreen + Padding + RowH; // leave room for title

            // Filename input + extension label
            int extW = 60;
            _inputRect = new Rectangle(inX, inY, width - Padding * 2 - extW - 8, InputH);
            _extRect   = new Rectangle(_inputRect.Right + 8, inY, extW, InputH);

            // Format buttons row
            int row2Y = _inputRect.Bottom + 16;
            int gap = 12;
            int btnsW = BtnW * 2 + gap;
            int btnsX = xPositionOnScreen + (width - btnsW) / 2;
            _txtBtn  = new Rectangle(btnsX,                   row2Y, BtnW, RowH);
            _jsonBtn = new Rectangle(btnsX + BtnW + gap,      row2Y, BtnW, RowH);

            // Save button
            int row3Y = _txtBtn.Bottom + 16;
            _saveBtn = new Rectangle(xPositionOnScreen + (width - BtnW) / 2, row3Y, BtnW, RowH);

            _closeBtn = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - 36, yPositionOnScreen - 8, 48, 48),
                Game1.mouseCursors, new Rectangle(337, 494, 12, 12), 4f);
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
            => RebuildLayout();

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect,
                Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                xPositionOnScreen, yPositionOnScreen, width, height, Color.White, 1f, false);

            // Title
            b.DrawString(Game1.dialogueFont, "Export Dialogue Log",
                new Vector2(xPositionOnScreen + Padding, yPositionOnScreen + Padding - 4),
                Game1.textColor, 0f, Vector2.Zero, 0.6f, SpriteEffects.None, 0f);

            // Filename input
            b.Draw(Game1.fadeToBlackRect, _inputRect,
                _inputActive ? Color.Black * 0.22f : Color.Black * 0.12f);
            if (_inputActive)
                b.Draw(Game1.fadeToBlackRect,
                    new Rectangle(_inputRect.X, _inputRect.Bottom - 2, _inputRect.Width, 2),
                    new Color(101, 55, 0) * 0.6f);

            string display = _filename + (_inputActive ? "|" : "");
            b.DrawString(Game1.smallFont, display,
                new Vector2(_inputRect.X + 6,
                            _inputRect.Y + (_inputRect.Height - Game1.smallFont.LineSpacing) / 2f),
                Game1.textColor);

            // Extension label (right of input)
            Vector2 extSz = Game1.smallFont.MeasureString(_ext);
            b.Draw(Game1.fadeToBlackRect, _extRect, Color.Black * 0.18f);
            b.DrawString(Game1.smallFont, _ext,
                new Vector2(_extRect.X + (_extRect.Width - extSz.X) / 2f,
                            _extRect.Y + (_extRect.Height - extSz.Y) / 2f),
                Game1.textColor * 0.75f);

            DrawButton(b, _txtBtn,  "As Text", _ext == ".txt");
            DrawButton(b, _jsonBtn, "As JSON", _ext == ".json");
            DrawButton(b, _saveBtn, "Save",    false);

            // Toast
            if (_toast != null && Game1.currentGameTime != null
                && Game1.currentGameTime.TotalGameTime.TotalSeconds < _toastUntil)
            {
                Vector2 tSz = Game1.smallFont.MeasureString(_toast);
                b.DrawString(Game1.smallFont, _toast,
                    new Vector2(xPositionOnScreen + (width - tSz.X) / 2f,
                                yPositionOnScreen + height + 8),
                    Color.White);
            }

            _closeBtn.draw(b);
            drawMouse(b);
        }

        private static void DrawButton(SpriteBatch b, Rectangle r, string label, bool active)
        {
            b.Draw(Game1.fadeToBlackRect, r,
                active ? Color.Black * 0.4f : Color.Black * 0.2f);
            Vector2 sz = Game1.smallFont.MeasureString(label);
            b.DrawString(Game1.smallFont, label,
                new Vector2(r.X + (r.Width - sz.X) / 2f, r.Y + (r.Height - sz.Y) / 2f),
                active ? Color.White : Game1.textColor);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (_closeBtn.containsPoint(x, y)) { Close(); return; }

            if (_inputRect.Contains(x, y))
            {
                _inputActive = true;
                if (playSound) Game1.playSound("smallSelect");
                return;
            }

            if (_txtBtn.Contains(x, y))
            {
                _ext = ".txt";
                if (playSound) Game1.playSound("smallSelect");
                return;
            }

            if (_jsonBtn.Contains(x, y))
            {
                _ext = ".json";
                if (playSound) Game1.playSound("smallSelect");
                return;
            }

            if (_saveBtn.Contains(x, y))
            {
                DoSave();
                if (playSound) Game1.playSound("bigSelect");
                return;
            }

            // Click outside the dialog box closes it
            if (!new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height).Contains(x, y))
            {
                Close();
                return;
            }

            _inputActive = false;
        }

        public override void receiveKeyPress(Keys key)
        {
            if (_inputActive)
            {
                if (key == Keys.Escape) { _inputActive = false; }
                return;
            }
            if (key == Keys.Escape) Close();
        }

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (!_inputActive) return;

            if (e.Key == Keys.Back)
            {
                if (_filename.Length > 0) _filename = _filename[..^1];
            }
            else if (e.Key == Keys.Enter || e.Key == Keys.Escape) return;
            else if (!char.IsControl(e.Character))
            {
                // Strip filesystem-illegal chars
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), e.Character) < 0)
                    _filename += e.Character;
            }
        }

        private void DoSave()
        {
            string name = string.IsNullOrWhiteSpace(_filename)
                ? DateTime.Now.ToString("dd-MM-yyyy")
                : _filename.Trim();

            string folder = Path.Combine(ModEntry.ModFolderPath, "exports");
            Directory.CreateDirectory(folder);
            string fullPath = Path.Combine(folder, name + _ext);

            string content = _ext == ".json"
                ? DialogueLog.ExportAsJson()
                : DialogueLog.ExportAsText();

            File.WriteAllText(fullPath, content);

            // Reveal in OS file explorer (cross-platform best-effort)
            try
            {
                if (OperatingSystem.IsWindows())
                    Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", $"-R \"{fullPath}\"");
                else
                    Process.Start("xdg-open", $"\"{folder}\"");
            }
            catch { /* reveal is best-effort */ }

            _toast = $"Saved to exports/{name}{_ext}";
            _toastUntil = (Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0) + 4;
        }

        public void Close()
        {
            if (IsClosed) return;
            IsClosed = true;
            Game1.game1.Window.TextInput -= OnTextInput;
        }

        protected override void cleanupBeforeExit()
        {
            Close();
            base.cleanupBeforeExit();
        }
    }
}
