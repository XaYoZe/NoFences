using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NoFences.Theming
{
    /// <summary>
    /// 应用右键菜单中使用的语义图标。枚举与具体 ToolStripMenuItem 解耦，
    /// 以后调整菜单顺序或增加另一种主题绘制方式时无需依赖控件名称字符串。
    /// </summary>
    internal enum ThemedMenuIcon
    {
        ManageIcons,
        Delete,
        Lock,
        Minify,
        Rename,
        TitleHeight,
        Theme,
        DarkMode,
        NewFence,
        CloseFence,
        QuitApplication
    }

    /// <summary>
    /// 以 GDI+ 生成 16×16 的主题菜单图标。Win11/Standard 使用四倍超采样的
    /// 彩色矢量风格图标，既不依赖系统 Emoji 字体或第三方 SVG 解析器，又能在
    /// 浅色、深色和自定义主题中保持清晰；XP 继续使用经典高对比像素风格。
    /// </summary>
    internal static class ThemedMenuIconFactory
    {
        private const int IconSize = 16;
        private const int ColorfulRenderScale = 4;

        private static readonly Color Blue = Color.FromArgb(65, 132, 246);
        private static readonly Color Cyan = Color.FromArgb(43, 194, 225);
        private static readonly Color Green = Color.FromArgb(43, 190, 116);
        private static readonly Color Yellow = Color.FromArgb(255, 197, 61);
        private static readonly Color Orange = Color.FromArgb(255, 137, 55);
        private static readonly Color Red = Color.FromArgb(239, 78, 91);
        private static readonly Color Pink = Color.FromArgb(236, 91, 161);
        private static readonly Color Purple = Color.FromArgb(139, 92, 246);
        private static readonly Color Indigo = Color.FromArgb(91, 105, 232);

        public static Image Create(ThemedMenuIcon icon, ThemeDefinition theme)
        {
            var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppPArgb);
            bitmap.SetResolution(96f, 96f);

            if (theme.MenuStyle == ThemeMenuStyle.WindowsXp)
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    DrawWindowsXpIcon(graphics, icon);
                }
            }
            else
            {
                DrawColorfulIconSupersampled(bitmap, icon, theme);
            }

            return bitmap;
        }

        /// <summary>在高分辨率透明画布绘制后缩小，改善 16 像素图标的曲线与斜线边缘。</summary>
        private static void DrawColorfulIconSupersampled(
            Bitmap destination,
            ThemedMenuIcon icon,
            ThemeDefinition theme)
        {
            int renderSize = IconSize * ColorfulRenderScale;
            using (var rendered = new Bitmap(
                renderSize,
                renderSize,
                PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(rendered))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.ScaleTransform(ColorfulRenderScale, ColorfulRenderScale);
                    DrawColorfulIcon(graphics, icon, theme);
                }

                using (Graphics graphics = Graphics.FromImage(destination))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(
                        rendered,
                        new Rectangle(0, 0, IconSize, IconSize),
                        0,
                        0,
                        renderSize,
                        renderSize,
                        GraphicsUnit.Pixel);
                }
            }
        }

        /// <summary>绘制默认与 Win11 菜单共用的彩色矢量风格语义图标。</summary>
        private static void DrawColorfulIcon(
            Graphics graphics,
            ThemedMenuIcon icon,
            ThemeDefinition theme)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            bool darkBackground = ThemeDrawing.IsDark(theme.MenuBackgroundColor);
            Color outline = darkBackground
                ? Color.FromArgb(205, 245, 247, 255)
                : Color.FromArgb(185, 30, 38, 52);

            switch (icon)
            {
                case ThemedMenuIcon.ManageIcons:
                    DrawManageIcons(graphics, outline);
                    break;
                case ThemedMenuIcon.Delete:
                    DrawDeleteIcon(graphics, outline);
                    break;
                case ThemedMenuIcon.Lock:
                    DrawLockIcon(graphics, outline);
                    break;
                case ThemedMenuIcon.Minify:
                    DrawMinifyIcon(graphics, outline);
                    break;
                case ThemedMenuIcon.Rename:
                    DrawRenameIcon(graphics, outline);
                    break;
                case ThemedMenuIcon.TitleHeight:
                    DrawTitleHeightIcon(graphics, outline);
                    break;
                case ThemedMenuIcon.Theme:
                    DrawThemeIcon(graphics, outline, theme.MenuBackgroundColor);
                    break;
                case ThemedMenuIcon.DarkMode:
                    DrawDarkModeIcon(graphics, outline);
                    break;
                case ThemedMenuIcon.NewFence:
                    DrawNewFenceIcon(graphics, outline);
                    break;
                case ThemedMenuIcon.CloseFence:
                    DrawCloseFenceIcon(graphics, outline);
                    break;
                case ThemedMenuIcon.QuitApplication:
                    DrawQuitIcon(graphics, outline);
                    break;
            }
        }

        /// <summary>绘制彩色应用网格和确认徽标。</summary>
        private static void DrawManageIcons(Graphics graphics, Color outline)
        {
            FillGradientRoundedRectangle(graphics, new RectangleF(1.2f, 1.2f, 6f, 6f), 1.4f, Blue, Cyan, outline);
            FillGradientRoundedRectangle(graphics, new RectangleF(8.8f, 1.2f, 6f, 6f), 1.4f, Purple, Pink, outline);
            FillGradientRoundedRectangle(graphics, new RectangleF(1.2f, 8.8f, 6f, 6f), 1.4f, Orange, Yellow, outline);
            FillGradientRoundedRectangle(graphics, new RectangleF(8.8f, 8.8f, 6f, 6f), 1.4f, Indigo, Blue, outline);

            DrawCircleBadge(graphics, new RectangleF(9.2f, 9.2f, 6.1f, 6.1f), Green, outline);
            using (var check = CreateRoundedPen(Color.White, 1.45f))
            {
                graphics.DrawLines(check, new[]
                {
                    new PointF(10.7f, 12.2f),
                    new PointF(12f, 13.4f),
                    new PointF(14.2f, 10.7f)
                });
            }
        }

        /// <summary>绘制带高光槽线的珊瑚红垃圾桶。</summary>
        private static void DrawDeleteIcon(Graphics graphics, Color outline)
        {
            FillGradientRoundedRectangle(graphics, new RectangleF(4f, 5f, 8f, 9.3f), 1.6f, Red, Orange, outline, 90f);
            FillGradientRoundedRectangle(graphics, new RectangleF(2.8f, 3.1f, 10.4f, 2.5f), 1.1f, Pink, Red, outline, 0f);
            FillGradientRoundedRectangle(graphics, new RectangleF(6.1f, 1.4f, 3.8f, 2.2f), 0.9f, Yellow, Orange, outline, 0f);

            using (var detail = CreateRoundedPen(Color.FromArgb(215, 255, 255, 255), 0.8f))
            {
                graphics.DrawLine(detail, 6.6f, 7.1f, 6.6f, 12f);
                graphics.DrawLine(detail, 9.4f, 7.1f, 9.4f, 12f);
            }
        }

        /// <summary>绘制金色锁梁与蓝紫渐变锁体。</summary>
        private static void DrawLockIcon(Graphics graphics, Color outline)
        {
            using (var shackle = CreateRoundedPen(Yellow, 2.2f))
            using (var shackleOutline = CreateRoundedPen(outline, 3.3f))
            {
                graphics.DrawArc(shackleOutline, 4.1f, 1.2f, 7.8f, 8f, 180f, 180f);
                graphics.DrawArc(shackle, 4.1f, 1.2f, 7.8f, 8f, 180f, 180f);
            }

            FillGradientRoundedRectangle(graphics, new RectangleF(2.2f, 6f, 11.6f, 8.5f), 2.1f, Blue, Purple, outline, 35f);
            using (var key = new SolidBrush(Yellow))
            {
                graphics.FillEllipse(key, 6.8f, 8.2f, 2.4f, 2.4f);
                graphics.FillRectangle(key, 7.5f, 9.8f, 1f, 2.2f);
            }
        }

        /// <summary>绘制带彩色标题点和收起横线的蓝色窗口。</summary>
        private static void DrawMinifyIcon(Graphics graphics, Color outline)
        {
            var bounds = new RectangleF(1.2f, 2f, 13.6f, 12f);
            FillGradientRoundedRectangle(graphics, bounds, 2f, Blue, Cyan, outline, 90f);
            using (var bar = new LinearGradientBrush(
                new RectangleF(1.8f, 2.5f, 12.4f, 3.3f),
                Purple,
                Indigo,
                0f))
            {
                graphics.FillRectangle(bar, 1.8f, 2.5f, 12.4f, 3.3f);
            }

            using (var pink = new SolidBrush(Pink))
            using (var yellow = new SolidBrush(Yellow))
            using (var green = new SolidBrush(Green))
            {
                graphics.FillEllipse(pink, 2.8f, 3.4f, 1.2f, 1.2f);
                graphics.FillEllipse(yellow, 4.7f, 3.4f, 1.2f, 1.2f);
                graphics.FillEllipse(green, 6.6f, 3.4f, 1.2f, 1.2f);
            }

            using (var line = CreateRoundedPen(Color.White, 1.4f))
                graphics.DrawLine(line, 5.1f, 11.2f, 10.9f, 11.2f);
        }

        /// <summary>绘制带粉色橡皮和木质笔尖的金色铅笔。</summary>
        private static void DrawRenameIcon(Graphics graphics, Color outline)
        {
            PointF[] body =
            {
                new PointF(3.1f, 10.9f),
                new PointF(10.7f, 3.3f),
                new PointF(13.2f, 5.8f),
                new PointF(5.6f, 13.4f)
            };
            using (var fill = new LinearGradientBrush(
                new RectangleF(3f, 3f, 10.5f, 10.8f),
                Yellow,
                Orange,
                45f))
            using (var border = new Pen(outline, 0.75f))
            {
                border.LineJoin = LineJoin.Round;
                graphics.FillPolygon(fill, body);
                graphics.DrawPolygon(border, body);
            }

            PointF[] eraser =
            {
                new PointF(10.7f, 3.3f),
                new PointF(12.1f, 1.9f),
                new PointF(14.6f, 4.4f),
                new PointF(13.2f, 5.8f)
            };
            using (var fill = new SolidBrush(Pink))
            using (var border = new Pen(outline, 0.75f))
            {
                border.LineJoin = LineJoin.Round;
                graphics.FillPolygon(fill, eraser);
                graphics.DrawPolygon(border, eraser);
            }

            PointF[] tip =
            {
                new PointF(1.6f, 14.8f),
                new PointF(3.1f, 10.9f),
                new PointF(5.6f, 13.4f)
            };
            using (var wood = new SolidBrush(Color.FromArgb(255, 224, 164)))
            using (var border = new Pen(outline, 0.75f))
            using (var graphite = new SolidBrush(Color.FromArgb(55, 60, 72)))
            {
                graphics.FillPolygon(wood, tip);
                graphics.DrawPolygon(border, tip);
                graphics.FillPolygon(graphite, new[]
                {
                    new PointF(1.6f, 14.8f),
                    new PointF(2.2f, 13.2f),
                    new PointF(3.2f, 14.2f)
                });
            }

            using (var shine = CreateRoundedPen(Color.FromArgb(180, 255, 255, 255), 0.65f))
                graphics.DrawLine(shine, 5f, 10.7f, 10.8f, 4.9f);
        }

        /// <summary>绘制上下彩色边界和双向高度箭头。</summary>
        private static void DrawTitleHeightIcon(Graphics graphics, Color outline)
        {
            FillGradientRoundedRectangle(graphics, new RectangleF(1.5f, 1.3f, 13f, 2.4f), 1.2f, Purple, Pink, outline, 0f);
            FillGradientRoundedRectangle(graphics, new RectangleF(1.5f, 12.3f, 13f, 2.4f), 1.2f, Blue, Cyan, outline, 0f);

            using (var stem = CreateRoundedPen(Green, 1.4f))
                graphics.DrawLine(stem, 8f, 4.9f, 8f, 11.1f);
            using (var arrow = new SolidBrush(Yellow))
            using (var border = new Pen(outline, 0.55f))
            {
                PointF[] up =
                {
                    new PointF(8f, 4f),
                    new PointF(5.7f, 6.6f),
                    new PointF(10.3f, 6.6f)
                };
                PointF[] down =
                {
                    new PointF(8f, 12f),
                    new PointF(5.7f, 9.4f),
                    new PointF(10.3f, 9.4f)
                };
                graphics.FillPolygon(arrow, up);
                graphics.FillPolygon(arrow, down);
                graphics.DrawPolygon(border, up);
                graphics.DrawPolygon(border, down);
            }
        }

        /// <summary>绘制带多色颜料点的渐变调色盘。</summary>
        private static void DrawThemeIcon(Graphics graphics, Color outline, Color menuBackground)
        {
            var bounds = new RectangleF(1f, 1f, 14f, 14f);
            using (var fill = new LinearGradientBrush(bounds, Yellow, Orange, 45f))
            using (var border = new Pen(outline, 0.75f))
            {
                graphics.FillEllipse(fill, bounds);
                graphics.DrawEllipse(border, bounds);
            }

            using (var blue = new SolidBrush(Blue))
            using (var pink = new SolidBrush(Pink))
            using (var green = new SolidBrush(Green))
            using (var purple = new SolidBrush(Purple))
            using (var hole = new SolidBrush(ThemeDrawing.Mix(menuBackground, Color.White, 0.14f)))
            {
                graphics.FillEllipse(pink, 3.4f, 4f, 2.6f, 2.6f);
                graphics.FillEllipse(blue, 6.9f, 2.7f, 2.6f, 2.6f);
                graphics.FillEllipse(green, 10.1f, 4.6f, 2.6f, 2.6f);
                graphics.FillEllipse(purple, 3.1f, 8.1f, 2.6f, 2.6f);
                graphics.FillEllipse(hole, 8.3f, 9.2f, 3.4f, 2.8f);
            }
        }

        /// <summary>绘制蓝紫渐变月牙与金色星光。</summary>
        private static void DrawDarkModeIcon(Graphics graphics, Color outline)
        {
            using (var moon = CreateCrescentPath())
            using (var fill = new LinearGradientBrush(moon.GetBounds(), Purple, Blue, 45f))
            using (var border = new Pen(outline, 0.7f))
            {
                graphics.FillPath(fill, moon);
                graphics.DrawPath(border, moon);
            }

            PointF[] star =
            {
                new PointF(12.1f, 1.2f),
                new PointF(12.8f, 3f),
                new PointF(14.6f, 3.7f),
                new PointF(12.8f, 4.4f),
                new PointF(12.1f, 6.2f),
                new PointF(11.4f, 4.4f),
                new PointF(9.6f, 3.7f),
                new PointF(11.4f, 3f)
            };
            using (var fill = new SolidBrush(Yellow))
            using (var border = new Pen(outline, 0.5f))
            {
                graphics.FillPolygon(fill, star);
                graphics.DrawPolygon(border, star);
            }
        }

        /// <summary>绘制蓝色分区面板和绿色新增徽标。</summary>
        private static void DrawNewFenceIcon(Graphics graphics, Color outline)
        {
            DrawFencePanel(graphics, outline, Blue, Cyan);
            DrawCircleBadge(graphics, new RectangleF(9.2f, 1f, 6.1f, 6.1f), Green, outline);
            using (var plus = CreateRoundedPen(Color.White, 1.25f))
            {
                graphics.DrawLine(plus, 12.25f, 2.5f, 12.25f, 5.6f);
                graphics.DrawLine(plus, 10.7f, 4.05f, 13.8f, 4.05f);
            }
        }

        /// <summary>绘制靛蓝分区面板和珊瑚红关闭徽标。</summary>
        private static void DrawCloseFenceIcon(Graphics graphics, Color outline)
        {
            DrawFencePanel(graphics, outline, Indigo, Purple);
            DrawCircleBadge(graphics, new RectangleF(9.2f, 8.9f, 6.1f, 6.1f), Red, outline);
            using (var cross = CreateRoundedPen(Color.White, 1.2f))
            {
                graphics.DrawLine(cross, 10.9f, 10.6f, 13.6f, 13.3f);
                graphics.DrawLine(cross, 13.6f, 10.6f, 10.9f, 13.3f);
            }
        }

        /// <summary>绘制红橙渐变电源环和蓝色开关键。</summary>
        private static void DrawQuitIcon(Graphics graphics, Color outline)
        {
            var bounds = new RectangleF(2.1f, 2.2f, 11.8f, 11.8f);
            using (var outlinePen = CreateRoundedPen(outline, 3.5f))
                graphics.DrawArc(outlinePen, bounds, -48f, 276f);
            using (var gradient = new LinearGradientBrush(bounds, Pink, Orange, 45f))
            using (var ring = new Pen(gradient, 2.4f))
            {
                ring.StartCap = LineCap.Round;
                ring.EndCap = LineCap.Round;
                graphics.DrawArc(ring, bounds, -48f, 276f);
            }
            using (var outlinePen = CreateRoundedPen(outline, 3.2f))
            using (var switchPen = CreateRoundedPen(Blue, 2.1f))
            {
                graphics.DrawLine(outlinePen, 8f, 1.2f, 8f, 7.8f);
                graphics.DrawLine(switchPen, 8f, 1.2f, 8f, 7.8f);
            }
        }

        /// <summary>绘制带页签的渐变分区面板底图。</summary>
        private static void DrawFencePanel(
            Graphics graphics,
            Color outline,
            Color first,
            Color second)
        {
            PointF[] panel =
            {
                new PointF(1.2f, 4.1f),
                new PointF(5.2f, 4.1f),
                new PointF(6.7f, 5.4f),
                new PointF(14.3f, 5.4f),
                new PointF(14.3f, 14.2f),
                new PointF(1.2f, 14.2f)
            };
            using (var fill = new LinearGradientBrush(
                new RectangleF(1.2f, 4f, 13.1f, 10.2f),
                first,
                second,
                45f))
            using (var border = new Pen(outline, 0.75f))
            {
                border.LineJoin = LineJoin.Round;
                graphics.FillPolygon(fill, panel);
                graphics.DrawPolygon(border, panel);
            }

            FillGradientRoundedRectangle(
                graphics,
                new RectangleF(1.7f, 2.3f, 5.3f, 3f),
                1f,
                Purple,
                Pink,
                outline,
                0f);
        }

        /// <summary>绘制带细轮廓的圆形状态徽标。</summary>
        private static void DrawCircleBadge(
            Graphics graphics,
            RectangleF bounds,
            Color fill,
            Color outline)
        {
            using (var brush = new SolidBrush(fill))
            using (var border = new Pen(outline, 0.65f))
            {
                graphics.FillEllipse(brush, bounds);
                graphics.DrawEllipse(border, bounds);
            }
        }

        /// <summary>绘制带渐变填充和细轮廓的圆角矩形。</summary>
        private static void FillGradientRoundedRectangle(
            Graphics graphics,
            RectangleF bounds,
            float radius,
            Color first,
            Color second,
            Color outline,
            float angle = 45f)
        {
            using (var path = ThemeDrawing.CreateRoundedRectangle(bounds, radius))
            using (var fill = new LinearGradientBrush(bounds, first, second, angle))
            using (var border = new Pen(outline, 0.65f))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }
        }

        /// <summary>创建用于小尺寸图标的圆头圆角画笔。</summary>
        private static Pen CreateRoundedPen(Color color, float width)
        {
            return new Pen(color, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
        }

        private static void DrawWindowsXpIcon(Graphics graphics, ThemedMenuIcon icon)
        {
            // XP 图标保留较硬的像素边缘，只在圆形/斜线图形上启用轻度抗锯齿。
            graphics.SmoothingMode = SmoothingMode.None;
            using (var outline = new Pen(Color.FromArgb(55, 55, 55)))
            {
                switch (icon)
                {
                    case ThemedMenuIcon.ManageIcons:
                        using (var fill = new SolidBrush(Color.FromArgb(115, 169, 235)))
                        using (var check = new Pen(Color.FromArgb(20, 145, 45), 2f))
                        {
                            graphics.FillRectangle(fill, 2, 2, 5, 5);
                            graphics.FillRectangle(fill, 9, 2, 5, 5);
                            graphics.FillRectangle(fill, 2, 9, 5, 5);
                            graphics.DrawRectangle(outline, 2, 2, 5, 5);
                            graphics.DrawRectangle(outline, 9, 2, 5, 5);
                            graphics.DrawRectangle(outline, 2, 9, 5, 5);
                            graphics.DrawLines(check, new[]
                            {
                                new Point(9, 11), new Point(11, 13), new Point(15, 8)
                            });
                        }
                        break;
                    case ThemedMenuIcon.Delete:
                        using (var fill = new SolidBrush(Color.FromArgb(220, 74, 62)))
                        {
                            graphics.FillRectangle(fill, 5, 5, 7, 9);
                            graphics.DrawRectangle(outline, 5, 5, 7, 9);
                            graphics.DrawLine(outline, 4, 4, 13, 4);
                            graphics.DrawLine(outline, 7, 2, 10, 2);
                        }
                        break;
                    case ThemedMenuIcon.Lock:
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var fill = new SolidBrush(Color.FromArgb(255, 202, 45)))
                        {
                            graphics.DrawArc(outline, 4, 1, 8, 9, 180, -180);
                            graphics.FillRectangle(fill, 3, 6, 10, 8);
                            graphics.DrawRectangle(outline, 3, 6, 10, 8);
                        }
                        break;
                    case ThemedMenuIcon.Minify:
                        using (var fill = new SolidBrush(Color.FromArgb(77, 137, 218)))
                        {
                            graphics.FillRectangle(fill, 2, 3, 12, 10);
                            graphics.DrawRectangle(outline, 2, 3, 12, 10);
                            graphics.DrawLine(Pens.White, 5, 10, 11, 10);
                        }
                        break;
                    case ThemedMenuIcon.Rename:
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var pencil = new Pen(Color.FromArgb(240, 174, 44), 3f))
                        {
                            pencil.EndCap = LineCap.Square;
                            graphics.DrawLine(pencil, 3, 13, 12, 4);
                            graphics.DrawLine(outline, 3, 14, 13, 4);
                        }
                        break;
                    case ThemedMenuIcon.TitleHeight:
                        using (var blue = new Pen(Color.FromArgb(49, 106, 197), 2f))
                        {
                            graphics.DrawLine(outline, 2, 2, 14, 2);
                            graphics.DrawLine(outline, 2, 14, 14, 14);
                            graphics.DrawLine(blue, 8, 4, 8, 12);
                            graphics.DrawLine(blue, 8, 4, 5, 7);
                            graphics.DrawLine(blue, 8, 4, 11, 7);
                            graphics.DrawLine(blue, 8, 12, 5, 9);
                            graphics.DrawLine(blue, 8, 12, 11, 9);
                        }
                        break;
                    case ThemedMenuIcon.Theme:
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var palette = new SolidBrush(Color.FromArgb(234, 211, 150)))
                        {
                            graphics.FillEllipse(palette, 1, 1, 14, 14);
                            graphics.DrawEllipse(outline, 1, 1, 14, 14);
                            graphics.FillEllipse(Brushes.Red, 4, 4, 3, 3);
                            graphics.FillEllipse(Brushes.RoyalBlue, 8, 3, 3, 3);
                            graphics.FillEllipse(Brushes.LimeGreen, 10, 7, 3, 3);
                        }
                        break;
                    case ThemedMenuIcon.DarkMode:
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var yellow = new SolidBrush(Color.FromArgb(255, 210, 54)))
                        using (var moon = CreateCrescentPath())
                        {
                            graphics.FillPath(yellow, moon);
                            graphics.DrawPath(outline, moon);
                        }
                        break;
                    case ThemedMenuIcon.NewFence:
                        using (var fill = new SolidBrush(Color.FromArgb(115, 169, 235)))
                        using (var plus = new Pen(Color.FromArgb(20, 145, 45), 2f))
                        {
                            graphics.FillRectangle(fill, 2, 3, 11, 10);
                            graphics.DrawRectangle(outline, 2, 3, 11, 10);
                            graphics.DrawLine(plus, 11, 1, 11, 8);
                            graphics.DrawLine(plus, 7, 4, 14, 4);
                        }
                        break;
                    case ThemedMenuIcon.CloseFence:
                        using (var fill = new SolidBrush(Color.FromArgb(192, 192, 192)))
                        using (var cross = new Pen(Color.FromArgb(205, 50, 45), 2f))
                        {
                            graphics.FillRectangle(fill, 2, 3, 12, 10);
                            graphics.DrawRectangle(outline, 2, 3, 12, 10);
                            graphics.DrawLine(cross, 5, 6, 11, 12);
                            graphics.DrawLine(cross, 11, 6, 5, 12);
                        }
                        break;
                    case ThemedMenuIcon.QuitApplication:
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var red = new Pen(Color.FromArgb(210, 52, 45), 2.2f))
                        {
                            red.StartCap = LineCap.Round;
                            red.EndCap = LineCap.Round;
                            graphics.DrawArc(red, 3, 3, 10, 10, -48, 276);
                            graphics.DrawLine(red, 8, 1, 8, 8);
                        }
                        break;
                }
            }
        }

        private static GraphicsPath CreateCrescentPath()
        {
            var path = new GraphicsPath();
            path.AddArc(2, 1, 12, 14, 85, 215);
            path.AddArc(5, 1, 10, 11, 285, -205);
            path.CloseFigure();
            return path;
        }
    }
}
