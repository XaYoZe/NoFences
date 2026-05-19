using NoFences.Model;
using NoFences.Util;
using NoFences.Win32;
using Peter;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static NoFences.Win32.WindowUtil;

namespace NoFences
{
    public partial class FenceWindow : Form
    {
        private int logicalTitleHeight;
        private int titleHeight;
        private const int titleOffset = 3;
        private const float shadowDist = 1.5f;

        // 图标与文字布局尺寸（根据桌面图标大小动态计算）
        private int iconSize;        // 图标边长（正方形）
        private int itemWidth;       // 每个栅栏项的宽度（含间距）
        private int textHeight;      // 文字区域高度
        private int itemPadding;     // 图标与文字之间的间距
        private int itemHeight;      // 单个栅栏项的总高度 = iconSize + itemPadding + textHeight
        private ICONMETRICS iconMetrics; // 系统桌面图标度量（含字体、间距等）

        private readonly FenceInfo fenceInfo;

        private Font titleFont;
        private Font iconFont;

        private string selectedItem;
        private string hoveringItem;
        private bool shouldUpdateSelection;
        private bool shouldRunDoubleClick;
        private bool hasSelectionUpdated;
        private bool hasHoverUpdated;
        private bool isMinified;
        private int prevHeight;

        private int scrollHeight;
        private int scrollOffset;

        // 定时轮询桌面图标大小变化（Ctrl+滚轮缩放不会触发 WM_SETTINGCHANGE）
        private readonly Timer iconMetricsPollTimer;
        private int lastDesktopIconSize; // 上一次检测到的桌面图标逻辑间距，用于判断是否需要刷新

        private readonly ThrottledExecution throttledMove = new ThrottledExecution(TimeSpan.FromSeconds(4));
        private readonly ThrottledExecution throttledResize = new ThrottledExecution(TimeSpan.FromSeconds(4));

        private readonly ShellContextMenu shellContextMenu = new ShellContextMenu();

        private readonly ThumbnailProvider thumbnailProvider;

        /// <summary>
        /// 将设备像素（物理像素）转换为逻辑像素（96 DPI 坐标）。
        /// 使用原生 GetDpiForWindow 而非 CreateGraphics().DpiX，
        /// 因为 WinForms 会缓存 DPI 值，DPI 变更后不会自动更新。
        /// </summary>
        private int DevicePixelsToLogical(int devicePixels)
        {
            uint dpi = 96;
            if (IsHandleCreated)
                dpi = GetDpiForWindow(Handle);
            System.Diagnostics.Debug.WriteLine(dpi.ToString() + '-' + CreateGraphics().DpiX.ToString());
            
            return (int)Math.Round(devicePixels * 96.0 / dpi);
        }

        /// <summary>
        /// 加载系统图标度量并计算栅栏项布局尺寸。
        /// 优先从桌面 SysListView32 读取实际图标间距（支持 Ctrl+滚轮缩放），
        /// 回退到系统 SPI_GETICONMETRICS 的默认值。
        /// </summary>
        private void LoadAndApplyMetrics()
        {
            try
            {
                // 1. 通过 LVM_GETITEMSPACING 读取桌面 SysListView32 的实际图标间距（设备像素）
                int deviceSpacing = GetDesktopIconSpacing();

                // 2. 通过 SPI_GETICONMETRICS 读取系统图标度量（获取字体信息）
                iconMetrics = new ICONMETRICS();
                iconMetrics.cbSize = (uint)Marshal.SizeOf(typeof(ICONMETRICS));
                SystemParametersInfo(SPI_GETICONMETRICS, iconMetrics.cbSize, ref iconMetrics, 0);

                // 3. 将设备像素间距转换为逻辑像素（96 DPI），供 WinForms 自动缩放使用
                int logicalSpacing;
                if (deviceSpacing > 0)
                    logicalSpacing = DevicePixelsToLogical(deviceSpacing);
                else if (iconMetrics.iHorzSpacing > 0)
                    logicalSpacing = iconMetrics.iHorzSpacing; // SPI_GETICONMETRICS 返回的已是 96 DPI 逻辑值
                else
                    logicalSpacing = 75; // 兜底默认值

                // 4. 根据间距比例计算各项布局参数
                itemWidth = Math.Max(60, logicalSpacing);          // 项宽度不小于 60px
                iconSize = Math.Max(16, (int)(itemWidth * 0.43)); // 图标约占项宽度的 43%
                itemPadding = Math.Max(8, (int)(itemWidth * 0.20)); // 内边距约占 20%，保证图标上边距可见
                // 文字区域高度：按桌面图标字体行高 × 行数（iTitleWrap 启用时 2 行，否则 1 行）
                using (var tmpFont = CreateIconFontFromLogFont())
                {
                    int lineHeight = (int)Math.Ceiling(tmpFont.GetHeight());
                    textHeight = Math.Max(lineHeight, lineHeight * 2 + 4); // 按 Windows 桌面规则始终 2 行
                }
                itemHeight = iconSize + itemPadding + textHeight;  // 项总高度
            }
            catch
            {
                // 读取失败时使用合理的硬编码默认值
                itemWidth = 75;
                iconSize = 32;
                textHeight = 35;
                itemPadding = 15;
                itemHeight = iconSize + itemPadding + textHeight;
            }
        }

        /// <summary>
        /// 直接读取桌面 SysListView32 的实际图标间距（通过 LVM_GETITEMSPACING 消息）。
        /// 这能反映 Ctrl+滚轮 缩放桌面图标后的实时大小，
        /// 而 SPI_GETICONMETRICS 返回的是系统默认值，不会随 Ctrl+滚轮变化。
        /// </summary>
        /// <returns>水平图标间距（设备像素），失败时返回 -1</returns>
        private static int GetDesktopIconSpacing()
        {
            // 桌面窗口层级：Progman → SHELLDLL_DefView → SysListView32
            IntPtr hwndProgman = FindWindow("Progman", null);
            if (hwndProgman == IntPtr.Zero)
                return -1;

            IntPtr hwndDefView = FindWindowEx(hwndProgman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr hwndListView;

            if (hwndDefView != IntPtr.Zero)
            {
                hwndListView = FindWindowEx(hwndDefView, IntPtr.Zero, "SysListView32", null);
            }
            else
            {
                // 部分 Windows 版本的桌面由 WorkerW 窗口承载（备用查找路径）
                IntPtr hwndWorkerW = IntPtr.Zero;
                do
                {
                    hwndWorkerW = FindWindowEx(IntPtr.Zero, hwndWorkerW, "WorkerW", null);
                    if (hwndWorkerW != IntPtr.Zero)
                    {
                        hwndDefView = FindWindowEx(hwndWorkerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (hwndDefView != IntPtr.Zero)
                            break;
                    }
                } while (hwndWorkerW != IntPtr.Zero);

                hwndListView = hwndDefView != IntPtr.Zero
                    ? FindWindowEx(hwndDefView, IntPtr.Zero, "SysListView32", null)
                    : IntPtr.Zero;
            }

            if (hwndListView == IntPtr.Zero)
                return -1;

            // LVM_GETITEMSPACING (0x1033)：wParam=FALSE 表示获取大图标视图下的间距
            // 返回值：LOWORD = 水平间距，HIWORD = 垂直间距（单位：设备像素）
            IntPtr result = SendMessage(hwndListView, 0x1033, IntPtr.Zero, IntPtr.Zero);
            if (result == IntPtr.Zero)
                return -1;

            int spacingX = unchecked((short)((uint)result & 0xFFFF));
            return spacingX;
        }

        /// <summary>
        /// 重新加载图标度量并应用到 UI。
        /// 仅在图标大小实际发生变化时才重建缩略图和字体并触发重绘，
        /// 避免不必要的性能开销。
        /// </summary>
        private void ApplyIconMetrics()
        {
            var prevIconSize = iconSize;
            LoadAndApplyMetrics();
            // 更新上次检测值，供定时轮询对比使用
            int curDeviceSpacing = GetDesktopIconSpacing();
            int curLogicalSpacing = curDeviceSpacing > 0 ? DevicePixelsToLogical(curDeviceSpacing) : -1;
            lastDesktopIconSize = curLogicalSpacing;
            if (iconSize != prevIconSize)
            {
                // 图标大小变化时：更新缩略图尺寸、重建字体、触发重绘
                thumbnailProvider.TargetSize = LogicalToDeviceUnits(iconSize);
                ReloadFonts();
                Invalidate();
            }
        }

        /// <summary>
        /// 从 SPI_GETICONMETRICS 返回的 LOGFONT 结构体创建 .NET Font 对象。
        /// 不能使用 CreateFontIndirect + Font.FromHfont，因为 FromHfont 仅支持 TrueType 字体，
        /// 而桌面图标字体可能是非 TrueType 字体（如 MS Sans Serif），会抛出异常。
        /// 改为直接读取 LOGFONT 各字段，手动构造 Font 对象。
        /// </summary>
        private Font CreateIconFontFromLogFont()
        {
            try
            {
                var lf = iconMetrics.lfFont;
                if (lf.lfFaceName == null || lf.lfFaceName.Length == 0)
                    return new Font("Segoe UI", 9f);
                // lfHeight 是逻辑单位，需要转换为磅值（point）
                float fontSize = Math.Abs(lf.lfHeight);
                using (var g = CreateGraphics())
                {
                    fontSize = fontSize * 72f / g.DpiY;
                }
                if (fontSize < 6f) fontSize = 9f; // 最小字号保护
                var style = FontStyle.Regular;
                if (lf.lfItalic != 0) style |= FontStyle.Italic;
                if (lf.lfWeight >= 700) style |= FontStyle.Bold;
                if (lf.lfUnderline != 0) style |= FontStyle.Underline;
                if (lf.lfStrikeOut != 0) style |= FontStyle.Strikeout;
                return new Font(lf.lfFaceName, fontSize, style);
            }
            catch
            {
                return new Font("Segoe UI", 9f);
            }
        }

        /// <summary>
        /// 重新创建所有字体（先释放旧字体防止 GDI 资源泄漏）。
        /// 标题字体基于标题栏高度计算，图标字体从系统图标度量获取。
        /// </summary>
        private void ReloadFonts()
        {
            var family = new FontFamily("Segoe UI");
            titleFont?.Dispose();
            iconFont?.Dispose();
            titleFont = new Font(family, (int)Math.Floor(logicalTitleHeight / 2.0));
            iconFont = CreateIconFontFromLogFont();
        }

        public FenceWindow(FenceInfo fenceInfo)
        {
            // 先初始化组件以创建窗口句柄（后续 DPI 相关操作需要 Handle）
            InitializeComponent();

            // 应用 Windows 视觉效果
            DropShadow.ApplyShadows(this);      // DWM 原生窗口阴影
            BlurUtil.EnableBlur(Handle);        // Acrylic 背景模糊
            WindowUtil.HideFromAltTab(Handle);  // 从 Alt+Tab 隐藏
            DesktopUtil.GlueToDesktop(Handle);  // 粘附到桌面 Progman 窗口

            // 必须在 Handle 创建后计算图标度量（需要正确的 DPI 信息）
            LoadAndApplyMetrics();
            int curDeviceSpacing = GetDesktopIconSpacing();
            lastDesktopIconSize = curDeviceSpacing > 0 ? DevicePixelsToLogical(curDeviceSpacing) : -1;

            // 创建缩略图生成器（使用设备像素尺寸）
            thumbnailProvider = new ThumbnailProvider(LogicalToDeviceUnits(iconSize));

            // 每 1.5 秒轮询桌面图标间距变化（捕获 Ctrl+滚轮 缩放）
            iconMetricsPollTimer = new Timer { Interval = 1500 };
            iconMetricsPollTimer.Tick += (s, e) =>
            {
                int curSpacing = GetDesktopIconSpacing();
                int curLogical = curSpacing > 0 ? DevicePixelsToLogical(curSpacing) : -1;
                if (curLogical > 0 && curLogical != lastDesktopIconSize)
                {
                    ApplyIconMetrics();
                }
            };
            iconMetricsPollTimer.Start();

            // 标题栏高度：逻辑像素，有效范围 16~100
            logicalTitleHeight = (fenceInfo.TitleHeight < 16 || fenceInfo.TitleHeight > 100) ? 35 : fenceInfo.TitleHeight;
            titleHeight = LogicalToDeviceUnits(logicalTitleHeight);
            
            this.MouseWheel += FenceWindow_MouseWheel;
            thumbnailProvider.IconThumbnailLoaded += ThumbnailProvider_IconThumbnailLoaded;

            ReloadFonts();

            AllowDrop = true; // 允许拖放文件到栅栏

            this.fenceInfo = fenceInfo;
            Text = fenceInfo.Name;
            Location = new Point(fenceInfo.PosX, fenceInfo.PosY);

            Width = fenceInfo.Width;
            Height = fenceInfo.Height;

            prevHeight = Height;
            lockedToolStripMenuItem.Checked = fenceInfo.Locked;
            minifyToolStripMenuItem.Checked = fenceInfo.CanMinify;
            Minify(); // 初始应用最小化状态
        }

        /// <summary>
        /// 窗口消息处理。实现无边框窗口的拖动/调整大小、DPI 响应、
        /// 图标度量动态更新等自定义行为。
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            // WM_SIZING：限制窗口最小尺寸
            // 最小宽度取标题文本宽度和图标列宽的最大值（确保标题和图标都能完整显示）
            // 最小高度至少为标题栏 + 一个图标项 + 内边距
            if (m.Msg == WM_SIZING)
            {
                int titleTextWidth;
                using (var g = CreateGraphics())
                    titleTextWidth = (int)Math.Ceiling(g.MeasureString(Text, titleFont).Width);
                // 标题文本左右需留边距
                int titleMinWidth = titleTextWidth + itemPadding * 2;
                // 单个图标列的最小宽度
                int iconMinWidth = itemPadding * 2 + itemWidth;
                int minWidth = Math.Max(titleMinWidth, iconMinWidth);
                int minHeight = titleHeight + itemHeight + itemPadding * 2;

                var rect = Marshal.PtrToStructure<RECT>(m.LParam);
                int edge = m.WParam.ToInt32();

                if (rect.Width < minWidth)
                {
                    if (edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT)
                        rect.Left = rect.Right - minWidth;
                    else
                        rect.Right = rect.Left + minWidth;
                }
                if (rect.Height < minHeight)
                {
                    if (edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT)
                        rect.Top = rect.Bottom - minHeight;
                    else
                        rect.Bottom = rect.Top + minHeight;
                }
                Marshal.StructureToPtr(rect, m.LParam, true);
                m.Result = (IntPtr)1; // 表示已处理
                return;
            }

            // 移除系统边框（WM_NCCALCSIZE → 返回 0 表示整个窗口都是工作区）
            if (m.Msg == 0x0083)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            // 鼠标离开窗口 → 自动最小化（收缩为标题栏）
            var myrect = new Rectangle(Location, Size);
            if (m.Msg == 0x02a2 && !myrect.IntersectsWith(new Rectangle(MousePosition, new Size(1, 1))))
            {
                Minify();
            }

            // 阻止窗口最大化（SC_MAXIMIZE）
            if ((m.Msg == WM_SYSCOMMAND) && m.WParam.ToInt32() == 0xF032)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            // 系统 DPI 变化（设置 → 显示 → 缩放），重新计算图标尺寸
            if (m.Msg == WM_DPICHANGED)
            {
                ApplyIconMetrics();
            }

            // 阻止获取焦点（保持在桌面窗口下方，不抢 Progman 焦点）
            if (m.Msg == WM_SETFOCUS)
            {
                SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                return;
            }

            // 系统设置广播（主题/字体等变化），重新加载图标度量
            if (m.Msg == WM_SETTINGCHANGE)
            {
                ApplyIconMetrics();
            }

            base.WndProc(ref m);

            // 锁定状态或鼠标右键按下时，不允许拖动和调整大小
            if (MouseButtons == MouseButtons.Right || lockedToolStripMenuItem.Checked)
                return;

            // WM_NCHITTEST：自定义无边框窗口的拖动和调整大小区域
            if (m.Msg == WM_NCHITTEST)
            {
                var pt = PointToClient(new Point(m.LParam.ToInt32()));

                // 标题栏区域 → 模拟标题栏拖动
                if ((int)m.Result == HTCLIENT && pt.Y < titleHeight)
                {
                    m.Result = (IntPtr)HTCAPTION;
                    FenceWindow_MouseEnter(null, null);
                }

                // 四角：10px 热区用于对角线调整大小
                if (pt.X < 10 && pt.Y < 10)
                    m.Result = new IntPtr(HTTOPLEFT);
                else if (pt.X > (Width - 10) && pt.Y < 10)
                    m.Result = new IntPtr(HTTOPRIGHT);
                else if (pt.X < 10 && pt.Y > (Height - 10))
                    m.Result = new IntPtr(HTBOTTOMLEFT);
                else if (pt.X > (Width - 10) && pt.Y > (Height - 10))
                    m.Result = new IntPtr(HTBOTTOMRIGHT);
                else if (pt.Y > (Height - 10))
                    m.Result = new IntPtr(HTBOTTOM);
                else if (pt.X < 10)
                    m.Result = new IntPtr(HTLEFT);
                else if (pt.X > (Width - 10))
                    m.Result = new IntPtr(HTRIGHT);
            }
        }

        /// <summary>移除当前栅栏（删除分区及其保存的数据）。</summary>
        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Really remove this fence?", "Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                FenceManager.Instance.RemoveFence(fenceInfo);
                Close();
            }
        }

        /// <summary>退出整个应用程序，关闭所有栅栏窗口。</summary>
        private void quitApplicationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        /// <summary>从栅栏中删除当前悬停的条目。</summary>
        private void deleteItemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fenceInfo.Files.Remove(hoveringItem);
            hoveringItem = null;
            Save();
            Refresh();
        }

        /// <summary>右键菜单打开前：仅在有悬停条目时显示"删除"选项。</summary>
        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            deleteItemToolStripMenuItem.Visible = hoveringItem != null;
        }

        /// <summary>拖放进入：仅接受文件拖放（锁定状态下拒绝）。</summary>
        private void FenceWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && !lockedToolStripMenuItem.Checked)
                e.Effect = DragDropEffects.Move;
        }

        /// <summary>拖放释放：将文件添加到栅栏（去重，仅添加存在的文件）。</summary>
        private void FenceWindow_DragDrop(object sender, DragEventArgs e)
        {
            var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in dropped)
                if (!fenceInfo.Files.Contains(file) && ItemExists(file))
                    fenceInfo.Files.Add(file);
            Save();
            Refresh();
        }

        /// <summary>窗口大小变化：节流保存新尺寸（4 秒延迟）。</summary>
        private void FenceWindow_Resize(object sender, EventArgs e)
        {
            throttledResize.Run(() =>
            {
                fenceInfo.Width = Width;
                fenceInfo.Height = isMinified ? prevHeight : Height; // 最小化时保存展开前高度
                Save();
            });

            Refresh();
        }

        /// <summary>鼠标移动：触发重绘以更新悬停高亮。</summary>
        private void FenceWindow_MouseMove(object sender, MouseEventArgs e)
        {
            Refresh();
        }

        /// <summary>鼠标进入：如果允许最小化且当前是最小化状态，则展开窗口。</summary>
        private void FenceWindow_MouseEnter(object sender, EventArgs e)
        {
            if (minifyToolStripMenuItem.Checked && isMinified)
            {
                isMinified = false;
                Height = prevHeight;
            }
        }

        /// <summary>鼠标离开：尝试最小化窗口。选中状态不取消，由用户点击其他条目或空白区域来改变。</summary>
        private void FenceWindow_MouseLeave(object sender, EventArgs e)
        {
            Minify();
            Refresh();
        }

        /// <summary>
        /// 将窗口收缩为仅标题栏（最小化状态）。
        /// 仅在 CanMinify 启用且未最小化时生效。
        /// </summary>
        private void Minify()
        {
            if (minifyToolStripMenuItem.Checked && !isMinified)
            {
                isMinified = true;
                prevHeight = Height;
                Height = titleHeight;
                Refresh();
            }
        }

        private void minifyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (isMinified)
            {
                Height = prevHeight;
                isMinified = false;
            }
            fenceInfo.CanMinify = minifyToolStripMenuItem.Checked;
            Save();
        }

        /// <summary>单击：设置选中更新标志，由 Paint 周期实际处理。</summary>
        private void FenceWindow_Click(object sender, EventArgs e)
        {
            shouldUpdateSelection = true;
            Refresh();
        }

        /// <summary>双击：设置双击执行标志，由 Paint 周期实际处理。</summary>
        private void FenceWindow_DoubleClick(object sender, EventArgs e)
        {
            shouldRunDoubleClick = true;
            Refresh();
        }

        /// <summary>
        /// 主渲染方法。所有 GDI+ 绘制在此完成（未启用 DoubleBuffered，
        /// 因为手动渲染依赖当前绘制周期处理鼠标命中测试）。
        /// 绘制顺序：背景 → 标题栏 → 条目网格 → 滚动条 → 点击/悬停处理。
        /// </summary>
        private void FenceWindow_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clip = new Region(ClientRectangle);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // 背景：半透明黑色
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(100, Color.Black)), ClientRectangle);

            // 标题栏：居中文本 + 半透明黑色叠加
            e.Graphics.DrawString(Text, titleFont, Brushes.White, new PointF(Width / 2, titleOffset), new StringFormat { Alignment = StringAlignment.Center });
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(50, Color.Black)), new RectangleF(0, 0, Width, titleHeight));

            // 条目网格布局：从左到右、从上到下排列
            var x = itemPadding;
            var y = itemPadding;
            scrollHeight = 0;
            e.Graphics.Clip = new Region(new Rectangle(0, titleHeight, Width, Height - titleHeight));
            // 记录选中条目（仅当文字超过标准 2 行高度时才需要展开渲染）
            string expandEntryName = null;
            int expandEntryX = 0, expandEntryY = 0;
            FenceEntry expandEntry = null; // 用于在展开区块中重绘图标

            foreach (var file in fenceInfo.Files)
            {
                var entry = FenceEntry.FromPath(file);
                if (entry == null)
                    continue;

                bool isSelected = entry.Path == selectedItem;
                bool needsExpand = false;

                if (isSelected)
                {
                    // 用不限高度测量文字真实高度，超过标准 2 行高度才需展开
                    var testMaxSize = new SizeF(itemWidth - 12, 9999);
                    var testFormat = new StringFormat { Alignment = StringAlignment.Center };
                    var testSize = e.Graphics.MeasureString(entry.Name, iconFont, testMaxSize, testFormat);
                    needsExpand = testSize.Height > textHeight;
                }

                RenderEntry(e.Graphics, entry, x, y + titleHeight - scrollOffset, skipText: needsExpand);

                if (needsExpand)
                {
                    expandEntryName = entry.Name;
                    expandEntryX = x;
                    expandEntryY = y + titleHeight - scrollOffset;
                    expandEntry = entry;
                }

                var itemBottom = y + itemHeight;
                if (itemBottom > scrollHeight)
                    scrollHeight = itemBottom;

                x += itemWidth + itemPadding;
                if (x + itemWidth > Width) // 换行
                {
                    x = itemPadding;
                    y += itemHeight + itemPadding;
                }
            }

            // 选中条目文字展开渲染：仅当文字超过标准 2 行高度时才进入此分支，高度不限，渲染在最顶层
            if (expandEntryName != null)
            {
                var expandTextPadding = itemPadding / 3;
                var expandTextPos = new PointF(expandEntryX + 6, expandEntryY + iconSize + expandTextPadding);
                var expandMaxSize = new SizeF(itemWidth - 12, 9999); // 不限制行数
                var expandFormat = new StringFormat
                {
                    Alignment = StringAlignment.Center
                    // 不设置 Trimming，显示全部文字
                };
                var expandTextSize = e.Graphics.MeasureString(expandEntryName, iconFont, expandMaxSize, expandFormat);
                float drawHeight = expandTextSize.Height;
                var drawSize = new SizeF(expandTextSize.Width, drawHeight);

                // 绘制选中高亮框（高度适配展开后的文案）
                var gap = expandTextPadding;
                var expandOutlineRect = new Rectangle(expandEntryX - 2, expandEntryY - 2,
                    itemWidth + 2, iconSize + (int)drawHeight + gap + 2);
                var expandOutlineRectInner = expandOutlineRect.Shrink(1);
                e.Graphics.DrawRectangle(new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)), expandOutlineRectInner);
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(80, SystemColors.GradientInactiveCaption)), expandOutlineRect);

                // 重绘图标（图层在最上方，防止被高亮框覆盖变模糊）
                var expandIcon = expandEntry?.ExtractIcon(thumbnailProvider);
                if (expandIcon != null)
                {
                    var expandIconRect = new Rectangle(expandEntryX + itemWidth / 2 - iconSize / 2, expandEntryY, iconSize, iconSize);
                    e.Graphics.DrawIcon(expandIcon, expandIconRect);
                }

                // 绘制文字阴影
                e.Graphics.DrawString(expandEntryName, iconFont,
                    new SolidBrush(Color.FromArgb(180, 15, 15, 15)),
                    new RectangleF(expandTextPos.Move(shadowDist, shadowDist), drawSize),
                    expandFormat);
                // 绘制白色前景文字
                e.Graphics.DrawString(expandEntryName, iconFont, Brushes.White,
                    new RectangleF(expandTextPos, drawSize),
                    expandFormat);

                // 鼠标悬浮时叠加高亮（使用与展开高亮框一致的尺寸，降低透明度让选中态更明显）
                var expandMousePos = PointToClient(MousePosition);
                if (expandMousePos.X >= expandEntryX - 2 && expandMousePos.Y >= expandEntryY - 2 &&
                    expandMousePos.X < expandEntryX + itemWidth + 2 &&
                    expandMousePos.Y < expandEntryY + iconSize + (int)drawHeight + gap + 2)
                {
                    // 悬浮时降低高亮框透明度：用更低的 alpha 叠加一层，使整体看起来更亮更通透
                    e.Graphics.DrawRectangle(new Pen(Color.FromArgb(40, SystemColors.ActiveBorder)), expandOutlineRectInner);
                    e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(30, Color.White)), expandOutlineRect);
                }
            }

            // 计算内容溢出高度（用于滚动条）
            scrollHeight -= (ClientRectangle.Height - titleHeight);

            // 滚动条：仅在内容溢出时绘制
            if (scrollHeight > 0)
            {
                var contentHeight = Height - titleHeight;
                var scrollbarHeight = contentHeight - scrollHeight;
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(150, Color.Black)), new Rectangle(Width - 5, titleHeight + scrollOffset, 5, scrollbarHeight));

                scrollOffset = Math.Min(scrollOffset, scrollHeight);
            }



            //  单击/双击处理标志重置（这些标志在 Paint 周期中执行实际操作）
            if (shouldUpdateSelection && !hasSelectionUpdated)
                selectedItem = null;

            if (!hasHoverUpdated)
                hoveringItem = null;

            shouldRunDoubleClick = false;
            shouldUpdateSelection = false;
            hasSelectionUpdated = false;
            hasHoverUpdated = false;
        }

        /// <summary>
        /// 渲染单个栅栏条目（图标 + 文字 + 选中/悬停高亮背景）。
        /// 当 skipText 为 true 时跳过文字绘制（选中条目文字将在循环后单独渲染到最顶层，保证 z-order）。
        /// </summary>
        private void RenderEntry(Graphics g, FenceEntry entry, int x, int y, bool skipText = false)
        {
            var icon = entry.ExtractIcon(thumbnailProvider);
            var name = entry.Name;

            var textPadding = itemPadding / 3;
            // 按 Windows 桌面规则：文本左右各留 6px 安全距离
            var textPosition = new PointF(x + 6, y + iconSize + textPadding);
            var textMaxSize = new SizeF(itemWidth - 12, textHeight);

            // 按 Windows 桌面规则始终允许自动换行，居中、末尾省略号截断
            var stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };

            var textSize = g.MeasureString(name, iconFont, textMaxSize, stringFormat);
            var gap = textPadding;
            var outlineRect = new Rectangle(x - 2, y - 2, itemWidth + 2, iconSize + (int)textSize.Height + gap + 2);
            var outlineRectInner = outlineRect.Shrink(1);

            var mousePos = PointToClient(MousePosition);
            var mouseOver = mousePos.X >= x && mousePos.Y >= y && mousePos.X < x + outlineRect.Width && mousePos.Y < y + outlineRect.Height;

            if (mouseOver)
            {
                hoveringItem = entry.Path;
                hasHoverUpdated = true;
            }

            // 单击选中（由 Paint 周期统一处理以保证命中测试一致性）
            if (mouseOver && shouldUpdateSelection)
            {
                selectedItem = entry.Path;
                shouldUpdateSelection = false;
                hasSelectionUpdated = true;
            }

            // 双击打开（由 Paint 周期统一处理以保证命中测试一致性）
            if (mouseOver && shouldRunDoubleClick)
            {
                shouldRunDoubleClick = false;
                entry.Open();
            }

            // 绘制选中/悬停背景
            if (selectedItem == entry.Path)
            {
                if (skipText)
                {
                    // 需要展开的选中条目：高亮框和悬浮叠加均在循环后根据展开高度绘制，此处不做任何绘制
                }
                else
                {
                    // 一行文本的选中条目：在此正常绘制高亮框
                    g.DrawRectangle(new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)), outlineRectInner);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(80, SystemColors.GradientInactiveCaption)), outlineRect);
                    if (mouseOver)
                    {
                        g.DrawRectangle(new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)), outlineRectInner);
                        g.FillRectangle(new SolidBrush(Color.FromArgb(100, SystemColors.GradientActiveCaption)), outlineRect);
                    }
                }
            }
            else
            {
                if (mouseOver)
                {
                    g.DrawRectangle(new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)), outlineRectInner);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(80, SystemColors.ActiveCaption)), outlineRect);
                }
            }

            // 绘制图标（居中缩放至 iconSize × iconSize）
            var iconRect = new Rectangle(x + itemWidth / 2 - iconSize / 2, y, iconSize, iconSize);
            g.DrawIcon(icon, iconRect);

            // 选中条目跳过文字绘制（将在循环后单独渲染到最顶层以保证 z-order 正确）
            if (skipText)
                return;

            // 绘制文字（先画阴影偏移，再画白色前景）
            g.DrawString(name, iconFont, new SolidBrush(Color.FromArgb(180, 15, 15, 15)), new RectangleF(textPosition.Move(shadowDist, shadowDist), textMaxSize), stringFormat);
            g.DrawString(name, iconFont, Brushes.White, new RectangleF(textPosition, textMaxSize), stringFormat);
        }

        /// <summary>重命名栅栏。</summary>
        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var dialog = new EditDialog(Text);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                Text = dialog.NewName;
                fenceInfo.Name = Text;
                Refresh();
                Save();
            }
        }

        /// <summary>创建新栅栏。</summary>
        private void newFenceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FenceManager.Instance.CreateFence("New fence");
        }

        /// <summary>窗口关闭：如果最后一个栅栏关闭则退出应用。</summary>
        private void FenceWindow_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (Application.OpenForms.Count == 0)
                Application.Exit();
        }

        private readonly object saveLock = new object();
        /// <summary>保存栅栏数据到磁盘（线程安全）。</summary>
        private void Save()
        {
            lock (saveLock)
            {
                FenceManager.Instance.UpdateFence(fenceInfo);
            }
        }

        /// <summary>窗口位置变化：节流保存新坐标（4 秒延迟）。</summary>
        private void FenceWindow_LocationChanged(object sender, EventArgs e)
        {
            throttledMove.Run(() =>
            {
                fenceInfo.PosX = Location.X;
                fenceInfo.PosY = Location.Y;
                Save();
            });
        }

        /// <summary>切换锁定状态。</summary>
        private void lockedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fenceInfo.Locked = lockedToolStripMenuItem.Checked;
            Save();
        }

        private void FenceWindow_Load(object sender, EventArgs e)
        {

        }

        /// <summary>调整标题栏高度：弹出 HeightDialog 让用户选择。</summary>
        private void titleSizeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var dialog = new HeightDialog(fenceInfo.TitleHeight);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                fenceInfo.TitleHeight = dialog.TitleHeight;
                logicalTitleHeight = dialog.TitleHeight;
                titleHeight = LogicalToDeviceUnits(logicalTitleHeight);
                ReloadFonts();
                Minify();
                if (isMinified)
                {
                    Height = titleHeight;
                }
                Refresh();
                Save();
            }
        }

        /// <summary>
        /// 鼠标右键点击处理。
        /// - 右键悬停条目：显示 Shell 上下文菜单
        /// - Shift+右键 或 右键空白区域：显示应用自身的上下文菜单
        /// </summary>
        private void FenceWindow_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            if (hoveringItem != null && !ModifierKeys.HasFlag(Keys.Shift))
            {
                // 右键条目 → Windows Shell 右键菜单
                shellContextMenu.ShowContextMenu(new[] { new FileInfo(hoveringItem) }, MousePosition);
            }
            else
            {
                // Shift+右键 或 空白处 → 应用菜单
                appContextMenu.Show(this, e.Location);
            }
        }

        /// <summary>鼠标滚轮：控制栅栏内容垂直滚动。</summary>
        private void FenceWindow_MouseWheel(object sender, MouseEventArgs e)
        {
            if (scrollHeight < 1)
                return;

            scrollOffset -= Math.Sign(e.Delta) * 10; // 每次滚动 10 像素
            if (scrollOffset < 0)
                scrollOffset = 0;
            if (scrollOffset > scrollHeight)
                scrollOffset = scrollHeight;

            Invalidate();
        }

        /// <summary>缩略图异步加载完成 → 触发重绘。</summary>
        private void ThumbnailProvider_IconThumbnailLoaded(object sender, EventArgs e)
        {
            Invalidate();
        }

        /// <summary>检查路径是否存在（文件或文件夹）。</summary>
        private bool ItemExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }
    }

}

