using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.GeoDatabaseExtensions;
using ESRI.ArcGIS.Editor;
using ESRI.ArcGIS.Geometry;
using System.Runtime.InteropServices;

namespace SmoothRadiusAddin
{
    class SmoothContext : System.ComponentModel.INotifyPropertyChanged
    {
        double MaxTangentLineAngleInRadians = 0.05;

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
                WarningVisibility = value > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
                RaisePropertyChanges("WarningVisibility", "WarningCount");
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

        double _Median = Double.NaN;
        public double Median
        {
            get
            {
                if (Double.IsNaN(_Median))
                {
                    double[] sorted = m_curves.Where(w => w.IsCurve).Select(w => Math.Abs(w.Radius)).ToArray();
                    Array.Sort(sorted);

                    int size = sorted.Length;
                    int mid = size / 2;
                    _Median = (size % 2 != 0) ? (double)sorted[mid] : ((double)sorted[mid] + (double)sorted[mid - 1]) / 2;
                }
                return _Median;
            }
            set
            {
                _Min = value;
                RaisePropertyChanges("Median");
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

        public System.Windows.Visibility WarningVisibility { get; private set; }

        void determinConnectivity(Line current, double fromAngle, double toAngle, bool IsForward, List<Line> remaining)
        {
            var grouped = remaining.GroupBy(w=>FabricFunctions.GetRelativeOrientation((IPolyline)current.Shape, (IPolyline)w.Shape));

            foreach (var orientationSet in grouped)
            {
                RelativeOrientation orientation = orientationSet.Key;
                if (orientation == RelativeOrientation.Disjoint)
                    continue;

                remaining.RemoveAll(w => orientationSet.Contains(w));

                foreach (Line l in orientationSet)
                {
                    l.ClearWarnings();
                    double l_fromAngle = FabricFunctions.GetSlopeAtStartPoint((IPolyline)l.Shape);
                    double l_toAngle = FabricFunctions.GetSlopeAtEndPoint((IPolyline)l.Shape);
                    bool l_isForward = l.Radius > 0;

                    // if the segment isn't a curve, update everything
                    if (!l.IsCurve)
                    {
                        ILine line = new ESRI.ArcGIS.Geometry.Line() { FromPoint = ((IPolyline)l.Shape).FromPoint, ToPoint = ((IPolyline)l.Shape).ToPoint };
                        double halfdelta = Math.Asin(line.Length / 2 / Value);
                        l_fromAngle = l_fromAngle - halfdelta;
                        l_toAngle = l_toAngle + halfdelta;

                        StraightLine sl = (StraightLine)l;
                        sl.StartAngle = l_fromAngle;
                        sl.EndAngle = l_toAngle;

                        switch (orientation)
                        {
                            case RelativeOrientation.FromA_ToB:
                            case RelativeOrientation.ToA_FromB:
                            case RelativeOrientation.ACoversB_Reverse:
                            case RelativeOrientation.BCoversA_Reverse:
                            case RelativeOrientation.Reverse:
                            case RelativeOrientation.Overlapping_Reverse:
                                l_isForward = !IsForward;
                                break;
                            case RelativeOrientation.Same:
                            case RelativeOrientation.ToA_ToB:
                            case RelativeOrientation.FromA_FromB:
                            case RelativeOrientation.ACoversB_Same:
                            case RelativeOrientation.BCoversA_Same:
                            case RelativeOrientation.Overlapping_Same:
                                l_isForward = IsForward;
                                break;
                            case RelativeOrientation.Disjoint:
                            case RelativeOrientation.Parallel:
                                throw new Exception("determinConnectivity, straight line, Invalid Relative Orientation");
                        }

                        sl.IsForward = l_isForward;
                    }

                    // check to make sure that there aren't any angle or orientation differences
                    double diff; bool correctDirection;
                    if (orientation == RelativeOrientation.FromA_ToB)
                    {
                        diff = FabricFunctions.DiffAngles(fromAngle, l_toAngle);
                        correctDirection = l_isForward == IsForward;
                    }
                    else if (orientation == RelativeOrientation.ToA_FromB)
                    {
                        diff = FabricFunctions.DiffAngles(toAngle, l_fromAngle);
                        correctDirection = l_isForward == IsForward;
                    }
                    else if (orientation == RelativeOrientation.FromA_FromB)
                    {
                        diff = FabricFunctions.DiffAngles(fromAngle, l_fromAngle);
                        correctDirection = l_isForward != IsForward;
                    }
                    else if (orientation == RelativeOrientation.ToA_ToB)
                    {
                        diff = FabricFunctions.DiffAngles(toAngle, l_toAngle);
                        correctDirection = l_isForward != IsForward;
                    }
                    else if (orientation == RelativeOrientation.Same || orientation == RelativeOrientation.ACoversB_Same || orientation == RelativeOrientation.BCoversA_Same || orientation == RelativeOrientation.Overlapping_Same)
                    {
                        diff = 0.0;
                        correctDirection = l_isForward == IsForward;
                    }
                    else if (orientation == RelativeOrientation.Reverse || orientation == RelativeOrientation.ACoversB_Reverse || orientation == RelativeOrientation.BCoversA_Reverse || orientation == RelativeOrientation.Overlapping_Reverse)
                    {
                        diff = 0.0;
                        correctDirection = l_isForward != IsForward;
                    }
                    else
                        throw new Exception("determinConnectivity, curve, Invalid Relative Orientation");

                    if (Math.Abs(diff) > MaxTangentLineAngleInRadians)
                        l.AddWarning(String.Format("The angle of this segment is {0:F4} degrees different than an adjacent segment", FabricFunctions.toDegrees(diff)));
                    if (!correctDirection)
                        l.AddWarning("The sign of the radius does not match an adjacent segment");

                    determinConnectivity(l, l_fromAngle, l_toAngle, l_isForward, remaining);
                }
            }
        }
        void updateWarnings()
        {
            List<Line> remaining = new List<Line>(m_curves);
            while (remaining.Count > 0)
            {
                Line current = remaining.FirstOrDefault(w => w.IsCurve);
                if (current == null)
                    throw new Exception("A group of features containing no curves was found");

                remaining.Remove(current);

                current.ClearWarnings();
                double startAngle = FabricFunctions.GetSlopeAtStartPoint((IPolyline)current.Shape);
                double endAngle = FabricFunctions.GetSlopeAtEndPoint((IPolyline)current.Shape);
                bool isForward = current.Radius > 0;

                determinConnectivity(current, startAngle, endAngle, isForward, remaining);
            }

            // Test for significantly different radii
            double lowerBound = Median - (Median * 0.05);
            double upperBound = Median + (Median * 0.05); 
            foreach (Line line in m_curves)
            {
                if (!line.IsCurve)
                    line.AddWarning("The line does not have a radius set");
                else if (lowerBound > Math.Abs(line.Radius) || Math.Abs(line.Radius) > upperBound)
                    line.AddWarning("The radius value is more than 5% off the median");
            }

            WarningCount = m_curves.Where(w => !string.IsNullOrEmpty(w.Warning)).Count();
        }

        void Curves_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Min = Double.NaN;
            Max = Double.NaN;
            updateWarnings();
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

            if (!FabricFunctions.StartCadastralEditOperation(m_editor, m_cadFab, parcelIDs, "Smooth curves", esriCadastralFabricTable.esriCFTLines, esriCadastralFabricTable.esriCFTPoints))
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
            int indxHistorical = m_cadPoints.Fields.FindField("HISTORICAL");
            int indxCenterPoint = m_cadPoints.Fields.FindField("CENTERPOINT");
            int indxStartDate = m_cadPoints.Fields.FindField("SYSTEMSTARTDATE");

            bool zAware = m_cadPoints.Fields.get_Field(m_cadPoints.Fields.FindField(m_cadPoints.ShapeFieldName)).GeometryDef.HasZ;
            double sumx = 0.0, sumy = 0.0, sumz = 0.0;
            int count = 0, zCount = 0;

            DateTime minStartDate = DateTime.MaxValue;

            IQueryFilter qFilter = null;
            IFeatureCursor cursor = null;
            IFeature feature = null;
            try
            {
                qFilter = new QueryFilter() { WhereClause = where };
                cursor = m_cadPoints.Search(qFilter, true);

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
                    DateTime thisDateTime;
                    if (feature.SafeRead(indxStartDate, out thisDateTime))
                        minStartDate = (minStartDate < thisDateTime) ? minStartDate : thisDateTime;
                    Marshal.ReleaseComObject(feature);
                }
            }
            finally
            {
                if (feature != null) Marshal.ReleaseComObject(feature);
                if (cursor != null) Marshal.ReleaseComObject(cursor);
                if (qFilter != null) Marshal.ReleaseComObject(qFilter);
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

            try
            {
                //Insert new center point
                feature = m_cadPoints.CreateFeature();
                feature.set_Value(indxX, X);
                feature.set_Value(indxY, Y);
                if (!double.IsNaN(Z))
                {
                    feature.set_Value(indxZ, Z);
                }
                feature.set_Value(indxCenterPoint, 1);
                feature.set_Value(indxHistorical, 0);
                feature.set_Value(indxStartDate, minStartDate);
                feature.Shape = meanCenter;
                feature.Store();
                return feature.OID;
            }
            finally
            {
                if (feature != null) Marshal.ReleaseComObject(feature);
            }
        }

        private void updateLines(int meanCenterpointID)
        {
            int RadiusFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.RadiusFieldName);
            int CenterpointFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.CenterpointIDFieldName);
            int BearingFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.BearingFieldName);
            int ArcLengthFieldIndx = m_cadLines.Fields.FindField(FabricFunctions.ArcLengthFieldName);
            int ParcelIDFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.ParcelIDFieldName);
            int FromPointFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.FromPointFieldName);
            int ToPointFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.ToPointFieldName);
            int CategoryFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.CategoryFieldName);
            int SequenceFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.SequenceFieldName);
            int TypeFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.TypeFieldName);
            int DistanceFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.DistanceFieldName);
            int HistoricalFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.HistoricalFieldName);
            int LineParametersFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.LineParametersFieldName);
            int DensifyTypeIdx = m_cadLines.Fields.FindField(FabricFunctions.DensifyTypeName);
            int SystemStartDateFieldIdx = m_cadLines.Fields.FindField(FabricFunctions.SystemStartDateFieldName);

            IGeometry geometry = FabricFunctions.CreateNilGeometry(m_cadLines);

            IFeatureCursor insert_cursor = null;
            IFeatureBuffer insert_buffer = null;
            IFeatureCursor update_cursor = null;
            IFeature feature = null;
            try
            {
                insert_cursor = m_cadLines.Insert(true);
                insert_buffer = m_cadLines.CreateFeatureBuffer();

                string lineWhere = String.Format("{0} in ({1})", m_cadLines.OIDFieldName, String.Join(",", m_curves.Select(w => w.ObjectID.ToString()).ToArray()));
                IQueryFilter qFilter = new QueryFilter() { WhereClause = lineWhere };
                update_cursor = m_cadLines.Update(qFilter, false);
                Dictionary<int, int> sequenceCache = new Dictionary<int, int>();
                while ((feature = update_cursor.NextFeature()) != null)
                {
                    object radius_obj = feature.get_Value(RadiusFieldIdx);
                    if (DBNull.Value.Equals(radius_obj))
                    {
                        StraightLine sl = m_curves.FirstOrDefault(w => w.ObjectID == feature.OID) as StraightLine;
                        if (sl == null)
                            throw new Exception("Line not found");

                        double correctedValue = Value;
                        if (!sl.IsForward)
                            correctedValue = Value * -1;
                        feature.set_Value(RadiusFieldIdx, correctedValue);

                        double length = 0;
                        if (feature != null)
                        {
                            IPolyline polyline = feature.ShapeCopy as IPolyline;
                            if (polyline != null)
                            {
                                length = ((IProximityOperator)polyline.FromPoint).ReturnDistance(polyline.ToPoint);
                                feature.set_Value(ArcLengthFieldIndx, length);
                                System.Runtime.InteropServices.Marshal.ReleaseComObject(polyline);
                            }
                        }

                        feature.set_Value(CenterpointFieldIdx, meanCenterpointID);

                        int parcelID = (int)feature.get_Value(ParcelIDFieldIdx);

                        double featureBearing = (double)feature.get_Value(BearingFieldIdx);
                        double halfdelta = FabricFunctions.toDegrees(Math.Asin(length / 2 / correctedValue));

                        //perpendicular to the chord
                        double perpendicular = (correctedValue > 0) ? featureBearing + 90 : featureBearing - 90;
                        if (perpendicular > 360)
                            perpendicular = perpendicular - 360;
                        else if (perpendicular < 0)
                            perpendicular = perpendicular + 360;

                        for (int i = 0; i < 2; i++)
                        {
                            insert_buffer.set_Value(ParcelIDFieldIdx, parcelID);
                            insert_buffer.set_Value(ToPointFieldIdx, meanCenterpointID);
                            insert_buffer.set_Value(CategoryFieldIdx, 4);
                            insert_buffer.set_Value(SequenceFieldIdx, FabricFunctions.GetNextSequenceID(m_cadLines, parcelID, sequenceCache));
                            insert_buffer.set_Value(TypeFieldIdx, 0);
                            insert_buffer.set_Value(DistanceFieldIdx, Value);
                            insert_buffer.set_Value(HistoricalFieldIdx, 0);
                            insert_buffer.set_Value(LineParametersFieldIdx, 0);
                            insert_buffer.set_Value(DensifyTypeIdx, 0);
                            insert_buffer.set_Value(SystemStartDateFieldIdx, feature.get_Value(SystemStartDateFieldIdx));
                            insert_buffer.Shape = geometry;

                            if (i == 0) // startpoint
                            {
                                insert_buffer.set_Value(FromPointFieldIdx, feature.get_Value(FromPointFieldIdx));
                                insert_buffer.set_Value(BearingFieldIdx, perpendicular + halfdelta);
                            }
                            else  //endpoint
                            {
                                insert_buffer.set_Value(FromPointFieldIdx, feature.get_Value(ToPointFieldIdx));
                                insert_buffer.set_Value(BearingFieldIdx, perpendicular - halfdelta);
                            }

                            insert_cursor.InsertFeature(insert_buffer);
                        }
                    }
                    else
                    {
                        double radius = (Double)radius_obj;
                        int oldCenterPointID = (int)feature.get_Value(CenterpointFieldIdx);

                        //update curve
                        if (radius < 0)
                            feature.set_Value(RadiusFieldIdx, Value * -1);
                        else
                            feature.set_Value(RadiusFieldIdx, Value);
                        feature.set_Value(CenterpointFieldIdx, meanCenterpointID);

                        //update radial lines
                        IQueryFilter radial_filter = null;
                        IFeatureCursor radial_cursor = null;
                        IFeature radial_feature = null;
                        try
                        {
                            radial_filter = new QueryFilter()
                            {
                                WhereClause = string.Format("({0} = {1} or {0} = {2}) and {3} = {4} and {5} = {6}",
                                           FabricFunctions.FromPointFieldName, feature.get_Value(FromPointFieldIdx), feature.get_Value(ToPointFieldIdx),
                                           FabricFunctions.ToPointFieldName, oldCenterPointID,
                                           FabricFunctions.ParcelIDFieldName, feature.get_Value(ParcelIDFieldIdx))
                            };
                            radial_cursor = m_cadLines.Update(radial_filter, false);
                            int radial_distanceIndx = radial_cursor.Fields.FindField(FabricFunctions.DistanceFieldName);
                            int radial_toPointFieldNameIndx = radial_cursor.Fields.FindField(FabricFunctions.ToPointFieldName);
                            while ((radial_feature = radial_cursor.NextFeature()) != null)
                            {
                                radial_feature.set_Value(radial_distanceIndx, Value);
                                radial_feature.set_Value(radial_toPointFieldNameIndx, meanCenterpointID);
                                radial_cursor.UpdateFeature(radial_feature);
                                Marshal.ReleaseComObject(radial_feature);
                            }
                        }
                        finally
                        {
                            if (radial_feature != null) Marshal.ReleaseComObject(radial_feature);
                            if (radial_cursor != null) Marshal.ReleaseComObject(radial_cursor);
                            if (radial_filter != null) Marshal.ReleaseComObject(radial_filter);
                        }
                    }
                    update_cursor.UpdateFeature(feature);
                    Marshal.ReleaseComObject(feature);
                }
            }
            finally
            {
                if (feature != null) Marshal.ReleaseComObject(feature);
                if (insert_buffer != null) Marshal.ReleaseComObject(insert_buffer);
                if (insert_cursor != null) Marshal.ReleaseComObject(insert_cursor);
                if (update_cursor != null) Marshal.ReleaseComObject(update_cursor);
            }
        }

        private void deleteOraphanedPoints(IEnumerable<int> centerPoints)
        {
            List<int> allIds = new List<int>(centerPoints);

            IQueryFilter qFilter = null;
            IFeatureCursor cursor = null;
            IFeature row = null;
            try
            {
                qFilter = new QueryFilter() { WhereClause = String.Format("CENTERPOINTID in ({0})", String.Join(",", centerPoints.Select(i => i.ToString()))) };
                //((IQueryFilterDefinition2)qFilter).PrefixClause = "DISTINCT";
                //Distinct clauses don't appear to work inside of edit sessions, reproduced to 10.1
                //...work around using client side distinct.
                //https://geonet.esri.com/thread/83171

                cursor = m_cadLines.Search(qFilter, false);
                int indxCenterpoint = cursor.Fields.FindField("CENTERPOINTID");
                List<int> usedID = new List<int>();

                while ((row = cursor.NextFeature()) != null)
                {
                    int centerpoint;
                    if (row.SafeRead(indxCenterpoint, out centerpoint))
                    {
                        usedID.Add(centerpoint);
                    }
                    Marshal.ReleaseComObject(row);
                }

                foreach (int i in usedID.Distinct())
                    allIds.Remove(i);
            }
            finally
            {
                if (row != null) Marshal.ReleaseComObject(row);
                if (cursor != null) Marshal.ReleaseComObject(cursor);
                if (qFilter != null) Marshal.ReleaseComObject(qFilter);
            }

            if (allIds.Count > 0)
            {
                IQueryFilter deleteFilter = new QueryFilter() { WhereClause = String.Format("{0} in ({1})", m_cadPoints.OIDFieldName, String.Join(",", allIds)) };
                ((ITable)m_cadPoints).DeleteSearchedRows(deleteFilter);
            }
        }
    }
}
