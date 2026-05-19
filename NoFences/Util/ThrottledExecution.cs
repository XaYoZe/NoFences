using System;
using System.Threading.Tasks;

namespace NoFences.Util
{
    /// <summary>
    /// 节流执行工具。用于移动/调整大小等高频事件中延迟保存操作，
    /// 避免每次 WM_MOVE/WM_SIZE 都写磁盘造成性能问题。
    /// 
    /// 工作方式：如果距离上次执行超过 delay 间隔则立即执行；
    /// 否则等待剩余时间后执行一次（使用 volatile 标志保证线程安全）。
    /// </summary>
    public class ThrottledExecution
    {
        private TimeSpan delay;

        private DateTime lastExecution = DateTime.Now;

        private TimeSpan TimeSinceLastExecution => DateTime.Now - lastExecution;

        /// <summary>volatile 标志，防止重复排队等待任务</summary>
        private volatile bool isAwaiting;

        public ThrottledExecution(TimeSpan delay)
        {
            this.delay = delay;
        }

        /// <summary>
        /// 节流执行指定操作。高频调用时只会在满足间隔后执行最后一次。
        /// </summary>
        public async void Run(Action action)
        {
            if (TimeSinceLastExecution > delay)
                action.Invoke();   // 距上次执行已超过间隔，立即执行
            else if (!isAwaiting)  // 未在等待中，排队一次延迟执行
            {
                isAwaiting = true;
                while (TimeSinceLastExecution < delay)
                {
                    await Task.Delay((int)(delay.TotalMilliseconds - TimeSinceLastExecution.TotalMilliseconds));
                    action.Invoke();
                }
                isAwaiting = false;
            }
            lastExecution = DateTime.Now;
        }

    }
}
