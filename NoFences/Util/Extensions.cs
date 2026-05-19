using System.Drawing;

namespace NoFences.Util
{
    /// <summary>
    /// GDI+ 绘图相关的扩展方法，简化 PointF / Rectangle 的偏移计算。
    /// </summary>
    public static class Extensions
    {
        /// <summary>将点偏移指定量，返回新 PointF。</summary>
        public static PointF Move(this PointF point, float offsetX, float offsetY)
        {
            return new PointF(point.X + offsetX, point.Y + offsetY);
        }

        /// <summary>将矩形向内收缩指定量（四边同时内缩）。</summary>
        public static Rectangle Shrink(this Rectangle rect, int offset)
        {
            return new Rectangle(rect.X + offset, rect.Y + offset, rect.Width - offset * 2, rect.Height - offset * 2);
        }

    }
}
