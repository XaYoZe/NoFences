using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NoFences.Theming
{
    /// <summary>
    /// Renders application-owned context menus from ThemeDefinition. Native shell
    /// menus shown for files remain owned by Explorer and can only follow the
    /// requested system dark/light preference.
    /// </summary>
    public sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly ThemeDefinition theme;

        public ThemedToolStripRenderer(ThemeDefinition theme)
            : base(new ThemeColorTable(theme))
        {
            this.theme = theme.Clone();
            RoundedEdges = theme.CornerRadius > 0;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(theme.MenuBackgroundColor))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            var marginColor = ThemeDrawing.Mix(theme.MenuBackgroundColor, theme.ControlBackgroundColor, 0.35f);
            using (var brush = new SolidBrush(marginColor))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            bounds.Inflate(-2, -1);
            var color = (e.Item.Selected || e.Item.Pressed)
                ? theme.MenuHighlightColor
                : theme.MenuBackgroundColor;

            using (var brush = new SolidBrush(color))
            {
                int radius = theme.CornerRadius > 0 ? 4 : 0;
                using (var path = ThemeDrawing.CreateRoundedRectangle(bounds, radius))
                    e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = (e.Item.Selected || e.Item.Pressed)
                ? theme.MenuHighlightTextColor
                : theme.MenuTextColor;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var color = ThemeDrawing.Mix(theme.MenuBackgroundColor, theme.BorderColor, 0.55f);
            using (var pen = new Pen(color))
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, 30, y, e.Item.Width - 6, y);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            Color color = (e.Item.Selected || e.Item.Pressed)
                ? theme.MenuHighlightTextColor
                : theme.MenuTextColor;
            Point center = new Point(e.ArrowRectangle.Left + e.ArrowRectangle.Width / 2,
                e.ArrowRectangle.Top + e.ArrowRectangle.Height / 2);
            Point[] points;
            if (e.Direction == ArrowDirection.Left)
                points = new[] { new Point(center.X + 2, center.Y - 4), new Point(center.X - 2, center.Y), new Point(center.X + 2, center.Y + 4) };
            else if (e.Direction == ArrowDirection.Up)
                points = new[] { new Point(center.X - 4, center.Y + 2), new Point(center.X, center.Y - 2), new Point(center.X + 4, center.Y + 2) };
            else if (e.Direction == ArrowDirection.Down)
                points = new[] { new Point(center.X - 4, center.Y - 2), new Point(center.X, center.Y + 2), new Point(center.X + 4, center.Y - 2) };
            else
                points = new[] { new Point(center.X - 2, center.Y - 4), new Point(center.X + 2, center.Y), new Point(center.X - 2, center.Y + 4) };

            using (var brush = new SolidBrush(color))
                e.Graphics.FillPolygon(brush, points);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var box = e.ImageRectangle;
            box.Inflate(-1, -1);
            using (var brush = new SolidBrush(theme.AccentColor))
                e.Graphics.FillRectangle(brush, box);

            Color checkColor = ThemeDrawing.IsDark(theme.AccentColor) ? Color.White : Color.Black;
            using (var pen = new Pen(checkColor, 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                e.Graphics.DrawLines(pen, new[]
                {
                    new Point(box.Left + 3, box.Top + box.Height / 2),
                    new Point(box.Left + box.Width / 2 - 1, box.Bottom - 4),
                    new Point(box.Right - 3, box.Top + 3)
                });
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var pen = new Pen(theme.BorderColor))
                e.Graphics.DrawRectangle(pen, bounds);
        }

        private sealed class ThemeColorTable : ProfessionalColorTable
        {
            private readonly ThemeDefinition theme;

            public ThemeColorTable(ThemeDefinition theme)
            {
                this.theme = theme;
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground => theme.MenuBackgroundColor;
            public override Color ImageMarginGradientBegin => theme.MenuBackgroundColor;
            public override Color ImageMarginGradientMiddle => theme.MenuBackgroundColor;
            public override Color ImageMarginGradientEnd => theme.MenuBackgroundColor;
            public override Color MenuBorder => theme.BorderColor;
            public override Color MenuItemBorder => theme.BorderColor;
            public override Color MenuItemSelected => theme.MenuHighlightColor;
            public override Color MenuItemSelectedGradientBegin => theme.MenuHighlightColor;
            public override Color MenuItemSelectedGradientEnd => theme.MenuHighlightColor;
            public override Color MenuItemPressedGradientBegin => theme.MenuHighlightColor;
            public override Color MenuItemPressedGradientMiddle => theme.MenuHighlightColor;
            public override Color MenuItemPressedGradientEnd => theme.MenuHighlightColor;
            public override Color SeparatorDark => ThemeDrawing.Mix(theme.MenuBackgroundColor, theme.BorderColor, 0.55f);
            public override Color SeparatorLight => theme.MenuBackgroundColor;
        }
    }
}
