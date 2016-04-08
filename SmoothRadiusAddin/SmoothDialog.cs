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
        SmoothContext context;

        public SmoothDialog()
        {
            InitializeComponent();

            ((System.Windows.Controls.UserControl)elementHost1.Child).SizeChanged += new System.Windows.SizeChangedEventHandler(SmoothDialog_SizeChanged);

            InitialSizeDiff = ((double)this.Height) - elementHost1.Size.Height;

            this.Text = this.Text + String.Concat(" (", ThisAddIn.Version, ")");
        }

        void SmoothDialog_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            this.Height = (int)(elementHost1.Size.Height + InitialSizeDiff);
            this.Refresh();
        }

        internal void SetContext(SmoothContext context)
        {
            ((System.Windows.Controls.UserControl)elementHost1.Child).DataContext = this.context = context;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (context != null)
            {
                if (context.WarningCount > 0)
                {
                    if (System.Windows.Forms.DialogResult.Yes != MessageBox.Show(
                        String.Format("There are {0} warnings present in the selected segments.  Are you sure you want to apply the updates?", context.WarningCount), "Warnings Present", MessageBoxButtons.YesNo))
                    {
                        return;
                    }
                }

                if (Math.Abs(context.Value - context.Median) > (context.Median * 0.05))
                {
                    if (System.Windows.Forms.DialogResult.Yes != MessageBox.Show(
                        "The current value is more than 5% different than the current median value.  Are you sure you want to apply the updates?", "Value Difference", MessageBoxButtons.YesNo))
                    {
                        return;
                    }
                }
            }
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
