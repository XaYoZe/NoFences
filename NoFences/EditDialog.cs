using System;
using System.Windows.Forms;

namespace NoFences
{
    /// <summary>
    /// 重命名对话框。用于修改栅栏或条目的名称。
    /// </summary>
    public partial class EditDialog : Form
    {
        /// <summary>用户输入的新名称</summary>
        public string NewName => tbName.Text;

        public EditDialog(string oldName)
        {
            InitializeComponent();
            tbName.Text = oldName; // 预填当前名称
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }
    }
}
