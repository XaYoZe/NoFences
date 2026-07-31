using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace NoFences.Theming
{
    /// <summary>
    /// Shared GDI+ helpers used by the fence, menu renderer, and live preview.
    /// Keeping geometry and image fitting here prevents each themed surface from
    /// developing subtly different corner or background-image behavior.
    /// </summary>
    public static class ThemeDrawing
    {
        public static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            radius = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f));
            if (radius <= 0.5f)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            float diameter = radius * 2f;
            var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color WithOpacity(Color color, int opacityPercent)
        {
            int alpha = Math.Max(0, Math.Min(255, opacityPercent * 255 / 100));
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        public static Color WithAlpha(Color color, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color.R, color.G, color.B);
        }

        public static Color Mix(Color first, Color second, float secondWeight)
        {
            secondWeight = Math.Max(0f, Math.Min(1f, secondWeight));
            float firstWeight = 1f - secondWeight;
            return Color.FromArgb(
                (int)(first.A * firstWeight + second.A * secondWeight),
                (int)(first.R * firstWeight + second.R * secondWeight),
                (int)(first.G * firstWeight + second.G * secondWeight),
                (int)(first.B * firstWeight + second.B * secondWeight));
        }

        public static bool IsDark(Color color)
        {
            // Perceived luminance gives better text contrast than averaging RGB.
            double luminance = color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;
            return luminance < 145;
        }

        /// <summary>
        /// Loads a detached bitmap without keeping the selected file locked. This
        /// lets the user replace or delete a configured image while NoFences runs.
        /// Invalid, missing, and unsupported images simply produce null.
        /// </summary>
        public static Image LoadImageWithoutLock(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var source = Image.FromStream(stream))
                    return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        public static void DrawBackgroundImage(
            Graphics graphics,
            Image image,
            Rectangle bounds,
            ThemeImageLayout layout,
            int opacityPercent)
        {
            if (graphics == null || image == null || bounds.Width <= 0 || bounds.Height <= 0 || opacityPercent <= 0)
                return;

            var state = graphics.Save();
            graphics.SetClip(bounds, CombineMode.Intersect);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            using (var attributes = CreateOpacityAttributes(opacityPercent))
            {
                if (layout == ThemeImageLayout.Tile)
                {
                    for (int y = bounds.Top; y < bounds.Bottom; y += image.Height)
                    {
                        for (int x = bounds.Left; x < bounds.Right; x += image.Width)
                        {
                            DrawImage(graphics, image, new Rectangle(x, y, image.Width, image.Height), attributes);
                        }
                    }
                }
                else
                {
                    DrawImage(graphics, image, CalculateImageBounds(image.Size, bounds, layout), attributes);
                }
            }

            graphics.Restore(state);
        }

        private static ImageAttributes CreateOpacityAttributes(int opacityPercent)
        {
            float opacity = Math.Max(0, Math.Min(100, opacityPercent)) / 100f;
            var matrix = new ColorMatrix { Matrix33 = opacity };
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            return attributes;
        }

        private static void DrawImage(Graphics graphics, Image image, Rectangle destination, ImageAttributes attributes)
        {
            graphics.DrawImage(
                image,
                destination,
                0,
                0,
                image.Width,
                image.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        private static Rectangle CalculateImageBounds(Size imageSize, Rectangle bounds, ThemeImageLayout layout)
        {
            if (layout == ThemeImageLayout.Stretch)
                return bounds;

            if (layout == ThemeImageLayout.Center)
            {
                return new Rectangle(
                    bounds.Left + (bounds.Width - imageSize.Width) / 2,
                    bounds.Top + (bounds.Height - imageSize.Height) / 2,
                    imageSize.Width,
                    imageSize.Height);
            }

            double scaleX = bounds.Width / (double)imageSize.Width;
            double scaleY = bounds.Height / (double)imageSize.Height;
            double scale = layout == ThemeImageLayout.Fit
                ? Math.Min(scaleX, scaleY)
                : Math.Max(scaleX, scaleY);
            int width = Math.Max(1, (int)Math.Round(imageSize.Width * scale));
            int height = Math.Max(1, (int)Math.Round(imageSize.Height * scale));
            return new Rectangle(
                bounds.Left + (bounds.Width - width) / 2,
                bounds.Top + (bounds.Height - height) / 2,
                width,
                height);
        }
    }
}
