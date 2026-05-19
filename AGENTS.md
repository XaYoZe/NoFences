# AGENTS.md

本文件为在此仓库中工作的 AI 助手提供指导。

## 构建与运行
- 仅支持 VS 解决方案；不支持 `dotnet` CLI。构建命令：`msbuild NoFences.sln /p:Configuration=Release`
- 仅目标 x64 平台（硬编码于 [`NoFences.csproj`](NoFences/NoFences.csproj:18)）
- **无测试用例。** 没有测试项目，未配置任何测试框架。

## 关键陷阱
- [`FenceInfo`](NoFences/Model/FenceInfo.cs:13) 的属性**绝对不可重命名** — 它们通过 `XmlSerializer` 直接序列化到磁盘，无 `[XmlElement]` 自定义映射。重命名会破坏已有用户的栅栏元数据文件。**添加新属性是安全的**（反序列化时获得默认值），但删除或重命名会崩溃。
- [`ShellContextMenu`](NoFences/Win32/ShellContextMenu.cs:9) 位于 `Peter` 命名空间（而非 `NoFences.Win32`），是第三方 CodeProject 库代码。通过 `using Peter;` 导入，切勿将其移到其他命名空间。
- `Font.FromHfont()` 对非 TrueType 字体（如桌面图标字体 MS Shell Dlg）会抛异常。必须从 `LOGFONT` 手动提取各字段，用 `new Font(...)` 构造。（参见 [`CreateIconFontFromLogFont`](NoFences/FenceWindow.cs:202)）

## 架构
- 通过命名 `Mutex("No_fences")` 强制单实例，代码在 [`Program.cs`](NoFences/Program.cs:25)。
- 栅栏元数据持久化路径：`%LocalAppData%/NoFences/<guid>/__fence_metadata.xml`。
- 栅栏窗口是无边框的，通过 [`DesktopUtil.GlueToDesktop`](NoFences/Win32/DesktopUtil.cs:49) 将 `GWL_HWNDPARENT` 设为 `Progman` 窗口句柄，使其显示在桌面图标下方、能在 Win+D 后继续存在。同时通过 `WS_EX_TOOLWINDOW` 从 Alt+Tab 隐藏，并在 `WM_SETFOCUS` 时推至 `HWND_BOTTOM` 阻止获取焦点。
- DPI 感知模式为 `PerMonitorV2`，配置在 [`App.config`](NoFences/App.config:7)；`FenceInfo.Width/Height` 保存的是 DPI 缩放后的值，`TitleHeight` 为逻辑像素值。
- 无边框窗口的拖拽/调整大小通过 [`WndProc`](NoFences/FenceWindow.cs:302) 中的 `WM_NCHITTEST` (0x84) 实现：标题栏区域返回 `HTCAPTION`，四角和边缘 10px 热区返回对应 resize 方向。**锁定状态或鼠标右键按下时**不处理拖拽/调整大小。
- [`ThumbnailProvider`](NoFences/Util/ThumbnailProvider.cs) 为图片文件异步生成缩略图：先立即显示关联图标，然后异步替换为缩略图（`IconThumbnailLoaded` 事件触发 `Invalidate()`）。通过 `SemaphoreSlim(4)` 限制并发解码，`TargetSize` 支持运行时动态更新以匹配 DPI 变化。
- 桌面窗口查找回退链：`Progman` → `SHELLDLL_DefView` → `SysListView32`，找不到则尝试 `WorkerW` 窗口。

## 桌面图标度量系统
- 图标/文字大小通过 [`GetDesktopIconSpacing()`](NoFences/FenceWindow.cs:127) 动态读取桌面 `SysListView32` 的 `LVM_GETITEMSPACING` (0x1033) 消息 — 这获取的是当前实际视图状态，而非系统默认值。
- `SPI_GETICONMETRICS` 返回的是系统级默认值，**不反映** Ctrl+滚轮缩放后的实际图标间距。
- 1.5 秒定时器轮询作为 Ctrl+滚轮检测的补充（`WM_SETTINGCHANGE` 不会因 Ctrl+滚轮而触发）。同时响应 `WM_DPICHANGED` 和 `WM_SETTINGCHANGE`。
- [`DevicePixelsToLogical()`](NoFences/FenceWindow.cs:62) 必须用原生 `GetDpiForWindow(Handle)` 而非 `CreateGraphics().DpiX` — WinForms 会缓存 DPI 值，DPI 变更后不会更新。
- .NET Framework 4.8 有 `LogicalToDeviceUnits()` 但**没有** `DeviceToLogicalUnits()`（仅 .NET Core 3.0+ 有），故需自定义实现。

## 自定义模式
- [`FenceEntry.Open()`](NoFences/Model/FenceEntry.cs:68) 使用 fire-and-forget 的 `Task.Run(() => ...)`（绝不 await），以避免启动文件时 UI 线程冻结。
- [`ThrottledExecution`](NoFences/Util/ThrottledExecution.cs) 使用 `volatile` 的 `isAwaiting` 标志包装移动/调整大小时的保存操作，实现线程安全的延迟持久化（4 秒间隔）。
- 所有渲染均为 `FenceWindow_Paint` 中的手动 GDI+ 绘制；未使用 WinForms 的 `DoubleBuffered`。
- 所有源代码文件均需使用中文 `<summary>` XML 文档注释。
- GDI+ 像素偏移使用 [`Extensions.Move`](NoFences/Util/Extensions.cs:11) 和 [`Extensions.Shrink`](NoFences/Util/Extensions.cs:17) 扩展方法。
- 右键上下文菜单：右键点击条目调用 `shellContextMenu.ShowContextMenu`（Windows Shell 右键菜单）；**Shift+右键** 则绕过，弹出应用自身的上下文菜单。
- `using static NoFences.Win32.WindowUtil;` 是 WM 常量（`WM_NCHITTEST`、`HTCLIENT`、`HTCAPTION` 等）的导入风格。

## GDI+ 渲染陷阱与图标文字规则
- `Graphics.MeasureString` 的返回值受传入布局矩形**高度**限制：用 `SizeF(width, textHeight)` 测量时返回高度 ≤ textHeight，多行文本被截断。获取真实文案高度必须用 `SizeF(width, 9999)`（参见 [`FenceWindow_Paint`](NoFences/FenceWindow.cs:582) 中的 `needsExpand` 判断和展开文字绘制）。
- 选中条目展开文字采用**两遍渲染**：第一遍 `RenderEntry(skipText: true)` 跳过文字，循环结束后第二遍依次绘制展开高亮框 → 重绘图标（防止半透明框覆盖图标导致模糊）→ 展开文字。层级顺序不可打乱。
- [`RenderEntry`](NoFences/FenceWindow.cs:695) 内的 `outlineRect` 基于 2 行 `textHeight`；展开条目必须用循环后 [`FenceWindow_Paint`](NoFences/FenceWindow.cs:627) 中基于 `expandOutlineRect` 的展开尺寸进行悬浮检测和边框绘制，否则鼠标悬浮边框大小与选中高亮框不一致。
- 内边距比例保持 20%（`itemPadding = itemWidth * 0.20`），与原始设计一致，避免图标上边距消失。

## 选中状态规则
- 选中仅通过 [`FenceWindow_Click`](NoFences/FenceWindow.cs:529) 的 `shouldUpdateSelection` 标志设置；[`FenceWindow_MouseLeave`](NoFences/FenceWindow.cs:496) **不清除**选中。取消选中仅通过点击空白区域或点击另一条目。
- 单行文字条目选中后**不触发**展开（`needsExpand = testSize.Height > textHeight`），仅在多行文字时进入展开渲染路径。
