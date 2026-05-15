# 架构规则（仅非显而易见内容）

- **仅单实例运行**：通过命名 `Mutex("No_fences")` 强制实现。不支持多窗口启动器。参见 [`Program.cs`](NoFences/Program.cs:21)。
- **栅栏窗口以桌面为父窗口**：[`DesktopUtil.GlueToDesktop`](NoFences/Win32/DesktopUtil.cs:38) 将 `GWL_HWNDPARENT` 设置为 `Progman` 窗口句柄。这意味着栅栏窗口显示在桌面图标下方、壁纸上方，并且能在 `Win+D`（显示桌面）后继续存在。
- **无焦点架构**：每次 [`WM_SETFOCUS`](NoFences/FenceWindow.cs:113) 都被拦截，窗口被推至 `HWND_BOTTOM`。任何 WinForms 控件都不能接收键盘输入。所有交互仅通过鼠标驱动。
- **手动 GDI+ 渲染管线**：[`FenceWindow_Paint`](NoFences/FenceWindow.cs:261-324) 处理所有视觉渲染（背景、标题、项目、选中高亮、滚动条）。绘制周期同时充当鼠标输入分发机制，通过标志字段（`shouldUpdateSelection`、`shouldRunDoubleClick`、`hasHoverUpdated`）实现。**不要引入**会导致重入绘制循环的 `Invalidate()` 调用。
- **节流持久化**：移动事件通过 [`ThrottledExecution`](NoFences/Util/ThrottledExecution.cs) 在 4 秒不活动后保存。调整大小事件使用独立的节流实例。`volatile bool isAwaiting` 防止堆叠多个延迟保存。
- **fire-and-forget 文件启动**：[`FenceEntry.Open()`](NoFences/Model/FenceEntry.cs:49) 发起 `Task.Run` 但故意不 await。UI 线程绝不能因 `Process.Start` 而阻塞。
- **缩略图管线**：[`ThumbnailProvider`](NoFences/Util/ThumbnailProvider.cs) 立即显示关联图标，然后异步替换为 32×32 缩略图。`IconThumbnailLoaded` 事件触发 `Invalidate()` 进行重绘。通过 `SemaphoreSlim` 限制并发解码最多 4 个。
- **零 NuGet 依赖**：无任何外部包。所有 Win32 互操作均为 `NoFences.Win32` 命名空间和第三方 `Peter` 命名空间中的手写 P/Invoke。
- **`FenceInfo` 即为数据库模式**：没有独立的数据库。[`FenceInfo`](NoFences/Model/FenceInfo.cs:8) 的 XML 序列化本身就是持久化层。添加属性是安全的（反序列化时会获得默认值），但重命名或删除属性会破坏现有用户数据。
- **`FenceManager` 是单例**：[`FenceManager.Instance`](NoFences/Model/FenceManager.cs:9) 是栅栏生命周期（创建、加载、更新、删除）的唯一入口。`FenceWindow` 中的所有保存操作都通过 `FenceManager.Instance.UpdateFence()` 路由。
