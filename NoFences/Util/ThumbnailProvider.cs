using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoFences.Win32;

namespace NoFences.Util
{
    /// <summary>
    /// 图标与缩略图生成器。异步提取 Shell 高分辨率图标或生成图片缩略图，
    /// 带尺寸缓存和并发控制。
    /// 使用 SemaphoreSlim 限制最多 4 个并发解码任务，防止 OOM。
    /// </summary>
    public class ThumbnailProvider
    {
        /// <summary>.NET 原生支持的图片文件扩展名</summary>
        private static readonly string[] SupportedExtensions =
        {
            ".bmp",
            ".gif",
            ".jpg",
            ".jpeg",
            ".png",
            ".tiff",
            ".tif"
        };

        private int targetSize;

        /// <summary>
        /// 缩略图目标尺寸（设备像素）。
        /// 支持运行时动态调整以匹配桌面图标大小变化。
        /// </summary>
        public int TargetSize
        {
            get => targetSize;
            set
            {
                int normalizedSize = Math.Max(16, value);
                if (targetSize == normalizedSize)
                    return;

                targetSize = normalizedSize;
                // 保留上一档图标作为过渡；下次绘制会异步生成新尺寸
            }
        }

        /// <summary>缩略图缓存项。</summary>
        private class ThumbnailState
        {
            public Icon icon;
            public int targetSize;
        }

        /// <summary>最多允许 4 个并发图片解码，防止 OOM</summary>
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(4);
        private readonly IDictionary<string, ThumbnailState> iconCache = new Dictionary<string, ThumbnailState>();

        /// <summary>当异步缩略图加载完成时触发，通知 UI 刷新。</summary>
        public event EventHandler IconThumbnailLoaded;

        public ThumbnailProvider(int targetSize = 32)
        {
            this.targetSize = Math.Max(16, targetSize);
        }

        /// <summary>判断文件是否为支持的图片格式。</summary>
        public bool IsSupported(string path)
        {
            return SupportedExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 获取或生成指定路径的图标。
        /// 图片文件生成高质量缩略图，其他条目异步提取目标尺寸的 Shell 图标。
        /// </summary>
        public Icon GenerateIcon(string path)
        {
            ThumbnailState state;
            if (iconCache.TryGetValue(path, out state))
            {
                if (state.targetSize != targetSize)
                {
                    if (IsSupported(path))
                        SubmitGeneratorTask(path, state);
                    else
                        SubmitShellIconTask(path, state);
                }
                return state.icon;
            }

            return IsSupported(path)
                ? SubmitGeneratorTask(path).icon
                : SubmitShellIconTask(path).icon;
        }

        /// <summary>
        /// 提交异步缩略图生成任务。
        /// 先以 Shell 图标作为占位，然后在后台线程解码并缩放图片，
        /// 完成后更新缓存并触发 IconThumbnailLoaded 事件通知 UI 刷新。
        /// </summary>
        private ThumbnailState SubmitGeneratorTask(string path, ThumbnailState state = null)
        {
            if (state == null)
            {
                state = new ThumbnailState() { icon = IconUtil.GetFallbackIcon(path) };
                iconCache[path] = state;
            }
            int generationSize = targetSize;
            state.targetSize = generationSize;

            Task.Run(() =>
            {
                semaphore.Wait(); // 限制并发数
                try
                {
                    using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))
                    using (var img = Image.FromStream(ms))
                    using (var thumb = new Bitmap(generationSize, generationSize, PixelFormat.Format32bppArgb))
                    {
                        using (var graphics = Graphics.FromImage(thumb))
                        {
                            graphics.Clear(Color.Transparent);
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.CompositingQuality = CompositingQuality.HighQuality;
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                            float scale = Math.Min(
                                generationSize / (float)img.Width,
                                generationSize / (float)img.Height);
                            int width = Math.Max(1, (int)Math.Round(img.Width * scale));
                            int height = Math.Max(1, (int)Math.Round(img.Height * scale));
                            var destination = new Rectangle(
                                (generationSize - width) / 2,
                                (generationSize - height) / 2,
                                width,
                                height);
                            graphics.DrawImage(img, destination, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
                        }

                        IntPtr iconHandle = thumb.GetHicon();
                        try
                        {
                            var icon = (Icon)Icon.FromHandle(iconHandle).Clone();
                            if (state.targetSize == generationSize)
                            {
                                state.icon = icon;
                                IconThumbnailLoaded?.Invoke(this, EventArgs.Empty);
                            }
                            else
                            {
                                icon.Dispose();
                                return state.icon;
                            }
                            return icon;
                        }
                        finally
                        {
                            IconUtil.DestroyIcon(iconHandle);
                        }
                    }
                }
                catch
                {
                    return state.icon;
                }
                finally
                {
                    semaphore.Release();
                }
            });
            return state; // 立即返回占位图标，不阻塞调用方
        }

        /// <summary>
        /// 在后台按当前目标尺寸提取 Shell 高分辨率图标，
        /// UI 线程先使用轻量级 SHGetFileInfo 图标作为占位。
        /// </summary>
        private ThumbnailState SubmitShellIconTask(string path, ThumbnailState state = null)
        {
            if (state == null)
            {
                state = new ThumbnailState() { icon = IconUtil.GetFallbackIcon(path) };
                iconCache[path] = state;
            }
            int generationSize = targetSize;
            state.targetSize = generationSize;

            Task.Run(() =>
            {
                semaphore.Wait();
                try
                {
                    var icon = IconUtil.GetLargeIcon(path, generationSize);
                    if (state.targetSize == generationSize)
                    {
                        state.icon = icon;
                        IconThumbnailLoaded?.Invoke(this, EventArgs.Empty);
                    }
                    return icon;
                }
                catch
                {
                    return state.icon;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            return state;
        }

    }
}
