using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace NoFences.Util
{
    /// <summary>
    /// 基于 UI 消息循环的可取消防抖执行器。高频事件只保留最后一个操作，
    /// 避免 async void 在窗口关闭后继续访问控件或把异常抛回 UI 线程。
    /// </summary>
    public sealed class ThrottledExecution : IDisposable
    {
        private readonly Timer timer;
        private Action pendingAction;
        private bool disposed;

        public ThrottledExecution(TimeSpan delay)
        {
            int interval = (int)Math.Max(1, Math.Min(int.MaxValue, delay.TotalMilliseconds));
            timer = new Timer { Interval = interval };
            timer.Tick += Timer_Tick;
        }

        /// <summary>替换当前待执行操作，并从本次调用重新开始计算延迟。</summary>
        public void Run(Action action)
        {
            if (disposed)
                return;
            pendingAction = action ?? throw new ArgumentNullException(nameof(action));
            timer.Stop();
            timer.Start();
        }

        /// <summary>立即执行最后一个待处理操作。</summary>
        public void Flush()
        {
            if (disposed)
                return;
            timer.Stop();
            ExecutePending();
        }

        /// <summary>丢弃尚未执行的操作。</summary>
        public void Cancel()
        {
            timer.Stop();
            pendingAction = null;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            timer.Stop();
            ExecutePending();
        }

        private void ExecutePending()
        {
            Action action = pendingAction;
            pendingAction = null;
            if (action == null)
                return;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Deferred fence persistence failed: " + ex);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            Cancel();
            disposed = true;
            timer.Dispose();
        }
    }
}
