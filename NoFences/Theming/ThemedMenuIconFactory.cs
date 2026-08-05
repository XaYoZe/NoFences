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
    /// 以 GDI+ 生成 16×16 的主题菜单图标。Win11/Standard 使用与文字同色的
    /// 细线图标，XP 使用高饱和填色与清晰轮廓，避免额外维护多套位图资源，
    /// 同时保证浅色、深色和自定义主题都能获得正确对比度。
    /// </summary>
    internal static class ThemedMenuIconFactory
    {
        private const int IconSize = 16;

        public static Image Create(ThemedMenuIcon icon, ThemeDefinition theme)
        {
            var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppPArgb);
            bitmap.SetResolution(96f, 96f);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                if (theme.MenuStyle == ThemeMenuStyle.WindowsXp)
                    DrawWindowsXpIcon(graphics, icon);
                else
                    DrawFluentIcon(graphics, icon, theme.MenuTextColor);
            }
            return bitmap;
        }

        private static void DrawFluentIcon(
            Graphics graphics,
            ThemedMenuIcon icon,
            Color color)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.35f))
            using (var brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (icon)
                {
                    case ThemedMenuIcon.ManageIcons:
                        graphics.DrawRectangle(pen, 2.5f, 2.5f, 4, 4);
                        graphics.DrawRectangle(pen, 9.5f, 2.5f, 4, 4);
                        graphics.DrawRectangle(pen, 2.5f, 9.5f, 4, 4);
                        graphics.DrawLines(pen, new[]
                        {
                            new PointF(9, 11), new PointF(10.5f, 12.5f), new PointF(14, 8.5f)
                        });
                        break;
                    case ThemedMenuIcon.Delete:
                        graphics.DrawLine(pen, 4, 4, 12, 4);
                        graphics.DrawLine(pen, 6, 2.5f, 10, 2.5f);
                        graphics.DrawRectangle(pen, 5, 5, 6, 8);
                        graphics.DrawLine(pen, 7, 7, 7, 11);
                        graphics.DrawLine(pen, 9, 7, 9, 11);
                        break;
                    case ThemedMenuIcon.Lock:
                        graphics.DrawArc(pen, 4.5f, 1.5f, 7, 8, 180, -180);
                        graphics.DrawRectangle(pen, 3.5f, 6, 9, 7);
                        graphics.FillEllipse(brush, 7, 8.5f, 2, 2);
                        break;
                    case ThemedMenuIcon.Minify:
                        graphics.DrawRectangle(pen, 2.5f, 3, 11, 10);
                        graphics.DrawLine(pen, 5, 9.5f, 11, 9.5f);
                        break;
                    case ThemedMenuIcon.Rename:
                        graphics.DrawLine(pen, 3, 12.5f, 11.5f, 4);
                        graphics.DrawLine(pen, 4.5f, 14, 13, 5.5f);
                        graphics.DrawLine(pen, 11.5f, 4, 13, 5.5f);
                        graphics.DrawLine(pen, 3, 12.5f, 2.5f, 14.5f);
                        graphics.DrawLine(pen, 2.5f, 14.5f, 4.5f, 14);
                        break;
                    case ThemedMenuIcon.TitleHeight:
                        graphics.DrawLine(pen, 3, 3, 13, 3);
                        graphics.DrawLine(pen, 3, 13, 13, 13);
                        graphics.DrawLine(pen, 8, 5, 8, 11);
                        graphics.DrawLines(pen, new[]
                        {
                            new Point(6, 7), new Point(8, 5), new Point(10, 7)
                        });
                        graphics.DrawLines(pen, new[]
                        {
                            new Point(6, 9), new Point(8, 11), new Point(10, 9)
                        });
                        break;
                    case ThemedMenuIcon.Theme:
                        graphics.DrawEllipse(pen, 2, 2, 12, 12);
                        graphics.FillEllipse(brush, 5, 4, 1.8f, 1.8f);
                        graphics.FillEllipse(brush, 8, 3.5f, 1.8f, 1.8f);
                        graphics.FillEllipse(brush, 10.5f, 5.5f, 1.8f, 1.8f);
                        graphics.DrawArc(pen, 5, 7, 7, 5, 10, 150);
                        break;
                    case ThemedMenuIcon.DarkMode:
                        using (var moon = CreateCrescentPath())
                            graphics.FillPath(brush, moon);
                        break;
                    case ThemedMenuIcon.NewFence:
                        graphics.DrawRectangle(pen, 2.5f, 3, 10, 10);
                        graphics.DrawLine(pen, 10.5f, 1.5f, 10.5f, 7.5f);
                        graphics.DrawLine(pen, 7.5f, 4.5f, 13.5f, 4.5f);
                        break;
                    case ThemedMenuIcon.CloseFence:
                        graphics.DrawRectangle(pen, 2.5f, 3, 11, 10);
                        graphics.DrawLine(pen, 5.5f, 6, 10.5f, 11);
                        graphics.DrawLine(pen, 10.5f, 6, 5.5f, 11);
                        break;
                    case ThemedMenuIcon.QuitApplication:
                        graphics.DrawArc(pen, 3, 3, 10, 10, -48, 276);
                        graphics.DrawLine(pen, 8, 1.5f, 8, 8);
                        break;
                }
            }
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
