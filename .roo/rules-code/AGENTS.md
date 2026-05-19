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
- 所有代码必须包含中文 `<summary>` XML 文档注释。
- [`DevicePixelsToLogical()`](NoFences/FenceWindow.cs:62) 必须使用原生 `GetDpiForWindow(Handle)` — 禁止使用 `CreateGraphics().DpiX`（WinForms 会缓存该值，DPI 变更后不更新）。
- .NET Framework 4.8 仅有 `LogicalToDeviceUnits()`；`DeviceToLogicalUnits()` 不存在（仅 .NET Core 3.0+ 有），必须用自定义转换。
- [`thumbnailProvider.TargetSize`](NoFences/Util/ThumbnailProvider.cs:35) 支持运行时动态更新以响应 DPI/图标大小变化。

## GDI+ 渲染关键陷阱
- [`Graphics.MeasureString`](NoFences/FenceWindow.cs:681) 返回值受传入布局矩形**高度**限制：`SizeF(width, textHeight)` 测多行文本会截断。获取真实高度必须用 `SizeF(width, 9999)`。
- 选中条目展开文字采用**两遍渲染**：第一遍 `RenderEntry(skipText: true)` 跳过文字，循环结束后第二遍依次绘制展开高亮框 → 重绘图标（防止半透明框覆盖图标导致模糊）→ 展开文字。**不可打乱此顺序**。
- [`RenderEntry`](NoFences/FenceWindow.cs:674) 内的 `outlineRect` 基于 2 行 `textHeight`；展开条目的悬浮边框必须在循环后 [`FenceWindow_Paint`](NoFences/FenceWindow.cs:608) 中使用 `expandOutlineRect` 尺寸绘制，而非 `RenderEntry` 内部。
- `itemPadding` 必须为 `itemWidth * 0.20`（非 `0.13`），否则图标上边距消失。

## 选中状态渲染规则
- 选中仅通过 [`FenceWindow_Click`](NoFences/FenceWindow.cs:529) 的 `shouldUpdateSelection` 标志设置；[`FenceWindow_MouseLeave`](NoFences/FenceWindow.cs:496) **不清除**选中。
- 单行文字条目选中后**不触发**展开（`needsExpand = testSize.Height > textHeight`），仅在多行文字时进入展开渲染路径。单行条目使用 `RenderEntry` 内部的正常 2 行高亮框。
- 悬浮叠加使用极低透明度白色（`Color.White` 30 alpha）"提亮"，而非更多蓝色加深。
