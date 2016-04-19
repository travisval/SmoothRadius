using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.GeoDatabaseExtensions;
using System.Runtime.InteropServices;
using ESRI.ArcGIS.Editor;
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Geometry;

namespace SmoothRadiusAddin
{
    public enum RelativeOrientation { ToA_ToB = 1, ToA_FromB = 3, FromA_ToB = 2, FromA_FromB = 4, Same = 5, Reverse = 6, Parallel = 7, Disjoint = 8, 
        ACoversB_Same, ACoversB_Reverse, BCoversA_Same, BCoversA_Reverse, Overlapping_Same, Overlapping_Reverse }

    public static class FabricFunctions
    {
        public const string RadiusFieldName = "Radius";
        public const string CenterpointIDFieldName = "CenterPointID";
        public const string ParcelIDFieldName = "ParcelID";
        public const string FromPointFieldName = "FromPointID";
        public const string ToPointFieldName = "ToPointID";
        public const string SystemStartDateFieldName = "SystemStartDate";

        public const string CategoryFieldName = "Category";
        public const string SequenceFieldName = "Sequence";
        public const string TypeFieldName = "Type";
        public const string HistoricalFieldName = "Historical";
        public const string LineParametersFieldName = "LineParameters";
        public const string DensifyTypeName = "DensifyType";

        public const string DistanceFieldName = "Distance";
        public const string ArcLengthFieldName = "ArcLength";

        public const string BearingFieldName = "Bearing";

        /// <summary>
        /// Safely accesses a value, accounting for DBNull values
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="feature"></param>
        /// <param name="Index"></param>
        /// <param name="Value"></param>
        /// <returns><c>true</c> if the value is not null; otherwise <c>false</c></returns>
        /// <exception cref="InvalidCastException">The value is not null, but can not be coverted to the provided type.</exception>
        public static bool SafeRead<T>(this IFeature feature, int Index, out T Value)
        {
            return SafeRead((IRow)feature, Index, out Value);
        }
        /// <summary>
        /// Safely accesses a value, accounting for DBNull values
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="feature"></param>
        /// <param name="Index"></param>
        /// <param name="Value"></param>
        /// <returns><c>true</c> if the value is not null; otherwise <c>false</c></returns>
        /// <exception cref="InvalidCastException">The value is not null, but can not be coverted to the provided type.</exception>
        public static bool SafeRead<T>(this IRow feature, int Index, out T Value)
        {
            object readData = feature.get_Value(Index);

            if (readData is T)
            {
                Value = (T)readData;

            }
            else if (DBNull.Value.Equals(readData))
            {
                Value = default(T);
                return false;
            }
            else
            {
                Value = default(T);
                try
                {
                    Value = (T)Convert.ChangeType(readData, typeof(T));
                }
                catch (InvalidCastException)
                {
                    throw;
                }
            }
            return true;
        }

        public static void RegenerateFabric(ICadastralFabric fabric, IEnumerable<int> parcelIDs)
        {
            ESRI.ArcGIS.GeoDatabaseExtensions.ICadastralFabricRegeneration pRegenFabric = new ESRI.ArcGIS.GeoDatabaseExtensions.CadastralFabricRegenerator();
            #region regenerator enum
            // enum esriCadastralRegeneratorSetting
            // esriCadastralRegenRegenerateGeometries         =   1
            // esriCadastralRegenRegenerateMissingRadials     =   2,
            // esriCadastralRegenRegenerateMissingPoints      =   4,
            // esriCadastralRegenRemoveOrphanPoints           =   8,
            // esriCadastralRegenRemoveInvalidLinePoints      =   16,
            // esriCadastralRegenSnapLinePoints               =   32,
            // esriCadastralRegenRepairLineSequencing         =   64,
            // esriCadastralRegenRepairPartConnectors         =   128

            // By default, the bitmask member is 0 which will only regenerate geometries.
            // (equivalent to passing in regeneratorBitmask = 1)
            #endregion

            pRegenFabric.CadastralFabric = fabric;
            pRegenFabric.RegeneratorBitmask = 1 + 2 + 4;

            IFIDSet fids = new FIDSet();
            foreach (int i in parcelIDs)
                fids.Add(i);
            pRegenFabric.RegenerateParcels(fids, false, null);
        }

        static bool CanNotUndoWarning = false;
        public static bool StartCadastralEditOperation(IEditor editor, ICadastralFabric fabric, IEnumerable<int> parcelsToLock, string JobDescription, params esriCadastralFabricTable[] tables)
        {
            IWorkspace workspace = ((IDataset)fabric).Workspace;

            if (editor.EditState == esriEditState.esriStateNotEditing)
                throw new Exception("Start an edit session before calling StartCadastralEditOperation");

            bool isVersioned = isFabricVersioned(fabric);

            if (!isVersioned && workspace.Type == esriWorkspaceType.esriRemoteDatabaseWorkspace)
            {
                if (!CanNotUndoWarning)
                {
                    CanNotUndoWarning = true;
                    System.Windows.Forms.DialogResult dlgRes = System.Windows.Forms.MessageBox.Show(
                        "Fabric is not registered as versioned." + Environment.NewLine +
                        "You will not be able to undo, proceed?", "No support for Undo", System.Windows.Forms.MessageBoxButtons.OKCancel);
                    if (dlgRes == System.Windows.Forms.DialogResult.Cancel)
                        return false;
                }

                //see if parcel locks can be obtained on the selected parcels. First create a job.
                ICadastralJob pJob = new CadastralJob();
                String JobName = pJob.Name = Convert.ToString(DateTime.Now);
                pJob.Owner = System.Windows.Forms.SystemInformation.UserName;
                pJob.Description = JobDescription;
                try
                {
                    Int32 jobId = fabric.CreateJob(pJob);
                }
                catch (COMException ex)
                {
                    if (ex.ErrorCode == (int)fdoError.FDO_E_CADASTRAL_FABRIC_JOB_ALREADY_EXISTS)
                    {
                        System.Windows.Forms.MessageBox.Show("Job named: '" + pJob.Name + "', already exists");
                    }
                    else
                    {
                        System.Windows.Forms.MessageBox.Show(ex.Message);
                    }
                    return false;
                }
            
                ICadastralFabricLocks fabricLocks = (ICadastralFabricLocks)fabric;
        
                ILongArray affectedParcels = new LongArray();
                foreach (int i in parcelsToLock)
                    affectedParcels.Add(i);
                           
                fabricLocks.LockingJob = JobName;
                ILongArray pLocksInConflict = null;
                ILongArray pSoftLcksInConflict = null;

                try
                {
                    fabricLocks.AcquireLocks(affectedParcels, true, ref pLocksInConflict, ref pSoftLcksInConflict);
                }
                catch (COMException pCOMEx)
                {
                    if (pCOMEx.ErrorCode == (int)fdoError.FDO_E_CADASTRAL_FABRIC_JOB_LOCK_ALREADY_EXISTS ||
                        pCOMEx.ErrorCode == (int)fdoError.FDO_E_CADASTRAL_FABRIC_JOB_CURRENTLY_EDITED)
                    {
                        System.Windows.Forms.MessageBox.Show("Edit Locks could not be acquired on all selected parcels.");
                        // since the operation is being aborted, release any locks that were acquired
                        fabricLocks.UndoLastAcquiredLocks();
                    }
                    else
                        System.Windows.Forms.MessageBox.Show(pCOMEx.Message + Environment.NewLine + Convert.ToString(pCOMEx.ErrorCode));

                    return false;
                }
            }

            if (editor.EditState == esriEditState.esriStateEditing)
            {
                try
                {
                    editor.StartOperation();
                }
                catch
                {
                    System.Windows.Forms.MessageBox.Show("Aborting previous edit operation");
                    editor.AbortOperation();
                    editor.StartOperation();
                }
            }

            ICadastralFabricSchemaEdit2 pSchemaEd = (ICadastralFabricSchemaEdit2)fabric;
            foreach (esriCadastralFabricTable tableType in tables)
            {
                pSchemaEd.ReleaseReadOnlyFields(fabric.get_CadastralTable(tableType), tableType);
            }
            return true;

        }

        private static bool isFabricVersioned(ICadastralFabric fabric)
        {
            IWorkspace workspace = ((IDataset)fabric).Workspace;
            if (workspace.Type == esriWorkspaceType.esriRemoteDatabaseWorkspace)
            {
                ITable table = fabric.get_CadastralTable(esriCadastralFabricTable.esriCFTParcels);
                IVersionedObject versionedObject = (IVersionedObject)table;
                return versionedObject.IsRegisteredAsVersioned;
            }
            return false;
        }

        static bool StartEditing(IWorkspace TheWorkspace, bool IsUnversioned)   // Start EditSession + create EditOperation
        {
            bool IsFileBasedGDB =
              (!(TheWorkspace.WorkspaceFactory.WorkspaceType == esriWorkspaceType.esriRemoteDatabaseWorkspace));

            IWorkspaceEdit pWSEdit = (IWorkspaceEdit)TheWorkspace;
            if (pWSEdit.IsBeingEdited())
            {
                System.Windows.Forms.MessageBox.Show("The workspace is being edited by another process.");
                return false;
            }

            if (!IsFileBasedGDB)
            {
                IMultiuserWorkspaceEdit pMUWSEdit = (IMultiuserWorkspaceEdit)TheWorkspace;
                try
                {
                    if (pMUWSEdit.SupportsMultiuserEditSessionMode(esriMultiuserEditSessionMode.esriMESMNonVersioned) && IsUnversioned)
                    {
                        pMUWSEdit.StartMultiuserEditing(esriMultiuserEditSessionMode.esriMESMNonVersioned);
                    }
                    else if (pMUWSEdit.SupportsMultiuserEditSessionMode(esriMultiuserEditSessionMode.esriMESMVersioned) && !IsUnversioned)
                    {
                        pMUWSEdit.StartMultiuserEditing(esriMultiuserEditSessionMode.esriMESMVersioned);
                    }

                    else
                    {
                        return false;
                    }
                }
                catch (COMException ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message + "  " + Convert.ToString(ex.ErrorCode), "Start Editing");
                    return false;
                }
            }
            else
            {
                try
                {
                    pWSEdit.StartEditing(false);
                }
                catch (COMException ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message + "  " + Convert.ToString(ex.ErrorCode), "Start Editing");
                    return false;
                }
            }

            pWSEdit.DisableUndoRedo();
            try
            {
                pWSEdit.StartEditOperation();
            }
            catch
            {
                pWSEdit.StopEditing(false);
                return false;
            }
            return true;
        }
        static bool SetupEditEnvironment(IWorkspace TheWorkspace, ICadastralFabric TheFabric, IEditor TheEditor, out bool IsFileBasedGDB, out bool IsUnVersioned, out bool UseNonVersionedEdit)
        {
            IsFileBasedGDB = false;
            IsUnVersioned = false;
            UseNonVersionedEdit = false;

            ITable pTable = TheFabric.get_CadastralTable(esriCadastralFabricTable.esriCFTParcels);

            IsFileBasedGDB = (!(TheWorkspace.WorkspaceFactory.WorkspaceType == esriWorkspaceType.esriRemoteDatabaseWorkspace));

            if (!(IsFileBasedGDB))
            {
                IVersionedObject pVersObj = (IVersionedObject)pTable;
                IsUnVersioned = (!(pVersObj.IsRegisteredAsVersioned));
                pTable = null;
                pVersObj = null;
            }
            if (IsUnVersioned && !IsFileBasedGDB)
            {
                System.Windows.Forms.DialogResult dlgRes = System.Windows.Forms.MessageBox.Show("Fabric is not registered as versioned." +
                  "\r\n You will not be able to undo." +
                  "\r\n Click 'OK' to update features permanently.",
                  "Continue with update?", System.Windows.Forms.MessageBoxButtons.OKCancel);
                if (dlgRes == System.Windows.Forms.DialogResult.OK)
                {
                    UseNonVersionedEdit = true;
                }
                else if (dlgRes == System.Windows.Forms.DialogResult.Cancel)
                {
                    return false;
                }
                //MessageBox.Show("The fabric tables are non-versioned." +
                //   "\r\n Please register as versioned, and try again.");
                //return false;
            }
            else if (TheEditor != null && TheEditor.EditState == esriEditState.esriStateNotEditing)
            {
                System.Windows.Forms.MessageBox.Show("Please start editing first and try again.", "Delete", System.Windows.Forms.MessageBoxButtons.OK);
                return false;
            }
            return true;
        }


        public static RelativeOrientation GetRelativeOrientation(IPolyline polylineA, IPolyline polylineB)
        {
            //check to see if the segments overlap

            double line_to = ((IProximityOperator)polylineA).ReturnDistance(polylineB.ToPoint);
            double line_from = ((IProximityOperator)polylineA).ReturnDistance(polylineB.FromPoint);

            double to_line = ((IProximityOperator)polylineB).ReturnDistance(polylineA.ToPoint);
            double from_line = ((IProximityOperator)polylineB).ReturnDistance(polylineA.FromPoint);

            //fully overlap
            if (line_to < 0.005 && line_from < 0.005 && to_line < 0.005 && from_line < 0.005)
            {
                //if they do, find the relative orientation and then return Same or Reverse
                RelativeOrientation orientation = GetRelativeOrientation_ignoreSame(polylineA, polylineB);
                if (orientation == RelativeOrientation.ToA_ToB || orientation == RelativeOrientation.FromA_FromB)
                    return RelativeOrientation.Same;
                if (orientation == RelativeOrientation.FromA_ToB || orientation == RelativeOrientation.ToA_FromB)
                    return RelativeOrientation.Reverse;

                throw new Exception("Invalid relative same/revers for overlapping segments returend");
            }
            if (line_to < 0.005 && line_from < 0.005)
            {
                //if they do, find the relative orientation and then return Same or Reverse
                RelativeOrientation orientation = GetRelativeOrientation_ignoreSame(polylineA, polylineB);
                if (orientation == RelativeOrientation.ToA_ToB || orientation == RelativeOrientation.FromA_FromB)
                    return RelativeOrientation.ACoversB_Same;
                if (orientation == RelativeOrientation.FromA_ToB || orientation == RelativeOrientation.ToA_FromB)
                    return RelativeOrientation.ACoversB_Reverse;

                throw new Exception("Invalid relative same/revers for overlapping segments returend");
            }
            if (to_line < 0.005 && from_line < 0.005)
            {
                //if they do, find the relative orientation and then return Same or Reverse
                RelativeOrientation orientation = GetRelativeOrientation_ignoreSame(polylineA, polylineB);
                if (orientation == RelativeOrientation.ToA_ToB || orientation == RelativeOrientation.FromA_FromB)
                    return RelativeOrientation.BCoversA_Same;
                if (orientation == RelativeOrientation.FromA_ToB || orientation == RelativeOrientation.ToA_FromB)
                    return RelativeOrientation.BCoversA_Reverse;

                throw new Exception("Invalid relative same/revers for overlapping segments returend");
            }
            else if ((line_to < 0.005 && to_line < 0.005) || (line_from < 0.005 && from_line < 0.005))
            {
                return RelativeOrientation.Overlapping_Reverse;
            }
            else if ((line_from < 0.005 && to_line < 0.005) || (line_to < 0.005 && from_line < 0.005))
            {
                return RelativeOrientation.Overlapping_Same;
            }
            else if (line_to > 0.005 && line_from > 0.005 && to_line > 0.005 && line_from > 0.005)
            {
                return RelativeOrientation.Disjoint;
            }
            //if they don't, just get the relative orientation
            return GetRelativeOrientation_ignoreSame(polylineA, polylineB);
        }
        private static RelativeOrientation GetRelativeOrientation_ignoreSame(IPolyline polylineA, IPolyline polylineB)
        {
            RelativeOrientation ret = RelativeOrientation.ToA_ToB;
            double min = 0;

            double To_To = min = ((IProximityOperator)polylineA.ToPoint).ReturnDistance(polylineB.ToPoint);
            if (To_To < 0.005)
                return RelativeOrientation.ToA_ToB;


            double From_From = ((IProximityOperator)polylineA.FromPoint).ReturnDistance(polylineB.FromPoint);
            if (From_From < 0.005)
            {
                return RelativeOrientation.FromA_FromB;
            }
            else if (From_From < To_To)
            {
                min = From_From;
                ret = RelativeOrientation.FromA_FromB;
            }


            double From_To = ((IProximityOperator)polylineA.ToPoint).ReturnDistance(polylineB.FromPoint);
            if (From_To < 0.005)
            {
                return RelativeOrientation.ToA_FromB;
            }
            if (From_To < min)
            {
                ret = RelativeOrientation.ToA_FromB;
                min = From_To;
            }


            double To_From = ((IProximityOperator)polylineA.FromPoint).ReturnDistance(polylineB.ToPoint);
            if (To_From < min)
            {
                ret = RelativeOrientation.FromA_ToB;
            }

            return (RelativeOrientation)ret;
        }

        public static double GetSlopeAtStartPoint(IPolyline geometry)
        {
            return GetSlopeAt(geometry, esriSegmentExtension.esriExtendAtFrom, 0.0);
        }
        public static double GetSlopeAtEndPoint(IPolyline geometry)
        {
            return GetSlopeAt(geometry, esriSegmentExtension.esriExtendAtTo, 100.0);
        }
        public static double GetSlopeAt(IPolyline geometry, esriSegmentExtension extension, double percentageAlongLine)
        {
            ILine line = new ESRI.ArcGIS.Geometry.Line();
            geometry.QueryTangent(extension, percentageAlongLine, true, 1.0, line);
            return line.Angle;
        }

        public static double ReverseAngle(double angle)
        {
            return (angle > 0) ? angle - Math.PI : angle + Math.PI;
        }

        public static double DiffAngles(double angleA, double angleB)
        {
            IVector3D vectorA = new Vector3D() as IVector3D;
            vectorA.PolarSet(angleA, 0, 1);

            IVector3D vectorB = new Vector3D() as IVector3D;
            vectorB.PolarSet(angleB, 0, 1);

            return Math.Acos(vectorA.DotProduct(vectorB));
        }

        public static double ToRadians(double degrees)
        {
            return degrees / (180 / Math.PI);
        }
        public static double toDegrees(double radians)
        {
            return radians * (180 / Math.PI);
        }

        public static int GetNextSequenceID(IFeatureClass linesFeatureClass, int parcelID, Dictionary<int, int> idCache = null)
        {
            IFeatureCursor maxCursor = null;
            IRow maxFeat = null;
            try
            {
                if (idCache != null && idCache.ContainsKey(parcelID))
                    return idCache[parcelID]++;

                int maxSequence = 0;
                maxCursor = linesFeatureClass.Search(new QueryFilter()
                {
                    SubFields = String.Format("{0}, {1}, {2}", linesFeatureClass.OIDFieldName, SequenceFieldName, ParcelIDFieldName),
                    WhereClause = String.Format("{0} = {1}", ParcelIDFieldName, parcelID)
                }, true);

                int seqenceIdx = maxCursor.Fields.FindField(SequenceFieldName);

                
                while ((maxFeat = maxCursor.NextFeature()) != null)
                {
                    maxSequence = Math.Max((int)maxFeat.get_Value(seqenceIdx), maxSequence);
                    Marshal.ReleaseComObject(maxFeat);
                }

                if (maxSequence <= 0)
                    throw new Exception("Failed to find max sequence value");

                maxSequence++;
                if (idCache != null)
                    idCache.Add(parcelID, maxSequence);
                return maxSequence;
            }
            finally
            {
                if(maxCursor != null)
                    Marshal.ReleaseComObject(maxCursor);
                if (maxFeat != null)
                    Marshal.ReleaseComObject(maxFeat);
            }
        }
        public static IGeometry CreateNilGeometry(IFeatureClass linesFeatureClass)
        {
            // Create a Nil geometry
            IGeometryFactory3 geometryFactory = new GeometryEnvironmentClass();
            IGeometry geometry = new PolylineClass();
            geometryFactory.CreateEmptyGeometryByType(linesFeatureClass.ShapeType, out geometry);
            IGeometryDef geometryDef = linesFeatureClass.Fields.get_Field(linesFeatureClass.FindField(linesFeatureClass.ShapeFieldName)).GeometryDef;

            if (geometryDef.HasZ)
            {
                IZAware zAware = (IZAware)(geometry);
                zAware.ZAware = true;
            }
            if (geometryDef.HasM)
            {
                IMAware mAware = (IMAware)(geometry);
                mAware.MAware = true;
            }

            return geometry;
        }
    }
}
