using NoFences.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NoFences.Theming
{
    /// <summary>
    /// Applies dialog and menu colors recursively. Forms opt into this service
    /// instead of duplicating theme-specific conditionals in individual screens.
    /// </summary>
    public static class ThemeUi
    {
        // Fonts are shared for the lifetime of this small desktop application.
        // WinForms controls do not consistently own/dispose assigned Font objects,
        // so a cache avoids leaking a new HFONT on every live-preview update.
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

            menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
            menu.Renderer = new ThemedToolStripRenderer(theme);
            menu.BackColor = theme.MenuBackgroundColor;
            menu.ForeColor = theme.MenuTextColor;
            menu.Font = GetThemeFont(theme.FontFamilyName, menu.Font.Size, menu.Font.Style);
            ApplyToToolStripItems(menu.Items, theme);
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
                var strip = (ToolStrip)control;
                strip.Renderer = new ThemedToolStripRenderer(theme);
                strip.BackColor = theme.MenuBackgroundColor;
                strip.ForeColor = theme.MenuTextColor;
                ApplyToToolStripItems(strip.Items, theme);
            }
            else
            {
                control.BackColor = theme.DialogBackgroundColor;
            }
        }

        private static void ApplyToToolStripItems(ToolStripItemCollection items, ThemeDefinition theme)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = theme.MenuBackgroundColor;
                item.ForeColor = theme.MenuTextColor;
                item.Font = GetThemeFont(theme.FontFamilyName, item.Font.Size, item.Font.Style);
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems)
                    ApplyToToolStripItems(menuItem.DropDownItems, theme);
            }
        }

        private static Font GetThemeFont(string familyName, float size, FontStyle style)
        {
            if (string.IsNullOrWhiteSpace(familyName))
                familyName = "Segoe UI";
            string key = familyName + "|" + size + "|" + (int)style;
            lock (fontCacheLock)
            {
                if (fontCache.TryGetValue(key, out var cached))
                    return cached;
                try
                {
                    cached = new Font(familyName, size, style);
                }
                catch
                {
                    cached = new Font("Segoe UI", size, style);
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
