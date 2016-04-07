using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ESRI.ArcGIS.Geometry;

namespace SmoothRadiusAddin
{
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
            set
            {
                _Warning = value;
                _ListFontWieght = String.IsNullOrEmpty(value) ? System.Windows.FontWeights.Normal : System.Windows.FontWeights.Bold;
                RaisePropertyChanges("HasWarning", "Warning");
            }
        }
        System.Windows.FontWeight _ListFontWieght = System.Windows.FontWeights.Normal;
        public System.Windows.FontWeight ListFontWieght { get { return _ListFontWieght; } }

        public Line(int objectID, bool isCurve, double radius, int centerpointID, int parcelID, IGeometry shape)
        {
            this.ObjectID = objectID;
            this.Radius = radius;
            this.CenterpointID = centerpointID;
            this.ParcelID = parcelID;
            this.IsCurve = isCurve;
            this.Shape = shape;
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
