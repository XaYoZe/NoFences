# 问答模式规则（仅非显而易见内容）

- "NoFences" 是 Stardock Fences（售价 11€）的免费克隆版。参见 [`README.md`](README.md:3)。
- 栅栏窗口**不是普通的 WinForms 窗口**：它们无边框、通过 [`DesktopUtil.GlueToDesktop`](NoFences/Win32/DesktopUtil.cs:38) 将父窗口设为 `Progman` 桌面窗口、通过 `WS_EX_TOOLWINDOW` 从 Alt+Tab 隐藏，且无法获取焦点（被推至 `HWND_BOTTOM`）。
- 栅栏持久化方式：每个栅栏在 `%LocalAppData%/NoFences/` 下对应一个 `<guid>` 子目录，其中包含一个 `__fence_metadata.xml` 文件（序列化的 [`FenceInfo`](NoFences/Model/FenceInfo.cs:8)）。
- `Peter` 命名空间（包含 [`ShellContextMenu`](NoFences/Win32/ShellContextMenu.cs:9)）是来自 CodeProject 的第三方代码 — 原作者：Andreas Johansson。它是独立的 Windows Shell 上下文菜单实现。
- DPI 缩放：[`App.config`](NoFences/App.config:7) 设置为 `PerMonitorV2`。`FenceInfo` 中的 `Width`/`Height` 是原样存储的 DPI 缩放值；`TitleHeight` 以逻辑像素存储。
- 图标和文字大小通过读取桌面 `SysListView32` 的 `LVM_GETITEMSPACING` (0x1033) 动态匹配用户桌面设置，包括 Ctrl+滚轮实时缩放。系统通过 1.5 秒定时器轮询检测 Ctrl+滚轮变化，并响应 `WM_DPICHANGED` 和 `WM_SETTINGCHANGE`。
- "Minify"（最小化至标题栏）功能：当 `CanMinify` 被勾选时，鼠标离开后栅栏折叠到 `titleHeight`，鼠标进入时恢复。通过上下文菜单复选框 `minifyToolStripMenuItem` 切换。
- 栅栏窗口中的自定义滚动条完全由 GDI+ 绘制（参见 [`FenceWindow.cs`](NoFences/FenceWindow.cs:38-39) 中的 `scrollOffset`/`scrollHeight` 字段），而非 WinForms 的 `ScrollBar` 控件。
- 图标文字渲染完全模拟 Windows 桌面规则：2 行换行 + 省略号截断，选中条目动态展开显示全部文字。所有文字使用白色前景 + 深色阴影。`Graphics.MeasureString` 的返回值会被布局矩形高度限制。
