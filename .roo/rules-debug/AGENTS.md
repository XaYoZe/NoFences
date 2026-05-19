# 调试模式规则（仅非显而易见内容）

- 默认**没有任何调试输出**。[`FenceWindow.cs`](NoFences/FenceWindow.cs:67) 中 `DevicePixelsToLogical` 的 `Debug.WriteLine` 会在 VS 输出窗口输出 DPI 值。更多消息需手动取消注释或添加。
- 栅栏元数据位于 `%LocalAppData%/NoFences/<guid>/__fence_metadata.xml`。要检查或手动修复损坏的状态，请查看此处 — 每个以 GUID 命名的子目录对应一个栅栏。
- [`Program.cs`](NoFences/Program.cs:21) 中的 `Mutex("No_fences")` 强制单实例运行。如果应用无法启动，请检查任务管理器中是否有僵尸 `NoFences.exe` 进程持有该 mutex。
- [`FenceWindow.WndProc`](NoFences/FenceWindow.cs:302) 中的 `WM_NCHITTEST` (0x84) 处理是无边框窗口拖拽/调整大小的核心。如果拖拽或调整大小失效，请检查命中测试结果链。
- [`WM_SETFOCUS`](NoFences/FenceWindow.cs:369) 处理程序将窗口推至 `HWND_BOTTOM` — 这意味着**任何 WinForms 控件都不会获得键盘焦点**。不要指望 `TextBox.Focused` 或 `GotFocus` 事件能正常工作。
- [`ThrottledExecution`](NoFences/Util/ThrottledExecution.cs) 的 `isAwaiting` 字段是 `volatile` — 保存是异步的，可能重叠。如果位置/大小保存丢失，请检查节流循环中的竞态条件。
- [`FenceEntry.Open()`](NoFences/Model/FenceEntry.cs:49) 静默吞下异常，仅通过 `Console.WriteLine` 输出。文件启动失败不会报错 — 请检查控制台/输出窗口中是否有 `"Failed to start:"` 消息。
- [`ThumbnailProvider`](NoFences/Util/ThumbnailProvider.cs:31) 的信号量限制缩略图生成，最多允许 4 个并发解码。如果在接近 4 个并发时出现 OOM，可能表示信号量没有被释放。
- **`Font.FromHfont()` 会崩溃**：桌面图标字体（`MS Shell Dlg`）不是 TrueType 字体，`Font.FromHfont()` 会抛出"只支持 TrueType 字体"。必须从 `LOGFONT` 中提取属性，用 `new Font()` 构造。
- **`SPI_GETICONMETRICS` 返回的是系统默认值，不是当前视图状态**：Ctrl+滚轮仅改变桌面 `SysListView32` 视图状态，不更新系统度量值。获取实际图标间距需用 `LVM_GETITEMSPACING` (0x1033)。
- **`CreateGraphics().DpiX` 会被 WinForms 缓存**：系统 DPI 变更后该值不更新。始终使用原生 `GetDpiForWindow(Handle)` 获取实时 DPI。
- 桌面窗口查找回退链：`Progman` → `SHELLDLL_DefView` → `SysListView32`，如果找不到则尝试 `WorkerW` 窗口。

## GDI+ 渲染调试陷阱
- **`MeasureString` 高度截断**：如果选中条目文字没有展开（多行文本被截为 2 行），检查是否在预测量时错误使用了 `SizeF(width, textHeight)` 而非 `SizeF(width, 9999)`。`MeasureString` 的返回值会被布局矩形高度限制。
- **图标选中后变模糊**：说明展开高亮框的半透明填充覆盖了图标。检查渲染顺序：必须为 `高亮框 → 重绘图标 → 文字`，图标不可在填充之前绘制。
- **悬浮边框大小不匹配选中框**：展开条目的 `outlineRect` 在 `RenderEntry` 内基于 2 行高度计算，而选中高亮框在循环后使用展开高度。悬浮检测和绘制必须在循环后的展开区块中使用 `expandOutlineRect`。
- **图标上边距消失**：`itemPadding` 比例应为 `itemWidth * 0.20`（非 `0.13`），否则对于 75px 的项宽度内边距仅 9px。
