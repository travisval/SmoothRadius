using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ESRI.ArcGIS.Editor;
using ESRI.ArcGIS.Framework;
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Geodatabase;
using System.Runtime.InteropServices;
using ESRI.ArcGIS.CadastralUI;
using ESRI.ArcGIS.GeoDatabaseExtensions;

namespace SmoothRadiusAddin
{

    /// <summary>
    /// A construction tool for ArcMap Editor, using shape constructors
    /// </summary>
    public partial class SmoothRadiusTool : ESRI.ArcGIS.Desktop.AddIns.Tool, IShapeConstructorTool, ISketchTool
    {
        private IEditor3 m_editor;
        private IEditEvents_Event m_editEvents;
        private IEditEvents5_Event m_editEvents5;
        private IEditSketch3 m_edSketch;
        private IShapeConstructor m_csc;

        private ICadastralEditor m_cadEd; 
        private ICadastralFabric m_cadFab; 
        private IFeatureClass m_fabricLines;
        private IFeatureClass m_fabricPoints; 



        public SmoothRadiusTool()
        {
            // Get the editor
            m_editor = ArcMap.Editor as IEditor3;
            m_editEvents = m_editor as IEditEvents_Event;
            m_editEvents5 = m_editor as IEditEvents5_Event;

            //m_editEvents.OnStartEditing += new IEditEvents_OnStartEditingEventHandler(m_editEvents_OnStartEditing);
            //m_editEvents.OnStopEditing += new IEditEvents_OnStopEditingEventHandler(m_editEvents_OnStopEditing);

            OnUpdate();
        }

        //void m_editEvents_OnStopEditing(bool save)
        //{
        //    try
        //    {
        //        MessageBox.Show("OnStopEditing");
        //        if (m_csc != null)
        //            MessageBox.Show("OnStopEditing: deactivating");
        //            m_csc.Deactivate();
        //    }
        //    catch (COMException exx)
        //    {
        //        MessageBox.Show(String.Format("m_editEvents_OnStopEditing: COMException: {0} ({1})", exx.Message, exx.ErrorCode));
        //    }
        //    catch (Exception exx)
        //    {
        //        MessageBox.Show(String.Format("m_editEvents_OnStopEditing: Exception: {0})", exx.Message));
        //    }
        //}

        //void m_editEvents_OnStartEditing()
        //{
        //    MessageBox.Show("OnStartEditing");
        //}

        protected override void OnUpdate()
        {
            try
            {
                Enabled = ArcMap.Application != null && m_editor != null && m_editor.EditState != esriEditState.esriStateNotEditing;
            }
            catch (COMException exx)
            {
                MessageBox.Show(String.Format("OnUpdate: COMException: {0} ({1})", exx.Message, exx.ErrorCode));
            }
            catch (Exception exx)
            {
                MessageBox.Show(String.Format("OnUpdate: Exception: {0})", exx.Message));
            }
        }

        protected override void OnActivate()
        {
            try
            {
                //MessageBox.Show("OnActivate");

                m_cadEd = (ICadastralEditor)ArcMap.Application.FindExtensionByName("esriCadastralUI.CadastralEditorExtension");
                m_cadFab = m_cadEd.CadastralFabric;

                if (m_cadFab == null)
                {
                    MessageBox.Show("No target fabric or edit session found. Please add a fabric to the map, start editing, and try again.");
                    return;
                }
                m_fabricLines = (IFeatureClass)m_cadFab.get_CadastralTable(esriCadastralFabricTable.esriCFTLines);
                m_fabricPoints = (IFeatureClass)m_cadFab.get_CadastralTable(esriCadastralFabricTable.esriCFTPoints);

                m_edSketch = m_editor as IEditSketch3;
                m_edSketch.GeometryType = esriGeometryType.esriGeometryPolyline;
                m_csc = new TraceConstructorClass();
                // Activate a shape constructor based on the current sketch geometry
                //if (m_edSketch.GeometryType == esriGeometryType.esriGeometryPoint | m_edSketch.GeometryType == esriGeometryType.esriGeometryMultipoint)
                //    m_csc = new PointConstructorClass();
                //else
                //   m_csc = new StraightConstructorClass();

                m_csc.Initialize(m_editor);
                m_edSketch.ShapeConstructor = m_csc;
                m_csc.Activate();

                // Setup events
                m_editEvents.OnSketchModified += OnSketchModified;
                m_editEvents5.OnShapeConstructorChanged += OnShapeConstructorChanged;
                m_editEvents.OnSketchFinished += OnSketchFinished;
            }
            catch (COMException exx)
            {
                MessageBox.Show(String.Format("OnActivate: COMException: {0} ({1})", exx.Message, exx.ErrorCode));
            }
            catch (Exception exx)
            {
                MessageBox.Show(String.Format("OnActivate: Exception: {0})", exx.Message));
            }
        }

        protected override bool OnDeactivate()
        {
            //MessageBox.Show("OnDeactivate");
            try
            {
                m_editEvents.OnSketchModified -= OnSketchModified;
                m_editEvents5.OnShapeConstructorChanged -= OnShapeConstructorChanged;
                m_editEvents.OnSketchFinished -= OnSketchFinished;
            }
            catch (COMException exx)
            {
                MessageBox.Show(String.Format("OnDeactivate: COMException: {0} ({1})", exx.Message, exx.ErrorCode));
            }
            catch (Exception exx)
            {
                MessageBox.Show(String.Format("OnDeactivate: Exception: {0})", exx.Message));
            }
            return true;
        }

        protected override void OnDoubleClick()
        {
            //DO NOT CALL BASE FUNCTIONS, this will cause a exception when application exits during an edit session
            //base.OnDoubleClick();

            //MessageBox.Show("OnDoubleClick");

            if (m_edSketch.Geometry == null)
                return;

            try
            {
                //if (Control.ModifierKeys == Keys.Shift)
                //{
                //    // Finish part
                //    ISketchOperation pso = new SketchOperation();
                //    pso.MenuString_2 = "Finish Sketch Part";
                //    pso.Start(m_editor);
                //    m_edSketch.FinishSketchPart();
                //    pso.Finish(null);
                //}
                //else
                    m_edSketch.FinishSketch();
            }
            catch (COMException exx)
            {
                MessageBox.Show(String.Format("OnDoubleClick: COMException: {0} ({1})", exx.Message, exx.ErrorCode));
            }
            catch (Exception exx)
            {
                MessageBox.Show(String.Format("OnDoubleClick: Exception: {0})", exx.Message));
            }
        }

        private void OnSketchModified()
        {
            try
            {
                //if (m_csc != null && m_csc.Enabled == true)
                m_csc.SketchModified();
            }
            catch (COMException exx)
            {
                MessageBox.Show(String.Format("OnSketchModified: COMException: {0} ({1})", exx.Message, exx.ErrorCode));
            }
            catch (Exception exx)
            {
                MessageBox.Show(String.Format("OnSketchModified: Exception: {0})", exx.Message));
            }
        }

        private void OnShapeConstructorChanged()
        {
            try
            {
                //MessageBox.Show(String.Format("OnShapeConstructorChanged, current {0}, new {1}", m_csc, m_edSketch.ShapeConstructor));
                // Activate a new constructor
                if (m_csc != null)
                    m_csc.Deactivate();
                m_csc = null;
                m_csc = m_edSketch.ShapeConstructor;
                if (m_csc != null)
                {
                    //if (m_csc.Active)
                    //{
                    //    MessageBox.Show("OnShapeConstructorChanged: Deactivate");
                    //    m_csc.Deactivate();
                    //}
                    //MessageBox.Show("OnShapeConstructorChanged: Reactivate");
                    
                    //Need these two lines or else the tool throws COMException if it is use immediatly after a save edits operation.
                    m_edSketch.RefreshSketch();
                    m_edSketch.GeometryType = esriGeometryType.esriGeometryPolyline;
                    
                    m_csc.Activate();
                }
            }
            catch (COMException exx)
            {
                MessageBox.Show(String.Format("OnShapeConstructorChanged: COMException: {0} ({1})", exx.Message, exx.ErrorCode));
            }
            catch (Exception exx)
            {
                MessageBox.Show(String.Format("OnShapeConstructorChanged: Exception: {0})", exx.Message));
            }
        }

        private void OnSketchFinished()
        {
            try
            {
                //MessageBox.Show("OnSketchFinished");

                IGeometry queryShape = ((ITopologicalOperator)m_edSketch.Geometry).Buffer(ArcMap.Document.SearchTolerance);

                int indxRadius = m_fabricLines.Fields.FindField("RADIUS");
                int indxCenterpointID = m_fabricLines.Fields.FindField("CENTERPOINTID");
                int indxParcelID = m_fabricLines.Fields.FindField("PARCELID");
                
                //Query Lines
                ISpatialFilter filter = new SpatialFilter()
                {
                    Geometry = queryShape,
                    GeometryField = m_fabricLines.ShapeFieldName,
                    SpatialRel = esriSpatialRelEnum.esriSpatialRelContains
                };
                IFeatureCursor cursor = m_fabricLines.Search(filter, true);
                IFeature feature = null;
                List<Line> curves = new List<Line>();
                double radius;
                int parcel, centerpoint;
                while ((feature = cursor.NextFeature()) != null)
                {
                    bool isRadius = feature.SafeRead(indxRadius, out radius);
                    bool isCenterpoint = feature.SafeRead(indxCenterpointID, out centerpoint);
                    bool isParcel = feature.SafeRead(indxParcelID, out parcel);
                                        
                    if (!isParcel)
                        throw new Exception("A null pracel identifier has been detected");
                    if (isRadius ^ isCenterpoint)
                        throw new Exception("A feature should have radius and centerpoint information set, or should not have either of the values set.");

                    curves.Add((isRadius) ?
                        new Line(feature.OID, isRadius, radius, centerpoint, parcel, feature.ShapeCopy) :
                        new StraightLine(feature.OID, parcel, feature.ShapeCopy));
                }
                Marshal.ReleaseComObject(cursor);

                if (curves.Count() > 0)
                {
                    SmoothRadiusWindow.Show(new SmoothContext(m_cadFab, m_editor, curves));
                }
                else
                {
                    MessageBox.Show("No features were found");
                }
            }
            catch (COMException exx)
            {
                MessageBox.Show(String.Format("OnSketchFinished: COMException: {0} ({1})", exx.Message, exx.ErrorCode));
            }
            catch (Exception exx)
            {
                MessageBox.Show(String.Format("OnSketchFinished: Exception: {0})", exx.Message));
            }
        }
    }
}
