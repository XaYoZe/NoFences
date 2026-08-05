using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NoFences.Theming
{
    /// <summary>
    /// 按 ThemeDefinition 和 MenuThemeProfile 绘制程序自有的右键菜单。
    ///
    /// 本渲染器只负责应用菜单（空白处右键或条目上的 Shift+右键）；文件条目
    /// 的普通右键菜单属于 Windows Explorer，只能跟随系统明暗偏好，不能安全地
    /// 注入本程序的任意颜色与几何样式。
    /// </summary>
    public sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly ThemeDefinition theme;
        private readonly MenuThemeProfile profile;

        public ThemedToolStripRenderer(ThemeDefinition theme)
            : base(new ThemeColorTable(theme))
        {
            this.theme = theme.Clone();
            profile = MenuThemeProfile.Create(this.theme);

            // ProfessionalRenderer 自带的 RoundedEdges 只提供旧式小圆角，
            // 无法表达 Win11 的 8px 容器圆角，因此由本类统一设置 Region。
            RoundedEdges = false;
        }

        /// <summary>
        /// Profile 供 ThemeUi 配置菜单尺寸使用。仅暴露只读对象，避免布局层
        /// 再次推导主题名称或复制一套尺寸常量。
        /// </summary>
        internal MenuThemeProfile Profile => profile;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            UpdateToolStripRegion(e.ToolStrip);

            Rectangle bounds = e.ToolStrip.ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            int containerRadius = ScaleLogical(
                e.ToolStrip,
                profile.ContainerCornerRadius);

            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = containerRadius > 0
                    ? SmoothingMode.AntiAlias
                    : SmoothingMode.None;
                using (var brush = new SolidBrush(theme.MenuBackgroundColor))
                {
                    if (containerRadius <= 0)
                    {
                        e.Graphics.FillRectangle(brush, bounds);
                    }
                    else
                    {
                        using (var path = ThemeDrawing.CreateRoundedRectangle(
                            new RectangleF(0, 0, bounds.Width, bounds.Height),
                            containerRadius))
                            e.Graphics.FillPath(brush, path);
                    }
                }
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Win11 与 XP 的图标槽和菜单主体同色；Standard 可保留很轻的色差。
            // AffectedBounds 由 ToolStrip 布局给出，填满它可避免勾选项附近露底。
            using (var brush = new SolidBrush(profile.ImageMarginColor))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        /// <summary>
        /// 程序菜单项会在布局后按主题改写最终行高，因此重新按最终高度居中
        /// 图标，避免继续沿用 WinForms 基于系统默认行高生成的 ImageRectangle。
        /// </summary>
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image == null)
            {
                base.OnRenderItemImage(e);
                return;
            }

            Rectangle imageRectangle = CenterVertically(
                e.Item,
                e.ImageRectangle);
            int horizontalOffset = ScaleLogical(
                e.ToolStrip,
                profile.ContentHorizontalInset + profile.ImageHorizontalOffset);
            if (horizontalOffset != 0)
            {
                imageRectangle.Offset(
                    horizontalOffset,
                    0);
            }
            var centeredArgs = new ToolStripItemImageRenderEventArgs(
                e.Graphics,
                e.Item,
                e.Image,
                imageRectangle);
            base.OnRenderItemImage(centeredArgs);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var fullBounds = new Rectangle(Point.Empty, e.Item.Size);

            // ToolStrip 只会使当前菜单项失效；先清除旧的悬停背景，才能保证
            // 鼠标移出后不会残留上一主题或上一状态的色块。
            using (var background = new SolidBrush(theme.MenuBackgroundColor))
                e.Graphics.FillRectangle(background, fullBounds);

            if (!e.Item.Selected && !e.Item.Pressed)
                return;

            var highlightBounds = fullBounds;
            int horizontalInset = ScaleLogical(
                e.ToolStrip,
                profile.ItemHorizontalInset);
            int verticalInset = ScaleLogical(
                e.ToolStrip,
                profile.ItemVerticalInset);
            int itemRadius = ScaleLogical(
                e.ToolStrip,
                profile.ItemCornerRadius);
            highlightBounds.Inflate(
                -horizontalInset,
                -verticalInset);
            if (highlightBounds.Width <= 0 || highlightBounds.Height <= 0)
                return;

            Color color = e.Item.Pressed
                ? profile.PressedColor
                : theme.MenuHighlightColor;
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = itemRadius > 0
                    ? SmoothingMode.AntiAlias
                    : SmoothingMode.None;
                using (var brush = new SolidBrush(color))
                using (var path = ThemeDrawing.CreateRoundedRectangle(
                    highlightBounds,
                    itemRadius))
                    e.Graphics.FillPath(brush, path);
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var menuItem = e.Item as ToolStripMenuItem;
            bool isShortcutText = menuItem != null &&
                !string.Equals(e.Text, menuItem.Text, StringComparison.Ordinal);

            int textVerticalOffset = (int)Math.Round(
                profile.TextVerticalOffset * GetDpiScale(e.ToolStrip),
                MidpointRounding.AwayFromZero);
            int contentHorizontalInset = ScaleLogical(
                e.ToolStrip,
                profile.ContentHorizontalInset);

            // ToolStripDropDownMenu 会先根据系统默认的 22px 左右行高生成文字矩形，
            // ThemeUi 随后再把 Win11 菜单项扩大到 34px。框架不会同步把旧文字矩形
            // 重新放到新行高的正中央，因此文字会明显靠上。这里保留框架计算出的
            // 水平位置和宽度，只把垂直绘制区域扩展到整个菜单项，再明确使用单行
            // 垂直居中。Profile 中的补偿值继续修正具体字体的可见字形中心；普通
            // 文字、快捷键以及禁用态最终都会经过同一套基线规则。
            e.TextRectangle = new Rectangle(
                e.TextRectangle.Left + contentHorizontalInset,
                textVerticalOffset,
                Math.Max(1, e.TextRectangle.Width - contentHorizontalInset * 2),
                e.Item.Height);
            e.TextFormat &= ~(TextFormatFlags.Bottom | TextFormatFlags.VerticalCenter);
            e.TextFormat |= TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding;

            if (isShortcutText)
            {
                // ToolStrip 会先按自然宽度计算快捷键矩形，再由 ThemeUi 扩大
                // Win11/XP 的菜单最小宽度。重新靠右定位，保证 Ctrl+C、Del 等
                // 快捷键始终贴近右内边距，而不是停留在文字后方。
                Size measured = TextRenderer.MeasureText(
                    e.Text,
                    e.TextFont,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int right = e.Item.Width -
                    ScaleLogical(e.ToolStrip, profile.ItemHorizontalInset) -
                    ScaleLogical(e.ToolStrip, profile.ItemPadding.Right) -
                    contentHorizontalInset;
                e.TextRectangle = new Rectangle(
                    Math.Max(e.TextRectangle.Left, right - measured.Width),
                    textVerticalOffset,
                    measured.Width,
                    e.Item.Height);
                e.TextFormat |= TextFormatFlags.Right |
                    TextFormatFlags.VerticalCenter;
            }

            if (!e.Item.Enabled && profile.DrawEmbossedDisabledText)
            {
                // XP 的禁用文字采用“右下白色高光 + 前景灰字”的浮雕效果。
                // TextRenderer 可同时正确处理主文字和右侧快捷键的对齐标志。
                var highlightBounds = e.TextRectangle;
                highlightBounds.Offset(1, 1);
                TextRenderer.DrawText(
                    e.Graphics,
                    e.Text,
                    e.TextFont,
                    highlightBounds,
                    profile.BorderHighlightColor,
                    e.TextFormat);
                TextRenderer.DrawText(
                    e.Graphics,
                    e.Text,
                    e.TextFont,
                    e.TextRectangle,
                    profile.DisabledTextColor,
                    e.TextFormat);
                return;
            }

            if (!e.Item.Enabled)
            {
                e.TextColor = profile.DisabledTextColor;
            }
            else if (e.Item.Selected || e.Item.Pressed)
            {
                e.TextColor = theme.MenuHighlightTextColor;
            }
            else
            {
                e.TextColor = theme.MenuTextColor;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var background = new SolidBrush(theme.MenuBackgroundColor))
                e.Graphics.FillRectangle(background, new Rectangle(Point.Empty, e.Item.Size));

            int separatorInset = ScaleLogical(e.ToolStrip, profile.SeparatorInset);
            int left = Math.Min(separatorInset, Math.Max(0, e.Item.Width - 1));
            int right = Math.Max(left, e.Item.Width - separatorInset - 1);
            int y = e.Item.Height / 2;

            using (var primary = new Pen(profile.SeparatorPrimaryColor))
                e.Graphics.DrawLine(primary, left, y, right, y);

            // XP 的第二条白线构成凹陷分隔槽；Win11/Standard 的颜色为透明，
            // 保持单条、低对比度的现代分隔线。
            if (profile.SeparatorSecondaryColor.A > 0 && y + 1 < e.Item.Height)
            {
                using (var secondary = new Pen(profile.SeparatorSecondaryColor))
                    e.Graphics.DrawLine(secondary, left, y + 1, right, y + 1);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            Color color;
            if (!e.Item.Enabled)
                color = profile.DisabledTextColor;
            else if (e.Item.Selected || e.Item.Pressed)
                color = theme.MenuHighlightTextColor;
            else
                color = theme.MenuTextColor;

            Point center = new Point(
                e.ArrowRectangle.Left + e.ArrowRectangle.Width / 2 -
                    ScaleLogical(e.Item?.Owner, profile.ContentHorizontalInset),
                e.ArrowRectangle.Top + e.ArrowRectangle.Height / 2);

            if (profile.DrawChevronArrow)
            {
                DrawFluentChevron(
                    e.Graphics,
                    center,
                    e.Direction,
                    color,
                    GetDpiScale(e.Item?.Owner));
                return;
            }

            // XP 和 Standard 使用早期 Win32 菜单的实心小三角，尺寸固定且
            // 不启用抗锯齿，以保留紧凑、清晰的像素边缘。
            Point[] points = CreateTriangle(
                center,
                e.Direction,
                GetDpiScale(e.Item?.Owner));
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.None;
                using (var brush = new SolidBrush(color))
                    e.Graphics.FillPolygon(brush, points);
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle imageRectangle = CenterVertically(
                e.Item,
                e.ImageRectangle);
            int horizontalOffset = ScaleLogical(
                e.ToolStrip,
                profile.ContentHorizontalInset + profile.ImageHorizontalOffset);
            if (horizontalOffset != 0)
            {
                imageRectangle.Offset(
                    horizontalOffset,
                    0);
            }
            Rectangle box = CreateCenteredSquare(
                imageRectangle,
                ScaleLogical(e.ToolStrip, 14));
            int radius = profile.Style == ThemeMenuStyle.Windows11
                ? ScaleLogical(e.ToolStrip, 2)
                : 0;
            Color fillColor = e.Item.Enabled
                ? theme.AccentColor
                : ThemeDrawing.Mix(theme.MenuBackgroundColor, theme.AccentColor, 0.36f);

            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = radius > 0
                    ? SmoothingMode.AntiAlias
                    : SmoothingMode.None;
                using (var brush = new SolidBrush(fillColor))
                using (var path = ThemeDrawing.CreateRoundedRectangle(box, radius))
                    e.Graphics.FillPath(brush, path);

                Color checkColor = ThemeDrawing.IsDark(fillColor)
                    ? Color.White
                    : Color.Black;
                using (var pen = new Pen(
                    checkColor,
                    Math.Max(1f, 1.8f * GetDpiScale(e.ToolStrip))))
                {
                    if (profile.Style == ThemeMenuStyle.Windows11)
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                    }
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new Point(box.Left + 3, box.Top + box.Height / 2),
                        new Point(box.Left + box.Width / 2 - 1, box.Bottom - 4),
                        new Point(box.Right - 3, box.Top + 3)
                    });
                }
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip.Width <= 1 || e.ToolStrip.Height <= 1)
                return;

            if (profile.DrawClassicThreeDimensionalBorder)
            {
                DrawClassicBorder(e.Graphics, e.ToolStrip.ClientRectangle);
                return;
            }

            GraphicsState state = e.Graphics.Save();
            try
            {
                int containerRadius = ScaleLogical(
                    e.ToolStrip,
                    profile.ContainerCornerRadius);
                e.Graphics.SmoothingMode = containerRadius > 0
                    ? SmoothingMode.AntiAlias
                    : SmoothingMode.None;
                using (var path = ThemeDrawing.CreateRoundedRectangle(
                    new RectangleF(
                        0.5f,
                        0.5f,
                        e.ToolStrip.Width - 1f,
                        e.ToolStrip.Height - 1f),
                    containerRadius))
                using (var pen = new Pen(profile.BorderOuterColor))
                    e.Graphics.DrawPath(pen, path);
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        private void DrawClassicBorder(Graphics graphics, Rectangle bounds)
        {
            int right = Math.Max(0, bounds.Right - 1);
            int bottom = Math.Max(0, bounds.Bottom - 1);
            using (var outer = new Pen(profile.BorderOuterColor))
                graphics.DrawRectangle(outer, 0, 0, right, bottom);

            if (right < 2 || bottom < 2)
                return;

            using (var highlight = new Pen(profile.BorderHighlightColor))
            using (var shadow = new Pen(profile.BorderShadowColor))
            {
                graphics.DrawLine(highlight, 1, 1, right - 1, 1);
                graphics.DrawLine(highlight, 1, 1, 1, bottom - 1);
                graphics.DrawLine(shadow, 1, bottom - 1, right - 1, bottom - 1);
                graphics.DrawLine(shadow, right - 1, 1, right - 1, bottom - 1);
            }
        }

        private void UpdateToolStripRegion(ToolStrip toolStrip)
        {
            Region oldRegion = toolStrip.Region;
            int containerRadius = ScaleLogical(
                toolStrip,
                profile.ContainerCornerRadius);
            if (containerRadius <= 0 ||
                toolStrip.Width <= 0 ||
                toolStrip.Height <= 0)
            {
                if (oldRegion != null)
                {
                    toolStrip.Region = null;
                    oldRegion.Dispose();
                }
                return;
            }

            using (var path = ThemeDrawing.CreateRoundedRectangle(
                new RectangleF(0, 0, toolStrip.Width, toolStrip.Height),
                containerRadius))
                toolStrip.Region = new Region(path);
            oldRegion?.Dispose();
        }

        private static void DrawFluentChevron(
            Graphics graphics,
            Point center,
            ArrowDirection direction,
            Color color,
            float scale)
        {
            float near = 2f * scale;
            float far = 4f * scale;
            PointF[] points;
            switch (direction)
            {
                case ArrowDirection.Left:
                    points = new[]
                    {
                        new PointF(center.X + near, center.Y - far),
                        new PointF(center.X - near, center.Y),
                        new PointF(center.X + near, center.Y + far)
                    };
                    break;
                case ArrowDirection.Up:
                    points = new[]
                    {
                        new PointF(center.X - far, center.Y + near),
                        new PointF(center.X, center.Y - near),
                        new PointF(center.X + far, center.Y + near)
                    };
                    break;
                case ArrowDirection.Down:
                    points = new[]
                    {
                        new PointF(center.X - far, center.Y - near),
                        new PointF(center.X, center.Y + near),
                        new PointF(center.X + far, center.Y - near)
                    };
                    break;
                default:
                    points = new[]
                    {
                        new PointF(center.X - near, center.Y - far),
                        new PointF(center.X + near, center.Y),
                        new PointF(center.X - near, center.Y + far)
                    };
                    break;
            }

            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(color, Math.Max(1f, 1.35f * scale)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    graphics.DrawLines(pen, points);
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static Point[] CreateTriangle(
            Point center,
            ArrowDirection direction,
            float scale)
        {
            int near = Math.Max(1, (int)Math.Round(2f * scale));
            int far = Math.Max(2, (int)Math.Round(4f * scale));
            switch (direction)
            {
                case ArrowDirection.Left:
                    return new[]
                    {
                        new Point(center.X + near, center.Y - far),
                        new Point(center.X - near, center.Y),
                        new Point(center.X + near, center.Y + far)
                    };
                case ArrowDirection.Up:
                    return new[]
                    {
                        new Point(center.X - far, center.Y + near),
                        new Point(center.X, center.Y - near),
                        new Point(center.X + far, center.Y + near)
                    };
                case ArrowDirection.Down:
                    return new[]
                    {
                        new Point(center.X - far, center.Y - near),
                        new Point(center.X, center.Y + near),
                        new Point(center.X + far, center.Y - near)
                    };
                default:
                    return new[]
                    {
                        new Point(center.X - near, center.Y - far),
                        new Point(center.X + near, center.Y),
                        new Point(center.X - near, center.Y + far)
                    };
            }
        }

        private static Rectangle CreateCenteredSquare(Rectangle bounds, int maximumSize)
        {
            int size = Math.Max(1, Math.Min(maximumSize, Math.Min(bounds.Width, bounds.Height)));
            return new Rectangle(
                bounds.Left + (bounds.Width - size) / 2,
                bounds.Top + (bounds.Height - size) / 2,
                size,
                size);
        }

        /// <summary>保持横向槽位不变，仅按菜单项最终行高重新垂直居中矩形。</summary>
        private static Rectangle CenterVertically(
            ToolStripItem item,
            Rectangle bounds)
        {
            if (item == null)
                return bounds;

            return new Rectangle(
                bounds.X,
                Math.Max(0, (item.Height - bounds.Height) / 2),
                bounds.Width,
                bounds.Height);
        }

        private static float GetDpiScale(ToolStrip toolStrip)
        {
            int dpi = toolStrip.DeviceDpi > 0 ? toolStrip.DeviceDpi : 96;
            return dpi / 96f;
        }

        private static int ScaleLogical(ToolStrip toolStrip, int logicalPixels)
        {
            if (logicalPixels <= 0)
                return 0;
            return Math.Max(1, (int)Math.Round(
                logicalPixels * GetDpiScale(toolStrip),
                MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// ProfessionalRenderer 仍会读取颜色表处理未覆盖的系统绘制路径，例如
        /// 极少出现的溢出按钮；颜色表与自定义绘制共享同一个主题快照。
        /// </summary>
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
            public override Color SeparatorDark => theme.BorderColor;
            public override Color SeparatorLight => theme.MenuBackgroundColor;
        }
    }
}
