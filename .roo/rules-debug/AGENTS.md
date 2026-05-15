# 调试模式规则（仅非显而易见内容）

- 默认**没有任何调试输出**。[`FenceWindow.cs`](NoFences/FenceWindow.cs:89) 中 `WndProc` 的 [`Console.WriteLine`] 已被注释。取消注释可追踪窗口消息。
- 栅栏元数据位于 `%LocalAppData%/NoFences/<guid>/__fence_metadata.xml`。要检查或手动修复损坏的状态，请查看此处 — 每个以 GUID 命名的子目录对应一个栅栏。
- [`Program.cs`](NoFences/Program.cs:21) 中的 `Mutex("No_fences")` 强制单实例运行。如果应用无法启动，请检查任务管理器中是否有僵尸 `NoFences.exe` 进程持有该 mutex。
- [`FenceWindow.WndProc`](NoFences/FenceWindow.cs:127-152) 中的 `WM_NCHITTEST` (0x84) 处理是无边框窗口拖拽/调整大小的核心。如果拖拽或调整大小失效，请检查命中测试结果链。
- [`WM_SETFOCUS`](NoFences/FenceWindow.cs:113) 处理程序将窗口推至 `HWND_BOTTOM` — 这意味着**任何 WinForms 控件都不会获得键盘焦点**。不要指望 `TextBox.Focused` 或 `GotFocus` 事件能正常工作。
- [`ThrottledExecution`](NoFences/Util/ThrottledExecution.cs) 的 `isAwaiting` 字段是 `volatile` — 保存是异步的，可能重叠。如果位置/大小保存丢失，请检查节流循环中的竞态条件。
- [`FenceEntry.Open()`](NoFences/Model/FenceEntry.cs:49) 静默吞下异常，仅通过 `Console.WriteLine` 输出。文件启动失败不会报错 — 请检查控制台/输出窗口中是否有 `"Failed to start:"` 消息。
- [`ThumbnailProvider`](NoFences/Util/ThumbnailProvider.cs:31) 的信号量限制缩略图生成，最多允许 4 个并发解码。如果在接近 4 个并发时出现 OOM，可能表示信号量没有被释放。
