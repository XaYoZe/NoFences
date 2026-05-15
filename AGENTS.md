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

## 自定义模式
- [`FenceEntry.Open()`](NoFences/Model/FenceEntry.cs:49) 使用 fire-and-forget 的 `Task.Run(() => ...)`（绝不 await），以避免启动文件时 UI 线程冻结。
- [`ThrottledExecution`](NoFences/Util/ThrottledExecution.cs) 使用 `volatile` 的 `isAwaiting` 标志包装移动/调整大小时的保存操作，实现线程安全的延迟持久化。
- 零 NuGet 包 — 除 .NET Framework 4.8 外无任何外部依赖。
- 所有渲染均为 `FenceWindow_Paint` 中的手动 GDI+ 绘制；未使用 WinForms 的 `DoubleBuffered`。
