using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ESRI.ArcGIS.Geometry;

namespace SmoothRadiusAddin
{
    class StraightLine : Line
    {
        public bool IsForward;

        public double StartAngle, EndAngle;

        public StraightLine(int objectID, int parcelID, IGeometry shape)
            : base (objectID, false, 0, 0, parcelID, shape)
        {
        }
    }

    class Line : System.ComponentModel.INotifyPropertyChanged
    {

        public int ObjectID { get; private set; }
        public double Radius { get; private set; }
        public int CenterpointID { get; private set; }

        public int ParcelID;
        public IGeometry Shape;

        public bool IsCurve { get; private set; }

        string _Warning = null;
        public string Warning
        {
            get { return _Warning; }
            private set
            {
                _Warning = value;
                ListFontWieght = String.IsNullOrEmpty(value) ? System.Windows.FontWeights.Normal : System.Windows.FontWeights.Bold;
                IconVisibility = String.IsNullOrEmpty(value) ? System.Windows.Visibility.Hidden : System.Windows.Visibility.Visible;
                RaisePropertyChanges("ListFontWieght", "Warning", "IconVisibility");
            }
        }
        public void AddWarning(string w)
        {
            if (Warning == null)
                Warning = w;
            else
                Warning = string.Concat(Warning, Environment.NewLine, w);
        }
        public void ClearWarnings()
        {
            Warning = null;
        }

        public System.Windows.FontWeight ListFontWieght { get; private set; }

        public System.Windows.Visibility IconVisibility { get; private set; }


        public Line(int objectID, bool isCurve, double radius, int centerpointID, int parcelID, IGeometry shape)
        {
            this.ObjectID = objectID;
            this.Radius = radius;
            this.CenterpointID = centerpointID;
            this.ParcelID = parcelID;
            this.IsCurve = isCurve;
            this.Shape = shape;
            
            ListFontWieght = System.Windows.FontWeights.Normal;
            IconVisibility = System.Windows.Visibility.Hidden;
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        void RaisePropertyChanges(params string[] ps)
        {
            if (PropertyChanged != null)
                foreach (string p in ps)
                    PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs(p));
        }
    }
}
