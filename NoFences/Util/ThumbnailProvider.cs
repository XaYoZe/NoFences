using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoFences.Util
{
    /// <summary>
    /// 缩略图生成器。为图片文件异步生成缩略图，带缓存和并发控制。
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
        /// 缩略图目标尺寸（逻辑像素）。
        /// 支持运行时动态调整以匹配桌面图标大小变化。
        /// </summary>
        public int TargetSize
        {
            get => targetSize;
            set => targetSize = Math.Max(16, value);
        }

        /// <summary>缩略图缓存项。</summary>
        private class ThumbnailState
        {
            public Icon icon;
        }

        /// <summary>最多允许 4 个并发图片解码，防止 OOM</summary>
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(4);
        private readonly IDictionary<string, ThumbnailState> iconCache = new Dictionary<string, ThumbnailState>();

        /// <summary>当异步缩略图加载完成时触发，通知 UI 刷新。</summary>
        public event EventHandler IconThumbnailLoaded;

        public ThumbnailProvider(int targetSize = 32)
        {
            this.targetSize = targetSize;
        }

        /// <summary>判断文件是否为支持的图片格式。</summary>
        public bool IsSupported(string path)
        {
            return SupportedExtensions.Any(ext => path.EndsWith(ext));
        }

        /// <summary>
        /// 获取或生成指定路径的缩略图图标。
        /// 如果缓存命中则直接返回，否则提交异步生成任务并返回占位图标。
        /// </summary>
        public Icon GenerateThumbnail(string path)
        {
            if (!iconCache.ContainsKey(path))
            {
                return SubmitGeneratorTask(path).icon;
            }
            else
            {
                return iconCache[path].icon;
            }
        }

        /// <summary>
        /// 提交异步缩略图生成任务。
        /// 先以关联图标作为占位，然后在后台线程解码并缩放图片，
        /// 完成后更新缓存并触发 IconThumbnailLoaded 事件通知 UI 刷新。
        /// </summary>
        private ThumbnailState SubmitGeneratorTask(string path)
        {
            // 先用关联图标作为占位
            var state = new ThumbnailState() { icon = Icon.ExtractAssociatedIcon(path) };
            iconCache[path] = state;

            Task.Run(() =>
            {
                semaphore.Wait(); // 限制并发数
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))
                {
                    using (var img = Image.FromStream(ms))
                    {
                        // 生成指定尺寸的缩略图
                        var thumb = (Bitmap)img.GetThumbnailImage(targetSize, targetSize, () => false, IntPtr.Zero);
                        var icon = Icon.FromHandle(thumb.GetHicon());
                        state.icon = icon;
                        IconThumbnailLoaded(this, new EventArgs()); // 通知 UI 刷新
                        semaphore.Release();
                        return icon;
                    }
                }
            });
            return state; // 立即返回占位图标，不阻塞调用方
        }

    }
}
