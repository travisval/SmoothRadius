using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Carto;
using System.Runtime.InteropServices;

namespace SmoothRadiusAddin
{
    /// <summary>
    /// Interaction logic for SmoothControl.xaml
    /// </summary>
    public partial class SmoothControl : UserControl
    {
        public SmoothControl()
        {
            InitializeComponent();
        }

        private void Flash(IFeature feature)
        {
            IFeatureIdentifyObj featIdentify = new ESRI.ArcGIS.CartoUI.FeatureIdentifyObject();
            featIdentify.Feature = feature;
            IIdentifyObj identify = featIdentify as IIdentifyObj;
            if (identify != null)
                identify.Flash(ArcMap.Document.ActivatedView.ScreenDisplay);

            Marshal.ReleaseComObject(featIdentify);
        }

        private void Flash_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            SmoothContext context = this.DataContext as SmoothContext;
            if (mi != null)
            {
                Line c = mi.CommandParameter as Line;
                if (c != null)
                {
                    IFeature feature = context.m_cadLines.GetFeature(c.ObjectID);
                    Flash(feature);
                    Marshal.ReleaseComObject(feature);
                }
            }
        }

        private void Exclude_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            SmoothContext context = this.DataContext as SmoothContext;
            if (mi != null)
            {
                Line c = mi.CommandParameter as Line;
                if (c != null)
                {
                    context.m_curves.Remove(c);
                }
            }
        }
    }
}
