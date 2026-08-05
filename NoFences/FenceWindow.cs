using NoFences.Model;
using NoFences.Theming;
using NoFences.Util;
using NoFences.Win32;
using Peter;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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

        // 图标与文字布局尺寸（根据桌面图标大小动态计算）
        private int iconSize;        // 图标边长（设备像素，直接用于 GDI+ 绘制）
        private int itemWidth;       // 每个栅栏项的宽度（设备像素，含间距）
        private int textHeight;      // 文字区域高度（设备像素）
        private int itemPadding;     // 图标与文字之间的间距（设备像素）
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

        // 图标管理模式使用独立多选集合，不改变普通模式的单选与文字展开规则。
        private bool isIconManagementMode;
        private readonly HashSet<string> iconManagementSelection =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rectangle> entryHitBounds =
            new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);
        private Rectangle iconManagementConfirmBounds;
        private Rectangle iconManagementCancelBounds;

        private int scrollHeight;
        private int scrollOffset;

        // 定时轮询桌面图标大小变化（Ctrl+滚轮缩放不会触发 WM_SETTINGCHANGE）
        private readonly Timer iconMetricsPollTimer;
        private int lastDesktopIconSpacing; // 上一次检测到的桌面图标设备像素间距
        private int lastDesktopIconSize;    // 上一次检测到的桌面图标逻辑像素边长

        private readonly ThrottledExecution throttledMove = new ThrottledExecution(TimeSpan.FromSeconds(4));
        private readonly ThrottledExecution throttledResize = new ThrottledExecution(TimeSpan.FromSeconds(4));

        private readonly ShellContextMenu shellContextMenu = new ShellContextMenu();

        private readonly ThumbnailProvider thumbnailProvider;

        // 主题对象始终保存为独立快照，确保一次绘制使用一致的颜色和尺寸。
        private ThemeDefinition currentTheme;
        private Image backgroundImage;
        private readonly List<Image> themedMenuImages = new List<Image>();

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
            return (int)Math.Round(devicePixels * 96.0 / dpi);
        }

        /// <summary>
        /// 将逻辑像素（96 DPI 坐标）转换为当前窗口的设备像素。
        /// 使用原生 GetDpiForWindow，避免 WinForms 缓存的 DPI 值滞后。
        /// </summary>
        private int LogicalPixelsToDevice(int logicalPixels)
        {
            uint dpi = 96;
            if (IsHandleCreated)
                dpi = GetDpiForWindow(Handle);
            return (int)Math.Round(logicalPixels * dpi / 96.0, MidpointRounding.AwayFromZero);
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
                // 2. 通过 Shell 当前文件夹视图读取实际图标边长（逻辑像素）
                int logicalDesktopIconSize = DesktopUtil.GetDesktopIconSize();

                lastDesktopIconSpacing = deviceSpacing > 0 ? deviceSpacing : -1;
                lastDesktopIconSize = logicalDesktopIconSize > 0 ? logicalDesktopIconSize : -1;

                // 3. 通过 SPI_GETICONMETRICS 读取系统图标度量（获取字体信息）
                iconMetrics = new ICONMETRICS();
                iconMetrics.cbSize = (uint)Marshal.SizeOf(typeof(ICONMETRICS));
                SystemParametersInfo(SPI_GETICONMETRICS, iconMetrics.cbSize, ref iconMetrics, 0);

                // 4. GDI+ 手工绘制直接使用设备像素；仅对 SPI 的逻辑值做一次 DPI 放大
                int deviceItemWidth;
                if (deviceSpacing > 0)
                    deviceItemWidth = deviceSpacing;
                else if (iconMetrics.iHorzSpacing > 0)
                    deviceItemWidth = LogicalPixelsToDevice(iconMetrics.iHorzSpacing);
                else
                    deviceItemWidth = LogicalPixelsToDevice(75);

                // 5. 根据桌面实际间距计算单元格宽度
                itemWidth = Math.Max(LogicalPixelsToDevice(60), deviceItemWidth);
                // 6. 应用桌面当前视图的真实图标尺寸
                int renderedIconSize;
                if (logicalDesktopIconSize > 0)
                    renderedIconSize = LogicalPixelsToDevice(logicalDesktopIconSize);
                else
                    renderedIconSize = LogicalPixelsToDevice(
                        Math.Max(16, (int)(DevicePixelsToLogical(itemWidth) * 0.43))); // 回退到估算值

                // Shell 返回 DIP，GDI+ 绘制矩形使用设备像素，因此在此统一乘以 DPI 比例
                iconSize = Math.Max(LogicalPixelsToDevice(16), renderedIconSize);
                // 图标放大时单元格至少与图标同宽，避免相邻图标重叠
                itemWidth = Math.Max(itemWidth, iconSize);
                itemPadding = Math.Max(LogicalPixelsToDevice(8), (int)(itemWidth * 0.20));
                // 文字区域高度：按桌面图标字体行高 × 行数（iTitleWrap 启用时 2 行，否则 1 行）
                using (var tmpFont = CreateIconFontFromLogFont())
                using (var graphics = CreateGraphics())
                {
                    int lineHeight = (int)Math.Ceiling(tmpFont.GetHeight(graphics));
                    textHeight = Math.Max(lineHeight, lineHeight * 2 + 4); // 按 Windows 桌面规则始终 2 行
                }
                itemHeight = iconSize + itemPadding / 3 + textHeight;  // 项总高度（与实际渲染中 icon-text 间距一致）
            }
            catch
            {
                // 读取失败时使用合理的硬编码默认值
                itemWidth = LogicalPixelsToDevice(75);
                iconSize = LogicalPixelsToDevice(32);
                textHeight = LogicalPixelsToDevice(35);
                itemPadding = LogicalPixelsToDevice(15);
                itemHeight = iconSize + itemPadding / 3 + textHeight;
            }
        }

        /// <summary>
        /// 查找桌面 SysListView32 窗口句柄。
        /// 桌面窗口层级：Progman → SHELLDLL_DefView → SysListView32，
        /// 部分 Windows 版本由 WorkerW 窗口承载。
        /// </summary>
        /// <returns>SysListView32 窗口句柄，失败时返回 IntPtr.Zero</returns>
        private static IntPtr FindDesktopListView()
        {
            IntPtr hwndProgman = FindWindow("Progman", null);
            if (hwndProgman == IntPtr.Zero)
                return IntPtr.Zero;

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

            return hwndListView;
        }

        /// <summary>
        /// 直接读取桌面 SysListView32 的实际图标间距（通过 LVM_GETITEMSPACING 消息）。
        /// 这能反映 Ctrl+滚轮 缩放桌面图标后的实时大小，
        /// 而 SPI_GETICONMETRICS 返回的是系统默认值，不会随 Ctrl+滚轮变化。
        /// </summary>
        /// <returns>水平图标间距（设备像素），失败时返回 -1</returns>
        private static int GetDesktopIconSpacing()
        {
            IntPtr hwndListView = FindDesktopListView();
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

            if (thumbnailProvider != null && iconSize != prevIconSize)
                thumbnailProvider.TargetSize = iconSize;

            // 系统设置消息也可能只改变字体，因此每次应用度量都重建字体并重绘
            ReloadFonts();
            UpdateRoundedRegion();
            Invalidate();
        }

        /// <summary>
        /// 从 SPI_GETICONMETRICS 返回的 LOGFONT 结构体创建 .NET Font 对象。
        /// 不能使用 CreateFontIndirect + Font.FromHfont，因为 FromHfont 仅支持 TrueType 字体，
        /// 而桌面图标字体可能是非 TrueType 字体（如 MS Sans Serif），会抛出异常。
        /// 改为直接读取 LOGFONT 各字段，手动构造常规字重 Font 对象；文字
        /// 清晰度由 DPI 自适应的柔和阴影补足，避免小字号字形显得过粗。
        /// </summary>
        private Font CreateIconFontFromLogFont()
        {
            try
            {
                var lf = iconMetrics.lfFont;
                string familyName = currentTheme != null &&
                    !string.IsNullOrWhiteSpace(currentTheme.FontFamilyName)
                        ? currentTheme.FontFamilyName
                        : lf.lfFaceName;
                if (string.IsNullOrWhiteSpace(familyName))
                    familyName = "Segoe UI";
                // lfHeight 是逻辑单位，需要转换为磅值（point）
                float fontSize = Math.Abs(lf.lfHeight);
                using (var g = CreateGraphics())
                {
                    fontSize = fontSize * 72f / g.DpiY;
                }
                if (fontSize < 6f) fontSize = 9f; // 最小字号保护
                // 使用字体的常规字重，并继续应用 LOGFONT 中的斜体、下划线
                // 和删除线信息；可读性由后续柔和阴影提供，不再依赖半粗体。
                var style = FontStyle.Regular;
                if (lf.lfItalic != 0) style |= FontStyle.Italic;
                if (lf.lfUnderline != 0) style |= FontStyle.Underline;
                if (lf.lfStrikeOut != 0) style |= FontStyle.Strikeout;
                return new Font(familyName, fontSize, style);
            }
            catch
            {
                return new Font("Segoe UI", 9f, FontStyle.Regular);
            }
        }

        /// <summary>
        /// 重新创建所有字体（先释放旧字体防止 GDI 资源泄漏）。
        /// 标题字体基于标题栏高度计算，图标字体从系统图标度量获取。
        /// </summary>
        private void ReloadFonts()
        {
            titleFont?.Dispose();
            iconFont?.Dispose();

            string familyName = currentTheme != null &&
                !string.IsNullOrWhiteSpace(currentTheme.FontFamilyName)
                    ? currentTheme.FontFamilyName
                    : "Segoe UI";
            float titleSize = Math.Max(8f, (float)Math.Floor(logicalTitleHeight / 2.0));
            try
            {
                titleFont = new Font(familyName, titleSize, FontStyle.Regular);
            }
            catch
            {
                titleFont = new Font("Segoe UI", titleSize, FontStyle.Regular);
            }
            iconFont = CreateIconFontFromLogFont();
        }

        public FenceWindow(FenceInfo fenceInfo)
        {
            this.fenceInfo = fenceInfo ?? throw new ArgumentNullException(nameof(fenceInfo));
            currentTheme = ThemeManager.Instance.CurrentTheme;

            // 先初始化组件以创建窗口句柄（后续 DPI 相关操作需要 Handle）
            InitializeComponent();
            iconManagementToolStripMenuItem.Text = ThemeText.IconManagement;
            themeToolStripMenuItem.Text = ThemeText.ThemeMenu;
            darkModeToolStripMenuItem.Text = ThemeText.DarkMode;
            darkModeToolStripMenuItem.Checked = ThemeManager.Instance.DarkModeEnabled;

            // 应用 Windows 视觉效果
            DropShadow.ApplyShadows(this);      // DWM 原生窗口阴影
            WindowUtil.HideFromAltTab(Handle);  // 从 Alt+Tab 隐藏
            DesktopUtil.GlueToDesktop(Handle);  // 粘附到桌面 Progman 窗口

            // 必须在 Handle 创建后计算图标度量（需要正确的 DPI 信息）
            LoadAndApplyMetrics();

            // 创建缩略图生成器（iconSize 已是设备像素尺寸）
            thumbnailProvider = new ThumbnailProvider(iconSize);

            // 每 1.5 秒同时轮询桌面图标间距与边长（捕获 Ctrl+滚轮缩放）
            iconMetricsPollTimer = new Timer { Interval = 1500 };
            iconMetricsPollTimer.Tick += (s, e) =>
            {
                int curSpacing = GetDesktopIconSpacing();
                int curIconSize = DesktopUtil.GetDesktopIconSize();
                bool spacingChanged = curSpacing > 0 &&
                    curSpacing != lastDesktopIconSpacing;
                bool iconSizeChanged = curIconSize > 0 &&
                    curIconSize != lastDesktopIconSize;
                if (spacingChanged || iconSizeChanged)
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

            Text = fenceInfo.Name;
            Location = new Point(fenceInfo.PosX, fenceInfo.PosY);

            Width = fenceInfo.Width;
            Height = fenceInfo.Height;

            prevHeight = Height;
            lockedToolStripMenuItem.Checked = fenceInfo.Locked;
            minifyToolStripMenuItem.Checked = fenceInfo.CanMinify;

            ThemeManager.Instance.ThemeChanged += ThemeManager_ThemeChanged;
            ApplyTheme(currentTheme);
            Minify(); // 初始应用最小化状态
        }

        /// <summary>
        /// 将全局主题应用到当前栅栏。位置、大小、锁定状态和文件列表等业务数据
        /// 不属于主题，因此切换主题时不会修改这些持久化信息。
        /// </summary>
        private void ApplyTheme(ThemeDefinition theme)
        {
            currentTheme = (theme ?? ThemePresets.CreateDefault()).Clone();
            currentTheme.Normalize();

            BackColor = currentTheme.MainPanelColor;
            ApplyContextMenuIcons();
            ThemeUi.ApplyToContextMenu(appContextMenu, currentTheme);
            darkModeToolStripMenuItem.Checked = ThemeManager.Instance.DarkModeEnabled;
            BlurUtil.SetBlur(Handle, currentTheme.EnableBlur);
            ReloadBackgroundImage();
            ReloadFonts();
            UpdateRoundedRegion();
            Invalidate(true);
        }

        /// <summary>
        /// 根据当前菜单风格重新生成应用菜单图标。Win11/Standard 使用彩色矢量
        /// 风格，XP 使用经典高对比图标；主题切换前释放旧位图，避免长期运行泄漏 GDI。
        /// </summary>
        private void ApplyContextMenuIcons()
        {
            ClearContextMenuIcons();
            SetContextMenuIcon(iconManagementToolStripMenuItem, ThemedMenuIcon.ManageIcons);
            SetContextMenuIcon(deleteItemToolStripMenuItem, ThemedMenuIcon.Delete);
            SetContextMenuIcon(renameToolStripMenuItem, ThemedMenuIcon.Rename);
            SetContextMenuIcon(titleSizeToolStripMenuItem, ThemedMenuIcon.TitleHeight);
            SetContextMenuIcon(themeToolStripMenuItem, ThemedMenuIcon.Theme);
            SetContextMenuIcon(newFenceToolStripMenuItem, ThemedMenuIcon.NewFence);
            SetContextMenuIcon(exitToolStripMenuItem, ThemedMenuIcon.CloseFence);
            SetContextMenuIcon(quitApplicationToolStripMenuItem, ThemedMenuIcon.QuitApplication);
        }

        private void SetContextMenuIcon(
            ToolStripMenuItem item,
            ThemedMenuIcon icon)
        {
            Image image = ThemedMenuIconFactory.Create(icon, currentTheme);
            themedMenuImages.Add(image);
            item.ImageScaling = ToolStripItemImageScaling.None;
            item.Image = image;
        }

        /// <summary>解除菜单项引用并释放本窗口生成的所有主题图标。</summary>
        private void ClearContextMenuIcons()
        {
            iconManagementToolStripMenuItem.Image = null;
            deleteItemToolStripMenuItem.Image = null;
            lockedToolStripMenuItem.Image = null;
            minifyToolStripMenuItem.Image = null;
            renameToolStripMenuItem.Image = null;
            titleSizeToolStripMenuItem.Image = null;
            themeToolStripMenuItem.Image = null;
            darkModeToolStripMenuItem.Image = null;
            newFenceToolStripMenuItem.Image = null;
            exitToolStripMenuItem.Image = null;
            quitApplicationToolStripMenuItem.Image = null;

            foreach (Image image in themedMenuImages)
                image.Dispose();
            themedMenuImages.Clear();
        }

        private void ThemeManager_ThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ApplyTheme(ThemeManager.Instance.CurrentTheme)));
                return;
            }

            ApplyTheme(ThemeManager.Instance.CurrentTheme);
        }

        /// <summary>
        /// 创建背景图片的内存副本，避免程序运行期间持续锁定用户选择的文件。
        /// </summary>
        private void ReloadBackgroundImage()
        {
            backgroundImage?.Dispose();
            backgroundImage = ThemeDrawing.LoadImageWithoutLock(currentTheme.BackgroundImagePath);
        }

        /// <summary>
        /// Form.Region 同时决定绘制轮廓与鼠标命中区域。圆角按当前 DPI 缩放，
        /// 从而在不同显示器上保持相同的视觉尺寸。
        /// </summary>
        private void UpdateRoundedRegion()
        {
            Region oldRegion = Region;
            if (currentTheme == null || currentTheme.CornerRadius <= 0 ||
                Width <= 1 || Height <= 1)
            {
                Region = null;
            }
            else
            {
                int radius = LogicalPixelsToDevice(currentTheme.CornerRadius);
                using (var path = ThemeDrawing.CreateRoundedRectangle(
                    new RectangleF(0, 0, Width, Height),
                    radius))
                {
                    Region = new Region(path);
                }
            }
            oldRegion?.Dispose();
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
                string error;
                if (FenceManager.Instance.TryRemoveFence(fenceInfo, out error))
                {
                    Close();
                }
                else
                {
                    MessageBox.Show(
                        this,
                        "Unable to restore all desktop shortcuts. The fence was kept to protect its files.\n\n" + error,
                        "Remove fence",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    Refresh();
                }
            }
        }

        /// <summary>退出整个应用程序，关闭所有栅栏窗口。</summary>
        private void quitApplicationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string error;
            if (FenceManager.Instance.TryRestoreDesktopShortcutsForExit(out error))
            {
                Application.Exit();
            }
            else
            {
                MessageBox.Show(
                    this,
                    "Some desktop shortcuts could not be restored. No files were overwritten and the application will remain open.\n\n" + error,
                    "Exit NoFences",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Refresh();
            }
        }

        /// <summary>从栅栏中删除当前悬停的条目。</summary>
        private void deleteItemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string item = hoveringItem;
            if (string.IsNullOrWhiteSpace(item))
                return;

            string error;
            if (FenceManager.Instance.TryRemoveEntry(fenceInfo, item, out error))
            {
                hoveringItem = null;
                if (selectedItem == item)
                    selectedItem = null;
                Refresh();
            }
            else
            {
                MessageBox.Show(
                    this,
                    "Unable to restore the desktop shortcut. The item remains in the fence.\n\n" + error,
                    "Remove item",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>从应用右键菜单进入图标多选管理模式。</summary>
        private void iconManagementToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (isMinified)
            {
                isMinified = false;
                Height = prevHeight;
            }

            isIconManagementMode = true;
            iconManagementSelection.Clear();
            selectedItem = null;
            hoveringItem = null;
            Refresh();
        }

        /// <summary>退出图标管理模式，并清理所有仅用于本次操作的选择状态。</summary>
        private void CancelIconManagement()
        {
            isIconManagementMode = false;
            iconManagementSelection.Clear();
            iconManagementConfirmBounds = Rectangle.Empty;
            iconManagementCancelBounds = Rectangle.Empty;
            hoveringItem = null;
            Refresh();
        }

        /// <summary>
        /// 确认移除当前多选条目。普通条目仅解除栅栏引用；受托管的桌面快捷方式
        /// 会先恢复文件及桌面坐标。失败条目保持选中，便于用户修复后重试。
        /// </summary>
        private void ConfirmIconManagement()
        {
            if (iconManagementSelection.Count == 0)
                return;

            var errors = new List<string>();
            var selectedPaths = new List<string>(iconManagementSelection);
            foreach (string path in selectedPaths)
            {
                string error;
                if (FenceManager.Instance.TryRemoveEntry(fenceInfo, path, out error))
                {
                    iconManagementSelection.Remove(path);
                    if (selectedItem == path)
                        selectedItem = null;
                }
                else
                {
                    errors.Add(Path.GetFileName(path) + "：" + error);
                }
            }

            if (errors.Count == 0)
            {
                CancelIconManagement();
                return;
            }

            Refresh();
            MessageBox.Show(
                this,
                ThemeText.RemoveItemsFailed + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, errors),
                ThemeText.RemoveItemsTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        /// <summary>处理图标管理模式中的底部按钮和条目多选点击。</summary>
        private void HandleIconManagementClick(Point location)
        {
            if (iconManagementCancelBounds.Contains(location))
            {
                CancelIconManagement();
                return;
            }

            if (iconManagementConfirmBounds.Contains(location))
            {
                ConfirmIconManagement();
                return;
            }

            int footerTop = Height - GetIconManagementFooterHeight();
            if (location.Y < titleHeight || location.Y >= footerTop)
                return;

            foreach (KeyValuePair<string, Rectangle> item in entryHitBounds)
            {
                if (!item.Value.Contains(location))
                    continue;

                if (!iconManagementSelection.Add(item.Key))
                    iconManagementSelection.Remove(item.Key);
                Refresh();
                return;
            }
        }

        /// <summary>
        /// 在应用自有右键菜单打开前更新动态菜单项，并在最终布局完成后重新应用
        /// 当前主题。该菜单由空白处右键或条目上的 Shift+右键触发。
        /// </summary>
        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            deleteItemToolStripMenuItem.Visible = hoveringItem != null;
            iconManagementToolStripMenuItem.Enabled = !isIconManagementMode;
            darkModeToolStripMenuItem.Checked = ThemeManager.Instance.DarkModeEnabled;
            // 可见性和勾选状态会触发 ToolStrip 重新布局；在这些状态更新后应用
            // 主题，确保空白处右键弹出的整个菜单窗口都按当前主题重新绘制。
            ThemeUi.ApplyToContextMenu(appContextMenu, currentTheme);
        }

        /// <summary>拖放进入：仅接受文件拖放（锁定状态下拒绝）。</summary>
        private void FenceWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                !lockedToolStripMenuItem.Checked &&
                !isIconManagementMode)
                e.Effect = DragDropEffects.Move;
        }

        /// <summary>
        /// 拖放释放：普通路径加入栅栏；桌面快捷方式则记录坐标并搬入托管目录。
        /// 任一条目失败时保留原文件，并在处理其余条目后统一提示。
        /// </summary>
        private void FenceWindow_DragDrop(object sender, DragEventArgs e)
        {
            if (isIconManagementMode)
                return;

            var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
            var errors = new List<string>();
            foreach (var file in dropped)
            {
                if (!ItemExists(file))
                    continue;

                string error;
                if (!FenceManager.Instance.TryAddEntry(fenceInfo, file, out error))
                    errors.Add(Path.GetFileName(file) + ": " + error);
            }

            Refresh();
            if (errors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Some items could not be added:\n\n" + string.Join(Environment.NewLine, errors),
                    "Add items",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>窗口大小变化：节流保存新尺寸（4 秒延迟）。</summary>
        private void FenceWindow_Resize(object sender, EventArgs e)
        {
            UpdateRoundedRegion();

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
            if (!isIconManagementMode && minifyToolStripMenuItem.Checked && !isMinified)
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
            if (isIconManagementMode)
                return;

            shouldUpdateSelection = true;
            Refresh();
        }

        /// <summary>双击：设置双击执行标志，由 Paint 周期实际处理。</summary>
        private void FenceWindow_DoubleClick(object sender, EventArgs e)
        {
            if (isIconManagementMode)
                return;

            shouldRunDoubleClick = true;
            Refresh();
        }

        /// <summary>
        /// FenceWindow_Paint 会覆盖整个客户区。跳过 WinForms 默认的纯色背景擦除，
        /// 才能让带透明度的主题背景交给 DWM 模糊合成，而不是先被压平。
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        /// <summary>
        /// 主渲染方法。所有 GDI+ 绘制在此完成（未启用 DoubleBuffered，
        /// 因为手动渲染依赖当前绘制周期处理鼠标命中测试）。
        /// 绘制顺序：背景 → 标题栏 → 条目网格 → 滚动条 → 点击/悬停处理。
        /// </summary>
        private void FenceWindow_Paint(object sender, PaintEventArgs e)
        {
            var theme = currentTheme ?? ThemePresets.CreateDefault();
            e.Graphics.SetClip(ClientRectangle);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            e.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

            // 主面板颜色、透明度与背景图片均来自当前主题。
            using (var backgroundBrush = new SolidBrush(ThemeDrawing.WithOpacity(
                theme.MainPanelColor,
                theme.MainPanelOpacityPercent)))
            {
                e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
            }
            ThemeDrawing.DrawBackgroundImage(
                e.Graphics,
                backgroundImage,
                ClientRectangle,
                theme.BackgroundImageLayout,
                theme.BackgroundImageOpacityPercent);

            // 标题背景先于标题文字绘制，避免降低文字本身的不透明度。
            using (var titleBrush = new SolidBrush(ThemeDrawing.WithOpacity(
                theme.TitleBarColor,
                theme.TitleBarOpacityPercent)))
            {
                e.Graphics.FillRectangle(
                    titleBrush,
                    new RectangleF(0, 0, Width, titleHeight));
            }
            using (var titleTextBrush = new SolidBrush(theme.TitleTextColor))
            using (var titleFormat = new StringFormat { Alignment = StringAlignment.Center })
            {
                e.Graphics.DrawString(
                    Text,
                    titleFont,
                    titleTextBrush,
                    new PointF(Width / 2f, titleOffset),
                    titleFormat);
            }

            // 条目网格布局：从左到右、从上到下排列
            var x = itemPadding;
            var y = itemPadding;
            scrollHeight = 0;
            entryHitBounds.Clear();
            int managementFooterHeight = isIconManagementMode
                ? GetIconManagementFooterHeight()
                : 0;
            int contentViewportHeight = Math.Max(
                0,
                Height - titleHeight - managementFooterHeight);
            var contentViewport = new Rectangle(
                0,
                titleHeight,
                Width,
                contentViewportHeight);
            var contentState = e.Graphics.Save();
            e.Graphics.SetClip(
                contentViewport,
                System.Drawing.Drawing2D.CombineMode.Intersect);
            // 记录选中条目（仅当文字超过标准 2 行高度时才需要展开渲染）
            string expandEntryName = null;
            int expandEntryX = 0, expandEntryY = 0;
            FenceEntry expandEntry = null; // 用于在展开区块中重绘图标

            foreach (var file in fenceInfo.Files)
            {
                var entry = FenceEntry.FromPath(file);
                if (entry == null)
                    continue;

                bool isSelected = !isIconManagementMode && entry.Path == selectedItem;
                bool needsExpand = false;

                if (isSelected)
                {
                    // 用不限高度测量文字真实高度，超过标准 2 行高度才需展开
                    var testMaxSize = new SizeF(itemWidth - 12, 9999);
                    var testFormat = new StringFormat { Alignment = StringAlignment.Center };
                    var testSize = e.Graphics.MeasureString(entry.Name, iconFont, testMaxSize, testFormat);
                    needsExpand = testSize.Height > textHeight;
                }

                Rectangle entryBounds = RenderEntry(
                    e.Graphics,
                    entry,
                    x,
                    y + titleHeight - scrollOffset,
                    skipText: needsExpand);
                entryHitBounds[entry.Path] = entryBounds;

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

            if (isIconManagementMode)
            {
                iconManagementSelection.RemoveWhere(path =>
                    !entryHitBounds.ContainsKey(path));
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
                var expandMousePos = PointToClient(MousePosition);
                bool expandMouseOver = expandOutlineRect.Contains(expandMousePos);
                if (theme.MenuStyle != ThemeMenuStyle.WindowsXp)
                {
                    DrawModernDesktopItemState(
                        e.Graphics,
                        expandOutlineRect,
                        true,
                        expandMouseOver,
                        theme);
                }

                // 重绘图标（图层在最上方，防止被高亮框覆盖变模糊）
                var expandIcon = expandEntry?.ExtractIcon(thumbnailProvider);
                if (expandIcon != null)
                {
                    var expandIconRect = new Rectangle(expandEntryX + itemWidth / 2 - iconSize / 2, expandEntryY, iconSize, iconSize);
                    DrawDesktopIconForState(
                        e.Graphics,
                        expandIcon,
                        expandIconRect,
                        true,
                        theme);
                }

                DrawDesktopIconTextForState(
                    e.Graphics,
                    expandEntryName,
                    new RectangleF(expandTextPos, drawSize),
                    expandFormat,
                    theme,
                    true,
                    expandMouseOver);
            }

            // 计算内容溢出高度（用于滚动条），确保不会出现负值
            scrollHeight = Math.Max(0, scrollHeight - contentViewportHeight);
            scrollOffset = Math.Min(scrollOffset, scrollHeight);

            // 滚动条：仅在内容溢出时绘制
            if (scrollHeight > 0)
            {
                int contentHeight = Math.Max(1, contentViewportHeight);
                int totalContentHeight = contentHeight + scrollHeight;
                int proportionalHeight = (int)Math.Round(
                    contentHeight * (contentHeight / (double)totalContentHeight));
                int scrollbarHeight = Math.Min(
                    contentHeight,
                    Math.Max(LogicalPixelsToDevice(20), proportionalHeight));
                int scrollbarTravel = contentHeight - scrollbarHeight;
                int scrollbarY = titleHeight + (int)Math.Round(
                    scrollOffset / (double)scrollHeight * scrollbarTravel);
                using (var scrollbarBrush = new SolidBrush(
                    ThemeDrawing.WithAlpha(theme.ScrollBarColor, 190)))
                {
                    e.Graphics.FillRectangle(
                        scrollbarBrush,
                        new Rectangle(Math.Max(0, Width - 5), scrollbarY, 5, scrollbarHeight));
                }
            }



            //  单击/双击处理标志重置（这些标志在 Paint 周期中执行实际操作）
            e.Graphics.Restore(contentState);

            if (isIconManagementMode)
                DrawIconManagementFooter(e.Graphics, theme, managementFooterHeight);

            // Draw a themed outline using the same radius as the actual window region.
            float borderRadius = theme.CornerRadius > 0
                ? LogicalPixelsToDevice(theme.CornerRadius)
                : 0;
            using (var borderPath = ThemeDrawing.CreateRoundedRectangle(
                new RectangleF(
                    0.5f,
                    0.5f,
                    Math.Max(0, Width - 1f),
                    Math.Max(0, Height - 1f)),
                borderRadius))
            using (var borderPen = new Pen(
                ThemeDrawing.WithAlpha(theme.BorderColor, 125)))
            {
                e.Graphics.DrawPath(borderPen, borderPath);
            }

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
        private Rectangle RenderEntry(Graphics g, FenceEntry entry, int x, int y, bool skipText = false)
        {
            var theme = currentTheme ?? ThemePresets.CreateDefault();
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

            var mousePos = PointToClient(MousePosition);
            var mouseOver = mousePos.X >= x && mousePos.Y >= y && mousePos.X < x + outlineRect.Width && mousePos.Y < y + outlineRect.Height;

            if (mouseOver)
            {
                hoveringItem = entry.Path;
                hasHoverUpdated = true;
            }

            // 单击选中（由 Paint 周期统一处理以保证命中测试一致性）
            if (!isIconManagementMode && mouseOver && shouldUpdateSelection)
            {
                selectedItem = entry.Path;
                shouldUpdateSelection = false;
                hasSelectionUpdated = true;
            }

            // 双击打开（由 Paint 周期统一处理以保证命中测试一致性）
            if (!isIconManagementMode && mouseOver && shouldRunDoubleClick)
            {
                shouldRunDoubleClick = false;
                entry.Open();
            }

            // 绘制选中/悬停背景。XP 默认悬浮无反馈，且选中效果由后续的
            // 图标染色和文字实色底组成，因此这里只处理现代整项高亮。
            bool selected = isIconManagementMode
                ? iconManagementSelection.Contains(entry.Path)
                : selectedItem == entry.Path;
            if (!skipText && theme.MenuStyle != ThemeMenuStyle.WindowsXp)
            {
                DrawModernDesktopItemState(
                    g,
                    outlineRect,
                    selected,
                    mouseOver,
                    theme);
            }

            // 绘制图标（居中缩放至 iconSize × iconSize）
            var iconRect = new Rectangle(x + itemWidth / 2 - iconSize / 2, y, iconSize, iconSize);
            DrawDesktopIconForState(g, icon, iconRect, selected, theme);
            if (isIconManagementMode)
                DrawIconManagementCheck(g, iconRect, selected, theme);

            // 选中条目跳过文字绘制（将在循环后单独渲染到最顶层以保证 z-order 正确）
            if (skipText)
                return outlineRect;

            DrawDesktopIconTextForState(
                g,
                name,
                new RectangleF(textPosition, textMaxSize),
                stringFormat,
                theme,
                selected,
                mouseOver);

            return outlineRect;
        }

        /// <summary>
        /// 绘制默认和 Win11 风格的整项状态层。颜色会根据面板明暗选择规范中的
        /// 深/浅覆盖方案，高亮范围始终使用动态计算后的完整项目矩形。
        /// </summary>
        private void DrawModernDesktopItemState(
            Graphics graphics,
            Rectangle bounds,
            bool selected,
            bool hovered,
            ThemeDefinition theme)
        {
            if (!selected && !hovered)
                return;

            Color fillColor;
            Color strokeColor;
            int fillAlpha;
            int strokeAlpha;

            if (SystemInformation.HighContrast)
            {
                fillColor = SystemColors.Highlight;
                strokeColor = selected
                    ? SystemColors.HighlightText
                    : SystemColors.WindowText;
                fillAlpha = 255;
                strokeAlpha = 255;
            }
            else
            {
                bool darkSurface = ThemeDrawing.IsDark(theme.MainPanelColor);
                if (selected && hovered)
                {
                    fillColor = Color.FromArgb(95, 145, 195);
                    strokeColor = Color.FromArgb(220, 238, 252);
                    fillAlpha = 61;   // 24%
                    strokeAlpha = 148; // 58%
                }
                else if (selected)
                {
                    fillColor = darkSurface
                        ? Color.FromArgb(130, 175, 215)
                        : Color.FromArgb(45, 95, 145);
                    strokeColor = darkSurface
                        ? Color.FromArgb(220, 238, 252)
                        : Color.FromArgb(45, 95, 145);
                    fillAlpha = 46;   // 18%
                    strokeAlpha = darkSurface ? 122 : 117;
                }
                else
                {
                    fillColor = darkSurface
                        ? Color.FromArgb(110, 155, 200)
                        : Color.FromArgb(45, 80, 115);
                    strokeColor = darkSurface
                        ? Color.FromArgb(210, 230, 248)
                        : Color.FromArgb(45, 80, 115);
                    fillAlpha = 20;   // 8%
                    strokeAlpha = darkSurface ? 61 : 56;
                }
            }

            float dpiScale = IsHandleCreated
                ? Math.Max(1f, GetDpiForWindow(Handle) / 96f)
                : 1f;
            float strokeWidth = dpiScale;
            var visualBounds = new RectangleF(
                bounds.X + strokeWidth / 2f,
                bounds.Y + strokeWidth / 2f,
                Math.Max(0, bounds.Width - strokeWidth),
                Math.Max(0, bounds.Height - strokeWidth));
            using (var path = ThemeDrawing.CreateRoundedRectangle(
                visualBounds,
                dpiScale))
            using (var fill = new SolidBrush(
                ThemeDrawing.WithAlpha(fillColor, fillAlpha)))
            using (var stroke = new Pen(
                ThemeDrawing.WithAlpha(strokeColor, strokeAlpha),
                strokeWidth))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(stroke, path);
            }
        }

        /// <summary>按当前桌面视觉风格绘制普通、选中或高对比度文字。</summary>
        private void DrawDesktopIconTextForState(
            Graphics graphics,
            string text,
            RectangleF bounds,
            StringFormat format,
            ThemeDefinition theme,
            bool selected,
            bool hovered)
        {
            if (theme.MenuStyle == ThemeMenuStyle.WindowsXp && selected)
            {
                DrawXpSelectedLabel(graphics, text, bounds);
                return;
            }

            if (theme.MenuStyle != ThemeMenuStyle.WindowsXp &&
                SystemInformation.HighContrast &&
                (selected || hovered))
            {
                using (var textBrush = new SolidBrush(SystemColors.HighlightText))
                    graphics.DrawString(text, iconFont, textBrush, bounds, format);
                return;
            }

            DrawDesktopStyleIconText(graphics, text, bounds, format, theme);
        }

        /// <summary>
        /// 绘制 XP 经典选中文字：每一行使用紧贴文字宽度的不透明系统高亮色
        /// 矩形，并以系统高亮文字色绘制，不使用阴影、透明度或圆角。
        /// </summary>
        private void DrawXpSelectedLabel(
            Graphics graphics,
            string text,
            RectangleF bounds)
        {
            float lineHeight = Math.Max(1f, iconFont.GetHeight(graphics));
            int maxLines = Math.Max(1, (int)Math.Floor(bounds.Height / lineHeight));
            List<string> lines = MeasureXpLabelLines(
                graphics,
                text,
                bounds.Width,
                lineHeight,
                maxLines);
            float horizontalPadding = Math.Max(1f, LogicalPixelsToDevice(1));

            using (var background = new SolidBrush(SystemColors.Highlight))
            using (var foreground = new SolidBrush(SystemColors.HighlightText))
            using (var lineFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap
            })
            using (var measureFormat = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                measureFormat.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                for (int index = 0; index < lines.Count; index++)
                {
                    string line = lines[index];
                    if (line.Length == 0)
                        continue;

                    float lineWidth = graphics.MeasureString(
                        line,
                        iconFont,
                        new SizeF(bounds.Width, lineHeight + 2f),
                        measureFormat).Width;
                    float backgroundWidth = Math.Min(
                        bounds.Width,
                        (float)Math.Ceiling(lineWidth) + horizontalPadding * 2f);
                    float lineY = bounds.Y + index * lineHeight;
                    graphics.FillRectangle(
                        background,
                        bounds.X + (bounds.Width - backgroundWidth) / 2f,
                        lineY,
                        backgroundWidth,
                        (float)Math.Ceiling(lineHeight));
                    graphics.DrawString(
                        line,
                        iconFont,
                        foreground,
                        new RectangleF(bounds.X, lineY, bounds.Width, lineHeight + 1f),
                        lineFormat);
                }
            }
        }

        /// <summary>按标签宽度和可用行数拆分 XP 选中文字，并在截断处添加省略号。</summary>
        private List<string> MeasureXpLabelLines(
            Graphics graphics,
            string text,
            float maximumWidth,
            float lineHeight,
            int maximumLines)
        {
            var lines = new List<string>();
            string remaining = text ?? string.Empty;
            using (var fitFormat = new StringFormat
            {
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.LineLimit
            })
            {
                while (remaining.Length > 0 && lines.Count < maximumLines)
                {
                    int charactersFitted;
                    int linesFilled;
                    graphics.MeasureString(
                        remaining,
                        iconFont,
                        new SizeF(maximumWidth, lineHeight * 1.5f),
                        fitFormat,
                        out charactersFitted,
                        out linesFilled);
                    if (charactersFitted <= 0)
                        charactersFitted = 1;

                    charactersFitted = Math.Min(charactersFitted, remaining.Length);
                    string line = remaining.Substring(0, charactersFitted)
                        .TrimEnd(' ', '\t', '\r', '\n');
                    remaining = remaining.Substring(charactersFitted)
                        .TrimStart(' ', '\t', '\r', '\n');
                    lines.Add(line);
                }
            }

            if (lines.Count == 0)
                lines.Add(string.Empty);
            if (remaining.Length > 0)
            {
                int lastIndex = lines.Count - 1;
                lines[lastIndex] = FitXpEllipsis(
                    graphics,
                    lines[lastIndex],
                    maximumWidth,
                    lineHeight);
            }

            return lines;
        }

        /// <summary>缩短 XP 标签末行，确保省略号仍处于标签最大宽度内。</summary>
        private string FitXpEllipsis(
            Graphics graphics,
            string line,
            float maximumWidth,
            float lineHeight)
        {
            string content = line.TrimEnd();
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                while (content.Length > 0)
                {
                    string candidate = content + "…";
                    SizeF size = graphics.MeasureString(
                        candidate,
                        iconFont,
                        new SizeF(maximumWidth, lineHeight + 2f),
                        format);
                    if (size.Width <= maximumWidth)
                        return candidate;
                    content = content.Substring(0, content.Length - 1).TrimEnd();
                }
            }

            return "…";
        }

        /// <summary>
        /// 以 DPI 自适应的多点采样绘制图标文字。阴影围绕右下偏移中心分布，
        /// 多层低透明度样本叠加成柔和模糊边缘，避免清晰重影或生硬描边。
        /// </summary>
        private void DrawDesktopStyleIconText(
            Graphics graphics,
            string text,
            RectangleF bounds,
            StringFormat format,
            ThemeDefinition theme)
        {
            float dpiScale = IsHandleCreated
                ? Math.Max(1f, GetDpiForWindow(Handle) / 96f)
                : 1f;
            float dropOffset = 1.1f * dpiScale;
            float blurRadius = 1.35f * dpiScale;
            float diagonalRadius = blurRadius * 0.72f;
            float innerRadius = blurRadius * 0.48f;

            var shadowState = graphics.Save();
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (var outerShadowBrush = new SolidBrush(
                ThemeDrawing.WithAlpha(theme.ItemTextShadowColor, 20)))
            using (var innerShadowBrush = new SolidBrush(
                ThemeDrawing.WithAlpha(theme.ItemTextShadowColor, 30)))
            using (var centerShadowBrush = new SolidBrush(
                ThemeDrawing.WithAlpha(theme.ItemTextShadowColor, 65)))
            {
                DrawTextShadowSample(graphics, text, outerShadowBrush, bounds, format,
                    dropOffset - blurRadius, dropOffset);
                DrawTextShadowSample(graphics, text, outerShadowBrush, bounds, format,
                    dropOffset + blurRadius, dropOffset);
                DrawTextShadowSample(graphics, text, outerShadowBrush, bounds, format,
                    dropOffset, dropOffset - blurRadius);
                DrawTextShadowSample(graphics, text, outerShadowBrush, bounds, format,
                    dropOffset, dropOffset + blurRadius);
                DrawTextShadowSample(graphics, text, outerShadowBrush, bounds, format,
                    dropOffset - diagonalRadius, dropOffset - diagonalRadius);
                DrawTextShadowSample(graphics, text, outerShadowBrush, bounds, format,
                    dropOffset + diagonalRadius, dropOffset - diagonalRadius);
                DrawTextShadowSample(graphics, text, outerShadowBrush, bounds, format,
                    dropOffset - diagonalRadius, dropOffset + diagonalRadius);
                DrawTextShadowSample(graphics, text, outerShadowBrush, bounds, format,
                    dropOffset + diagonalRadius, dropOffset + diagonalRadius);

                DrawTextShadowSample(graphics, text, innerShadowBrush, bounds, format,
                    dropOffset - innerRadius, dropOffset);
                DrawTextShadowSample(graphics, text, innerShadowBrush, bounds, format,
                    dropOffset + innerRadius, dropOffset);
                DrawTextShadowSample(graphics, text, innerShadowBrush, bounds, format,
                    dropOffset, dropOffset - innerRadius);
                DrawTextShadowSample(graphics, text, innerShadowBrush, bounds, format,
                    dropOffset, dropOffset + innerRadius);
                DrawTextShadowSample(graphics, text, centerShadowBrush, bounds, format,
                    dropOffset, dropOffset);
            }
            graphics.Restore(shadowState);

            using (var textBrush = new SolidBrush(theme.ItemTextColor))
                graphics.DrawString(text, iconFont, textBrush, bounds, format);
        }

        /// <summary>在指定偏移位置绘制一次低透明度文字阴影样本。</summary>
        private void DrawTextShadowSample(
            Graphics graphics,
            string text,
            Brush brush,
            RectangleF bounds,
            StringFormat format,
            float offsetX,
            float offsetY)
        {
            graphics.DrawString(
                text,
                iconFont,
                brush,
                new RectangleF(
                    bounds.X + offsetX,
                    bounds.Y + offsetY,
                    bounds.Width,
                    bounds.Height),
                format);
        }

        /// <summary>绘制图标管理模式右下角的确认、取消按钮和选择数量提示。</summary>
        private void DrawIconManagementFooter(
            Graphics graphics,
            ThemeDefinition theme,
            int footerHeight)
        {
            if (footerHeight <= 0)
                return;

            int footerTop = Math.Max(titleHeight, Height - footerHeight);
            var footerBounds = new Rectangle(0, footerTop, Width, Height - footerTop);
            using (var footerBrush = new SolidBrush(
                ThemeDrawing.WithAlpha(theme.TitleBarColor, 220)))
            using (var separatorPen = new Pen(
                ThemeDrawing.WithAlpha(theme.BorderColor, 145)))
            {
                graphics.FillRectangle(footerBrush, footerBounds);
                graphics.DrawLine(separatorPen, 0, footerTop, Width, footerTop);
            }

            int margin = LogicalPixelsToDevice(7);
            int gap = LogicalPixelsToDevice(5);
            int buttonHeight = Math.Min(
                LogicalPixelsToDevice(30),
                Math.Max(1, footerBounds.Height - margin * 2));
            int availableWidth = Math.Max(2, Width - margin * 2 - gap);
            int cancelWidth = Math.Max(
                1,
                Math.Min(LogicalPixelsToDevice(68), availableWidth / 2));
            int confirmWidth = Math.Max(
                1,
                Math.Min(
                    LogicalPixelsToDevice(88),
                    availableWidth - cancelWidth));
            int buttonTop = footerTop + (footerBounds.Height - buttonHeight) / 2;

            iconManagementCancelBounds = new Rectangle(
                Math.Max(0, Width - margin - cancelWidth),
                buttonTop,
                cancelWidth,
                buttonHeight);
            iconManagementConfirmBounds = new Rectangle(
                Math.Max(0, iconManagementCancelBounds.Left - gap - confirmWidth),
                buttonTop,
                confirmWidth,
                buttonHeight);

            Point mouse = PointToClient(MousePosition);
            bool canConfirm = iconManagementSelection.Count > 0;
            DrawIconManagementButton(
                graphics,
                iconManagementConfirmBounds,
                ThemeText.Confirm,
                canConfirm,
                iconManagementConfirmBounds.Contains(mouse),
                true,
                theme);
            DrawIconManagementButton(
                graphics,
                iconManagementCancelBounds,
                ThemeText.Cancel,
                true,
                iconManagementCancelBounds.Contains(mouse),
                false,
                theme);

            int hintRight = iconManagementConfirmBounds.Left - margin;
            if (hintRight > margin)
            {
                string hint = iconManagementSelection.Count > 0
                    ? ThemeText.SelectedItemCount(iconManagementSelection.Count)
                    : ThemeText.IconManagementHint;
                var hintBounds = new Rectangle(
                    margin,
                    footerTop,
                    hintRight - margin,
                    footerBounds.Height);
                TextRenderer.DrawText(
                    graphics,
                    hint,
                    iconFont ?? Font,
                    hintBounds,
                    theme.ItemTextColor,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding);
            }
        }

        /// <summary>返回当前 DPI 下图标管理底栏的设备像素高度。</summary>
        private int GetIconManagementFooterHeight()
        {
            return LogicalPixelsToDevice(46);
        }

        /// <summary>按当前主题绘制图标管理底栏中的单个操作按钮。</summary>
        private void DrawIconManagementButton(
            Graphics graphics,
            Rectangle bounds,
            string text,
            bool enabled,
            bool hovered,
            bool accent,
            ThemeDefinition theme)
        {
            Color fillColor;
            if (!enabled)
            {
                fillColor = ThemeDrawing.Mix(
                    theme.MainPanelColor,
                    theme.ControlBackgroundColor,
                    0.22f);
            }
            else if (accent)
            {
                fillColor = theme.AccentColor;
            }
            else
            {
                fillColor = ThemeDrawing.Mix(
                    theme.MainPanelColor,
                    theme.ControlBackgroundColor,
                    0.45f);
            }

            if (hovered && enabled)
            {
                fillColor = ThemeDrawing.Mix(
                    fillColor,
                    ThemeDrawing.IsDark(fillColor) ? Color.White : Color.Black,
                    0.12f);
            }

            int radius = theme.CornerRadius > 0
                ? LogicalPixelsToDevice(Math.Min(8, theme.CornerRadius))
                : 0;
            using (var path = ThemeDrawing.CreateRoundedRectangle(bounds, radius))
            using (var brush = new SolidBrush(
                ThemeDrawing.WithAlpha(fillColor, enabled ? 230 : 120)))
            using (var pen = new Pen(
                ThemeDrawing.WithAlpha(theme.BorderColor, enabled ? 190 : 90)))
            {
                graphics.FillPath(brush, path);
                graphics.DrawPath(pen, path);
            }

            Color textColor = !enabled
                ? ThemeDrawing.WithAlpha(theme.ItemTextColor, 105)
                : accent
                    ? (ThemeDrawing.IsDark(fillColor) ? Color.White : Color.Black)
                    : theme.ItemTextColor;
            TextRenderer.DrawText(
                graphics,
                text,
                iconFont ?? Font,
                bounds,
                textColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
        }

        /// <summary>在管理模式下为每个图标绘制可多选的圆形勾选标记。</summary>
        private void DrawIconManagementCheck(
            Graphics graphics,
            Rectangle iconBounds,
            bool selected,
            ThemeDefinition theme)
        {
            int size = Math.Max(LogicalPixelsToDevice(14), iconSize / 3);
            var checkBounds = new Rectangle(
                iconBounds.Right - size + LogicalPixelsToDevice(2),
                iconBounds.Top - LogicalPixelsToDevice(2),
                size,
                size);
            Color fillColor = selected
                ? theme.AccentColor
                : ThemeDrawing.WithAlpha(theme.MainPanelColor, 205);
            using (var fill = new SolidBrush(fillColor))
            using (var outline = new Pen(
                selected ? theme.AccentColor : theme.BorderColor,
                Math.Max(1f, LogicalPixelsToDevice(1))))
            {
                graphics.FillEllipse(fill, checkBounds);
                graphics.DrawEllipse(outline, checkBounds);
            }

            if (!selected)
                return;

            Color checkColor = ThemeDrawing.IsDark(theme.AccentColor)
                ? Color.White
                : Color.Black;
            using (var pen = new Pen(
                checkColor,
                Math.Max(1.5f, LogicalPixelsToDevice(1))))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                graphics.DrawLines(pen, new[]
                {
                    new Point(
                        checkBounds.Left + checkBounds.Width / 4,
                        checkBounds.Top + checkBounds.Height / 2),
                    new Point(
                        checkBounds.Left + checkBounds.Width / 2 - 1,
                        checkBounds.Bottom - checkBounds.Height / 4),
                    new Point(
                        checkBounds.Right - checkBounds.Width / 5,
                        checkBounds.Top + checkBounds.Height / 4)
                });
            }
        }

        /// <summary>
        /// 使用带 Alpha 通道的位图和高质量双三次插值绘制图标，
        /// 避免 DrawIcon 放大低分辨率图标时产生明显锯齿。
        /// </summary>
        private static void DrawIconHighQuality(Graphics graphics, Icon icon, Rectangle targetRectangle)
        {
            if (icon == null)
                return;

            using (var bitmap = icon.ToBitmap())
            {
                graphics.DrawImage(bitmap, targetRectangle);
            }
        }

        /// <summary>按当前桌面风格绘制原色图标或 XP 经典选中染色图标。</summary>
        private static void DrawDesktopIconForState(
            Graphics graphics,
            Icon icon,
            Rectangle targetRectangle,
            bool selected,
            ThemeDefinition theme)
        {
            if (selected && theme.MenuStyle == ThemeMenuStyle.WindowsXp)
            {
                DrawXpSelectedIcon(graphics, icon, targetRectangle);
                return;
            }

            DrawIconHighQuality(graphics, icon, targetRectangle);
        }

        /// <summary>
        /// 将 XP 选中图标与系统 Highlight 色按 50% 混合，同时保持源 Alpha，
        /// 使透明边缘和图标轮廓仍然清晰可辨。
        /// </summary>
        private static void DrawXpSelectedIcon(
            Graphics graphics,
            Icon icon,
            Rectangle targetRectangle)
        {
            if (icon == null)
                return;

            Color highlight = SystemColors.Highlight;
            float red = highlight.R / 255f * 0.5f;
            float green = highlight.G / 255f * 0.5f;
            float blue = highlight.B / 255f * 0.5f;
            var colorMatrix = new ColorMatrix(new[]
            {
                new[] { 0.5f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0.5f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0.5f, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { red, green, blue, 0f, 1f }
            });

            using (var bitmap = icon.ToBitmap())
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(
                    colorMatrix,
                    ColorMatrixFlag.Default,
                    ColorAdjustType.Bitmap);
                graphics.DrawImage(
                    bitmap,
                    targetRectangle,
                    0,
                    0,
                    bitmap.Width,
                    bitmap.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }
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

        /// <summary>打开全局主题配置面板。</summary>
        private void themeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var dialog = new ThemeConfigurationDialog())
            {
                dialog.ShowDialog(this);
            }
        }

        /// <summary>
        /// 独立切换应用颜色模式。这里只更新 DarkModeEnabled；当前 Win11、XP
        /// 或自定义风格 ID 保持不变，由 ThemeManager 解析该风格对应的颜色变体。
        /// </summary>
        private void darkModeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ThemeManager.Instance.SetDarkMode(darkModeToolStripMenuItem.Checked);
        }

        /// <summary>窗口关闭：如果最后一个栅栏关闭则退出应用。</summary>
        private void FenceWindow_FormClosed(object sender, FormClosedEventArgs e)
        {
            ThemeManager.Instance.ThemeChanged -= ThemeManager_ThemeChanged;
            thumbnailProvider.IconThumbnailLoaded -= ThumbnailProvider_IconThumbnailLoaded;
            iconMetricsPollTimer.Stop();
            iconMetricsPollTimer.Dispose();

            backgroundImage?.Dispose();
            backgroundImage = null;
            ClearContextMenuIcons();
            titleFont?.Dispose();
            titleFont = null;
            iconFont?.Dispose();
            iconFont = null;

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
        /// - 右键悬停任意条目：显示原生 Shell 上下文菜单
        /// - Shift+右键或右键空白区域：显示应用自身的上下文菜单
        /// 图标管理模式只响应左键多选及底栏按钮，避免误触打开或右键命令。
        /// </summary>
        private void FenceWindow_MouseClick(object sender, MouseEventArgs e)
        {
            if (isIconManagementMode)
            {
                if (e.Button == MouseButtons.Left)
                    HandleIconManagementClick(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Right)
                return;

            if (hoveringItem != null && !ModifierKeys.HasFlag(Keys.Shift))
            {
                // 右键条目 → Windows Shell 右键菜单
                shellContextMenu.ShowContextMenu(new[] { new FileInfo(hoveringItem) }, MousePosition);
            }
            else
            {
                // Shift+右键或空白处 → 应用菜单
                appContextMenu.Show(this, e.Location);
            }
        }

        /// <summary>鼠标滚轮：控制栅栏内容垂直滚动。</summary>
        private void FenceWindow_MouseWheel(object sender, MouseEventArgs e)
        {
            if (scrollHeight < 1)
                return;

            scrollOffset -= Math.Sign(e.Delta) * LogicalPixelsToDevice(10);
            if (scrollOffset < 0)
                scrollOffset = 0;
            if (scrollOffset > scrollHeight)
                scrollOffset = scrollHeight;

            Invalidate();
        }

        /// <summary>缩略图异步加载完成 → 触发重绘。</summary>
        private void ThumbnailProvider_IconThumbnailLoaded(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(Invalidate));
                }
                catch (InvalidOperationException)
                {
                    // 窗口可能在后台提取完成前已关闭
                }
                return;
            }

            Invalidate();
        }

        /// <summary>检查路径是否存在（文件或文件夹）。</summary>
        private bool ItemExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }
    }

}

