using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.GeoDatabaseExtensions;
using ESRI.ArcGIS.Editor;
using ESRI.ArcGIS.Geometry;

namespace SmoothRadiusAddin
{
    class SmoothContext : System.ComponentModel.INotifyPropertyChanged
    {
        double _Value = Double.NaN;
        public double Value
        {
            get
            {
                if(Double.IsNaN(_Value))
                    return Math.Round(m_curves.Where(w => w.IsCurve).Average(w => Math.Abs(w.Radius)), 2);
                return _Value;
            }
            set
            {
                _Value = value;
                _Max = Double.NaN;
                _Min = Double.NaN;
                RaisePropertyChanges("Min", "Max", "Value");
            }
        }

        int _WarningCount = 0;
        public int WarningCount
        {
            get { return _WarningCount; }
            set
            {
                _WarningCount = value;
                RaisePropertyChanges("WarningCount");
            }
        }

        double _Min = Double.NaN;
        public double Min
        {
            get
            {
                if (Double.IsNaN(_Min))
                    _Min = Math.Min(m_curves.Where(w => w.IsCurve).Min(w => Math.Abs(w.Radius)), Value);
                return _Min;
            }
            set
            {
                _Min = value;
                RaisePropertyChanges("Min", "Max", "Value");
            }
        }

        double _Max = Double.NaN;
        public double Max
        {
            get
            {
                if (Double.IsNaN(_Max))
                    _Max = Math.Max(m_curves.Where(w => w.IsCurve).Max(w => Math.Abs(w.Radius)), Value);
                return _Max;
            }
            set
            {
                _Max = value;
                RaisePropertyChanges("Min", "Max", "Value");
            }
        }

        public ObservableCollection<Line> m_curves { get; private set; }

        public ICadastralFabric m_cadFab;
        public IFeatureClass m_cadLines;
        public IFeatureClass m_cadPoints;

        IEditor m_editor = null;

        public SmoothContext(ICadastralFabric fabric, IEditor edit, IEnumerable<Line> lines)
        {
            this.m_curves = new ObservableCollection<Line>(lines);
            this.m_curves.CollectionChanged += new System.Collections.Specialized.NotifyCollectionChangedEventHandler(Curves_CollectionChanged);

            m_cadFab = fabric;
            m_cadLines = (IFeatureClass)fabric.get_CadastralTable(esriCadastralFabricTable.esriCFTLines);
            m_cadPoints = (IFeatureClass)fabric.get_CadastralTable(esriCadastralFabricTable.esriCFTPoints);

            m_editor = edit;

            updateWarnings();
        }

        void updateWarnings()
        {

            // Test for significantly different radii
            double[] sorted = m_curves.Where(w => w.IsCurve).Select(w => Math.Abs(w.Radius)).ToArray();
            Array.Sort(sorted);

            //get the median
            int size = sorted.Length;
            int mid = size / 2;
            double median = (size % 2 != 0) ? (double)sorted[mid] : ((double)sorted[mid] + (double)sorted[mid - 1]) / 2;

            double lowerBound = median - (median * 0.05);
            double upperBound = median + (median * 0.05); 
            foreach (Line line in m_curves)
            {
                if (!line.IsCurve)
                    line.Warning = "The line does not have a radius set";
                else if (lowerBound > Math.Abs(line.Radius) || Math.Abs(line.Radius) > upperBound)
                    line.Warning = "The radius value is more than 5% of the median";
            }

            WarningCount = m_curves.Where(w => !string.IsNullOrEmpty(w.Warning)).Count();
        }

        void Curves_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Min = Double.NaN;
            Max = Double.NaN;
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        void RaisePropertyChanges(params string[] ps)
        {
            if (PropertyChanged != null)
                foreach (string p in ps)
                    PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs(p));
        }

        public void Update()
        {
            var parcelIDs = m_curves.Where(l=>l.IsCurve).Select(l => l.ParcelID).Distinct();
            var centerpointIDs = m_curves.Where(l => l.IsCurve).Select(l => l.CenterpointID).Distinct();

            if (!FabricFunctions.StartCadastralEditOperation(m_editor, m_cadFab, parcelIDs, esriCadastralFabricTable.esriCFTLines, esriCadastralFabricTable.esriCFTPoints))
                return;

            //Get and mean center points
            var centerPoints = m_curves.GroupBy(w => w.CenterpointID.ToString()).Distinct().ToArray();
            int meanCenterpointID = (centerPoints.Count() > 1) ? mergeCenterPoints(centerPoints) : m_curves[0].CenterpointID;

            //Update lines
            //  Update curves
            //  Create curves from straigh segments
            updateLines(meanCenterpointID);

            deleteOraphanedPoints(centerpointIDs);
            
            FabricFunctions.RegenerateFabric(m_cadFab, parcelIDs);

            m_editor.StopOperation("Smooth curve segments.");
        }

        private int mergeCenterPoints(IGrouping<string, Line>[] centerPoints)
        {
            string where = String.Format("{0} in ({1})", m_cadPoints.OIDFieldName, String.Join(",", centerPoints.Select(p => p.Key)));

            int indxX = m_cadPoints.Fields.FindField("X");
            int indxY = m_cadPoints.Fields.FindField("Y");
            int indxZ = m_cadPoints.Fields.FindField("Z");
            int indxCenterPoint = m_cadPoints.Fields.FindField("CENTERPOINT");

            IQueryFilter qFilter = new QueryFilter() { WhereClause = where };
            IFeatureCursor cursor = m_cadPoints.Search(qFilter, true);
            IFeature feature = null;
            bool zAware = m_cadPoints.Fields.get_Field(m_cadPoints.Fields.FindField(m_cadPoints.ShapeFieldName)).GeometryDef.HasZ;
            double sumx = 0.0, sumy = 0.0, sumz = 0.0;
            int count = 0, zCount = 0;
            while ((feature = cursor.NextFeature()) != null)
            {
                int useCount = centerPoints.First(p => p.Key == feature.OID.ToString()).Count();
                IPoint shape = (IPoint)feature.Shape;
                double z = shape.Z;

                sumx += (useCount * (double)feature.get_Value(indxX));
                sumy += (useCount * (double)feature.get_Value(indxY));
                object z_obj = feature.get_Value(indxZ);
                if (zAware && !DBNull.Value.Equals(z_obj))
                {
                    sumz += (useCount * (double)z_obj);
                    zCount += useCount;
                }
                count += useCount;
            }
            double X = Math.Round(sumx / count, 6), Y = Math.Round(sumy / count, 6), Z = double.NaN;
            IPoint meanCenter = new Point() {  X = X, Y = Y  };
            if (zAware)
            {
                if (zCount > 0)
                    meanCenter.Z = Z = Math.Round(sumz / zCount, 6);
                else
                    meanCenter.Z = 0.0;
                ((IZAware)meanCenter).ZAware = true;
            }

            //Insert new center point
            feature = m_cadPoints.CreateFeature();
            feature.set_Value(indxX, X);
            feature.set_Value(indxY, Y);
            if (!double.IsNaN(Z))
            {
                feature.set_Value(indxZ, Z);
            }
            feature.set_Value(indxCenterPoint, 1);
            feature.Shape = meanCenter;
            return feature.OID;
        }

        private void updateLines(int meanCenterpointID)
        {
            
            int indxRadius = m_cadLines.Fields.FindField("RADIUS");
            int indxCenterpoint = m_cadLines.Fields.FindField("CENTERPOINTID");
            int indxParcelID = m_cadLines.Fields.FindField("PARCELID");

            string lineWhere = String.Format("{0} in ({1})", m_cadLines.OIDFieldName, String.Join(",", m_curves.Select(w => w.ObjectID.ToString()).ToArray()));
            IQueryFilter qFilter = new QueryFilter() { WhereClause = lineWhere };
            IFeatureCursor cursor = m_cadLines.Update(qFilter, true);
            IFeature feature = null;
            while ((feature = cursor.NextFeature()) != null)
            {
                object radius_obj = feature.get_Value(indxRadius);
                if (DBNull.Value.Equals(radius_obj))
                {
                    System.Windows.Forms.MessageBox.Show("Straight lines aren't supported....yet.");
                }
                else
                {
                    double radius = (Double)radius_obj;

                    if (radius < 0)
                        feature.set_Value(indxRadius, Value * -1);
                    else
                        feature.set_Value(indxRadius, Value);
                    feature.set_Value(indxCenterpoint, meanCenterpointID);
                }
                     
                cursor.UpdateFeature(feature);
            }
        }

        private void deleteOraphanedPoints(IEnumerable<int> centerPoints)
        {
            List<int> allIds = new List<int>(centerPoints);
            IQueryFilter qFilter = new QueryFilter() { SubFields = "CENTERPOINTID", WhereClause = String.Format("CENTERPOINTID in ({0})", String.Join(",", centerPoints.Select(i=>i.ToString()))) };
            ((IQueryFilterDefinition2)qFilter).PrefixClause = "DISTINCT";
            ICursor cursor = ((ITable)m_cadLines).Search(qFilter, false);
            IRow row = null;
            int indxCenterpoint = cursor.Fields.FindField("CENTERPOINTID");

            while ((row = cursor.NextRow()) != null)
            {
                int centerpoint;
                if (row.SafeRead(indxCenterpoint, out centerpoint))
                {
                    allIds.Remove(centerpoint);
                }
            }

            if (allIds.Count > 0)
            {
                IQueryFilter deleteFilter = new QueryFilter() { WhereClause = String.Format("{0} in ({1})", m_cadPoints.OIDFieldName, String.Join(",", allIds)) };
                ((ITable)m_cadPoints).DeleteSearchedRows(deleteFilter);
            }
        }
    }
}
