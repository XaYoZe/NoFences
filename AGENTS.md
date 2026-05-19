# AGENTS.md

本文件为在此仓库中工作的 AI 助手提供指导。

## 构建与运行
- 仅支持 VS 解决方案；不支持 `dotnet` CLI。构建命令：`msbuild NoFences.sln /p:Configuration=Release`
- 仅目标 x64 平台（硬编码于 [`NoFences.csproj`](NoFences/NoFences.csproj:18)）
- **无测试用例。** 没有测试项目，未配置任何测试框架。

## 关键陷阱
- [`FenceInfo`](NoFences/Model/FenceInfo.cs:8) 的属性**绝对不可重命名** — 因为它们通过 `XmlSerializer` 直接序列化到磁盘，无自定义属性（`[XmlElement]` 等）。重命名会破坏已有用户的栅栏元数据文件。
- [`ShellContextMenu`](NoFences/Win32/ShellContextMenu.cs:9) 位于 `Peter` 命名空间（而非 `NoFences.Win32`），它是从第三方 CodeProject 库借用的代码。

## 架构
- 通过命名 `Mutex("No_fences")` 强制单实例，代码在 [`Program.cs`](NoFences/Program.cs:21)。
- 栅栏元数据持久化路径：`%LocalAppData%/NoFences/<guid>/__fence_metadata.xml`。
- 栅栏窗口是无边框的，粘附到桌面 Progman 窗口，通过 `WS_EX_TOOLWINDOW` 从 Alt+Tab 隐藏，并在 `WM_SETFOCUS` 时推至 `HWND_BOTTOM` 来阻止获取焦点。
- DPI 感知模式为 `PerMonitorV2`，配置在 [`App.config`](NoFences/App.config:7)；`FenceInfo.Width/Height` 保存的是 DPI 缩放后的值。

## 桌面图标度量系统
- 图标/文字大小通过 [`GetDesktopIconSpacing()`](NoFences/FenceWindow.cs:120) 动态读取桌面 `SysListView32` 的 `LVM_GETITEMSPACING` (0x1033) 消息 — 这获取的是当前实际视图状态，而非系统默认值。
- `SPI_GETICONMETRICS` 返回的是系统级默认值，**不反映** Ctrl+滚轮缩放后的实际图标间距。
- 1.5 秒定时器轮询作为 Ctrl+滚轮检测的补充（`WM_SETTINGCHANGE` 不会因 Ctrl+滚轮而触发）。
- [`DevicePixelsToLogical()`](NoFences/FenceWindow.cs:62) 必须用原生 `GetDpiForWindow(Handle)` 而非 `CreateGraphics().DpiX` — WinForms 会缓存 DPI 值，DPI 变更后不会更新。
- .NET Framework 4.8 有 `LogicalToDeviceUnits()` 但**没有** `DeviceToLogicalUnits()`（仅 .NET Core 3.0+ 有），故需自定义实现。

## 自定义模式
- [`FenceEntry.Open()`](NoFences/Model/FenceEntry.cs:49) 使用 fire-and-forget 的 `Task.Run(() => ...)`（绝不 await），以避免启动文件时 UI 线程冻结。
- [`ThrottledExecution`](NoFences/Util/ThrottledExecution.cs) 使用 `volatile` 的 `isAwaiting` 标志包装移动/调整大小时的保存操作，实现线程安全的延迟持久化。
- 所有渲染均为 `FenceWindow_Paint` 中的手动 GDI+ 绘制；未使用 WinForms 的 `DoubleBuffered`。
- 所有源代码文件均需使用中文 `<summary>` XML 文档注释。

## GDI+ 渲染陷阱与图标文字规则
- [`Graphics.MeasureString`](NoFences/FenceWindow.cs:681) 的返回值受传入布局矩形**高度**限制：用 `SizeF(width, textHeight)` 测量时返回高度 ≤ textHeight，多行文本被截断。获取真实文案高度必须用 `SizeF(width, 9999)`。
- 选中条目展开文字采用**两遍渲染**：第一遍 `RenderEntry(skipText: true)` 跳过文字，循环结束后第二遍依次绘制展开高亮框 → 重绘图标（防止半透明框覆盖图标导致模糊）→ 展开文字。层级顺序不可打乱。
- [`RenderEntry`](NoFences/FenceWindow.cs:674) 内的 `outlineRect` 基于 2 行 `textHeight`；展开条目必须用循环后 [`FenceWindow_Paint`](NoFences/FenceWindow.cs:608) 中基于 `expandOutlineRect` 的展开尺寸进行悬浮检测和边框绘制，否则鼠标悬浮边框大小与选中高亮框不一致。
- 内边距比例保持 20%（`itemPadding = itemWidth * 0.20`），与原始设计一致，避免图标上边距消失。

## 选中状态规则
- 选中仅通过 [`FenceWindow_Click`](NoFences/FenceWindow.cs:529) 的 `shouldUpdateSelection` 标志设置；[`FenceWindow_MouseLeave`](NoFences/FenceWindow.cs:496) **不清除**选中。取消选中仅通过点击空白区域或点击另一条目。
- 单行文字条目选中后**不触发**展开（`needsExpand = testSize.Height > textHeight`），仅在多行文字时进入展开渲染路径。
