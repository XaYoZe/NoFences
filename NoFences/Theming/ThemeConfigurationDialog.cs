using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace NoFences.Theming
{
    /// <summary>
    /// Theme selection and custom-theme editor. The form is constructed in code
    /// because most rows are generated from theme metadata; adding another color
    /// later requires one descriptor instead of duplicated designer controls.
    /// </summary>
    public sealed class ThemeConfigurationDialog : Form
    {
        private readonly ComboBox themeComboBox = new ComboBox();
        private readonly CheckBox darkModeCheckBox = new CheckBox();
        private readonly Button copyToCustomButton = new Button();
        private readonly Button resetCustomButton = new Button();
        private readonly Button applyButton = new Button();
        private readonly Button okButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly ThemePreviewControl previewControl = new ThemePreviewControl();
        private readonly NumericUpDown cornerRadiusInput = new NumericUpDown();
        private readonly NumericUpDown panelOpacityInput = new NumericUpDown();
        private readonly NumericUpDown titleOpacityInput = new NumericUpDown();
        private readonly NumericUpDown imageOpacityInput = new NumericUpDown();
        private readonly CheckBox blurCheckBox = new CheckBox();
        private readonly TextBox fontFamilyTextBox = new TextBox();
        private readonly TextBox imagePathTextBox = new TextBox();
        private readonly ComboBox imageLayoutComboBox = new ComboBox();
        private readonly Button browseImageButton = new Button();
        private readonly Button clearImageButton = new Button();
        private readonly Label customHintLabel = new Label();
        private readonly List<Control> customOnlyControls = new List<Control>();
        private readonly List<ColorEditor> colorEditors = new List<ColorEditor>();
        private readonly List<ImageLayoutChoice> imageLayoutChoices = new List<ImageLayoutChoice>();

        private ThemeDefinition customLightTheme;
        private ThemeDefinition customDarkTheme;
        private ThemeColorMode loadedEditorColorMode;
        private bool isLoadingEditors;

        public ThemeConfigurationDialog()
        {
            customLightTheme = ThemeManager.Instance.CustomLightTheme;
            customDarkTheme = ThemeManager.Instance.CustomDarkTheme;
            InitializeDialog();
            isLoadingEditors = true;
            darkModeCheckBox.Checked = ThemeManager.Instance.DarkModeEnabled;
            isLoadingEditors = false;
            PopulateThemeChoices();
            LoadSelectedTheme();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // The native title-bar handle does not exist during construction.
            // Re-applying once shown lets ThemeUi update its dark/light DWM state.
            ApplyPreviewTheme(GetSelectedDefinition(), darkModeCheckBox.Checked);
        }

        private string T(string chinese, string english)
        {
            return ThemeText.Get(chinese, english);
        }

        private void InitializeDialog()
        {
            Text = T("主题风格", "Theme");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(780, 700);
            MinimumSize = new Size(680, 600);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            previewControl.Dock = DockStyle.Fill;
            previewControl.Margin = new Padding(0, 12, 0, 12);
            root.Controls.Add(previewControl, 0, 1);
            root.Controls.Add(BuildEditorTabs(), 0, 2);
            root.Controls.Add(BuildActionBar(), 0, 3);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private Control BuildHeader()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 2,
                Margin = Padding.Empty
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var label = new Label
            {
                Text = T("主题：", "Theme:"),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 7, 8, 0)
            };
            header.Controls.Add(label, 0, 0);

            themeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            themeComboBox.Dock = DockStyle.Fill;
            themeComboBox.Margin = new Padding(0, 2, 8, 2);
            themeComboBox.SelectedIndexChanged += ThemeComboBox_SelectedIndexChanged;
            header.Controls.Add(themeComboBox, 1, 0);

            copyToCustomButton.Text = T("基于此主题自定义", "Customize this theme");
            copyToCustomButton.AutoSize = true;
            copyToCustomButton.Click += CopyToCustomButton_Click;
            header.Controls.Add(copyToCustomButton, 2, 0);

            // Color mode is intentionally a separate row/control, not another
            // entry in the theme list. This keeps style identity (Win11/XP/custom)
            // independent from the application-wide Light/Dark choice.
            darkModeCheckBox.Text = T(
                "黑暗模式（独立于主题风格）",
                "Dark mode (independent of theme style)");
            darkModeCheckBox.AutoSize = true;
            darkModeCheckBox.Margin = new Padding(0, 8, 0, 0);
            darkModeCheckBox.CheckedChanged += DarkModeCheckBox_CheckedChanged;
            header.Controls.Add(darkModeCheckBox, 0, 1);
            header.SetColumnSpan(darkModeCheckBox, 3);
            return header;
        }

        private Control BuildEditorTabs()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var appearancePage = new TabPage(T("外观", "Appearance"));
            var colorPage = new TabPage(T("颜色", "Colors"));
            var imagePage = new TabPage(T("背景图片", "Background image"));
            tabs.TabPages.Add(appearancePage);
            tabs.TabPages.Add(colorPage);
            tabs.TabPages.Add(imagePage);

            appearancePage.Controls.Add(BuildAppearanceEditor());
            colorPage.Controls.Add(BuildColorEditor());
            imagePage.Controls.Add(BuildImageEditor());
            return tabs;
        }

        private Control BuildAppearanceEditor()
        {
            var layout = CreateTwoColumnEditor();
            AddEditorRow(layout, T("字体名称", "Font family"), fontFamilyTextBox);
            AddEditorRow(layout, T("圆角半径 (px)", "Corner radius (px)"), ConfigureNumber(cornerRadiusInput, 0, 48, " px"));
            AddEditorRow(layout, T("主面板不透明度 (%)", "Panel opacity (%)"), ConfigureNumber(panelOpacityInput, 20, 100, "%"));
            AddEditorRow(layout, T("标题栏不透明度 (%)", "Title opacity (%)"), ConfigureNumber(titleOpacityInput, 20, 100, "%"));

            blurCheckBox.Text = T("启用背景模糊（透明主题推荐）", "Enable background blur (recommended for transparent themes)");
            blurCheckBox.AutoSize = true;
            AddSpanningRow(layout, blurCheckBox);

            customHintLabel.AutoSize = true;
            customHintLabel.Text = T(
                "预置主题只读；自定义主题会分别保存浅色与黑暗模式配置。",
                "Presets are read-only. Custom saves separate Light and Dark variants.");
            customHintLabel.Margin = new Padding(3, 14, 3, 3);
            AddSpanningRow(layout, customHintLabel);

            fontFamilyTextBox.TextChanged += CustomValueChanged;
            cornerRadiusInput.ValueChanged += CustomValueChanged;
            panelOpacityInput.ValueChanged += CustomValueChanged;
            titleOpacityInput.ValueChanged += CustomValueChanged;
            blurCheckBox.CheckedChanged += CustomValueChanged;

            AddCustomControls(fontFamilyTextBox, cornerRadiusInput, panelOpacityInput,
                titleOpacityInput, blurCheckBox);
            return WrapEditor(layout);
        }

        private Control BuildColorEditor()
        {
            var layout = CreateTwoColumnEditor();
            AddColorEditor(layout, T("主面板", "Main panel"),
                t => t.MainPanelColorArgb, (t, value) => t.MainPanelColorArgb = value);
            AddColorEditor(layout, T("标题栏", "Title bar"),
                t => t.TitleBarColorArgb, (t, value) => t.TitleBarColorArgb = value);
            AddColorEditor(layout, T("标题文字", "Title text"),
                t => t.TitleTextColorArgb, (t, value) => t.TitleTextColorArgb = value);
            AddColorEditor(layout, T("项目文字", "Item text"),
                t => t.ItemTextColorArgb, (t, value) => t.ItemTextColorArgb = value);
            AddColorEditor(layout, T("项目文字阴影", "Item text shadow"),
                t => t.ItemTextShadowColorArgb, (t, value) => t.ItemTextShadowColorArgb = value);
            AddColorEditor(layout, T("项目悬停", "Item hover"),
                t => t.ItemHoverColorArgb, (t, value) => t.ItemHoverColorArgb = value);
            AddColorEditor(layout, T("项目选中", "Item selected"),
                t => t.ItemSelectedColorArgb, (t, value) => t.ItemSelectedColorArgb = value);
            AddColorEditor(layout, T("边框", "Border"),
                t => t.BorderColorArgb, (t, value) => t.BorderColorArgb = value);
            AddColorEditor(layout, T("滚动条", "Scroll bar"),
                t => t.ScrollBarColorArgb, (t, value) => t.ScrollBarColorArgb = value);
            AddColorEditor(layout, T("菜单背景", "Menu background"),
                t => t.MenuBackgroundColorArgb, (t, value) => t.MenuBackgroundColorArgb = value);
            AddColorEditor(layout, T("菜单文字", "Menu text"),
                t => t.MenuTextColorArgb, (t, value) => t.MenuTextColorArgb = value);
            AddColorEditor(layout, T("菜单悬停", "Menu highlight"),
                t => t.MenuHighlightColorArgb, (t, value) => t.MenuHighlightColorArgb = value);
            AddColorEditor(layout, T("菜单悬停文字", "Menu highlight text"),
                t => t.MenuHighlightTextColorArgb, (t, value) => t.MenuHighlightTextColorArgb = value);
            AddColorEditor(layout, T("设置页背景", "Settings background"),
                t => t.DialogBackgroundColorArgb, (t, value) => t.DialogBackgroundColorArgb = value);
            AddColorEditor(layout, T("设置页文字", "Settings text"),
                t => t.DialogTextColorArgb, (t, value) => t.DialogTextColorArgb = value);
            AddColorEditor(layout, T("输入控件背景", "Control background"),
                t => t.ControlBackgroundColorArgb, (t, value) => t.ControlBackgroundColorArgb = value);
            AddColorEditor(layout, T("输入控件文字", "Control text"),
                t => t.ControlTextColorArgb, (t, value) => t.ControlTextColorArgb = value);
            AddColorEditor(layout, T("强调色", "Accent"),
                t => t.AccentColorArgb, (t, value) => t.AccentColorArgb = value);
            return WrapEditor(layout);
        }

        private Control BuildImageEditor()
        {
            var layout = CreateTwoColumnEditor();
            var pathPanel = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                Margin = Padding.Empty
            };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            imagePathTextBox.Dock = DockStyle.Fill;
            browseImageButton.Text = T("浏览...", "Browse...");
            clearImageButton.Text = T("清除", "Clear");
            browseImageButton.AutoSize = true;
            clearImageButton.AutoSize = true;
            pathPanel.Controls.Add(imagePathTextBox, 0, 0);
            pathPanel.Controls.Add(browseImageButton, 1, 0);
            pathPanel.Controls.Add(clearImageButton, 2, 0);
            AddEditorRow(layout, T("图片文件", "Image file"), pathPanel);

            imageLayoutComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            imageLayoutComboBox.Dock = DockStyle.Fill;
            imageLayoutChoices.Add(new ImageLayoutChoice(ThemeImageLayout.Fill, T("填充（裁剪）", "Fill (crop)")));
            imageLayoutChoices.Add(new ImageLayoutChoice(ThemeImageLayout.Fit, T("适应（完整）", "Fit (contain)")));
            imageLayoutChoices.Add(new ImageLayoutChoice(ThemeImageLayout.Stretch, T("拉伸", "Stretch")));
            imageLayoutChoices.Add(new ImageLayoutChoice(ThemeImageLayout.Center, T("居中", "Center")));
            imageLayoutChoices.Add(new ImageLayoutChoice(ThemeImageLayout.Tile, T("平铺", "Tile")));
            foreach (var choice in imageLayoutChoices)
                imageLayoutComboBox.Items.Add(choice);
            AddEditorRow(layout, T("显示方式", "Layout"), imageLayoutComboBox);
            AddEditorRow(layout, T("图片不透明度 (%)", "Image opacity (%)"), ConfigureNumber(imageOpacityInput, 0, 100, "%"));

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                Text = T(
                    "图片路径会保存到主题配置中；读取后不会锁定原文件，文件缺失时自动回退为纯色背景。",
                    "The path is saved in the theme. The source file is not locked; a missing image falls back to the solid background."),
                Margin = new Padding(3, 14, 3, 3)
            };
            AddSpanningRow(layout, note);

            browseImageButton.Click += BrowseImageButton_Click;
            clearImageButton.Click += (sender, args) => imagePathTextBox.Clear();
            imagePathTextBox.TextChanged += CustomValueChanged;
            imageLayoutComboBox.SelectedIndexChanged += CustomValueChanged;
            imageOpacityInput.ValueChanged += CustomValueChanged;
            AddCustomControls(imagePathTextBox, browseImageButton, clearImageButton,
                imageLayoutComboBox, imageOpacityInput);
            return WrapEditor(layout);
        }

        private Control BuildActionBar()
        {
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 12, 0, 0)
            };

            cancelButton.Text = T("取消", "Cancel");
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.AutoSize = true;
            okButton.Text = T("确定", "OK");
            okButton.AutoSize = true;
            okButton.Click += OkButton_Click;
            applyButton.Text = T("应用", "Apply");
            applyButton.AutoSize = true;
            applyButton.Click += ApplyButton_Click;
            resetCustomButton.Text = T("恢复自定义默认值", "Reset custom defaults");
            resetCustomButton.AutoSize = true;
            resetCustomButton.Click += ResetCustomButton_Click;

            actions.Controls.Add(cancelButton);
            actions.Controls.Add(okButton);
            actions.Controls.Add(applyButton);
            actions.Controls.Add(resetCustomButton);
            return actions;
        }

        private TableLayoutPanel CreateTwoColumnEditor()
        {
            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Padding = new Padding(14)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private Control WrapEditor(Control editor)
        {
            var scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scrollPanel.Controls.Add(editor);
            return scrollPanel;
        }

        private NumericUpDown ConfigureNumber(NumericUpDown input, int minimum, int maximum, string suffix)
        {
            input.Minimum = minimum;
            input.Maximum = maximum;
            input.DecimalPlaces = 0;
            input.Increment = 1;
            input.Width = 140;
            input.Anchor = AnchorStyles.Left;
            input.TextAlign = HorizontalAlignment.Right;
            // NumericUpDown has no native suffix. A concise accessible name keeps
            // units understandable without owner-drawing the edit control.
            input.AccessibleDescription = suffix;
            return input;
        }

        private void AddEditorRow(TableLayoutPanel layout, string labelText, Control editor)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 9, 12, 8)
            };
            editor.Margin = new Padding(3, 5, 3, 5);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(editor, 1, row);
        }

        private void AddSpanningRow(TableLayoutPanel layout, Control control)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin = new Padding(3, 7, 3, 7);
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 2);
        }

        private void AddColorEditor(
            TableLayoutPanel layout,
            string label,
            Func<ThemeDefinition, int> getter,
            Action<ThemeDefinition, int> setter)
        {
            var editor = new ColorEditor(label, getter, setter);
            editor.Button.Width = 180;
            editor.Button.Height = 28;
            editor.Button.Tag = editor;
            editor.Button.Click += ColorButton_Click;
            colorEditors.Add(editor);
            customOnlyControls.Add(editor.Button);
            AddEditorRow(layout, label, editor.Button);
        }

        private void AddCustomControls(params Control[] controls)
        {
            customOnlyControls.AddRange(controls);
        }

        private void PopulateThemeChoices()
        {
            foreach (var provider in ThemeManager.Instance.GetThemeProviders())
                themeComboBox.Items.Add(new ThemeChoice(provider.Id, provider.DisplayName));
            themeComboBox.Items.Add(new ThemeChoice(ThemeIds.Custom, T("自定义", "Custom")));

            string selectedId = ThemeManager.Instance.SelectedThemeId;
            for (int i = 0; i < themeComboBox.Items.Count; i++)
            {
                var choice = (ThemeChoice)themeComboBox.Items[i];
                if (string.Equals(choice.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    themeComboBox.SelectedIndex = i;
                    break;
                }
            }
            if (themeComboBox.SelectedIndex < 0)
                themeComboBox.SelectedIndex = 0;
        }

        private void LoadSelectedTheme()
        {
            ThemeColorMode colorMode = GetSelectedColorMode();
            var theme = GetSelectedDefinition(colorMode);
            bool isCustom = IsCustomSelected();
            isLoadingEditors = true;
            try
            {
                loadedEditorColorMode = colorMode;
                fontFamilyTextBox.Text = theme.FontFamilyName;
                cornerRadiusInput.Value = theme.CornerRadius;
                panelOpacityInput.Value = theme.MainPanelOpacityPercent;
                titleOpacityInput.Value = theme.TitleBarOpacityPercent;
                blurCheckBox.Checked = theme.EnableBlur;
                imagePathTextBox.Text = theme.BackgroundImagePath;
                imageOpacityInput.Value = theme.BackgroundImageOpacityPercent;

                for (int i = 0; i < imageLayoutChoices.Count; i++)
                {
                    if (imageLayoutChoices[i].Value == theme.BackgroundImageLayout)
                    {
                        imageLayoutComboBox.SelectedIndex = i;
                        break;
                    }
                }

                foreach (var editor in colorEditors)
                    UpdateColorButton(editor, theme);
            }
            finally
            {
                isLoadingEditors = false;
            }

            foreach (var control in customOnlyControls)
                control.Enabled = isCustom;
            resetCustomButton.Enabled = isCustom;
            copyToCustomButton.Enabled = !isCustom;
            customHintLabel.Visible = !isCustom;
            ApplyPreviewTheme(theme, colorMode == ThemeColorMode.Dark);
        }

        private ThemeDefinition GetSelectedDefinition()
        {
            return GetSelectedDefinition(GetSelectedColorMode());
        }

        private ThemeDefinition GetSelectedDefinition(ThemeColorMode colorMode)
        {
            var choice = themeComboBox.SelectedItem as ThemeChoice;
            if (choice == null || string.Equals(choice.Id, ThemeIds.Custom, StringComparison.OrdinalIgnoreCase))
                return GetCustomTheme(colorMode).Clone();
            return ThemeManager.Instance.GetTheme(choice.Id, colorMode);
        }

        private ThemeColorMode GetSelectedColorMode()
        {
            return darkModeCheckBox.Checked ? ThemeColorMode.Dark : ThemeColorMode.Light;
        }

        private ThemeDefinition GetCustomTheme(ThemeColorMode colorMode)
        {
            return colorMode == ThemeColorMode.Dark
                ? customDarkTheme
                : customLightTheme;
        }

        private bool IsCustomSelected()
        {
            var choice = themeComboBox.SelectedItem as ThemeChoice;
            return choice != null && string.Equals(choice.Id, ThemeIds.Custom, StringComparison.OrdinalIgnoreCase);
        }

        private void WriteControlsToCustomTheme()
        {
            if (isLoadingEditors || !IsCustomSelected())
                return;

            // loadedEditorColorMode identifies which custom variant the visible
            // controls represent. This avoids writing light values into the dark
            // variant (or vice versa) while the independent switch is changing.
            var customTheme = GetCustomTheme(loadedEditorColorMode);
            customTheme.FontFamilyName = fontFamilyTextBox.Text.Trim();
            customTheme.CornerRadius = (int)cornerRadiusInput.Value;
            customTheme.MainPanelOpacityPercent = (int)panelOpacityInput.Value;
            customTheme.TitleBarOpacityPercent = (int)titleOpacityInput.Value;
            customTheme.EnableBlur = blurCheckBox.Checked;
            customTheme.BackgroundImagePath = imagePathTextBox.Text.Trim();
            customTheme.BackgroundImageOpacityPercent = (int)imageOpacityInput.Value;
            var layout = imageLayoutComboBox.SelectedItem as ImageLayoutChoice;
            if (layout != null)
                customTheme.BackgroundImageLayout = layout.Value;
            customTheme.Normalize();
        }

        private void ApplyPreviewTheme(ThemeDefinition theme, bool darkModeEnabled)
        {
            ThemeUi.ApplyToForm(this, theme, darkModeEnabled);
            // ThemeUi gives ordinary buttons control colors. Color swatches must be
            // restored afterwards so they continue to represent their actual values.
            foreach (var editor in colorEditors)
                UpdateColorButton(editor, theme);
            previewControl.Theme = theme;
        }

        private void UpdateColorButton(ColorEditor editor, ThemeDefinition theme)
        {
            Color color = Color.FromArgb(editor.Getter(theme));
            editor.Button.UseVisualStyleBackColor = false;
            editor.Button.BackColor = color;
            editor.Button.ForeColor = ThemeDrawing.IsDark(color) ? Color.White : Color.Black;
            editor.Button.Text = string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private void ThemeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!isLoadingEditors)
                LoadSelectedTheme();
        }

        private void DarkModeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoadingEditors)
                LoadSelectedTheme();
        }

        private void CustomValueChanged(object sender, EventArgs e)
        {
            if (isLoadingEditors || !IsCustomSelected())
                return;
            WriteControlsToCustomTheme();
            var customTheme = GetCustomTheme(loadedEditorColorMode);
            ApplyPreviewTheme(customTheme, loadedEditorColorMode == ThemeColorMode.Dark);
        }

        private void ColorButton_Click(object sender, EventArgs e)
        {
            if (!IsCustomSelected())
                return;

            var editor = ((Control)sender).Tag as ColorEditor;
            if (editor == null)
                return;
            var customTheme = GetCustomTheme(loadedEditorColorMode);
            using (var dialog = new ColorDialog
            {
                Color = Color.FromArgb(editor.Getter(customTheme)),
                FullOpen = true,
                AnyColor = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                editor.Setter(customTheme, dialog.Color.ToArgb());
            }

            UpdateColorButton(editor, customTheme);
            ApplyPreviewTheme(customTheme, loadedEditorColorMode == ThemeColorMode.Dark);
        }

        private void BrowseImageButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = T("选择主面板背景图片", "Select main-panel background image"),
                Filter = T(
                    "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
                    "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*"),
                CheckFileExists = true,
                Multiselect = false
            })
            {
                if (File.Exists(imagePathTextBox.Text))
                    dialog.InitialDirectory = Path.GetDirectoryName(imagePathTextBox.Text);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    imagePathTextBox.Text = dialog.FileName;
            }
        }

        private void CopyToCustomButton_Click(object sender, EventArgs e)
        {
            var choice = themeComboBox.SelectedItem as ThemeChoice;
            if (choice == null || string.Equals(choice.Id, ThemeIds.Custom, StringComparison.OrdinalIgnoreCase))
                return;

            // Copy both variants together. The user can switch modes later and
            // continue editing without losing the other mode's palette.
            customLightTheme = ThemeManager.Instance.GetTheme(choice.Id, ThemeColorMode.Light);
            customDarkTheme = ThemeManager.Instance.GetTheme(choice.Id, ThemeColorMode.Dark);
            customLightTheme.Name = "Custom";
            customDarkTheme.Name = "Custom";
            SelectThemeChoice(ThemeIds.Custom);
            LoadSelectedTheme();
        }

        private void ResetCustomButton_Click(object sender, EventArgs e)
        {
            customLightTheme = ThemePresets.CreateDefaultCustom(ThemeColorMode.Light);
            customDarkTheme = ThemePresets.CreateDefaultCustom(ThemeColorMode.Dark);
            LoadSelectedTheme();
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            CommitSelection();
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            CommitSelection();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void CommitSelection()
        {
            WriteControlsToCustomTheme();
            var choice = themeComboBox.SelectedItem as ThemeChoice;
            string id = choice != null ? choice.Id : ThemeIds.Windows11;
            ThemeManager.Instance.ApplySelection(
                id,
                customLightTheme,
                customDarkTheme,
                darkModeCheckBox.Checked);
            ApplyPreviewTheme(GetSelectedDefinition(), darkModeCheckBox.Checked);
        }

        private void SelectThemeChoice(string id)
        {
            isLoadingEditors = true;
            try
            {
                for (int i = 0; i < themeComboBox.Items.Count; i++)
                {
                    var choice = (ThemeChoice)themeComboBox.Items[i];
                    if (string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        themeComboBox.SelectedIndex = i;
                        return;
                    }
                }
            }
            finally
            {
                isLoadingEditors = false;
            }
        }

        private sealed class ThemeChoice
        {
            public ThemeChoice(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public override string ToString() => DisplayName;
        }

        private sealed class ImageLayoutChoice
        {
            public ImageLayoutChoice(ThemeImageLayout value, string displayName)
            {
                Value = value;
                DisplayName = displayName;
            }

            public ThemeImageLayout Value { get; }
            public string DisplayName { get; }
            public override string ToString() => DisplayName;
        }

        private sealed class ColorEditor
        {
            public ColorEditor(
                string displayName,
                Func<ThemeDefinition, int> getter,
                Action<ThemeDefinition, int> setter)
            {
                DisplayName = displayName;
                Getter = getter;
                Setter = setter;
                Button = new Button();
            }

            public string DisplayName { get; }
            public Func<ThemeDefinition, int> Getter { get; }
            public Action<ThemeDefinition, int> Setter { get; }
            public Button Button { get; }
        }
    }

    /// <summary>
    /// Compact, non-interactive preview showing the fence and context-menu palette.
    /// It intentionally uses the same ThemeDrawing helpers as the real fence.
    /// </summary>
    internal sealed class ThemePreviewControl : Control
    {
        private ThemeDefinition theme = ThemePresets.CreateWindows11();
        private Image backgroundImage;

        public ThemePreviewControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            MinimumSize = new Size(420, 150);
        }

        public ThemeDefinition Theme
        {
            get => theme.Clone();
            set
            {
                theme = value != null ? value.Clone() : ThemePresets.CreateWindows11();
                backgroundImage?.Dispose();
                backgroundImage = ThemeDrawing.LoadImageWithoutLock(theme.BackgroundImagePath);
                BackColor = theme.DialogBackgroundColor;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int menuWidth = Math.Min(190, Width / 3);
            var fenceBounds = new Rectangle(8, 8, Math.Max(220, Width - menuWidth - 28), Height - 16);
            var menuBounds = new Rectangle(fenceBounds.Right + 10, 24, menuWidth, Math.Min(132, Height - 32));
            DrawFence(e.Graphics, fenceBounds);
            DrawMenu(e.Graphics, menuBounds);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                backgroundImage?.Dispose();
            base.Dispose(disposing);
        }

        private void DrawFence(Graphics graphics, Rectangle bounds)
        {
            using (var path = ThemeDrawing.CreateRoundedRectangle(bounds, theme.CornerRadius))
            {
                using (var shadow = new SolidBrush(Color.FromArgb(45, Color.Black)))
                using (var shadowPath = ThemeDrawing.CreateRoundedRectangle(
                    new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width, bounds.Height), theme.CornerRadius))
                    graphics.FillPath(shadow, shadowPath);

                var state = graphics.Save();
                graphics.SetClip(path);
                using (var background = new SolidBrush(ThemeDrawing.WithOpacity(
                    theme.MainPanelColor, theme.MainPanelOpacityPercent)))
                    graphics.FillRectangle(background, bounds);
                ThemeDrawing.DrawBackgroundImage(graphics, backgroundImage, bounds,
                    theme.BackgroundImageLayout, theme.BackgroundImageOpacityPercent);

                var titleBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, 34);
                using (var titleBrush = new SolidBrush(ThemeDrawing.WithOpacity(
                    theme.TitleBarColor, theme.TitleBarOpacityPercent)))
                    graphics.FillRectangle(titleBrush, titleBounds);
                using (var titleFont = CreateThemeFont(11f, FontStyle.Bold))
                using (var titleText = new SolidBrush(theme.TitleTextColor))
                    graphics.DrawString(ThemeText.Get("工作区", "Workspace"), titleFont, titleText,
                        new RectangleF(titleBounds.X + 12, titleBounds.Y + 7, titleBounds.Width - 24, 22));

                DrawPreviewItem(graphics, bounds.X + 24, bounds.Y + 58, false, ThemeText.Get("文档", "Docs"));
                DrawPreviewItem(graphics, bounds.X + 116, bounds.Y + 58, true, ThemeText.Get("图片", "Images"));
                DrawPreviewItem(graphics, bounds.X + 208, bounds.Y + 58, false, ThemeText.Get("项目", "Project"));
                graphics.Restore(state);

                using (var borderPen = new Pen(ThemeDrawing.WithAlpha(theme.BorderColor, 150)))
                    graphics.DrawPath(borderPen, path);
            }
        }

        private void DrawPreviewItem(Graphics graphics, int x, int y, bool selected, string text)
        {
            var itemBounds = new Rectangle(x - 8, y - 5, 78, 70);
            if (selected)
            {
                using (var selectedBrush = new SolidBrush(ThemeDrawing.WithAlpha(theme.ItemSelectedColor, 125)))
                    graphics.FillRectangle(selectedBrush, itemBounds);
                using (var borderPen = new Pen(ThemeDrawing.WithAlpha(theme.BorderColor, 180)))
                    graphics.DrawRectangle(borderPen, itemBounds);
            }

            using (var folderBrush = new SolidBrush(theme.AccentColor))
                graphics.FillRectangle(folderBrush, x + 10, y, 34, 28);
            using (var font = CreateThemeFont(8.5f, FontStyle.Regular))
            using (var shadow = new SolidBrush(ThemeDrawing.WithAlpha(theme.ItemTextShadowColor, 150)))
            using (var foreground = new SolidBrush(theme.ItemTextColor))
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                var textBounds = new RectangleF(x - 5, y + 36, 65, 22);
                graphics.DrawString(text, font, shadow,
                    new RectangleF(textBounds.X + 1, textBounds.Y + 1, textBounds.Width, textBounds.Height),
                    format);
                graphics.DrawString(text, font, foreground, textBounds, format);
            }
        }

        private void DrawMenu(Graphics graphics, Rectangle bounds)
        {
            using (var background = new SolidBrush(theme.MenuBackgroundColor))
                graphics.FillRectangle(background, bounds);
            using (var border = new Pen(theme.BorderColor))
                graphics.DrawRectangle(border, bounds);

            string[] items =
            {
                ThemeText.Get("锁定", "Lock"),
                ThemeText.Get("主题风格...", "Theme..."),
                ThemeText.Get("新建桌面分区", "New fence")
            };
            using (var font = CreateThemeFont(9f, FontStyle.Regular))
            {
                for (int i = 0; i < items.Length; i++)
                {
                    var itemBounds = new Rectangle(bounds.X + 3, bounds.Y + 6 + i * 36, bounds.Width - 6, 30);
                    bool highlighted = i == 1;
                    if (highlighted)
                    {
                        using (var highlight = new SolidBrush(theme.MenuHighlightColor))
                            graphics.FillRectangle(highlight, itemBounds);
                    }
                    Color textColor = highlighted ? theme.MenuHighlightTextColor : theme.MenuTextColor;
                    TextRenderer.DrawText(graphics, items[i], font, itemBounds, textColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private Font CreateThemeFont(float size, FontStyle style)
        {
            try
            {
                return new Font(theme.FontFamilyName, size, style);
            }
            catch
            {
                return new Font("Segoe UI", size, style);
            }
        }
    }
}
