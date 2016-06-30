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
using ESRI.ArcGIS.Framework;
using ESRI.ArcGIS.esriSystem;
using System.ComponentModel;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Editor;

namespace SmoothRadiusAddin
{
    /// <summary>
    /// Designer class of the dockable window add-in. It contains WPF user interfaces that
    /// make up the dockable window.
    /// </summary>
    public partial class SmoothRadiusWindow : UserControl
    {
        static SmoothRadiusWindow s_this = null;

        public SmoothRadiusWindow()
        {
            InitializeComponent();
            s_this = this;
        }

        private Line GetCurrentLine(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi != null)
            {
                return mi.CommandParameter as Line;
            }
            return null;
        }
        private IFeature GetCurrentFeature(object sender, RoutedEventArgs e)
        {
            SmoothContext context = this.DataContext as SmoothContext;
            Line c = GetCurrentLine(sender,e);
            if (c != null && context != null && context.m_cadLines != null)
            {
                return context.m_cadLines.GetFeature(c.ObjectID);
            }
            return null;
        }
        
        private void Flash_Click(object sender, RoutedEventArgs e)
        {
            IFeature feature = GetCurrentFeature(sender,e);
            if (feature != null)
            {
                IFeatureIdentifyObj featIdentify = new ESRI.ArcGIS.CartoUI.FeatureIdentifyObject();
                featIdentify.Feature = feature;
                IIdentifyObj identify = featIdentify as IIdentifyObj;
                if (identify != null)
                    identify.Flash(ArcMap.Document.ActivatedView.ScreenDisplay);

                Marshal.ReleaseComObject(featIdentify);
                Marshal.ReleaseComObject(feature);
            }
        }

        private void Exclude_Click(object sender, RoutedEventArgs e)
        {
            Line c = GetCurrentLine(sender, e);
            if (c != null)
            {
                ((SmoothContext)this.DataContext).m_curves.Remove(c);
            }
        }
       
        private void PanTo_Click(object sender, RoutedEventArgs e)
        {
            IFeature feature = GetCurrentFeature(sender,e);
            if (feature != null)
            {

                double X = (feature.Extent.XMin + feature.Extent.XMax) / 2.0;
                double Y = (feature.Extent.YMin + feature.Extent.YMax) / 2.0;

                IEnvelope currentEnv = ArcMap.Document.ActivatedView.Extent;
                currentEnv.CenterAt(new ESRI.ArcGIS.Geometry.Point() { X = X, Y = Y });
                ArcMap.Document.ActivatedView.Extent = currentEnv;

                ArcMap.Document.ActivatedView.Refresh();
            }
        }

        private void ZoomTo_Click(object sender, RoutedEventArgs e)
        {
            IFeature feature = GetCurrentFeature(sender,e);
            if (feature != null)
            {
                IEnvelope extent = feature.Shape.Envelope;
                if (extent.Height == 0)
                    extent.Height = extent.Width / 4;
                if (extent.Width == 0)
                    extent.Width = extent.Height / 4;
                extent.Expand(1.2, 1.2, true);
                ArcMap.Document.ActivatedView.Extent = extent;
                ArcMap.Document.ActivatedView.Refresh();
                ArcMap.Document.ActivatedView.ScreenDisplay.UpdateWindow();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SmoothContext context = this.DataContext as SmoothContext;
            if (context != null)
            {

                if (context.WarningCount > 0)
                {
                    if (System.Windows.Forms.DialogResult.Yes != System.Windows.Forms.MessageBox.Show(
                        String.Format("There are {0} warnings present in the selected segments.  Are you sure you want to apply the updates?", context.WarningCount), "Warnings Present", System.Windows.Forms.MessageBoxButtons.YesNo))
                    {
                        return;
                    }
                }

                if (Math.Abs(context.Value - context.Median) > (context.Median * 0.05))
                {
                    if (System.Windows.Forms.DialogResult.Yes != System.Windows.Forms.MessageBox.Show(
                        "The current value is more than 5% different than the current median value.  Are you sure you want to apply the updates?", "Value Difference", System.Windows.Forms.MessageBoxButtons.YesNo))
                    {
                        return;
                    }
                }
                ESRI.ArcGIS.Framework.IMouseCursor appCursor = new ESRI.ArcGIS.Framework.MouseCursorClass();
                appCursor.SetCursor(2);
                try
                {
                    context.Update();
                }
                catch (Exception exx)
                {
                    MessageBox.Show(exx.ToString(), "Unexpected exception");
                }
                finally
                {
                    appCursor.SetCursor(0);
                    Close();

                    //Marshal.ReleaseComObject(appCursor);
                }
            }
        }
        
        /// <summary>
        /// Implementation class of the dockable window add-in. It is responsible for 
        /// creating and disposing the user interface class of the dockable window.
        /// </summary>
        public class AddinImpl : ESRI.ArcGIS.Desktop.AddIns.DockableWindow
        {
            private System.Windows.Forms.Integration.ElementHost m_windowUI;

            public AddinImpl()
            {

            }

            protected override IntPtr OnCreateChild()
            {
                m_windowUI = new System.Windows.Forms.Integration.ElementHost();
                m_windowUI.Child = new SmoothRadiusWindow();
                return m_windowUI.Handle;
            }

            protected override void Dispose(bool disposing)
            {
                if (m_windowUI != null)
                    m_windowUI.Dispose();

                base.Dispose(disposing);
            }

            
        }

        static IDockableWindow s_CurveByInfrenceWindow = null;
        static IDockableWindow GetWindow()
        {
            if (s_CurveByInfrenceWindow == null)
            {
                UID dockWinID = new UIDClass();
                dockWinID.Value = ThisAddIn.IDs.SmoothRadiusWindow;
                s_CurveByInfrenceWindow = ArcMap.DockableWindowManager.GetDockableWindow(dockWinID);

                s_CurveByInfrenceWindow.Caption = String.Format("{0} ({1})", s_CurveByInfrenceWindow.Caption, ThisAddIn.Version);

                // listen to the editing stop event so the window can be closed when the edit session is stopped.
                IEditEvents_Event editEvents = (IEditEvents_Event)ArcMap.Editor;
                editEvents.OnStopEditing += new IEditEvents_OnStopEditingEventHandler(editEvents_OnStopEditing);
            }
            return s_CurveByInfrenceWindow;
        }
        static void editEvents_OnStopEditing(bool save)
        {
            Close();
        }

        internal static void Show(SmoothContext context)
        {
            IDockableWindow window = GetWindow();
            s_this.DataContext = context;
            window.Show(true);
        }
        internal static void Close()
        {
            GetWindow().Show(false);
        }
    }
}
