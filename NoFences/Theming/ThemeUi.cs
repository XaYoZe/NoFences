using NoFences.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NoFences.Theming
{
    /// <summary>
    /// 递归应用设置窗口与菜单主题。窗口只依赖本服务，不需要分别判断
    /// Default、Windows 11 或 Windows XP，从而避免各页面出现不同步的主题逻辑。
    /// </summary>
    public static class ThemeUi
    {
        // 字体在这个轻量桌面程序的整个生命周期内共享。WinForms 控件对外部
        // Font 的所有权不一致，缓存可以避免实时预览每次刷新都泄漏一个 HFONT。
        private static readonly object fontCacheLock = new object();
        private static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();

        public static void ApplyToForm(Form form, ThemeDefinition theme)
        {
            ApplyToForm(form, theme, ThemeManager.Instance.DarkModeEnabled);
        }

        /// <summary>
        /// Applies a palette and an explicit color mode. The overload is needed by
        /// the configuration preview: its switch may differ from the committed
        /// application setting until the user presses Apply or OK.
        /// </summary>
        public static void ApplyToForm(Form form, ThemeDefinition theme, bool darkModeEnabled)
        {
            if (form == null || theme == null)
                return;

            form.BackColor = theme.DialogBackgroundColor;
            form.ForeColor = theme.DialogTextColor;
            form.Font = GetThemeFont(theme.FontFamilyName, form.Font.Size, form.Font.Style);
            ApplyToChildren(form.Controls, theme);

            if (form.IsHandleCreated)
                WindowUtil.TrySetImmersiveDarkMode(form.Handle, darkModeEnabled);
            form.Invalidate(true);
        }

        public static void ApplyToContextMenu(ContextMenuStrip menu, ThemeDefinition theme)
        {
            if (menu == null || theme == null)
                return;

            // Renderer 属性会把 RenderMode 切换为 Custom。不能在这里使用
            // ManagerRenderMode，否则菜单可能重新落回全局 ProfessionalRenderer，
            // 从而在主题切换后仍显示 Windows/设计器的默认颜色。
            ApplyToToolStrip(menu, theme, new ThemedToolStripRenderer(theme));
        }

        private static void ApplyToChildren(Control.ControlCollection controls, ThemeDefinition theme)
        {
            foreach (Control control in controls)
            {
                ApplyToControl(control, theme);
                if (control.HasChildren)
                    ApplyToChildren(control.Controls, theme);
            }
        }

        private static void ApplyToControl(Control control, ThemeDefinition theme)
        {
            control.ForeColor = theme.DialogTextColor;
            control.Font = GetThemeFont(theme.FontFamilyName, control.Font.Size, control.Font.Style);

            if (control is TextBoxBase)
            {
                control.BackColor = theme.ControlBackgroundColor;
                control.ForeColor = theme.ControlTextColor;
                ((TextBoxBase)control).BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is ComboBox)
            {
                control.BackColor = theme.ControlBackgroundColor;
                control.ForeColor = theme.ControlTextColor;
                ((ComboBox)control).FlatStyle = FlatStyle.Flat;
            }
            else if (control is NumericUpDown)
            {
                control.BackColor = theme.ControlBackgroundColor;
                control.ForeColor = theme.ControlTextColor;
                ((NumericUpDown)control).BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is ListBox || control is CheckedListBox)
            {
                control.BackColor = theme.ControlBackgroundColor;
                control.ForeColor = theme.ControlTextColor;
            }
            else if (control is Button)
            {
                var button = (Button)control;
                button.UseVisualStyleBackColor = false;
                button.BackColor = ThemeDrawing.Mix(theme.ControlBackgroundColor, theme.DialogBackgroundColor, 0.2f);
                button.ForeColor = theme.ControlTextColor;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = theme.BorderColor;
                button.FlatAppearance.MouseOverBackColor = theme.MenuHighlightColor;
                button.FlatAppearance.MouseDownBackColor = ThemeDrawing.Mix(theme.MenuHighlightColor, theme.AccentColor, 0.35f);
            }
            else if (control is TabControl || control is TabPage || control is Panel ||
                     control is TableLayoutPanel || control is FlowLayoutPanel || control is GroupBox)
            {
                control.BackColor = theme.DialogBackgroundColor;
                control.ForeColor = theme.DialogTextColor;
            }
            else if (control is TrackBar || control is CheckBox || control is RadioButton || control is Label)
            {
                // Some native WinForms controls (notably TrackBar) reject
                // Color.Transparent. Matching the parent color is visually
                // equivalent and works consistently on .NET Framework 4.8.
                control.BackColor = theme.DialogBackgroundColor;
                control.ForeColor = theme.DialogTextColor;
            }
            else if (control is ToolStrip)
            {
                ApplyToToolStrip(
                    (ToolStrip)control,
                    theme,
                    new ThemedToolStripRenderer(theme));
            }
            else
            {
                control.BackColor = theme.DialogBackgroundColor;
            }
        }

        /// <summary>
        /// 将同一个主题渲染器应用到工具栏及其所有下拉层级。
        /// ToolStripMenuItem.DropDown 本身是一个独立 ToolStrip；只修改顶层菜单
        /// 不会可靠地覆盖子菜单的背景、边框和悬停状态，因此必须递归处理。
        /// </summary>
        private static void ApplyToToolStrip(
            ToolStrip strip,
            ThemeDefinition theme,
            ThemedToolStripRenderer renderer)
        {
            MenuThemeProfile profile = renderer.Profile;
            bool isDropDown = strip is ToolStripDropDown;
            Padding menuPadding = ScalePadding(strip, profile.MenuPadding);
            Padding itemPadding = ScalePadding(strip, profile.ItemPadding);
            int minimumWidth = ScaleLogical(strip, profile.MinimumWidth);
            int itemHeight = ScaleLogical(strip, profile.ItemHeight);
            int separatorHeight = ScaleLogical(strip, profile.SeparatorHeight);

            strip.SuspendLayout();
            try
            {
                strip.Renderer = renderer;
                strip.BackColor = theme.MenuBackgroundColor;
                strip.ForeColor = theme.MenuTextColor;
                strip.Font = GetThemeFont(
                    profile.FontFamilyName,
                    profile.FontSize,
                    FontStyle.Regular,
                    theme.FontFamilyName);
                strip.ImageScalingSize = new Size(
                    ScaleLogical(strip, profile.ImageScalingSize.Width),
                    ScaleLogical(strip, profile.ImageScalingSize.Height));
                strip.GripStyle = ToolStripGripStyle.Hidden;

                if (isDropDown)
                {
                    strip.AutoSize = true;
                    strip.MinimumSize = new Size(minimumWidth, 0);
                    strip.Padding = menuPadding;

                    var dropDown = (ToolStripDropDown)strip;
                    dropDown.DropShadowEnabled = profile.DropShadowEnabled;
                    dropDown.Opacity = profile.Opacity;

                    var dropDownMenu = strip as ToolStripDropDownMenu;
                    if (dropDownMenu != null)
                    {
                        dropDownMenu.ShowImageMargin = true;
                        dropDownMenu.ShowCheckMargin = false;
                    }
                }

                ApplyToToolStripItems(
                    strip,
                    strip.Items,
                    theme,
                    renderer,
                    isDropDown,
                    itemPadding,
                    minimumWidth,
                    itemHeight,
                    separatorHeight);
            }
            finally
            {
                strip.ResumeLayout(true);
            }

            strip.PerformLayout();
            strip.Invalidate();
        }

        /// <summary>
        /// 同步菜单项本身的字体和前景色，并把主题继续传递给已创建的子菜单。
        /// </summary>
        private static void ApplyToToolStripItems(
            ToolStrip owner,
            ToolStripItemCollection items,
            ThemeDefinition theme,
            ThemedToolStripRenderer renderer,
            bool applyDropDownMetrics,
            Padding itemPadding,
            int minimumWidth,
            int itemHeight,
            int separatorHeight)
        {
            MenuThemeProfile profile = renderer.Profile;
            foreach (ToolStripItem item in items)
            {
                item.BackColor = theme.MenuBackgroundColor;
                item.ForeColor = theme.MenuTextColor;
                item.Font = GetThemeFont(
                    profile.FontFamilyName,
                    profile.FontSize,
                    item.Font.Style,
                    theme.FontFamilyName);

                if (applyDropDownMetrics)
                {
                    item.Margin = Padding.Empty;
                    if (item is ToolStripSeparator)
                    {
                        item.AutoSize = false;
                        item.Padding = Padding.Empty;
                        item.Size = new Size(
                            Math.Max(1, minimumWidth - owner.Padding.Horizontal),
                            separatorHeight);
                    }
                    else
                    {
                        // 先恢复 AutoSize 取得当前文字、快捷键和箭头的自然宽度，
                        // 再固定规格要求的行高；切换 Win11/XP 时不会继承旧尺寸。
                        item.AutoSize = true;
                        item.Padding = itemPadding;
                        Size preferred = item.GetPreferredSize(Size.Empty);
                        item.AutoSize = false;
                        item.Size = new Size(
                            Math.Max(
                                preferred.Width,
                                minimumWidth - owner.Padding.Horizontal),
                            itemHeight);
                    }
                }

                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems)
                    ApplyToToolStrip(menuItem.DropDown, theme, renderer);
            }
        }

        /// <summary>
        /// 把 Profile 中的 96-DPI 逻辑像素换算为当前菜单的设备像素。
        /// 在 Opening 阶段再次应用主题时，ContextMenuStrip 已取得所属显示器 DPI，
        /// 因而拖到不同缩放比例的显示器后仍可保持 22px/34px 的逻辑行高。
        /// </summary>
        private static int ScaleLogical(ToolStrip strip, int logicalPixels)
        {
            int dpi = strip.DeviceDpi > 0 ? strip.DeviceDpi : 96;
            return Math.Max(1, (int)Math.Round(
                logicalPixels * dpi / 96d,
                MidpointRounding.AwayFromZero));
        }

        private static Padding ScalePadding(ToolStrip strip, Padding logicalPadding)
        {
            return new Padding(
                ScaleLogicalAllowZero(strip, logicalPadding.Left),
                ScaleLogicalAllowZero(strip, logicalPadding.Top),
                ScaleLogicalAllowZero(strip, logicalPadding.Right),
                ScaleLogicalAllowZero(strip, logicalPadding.Bottom));
        }

        private static int ScaleLogicalAllowZero(ToolStrip strip, int logicalPixels)
        {
            if (logicalPixels == 0)
                return 0;
            int dpi = strip.DeviceDpi > 0 ? strip.DeviceDpi : 96;
            return Math.Max(1, (int)Math.Round(
                logicalPixels * dpi / 96d,
                MidpointRounding.AwayFromZero));
        }

        private static Font GetThemeFont(
            string familyName,
            float size,
            FontStyle style,
            string fallbackFamilyName = null)
        {
            if (string.IsNullOrWhiteSpace(familyName))
                familyName = "Segoe UI";
            if (string.IsNullOrWhiteSpace(fallbackFamilyName))
                fallbackFamilyName = "Segoe UI";
            string key = familyName + "|" + fallbackFamilyName + "|" + size + "|" + (int)style;
            lock (fontCacheLock)
            {
                if (fontCache.TryGetValue(key, out var cached))
                    return cached;
                try
                {
                    cached = new Font(familyName, size, style);

                    // GDI+ 对不存在的字体通常静默回退而不是抛异常。显式检查
                    // 实际 FontFamily，保证没有 Segoe UI Variable 的系统回退到
                    // 当前主题字体，而不是不可预测的 Microsoft Sans Serif。
                    if (!string.Equals(
                        cached.FontFamily.Name,
                        familyName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        cached.Dispose();
                        cached = new Font(fallbackFamilyName, size, style);
                    }
                }
                catch
                {
                    try
                    {
                        cached = new Font(fallbackFamilyName, size, style);
                    }
                    catch
                    {
                        cached = new Font("Segoe UI", size, style);
                    }
                }
                fontCache[key] = cached;
                return cached;
            }
        }
    }

    /// <summary>
    /// Base class for normal settings dialogs. It keeps an already-open dialog in
    /// sync when another fence changes the global theme.
    /// </summary>
    public class ThemeAwareForm : Form
    {
        public ThemeAwareForm()
        {
            ThemeManager.Instance.ThemeChanged += ThemeManager_ThemeChanged;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyApplicationTheme(ThemeManager.Instance.CurrentTheme);
        }

        protected virtual void ApplyApplicationTheme(ThemeDefinition theme)
        {
            ThemeUi.ApplyToForm(this, theme);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ThemeManager.Instance.ThemeChanged -= ThemeManager_ThemeChanged;
            base.Dispose(disposing);
        }

        private void ThemeManager_ThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ApplyApplicationTheme(ThemeManager.Instance.CurrentTheme)));
                return;
            }
            ApplyApplicationTheme(ThemeManager.Instance.CurrentTheme);
        }
    }
}
