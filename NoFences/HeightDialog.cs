using System;
using System.Windows.Forms;

namespace NoFences
{
    /// <summary>
    /// 标题栏高度调整对话框。
    /// 通过 TrackBar 滑块调整栅栏标题栏的高度（20~100px）。
    /// </summary>
    public partial class HeightDialog : Form
    {
        /// <summary>用户选择的标题栏高度（逻辑像素）</summary>
        public int TitleHeight => trackBarTitleHeight.Value;

        public HeightDialog(int val)
        {
            InitializeComponent();
            trackBarTitleHeight.Value = val;
            UpdateText();
        }

        /// <summary>更新显示文本为当前值 + "px"。</summary>
        private void UpdateText()
        {
            labelTitleHeight.Text = trackBarTitleHeight.Value + "px";
        }

        private void trackBarTitleHeight_Scroll(object sender, EventArgs e)
        {
            UpdateText();
        }

        /// <summary>恢复默认高度 35px。</summary>
        private void btnRestore_Click(object sender, EventArgs e)
        {
            trackBarTitleHeight.Value = 35;
            UpdateText();
        }

        private void HeightDialog_Load(object sender, EventArgs e)
        {

        }
    }
}
