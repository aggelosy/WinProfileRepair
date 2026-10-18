using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinProfileRepair
{
    internal static class UiTheme
    {
        internal static readonly Color Canvas = Color.FromArgb(247, 246, 243);
        internal static readonly Color SurfaceWhite = Color.White;
        internal static readonly Color Ink = Color.FromArgb(35, 38, 42);
        internal static readonly Color Muted = Color.FromArgb(112, 116, 123);
        internal static readonly Color Border = Color.FromArgb(225, 224, 220);
        internal static readonly Color Sidebar = Color.FromArgb(30, 33, 38);
        internal static readonly Color SidebarText = Color.FromArgb(215, 217, 220);
        internal static readonly Color Accent = Color.FromArgb(49, 95, 114);
        internal static readonly Color AccentTint = Color.FromArgb(234, 241, 243);
        internal static readonly Color Success = Color.FromArgb(44, 111, 88);
        internal static readonly Color Warning = Color.FromArgb(145, 97, 28);

        internal static GraphicsPath Rounded(RectangleF rectangle, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(Math.Max(0, radius * 2), Math.Min(rectangle.Width, rectangle.Height));
            if (diameter < 1) { path.AddRectangle(rectangle); return path; }
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static float Scale(Control control) { return Math.Max(1f, control.Font.SizeInPoints / 9f); }
    }

    internal class SurfacePanel : Panel
    {
        private bool showBorder = true;
        private int radius = 5;
        public bool ShowBorder { get { return showBorder; } set { showBorder = value; Invalidate(); } }
        public int Radius { get { return radius; } set { radius = Math.Max(0, value); Invalidate(); } }
        public SurfacePanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = UiTheme.SurfaceWhite;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!showBorder || Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = UiTheme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), radius))
            using (Pen border = new Pen(UiTheme.Border)) e.Graphics.DrawPath(border, shape);
        }
    }

    internal class ThemeButton : Button
    {
        private bool primary;
        private bool quiet;
        private bool hovered;
        private bool pressed;
        public bool Primary { get { return primary; } set { primary = value; Invalidate(); } }
        public bool Quiet { get { return quiet; } set { quiet = value; Invalidate(); } }
        public ThemeButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = UiTheme.SurfaceWhite;
            ForeColor = UiTheme.Ink;
            Height = 36;
            Padding = new Padding(14, 0, 14, 0);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Canvas : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Primary ? UiTheme.Accent : UiTheme.SurfaceWhite;
            Color text = Primary ? Color.White : UiTheme.Ink;
            if (quiet) fill = Parent == null ? UiTheme.Canvas : Parent.BackColor;
            if (!Enabled) { fill = Primary ? Color.FromArgb(231, 230, 226) : Color.FromArgb(246, 245, 242); text = Color.FromArgb(153, 155, 158); }
            else if (pressed) fill = Primary ? Color.FromArgb(22, 25, 29) : Color.FromArgb(230, 229, 225);
            else if (hovered) fill = Primary ? Color.FromArgb(58, 62, 69) : Color.FromArgb(239, 238, 234);
            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = UiTheme.Rounded(bounds, 5))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
                if (!quiet && !Primary) using (Pen pen = new Pen(UiTheme.Border)) e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Padding.Left, 0, Width - Padding.Horizontal, Height), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(4, 4, Width - 9, Height - 9), text, fill);
        }
    }

    internal class NavButton : Button
    {
        private bool selected;
        private bool hovered;
        private string badgeText;
        private string glyph;
        private bool light;
        public bool Light { get { return light; } set { light = value; Invalidate(); } }
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public string BadgeText { get { return badgeText; } set { badgeText = value; Invalidate(); } }
        public string Glyph { get { return glyph; } set { glyph = value; Invalidate(); } }
        public NavButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 42;
            BackColor = UiTheme.Sidebar;
            ForeColor = UiTheme.SidebarText;
            Cursor = Cursors.Hand;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(14, 0, 12, 0);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Sidebar : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = light ? (selected ? UiTheme.AccentTint : hovered && Enabled ? Color.FromArgb(244, 245, 243) : UiTheme.SurfaceWhite) : selected ? Color.FromArgb(54, 58, 65) : hovered && Enabled ? Color.FromArgb(42, 46, 52) : UiTheme.Sidebar;
            Color ink = !Enabled ? Color.FromArgb(112, 117, 125) : light ? (selected ? UiTheme.Accent : UiTheme.Muted) : selected ? Color.White : UiTheme.SidebarText;
            using (GraphicsPath path = UiTheme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 4))
            using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
            if (selected) using (SolidBrush line = new SolidBrush(light ? UiTheme.Accent : Color.FromArgb(220, 222, 225))) e.Graphics.FillRectangle(line, 0, 12, 2, Height - 24);
            float dpiScale = e.Graphics.DpiX / 96f;
            int glyphSize = (int)(16 * dpiScale);
            DrawGlyph(e.Graphics, glyph, new Rectangle((int)(13 * dpiScale), (Height - glyphSize) / 2, glyphSize, glyphSize), ink);
            int badgeWidth = String.IsNullOrEmpty(badgeText) ? 0 : Math.Max((int)(24 * dpiScale), TextRenderer.MeasureText(badgeText, Font).Width + (int)(10 * dpiScale));
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(40 * dpiScale), 0, Width - (int)(49 * dpiScale) - badgeWidth, Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (badgeWidth > 0)
            {
                int badgeHeight = (int)(22 * dpiScale);
                Rectangle badge = new Rectangle(Width - badgeWidth - (int)(9 * dpiScale), (Height - badgeHeight) / 2, badgeWidth, badgeHeight);
                using (GraphicsPath path = UiTheme.Rounded(badge, 3))
                using (SolidBrush brush = new SolidBrush(light ? Color.FromArgb(224, 233, 236) : selected ? Color.FromArgb(77, 82, 90) : Color.FromArgb(43, 47, 53))) e.Graphics.FillPath(brush, path);
                TextRenderer.DrawText(e.Graphics, badgeText, Font, badge, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(4, 4, Width - 9, Height - 9), ink, fill);
        }
        private static void DrawGlyph(Graphics graphics, string name, Rectangle bounds, Color color)
        {
            GraphicsState saved = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 16f, bounds.Height / 16f);
            using (Pen pen = new Pen(color, 1.2f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                if (name == "overview")
                {
                    graphics.DrawRectangle(pen, 1, 1, 5, 5); graphics.DrawRectangle(pen, 10, 1, 5, 5);
                    graphics.DrawRectangle(pen, 1, 10, 5, 5); graphics.DrawRectangle(pen, 10, 10, 5, 5);
                }
                else if (name == "history")
                {
                    graphics.DrawEllipse(pen, 1, 1, 14, 14); graphics.DrawLine(pen, 8, 4, 8, 8); graphics.DrawLine(pen, 8, 8, 11, 10);
                }
                else if (name == "backups")
                {
                    graphics.DrawRectangle(pen, 2, 5, 12, 10); graphics.DrawRectangle(pen, 1, 1, 14, 4); graphics.DrawLine(pen, 6, 9, 10, 9);
                }
                else if (name == "settings")
                {
                    graphics.DrawLine(pen, 1, 4, 15, 4); graphics.DrawLine(pen, 1, 12, 15, 12);
                    graphics.DrawEllipse(pen, 4, 1, 5, 6); graphics.DrawEllipse(pen, 8, 9, 5, 6);
                }
                else if (name == "findings")
                {
                    graphics.DrawRectangle(pen, 1, 1, 14, 14);
                    graphics.DrawLine(pen, 5, 5, 12, 5); graphics.DrawLine(pen, 5, 8, 12, 8); graphics.DrawLine(pen, 5, 11, 10, 11);
                    graphics.DrawLine(pen, 3, 5, 3.2f, 5); graphics.DrawLine(pen, 3, 8, 3.2f, 8);
                }
                else if (name == "review")
                {
                    graphics.DrawPolygon(pen, new[] { new PointF(8, 1), new PointF(15, 14), new PointF(1, 14) });
                    graphics.DrawLine(pen, 8, 6, 8, 9); graphics.DrawLine(pen, 8, 11, 8, 11.5f);
                }
                else if (name == "image")
                {
                    graphics.DrawRectangle(pen, 1, 2, 14, 12); graphics.DrawEllipse(pen, 4, 4, 2, 2);
                    graphics.DrawLines(pen, new[] { new Point(2, 13), new Point(7, 8), new Point(10, 11), new Point(12, 8), new Point(15, 11) });
                }
                else if (name == "restore")
                {
                    graphics.DrawArc(pen, 2, 2, 12, 12, 215, 295);
                    graphics.DrawLines(pen, new[] { new Point(1, 1), new Point(1, 6), new Point(6, 6) });
                    graphics.DrawLine(pen, 8, 5, 8, 9); graphics.DrawLine(pen, 8, 9, 11, 9);
                }
                else if (name == "cache")
                {
                    graphics.DrawArc(pen, 2, 2, 12, 12, 210, 270);
                    graphics.DrawLines(pen, new[] { new Point(1, 1), new Point(1, 6), new Point(6, 6) });
                    graphics.DrawLine(pen, 5, 9, 8, 12); graphics.DrawLine(pen, 8, 12, 13, 7);
                }
                else TextRenderer.DrawText(graphics, String.IsNullOrEmpty(name) ? "·" : name, SystemFonts.MessageBoxFont, new Rectangle(0, 0, 16, 16), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            graphics.Restore(saved);
        }
    }

    internal class StatCard : Control
    {
        private string caption = String.Empty;
        private string value = "—";
        private string footnote = String.Empty;
        private Color valueColor = UiTheme.Ink;
        public string Caption { get { return caption; } set { caption = value; UpdateAccessibleName(); Invalidate(); } }
        public string Value { get { return value; } set { this.value = value; UpdateAccessibleName(); Invalidate(); } }
        public string Footnote { get { return footnote; } set { footnote = value; UpdateAccessibleName(); Invalidate(); } }
        public Color ValueColor { get { return valueColor; } set { valueColor = value; Invalidate(); } }
        public StatCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.SurfaceWhite;
            ForeColor = UiTheme.Ink;
            Height = 104;
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
            UpdateAccessibleName();
        }
        private void UpdateAccessibleName() { AccessibleName = caption + ": " + value + ". " + footnote; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Canvas : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = UiTheme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 5))
            using (SolidBrush fill = new SolidBrush(UiTheme.SurfaceWhite))
            using (Pen pen = new Pen(UiTheme.Border)) { e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(pen, shape); }
            float scale = Math.Max(1f, Height / 104f);
            int inset = (int)(18 * scale);
            using (Font label = new Font(Font.FontFamily, 8f, FontStyle.Bold))
            using (Font number = new Font(Font.FontFamily, 25f, FontStyle.Regular))
            using (Font note = new Font(Font.FontFamily, 8f, FontStyle.Regular))
            {
                TextRenderer.DrawText(e.Graphics, caption, label, new Rectangle(inset, (int)(13 * scale), Width - inset * 2, (int)(18 * scale)), UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics, value, number, new Rectangle(inset - 1, (int)(31 * scale), Width - inset * 2, (int)(40 * scale)), valueColor, TextFormatFlags.Left | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(e.Graphics, footnote, note, new Rectangle(inset, (int)(77 * scale), Width - inset * 2, (int)(18 * scale)), UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            base.OnPaint(e);
        }
    }
}
