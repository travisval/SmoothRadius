using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SmoothRadiusAddin
{
    public partial class SmoothDialog : Form
    {
        private double InitialSizeDiff;

        public SmoothDialog()
        {
            InitializeComponent();

            ((System.Windows.Controls.UserControl)elementHost1.Child).SizeChanged += new System.Windows.SizeChangedEventHandler(SmoothDialog_SizeChanged);

            InitialSizeDiff = ((double)this.Height) - elementHost1.Size.Height;
        }

        void SmoothDialog_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            this.Height = (int)(elementHost1.Size.Height + InitialSizeDiff);
            this.Refresh();
        }

        internal void SetContext(SmoothContext context)
        {
            ((System.Windows.Controls.UserControl)elementHost1.Child).DataContext = context;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.Close();
        }
    }
}
