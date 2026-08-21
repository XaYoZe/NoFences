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
    /// 异步生成图片缩略图或 Shell 图标。每个缓存项拥有自己的 Icon，窗口关闭时
    /// 可确定性释放；后台任务通过取消令牌和 UI 同步上下文避免与绘制线程竞争。
    /// </summary>
    public sealed class ThumbnailProvider : IDisposable
    {
        private const long MaximumImageFileBytes = 128L * 1024L * 1024L;
        private const long MaximumDecodedPixels = 100L * 1000L * 1000L;

        private static readonly string[] SupportedExtensions =
        {
            ".bmp", ".gif", ".jpg", ".jpeg", ".png", ".tiff", ".tif"
        };

        private sealed class ThumbnailState
        {
            public Icon Icon;
            public int TargetSize;
            public int LoadingSize;
            public int FailedSize;
        }

        private readonly object syncRoot = new object();
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(4, 4);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly IDictionary<string, ThumbnailState> iconCache =
            new Dictionary<string, ThumbnailState>(StringComparer.OrdinalIgnoreCase);
        private readonly SynchronizationContext uiContext;
        private int targetSize;
        private bool disposed;

        public event EventHandler IconThumbnailLoaded;

        public ThumbnailProvider(int targetSize = 32)
        {
            this.targetSize = Math.Max(16, targetSize);
            uiContext = SynchronizationContext.Current;
        }

        /// <summary>缩略图目标尺寸（设备像素）。</summary>
        public int TargetSize
        {
            get
            {
                lock (syncRoot)
                    return targetSize;
            }
            set
            {
                lock (syncRoot)
                    targetSize = Math.Max(16, value);
            }
        }

        /// <summary>判断文件是否为支持直接解码的图片格式。</summary>
        public bool IsSupported(string path)
        {
            return SupportedExtensions.Any(ext =>
                path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>立即返回缓存或占位图标，并在需要时排队生成目标尺寸图标。</summary>
        public Icon GenerateIcon(string path)
        {
            lock (syncRoot)
            {
                if (disposed)
                    return null;

                ThumbnailState state;
                if (!iconCache.TryGetValue(path, out state))
                {
                    state = new ThumbnailState
                    {
                        Icon = IconUtil.GetFallbackIcon(path)
                    };
                    iconCache[path] = state;
                }

                int desiredSize = targetSize;
                if (state.TargetSize != desiredSize &&
                    state.LoadingSize != desiredSize &&
                    state.FailedSize != desiredSize)
                {
                    state.LoadingSize = desiredSize;
                    QueueGeneration(path, state, desiredSize, IsSupported(path));
                }
                return state.Icon;
            }
        }

        /// <summary>移除一个已不再展示的缓存项，并释放其独占图标资源。</summary>
        public void Remove(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            Icon icon = null;
            lock (syncRoot)
            {
                ThumbnailState state;
                if (iconCache.TryGetValue(path, out state))
                {
                    iconCache.Remove(path);
                    icon = state.Icon;
                }
            }
            icon?.Dispose();
        }

        private void QueueGeneration(
            string path,
            ThumbnailState state,
            int generationSize,
            bool imageThumbnail)
        {
            CancellationToken token = cancellation.Token;
            Task.Run(async () =>
            {
                Icon generated = null;
                bool entered = false;
                Exception failure = null;
                try
                {
                    await semaphore.WaitAsync(token).ConfigureAwait(false);
                    entered = true;
                    token.ThrowIfCancellationRequested();
                    generated = imageThumbnail
                        ? GenerateImageThumbnail(path, generationSize)
                        : IconUtil.GetLargeIcon(path, generationSize);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    if (entered)
                        semaphore.Release();
                }

                PostCompletion(path, state, generationSize, generated, failure);
            }, token);
        }

        private static Icon GenerateImageThumbnail(string path, int generationSize)
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > MaximumImageFileBytes)
                throw new InvalidDataException("图片文件过大，已跳过缩略图生成。");

            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var image = Image.FromStream(stream, false, true))
            {
                long decodedPixels = (long)image.Width * image.Height;
                if (decodedPixels > MaximumDecodedPixels)
                    throw new InvalidDataException("图片像素尺寸过大，已跳过缩略图生成。");

                using (var thumbnail = new Bitmap(
                    generationSize,
                    generationSize,
                    PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    float scale = Math.Min(
                        generationSize / (float)image.Width,
                        generationSize / (float)image.Height);
                    int width = Math.Max(1, (int)Math.Round(image.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(image.Height * scale));
                    var destination = new Rectangle(
                        (generationSize - width) / 2,
                        (generationSize - height) / 2,
                        width,
                        height);
                    graphics.DrawImage(
                        image,
                        destination,
                        0,
                        0,
                        image.Width,
                        image.Height,
                        GraphicsUnit.Pixel);

                    IntPtr iconHandle = thumbnail.GetHicon();
                    try
                    {
                        return (Icon)Icon.FromHandle(iconHandle).Clone();
                    }
                    finally
                    {
                        IconUtil.DestroyIcon(iconHandle);
                    }
                }
            }
        }

        private void PostCompletion(
            string path,
            ThumbnailState state,
            int generationSize,
            Icon generated,
            Exception failure)
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    generated?.Dispose();
                    return;
                }
            }

            SendOrPostCallback completion = _ => CompleteGeneration(
                path,
                state,
                generationSize,
                generated,
                failure);
            if (uiContext == null)
            {
                completion(null);
                return;
            }

            try
            {
                uiContext.Post(completion, null);
            }
            catch (Exception ex)
            {
                generated?.Dispose();
                System.Diagnostics.Trace.WriteLine(
                    "Unable to publish thumbnail completion: " + ex);
            }
        }

        private void CompleteGeneration(
            string path,
            ThumbnailState state,
            int generationSize,
            Icon generated,
            Exception failure)
        {
            Icon previous = null;
            bool notify = false;
            lock (syncRoot)
            {
                ThumbnailState current;
                bool isCurrent = !disposed &&
                                 iconCache.TryGetValue(path, out current) &&
                                 ReferenceEquals(current, state) &&
                                 state.LoadingSize == generationSize;
                if (isCurrent)
                {
                    state.LoadingSize = 0;
                    if (failure == null && generated != null && targetSize == generationSize)
                    {
                        previous = state.Icon;
                        state.Icon = generated;
                        state.TargetSize = generationSize;
                        state.FailedSize = 0;
                        generated = null;
                        notify = true;
                    }
                    else if (failure != null)
                    {
                        state.FailedSize = generationSize;
                    }
                }
            }

            previous?.Dispose();
            generated?.Dispose();
            if (failure != null)
                System.Diagnostics.Trace.WriteLine("Thumbnail generation failed: " + failure);
            if (notify)
            {
                try
                {
                    IconThumbnailLoaded?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        "Thumbnail completion handler failed: " + ex);
                }
            }
        }

        public void Dispose()
        {
            List<Icon> icons;
            lock (syncRoot)
            {
                if (disposed)
                    return;
                disposed = true;
                cancellation.Cancel();
                icons = iconCache.Values
                    .Select(state => state.Icon)
                    .Where(icon => icon != null)
                    .ToList();
                iconCache.Clear();
                IconThumbnailLoaded = null;
            }

            foreach (Icon icon in icons)
                icon.Dispose();
        }
    }
}
