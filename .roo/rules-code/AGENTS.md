# 代码模式规则（仅非显而易见内容）

- `using static NoFences.Win32.WindowUtil;` 是 WM 常量的导入风格（例如 `WM_NCHITTEST`、`HTCLIENT`、`HTCAPTION`）。参见 [`FenceWindow.cs`](NoFences/FenceWindow.cs:9)。
- [`ShellContextMenu`](NoFences/Win32/ShellContextMenu.cs:9) 位于 `Peter` 命名空间，通过 `using Peter;` 导入。不要将其移至 `NoFences.Win32`。
- [`FenceInfo`](NoFences/Model/FenceInfo.cs:8) 的属性绝不能重命名 — 它们使用 `XmlSerializer` 序列化，没有 `[XmlElement]` 属性。任何重命名都会破坏 `%LocalAppData%/NoFences/` 中的现有用户数据。
- 所有 GDI+ 渲染在 `FenceWindow_Paint` 中完成。不要启用 `DoubleBuffered` — 手动渲染依赖当前绘制周期进行鼠标命中测试（参见 [`FenceWindow.cs`](NoFences/FenceWindow.cs:30-31) 中的 `shouldUpdateSelection` / `shouldRunDoubleClick` 标志）。
- 保存持久化必须使用 [`ThrottledExecution`](NoFences/Util/ThrottledExecution.cs) 处理移动/调整大小事件（4 秒延迟）— 每次 `WM_MOVE` 都写 XML 会导致磁盘频繁写入。
- [`FenceEntry.Open()`](NoFences/Model/FenceEntry.cs:49) 使用 fire-and-forget 的 `Task.Run()` — 不要 `await` 它；设计意图是立即返回，不阻塞 UI 线程。
- 使用 [`Extensions.Move`](NoFences/Util/Extensions.cs:8) 和 [`Extensions.Shrink`](NoFences/Util/Extensions.cs:14) 处理 GDI+ 的 PointF/Rectangle 偏移计算。
- [`IconUtil.FolderLarge`](NoFences/Win32/IconUtil.cs:13) 是唯一的缓存文件夹图标 — 通过 null 合并运算符延迟初始化。
- 右键菜单逻辑：右键单击调用 [`shellContextMenu.ShowContextMenu`](NoFences/Win32/ShellContextMenu.cs:443)，参数为 `FileInfo[]`；Shift+右键则绕过该项，弹出应用自身的上下文菜单。
