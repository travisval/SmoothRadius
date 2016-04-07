using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.GeoDatabaseExtensions;
using System.Runtime.InteropServices;
using ESRI.ArcGIS.Editor;
using ESRI.ArcGIS.esriSystem;

namespace SmoothRadiusAddin
{
    public static class FabricFunctions
    {
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

        public static bool StartCadastralEditOperation(IEditor editor, ICadastralFabric fabric, IEnumerable<int> parcelsToLock, params esriCadastralFabricTable[] tables)
        {
            bool bIsFileBasedGDB = false;
            bool bIsUnVersioned = false;
            bool bUseNonVersionedDelete = false;
            IWorkspace pWS = ((IDataset)fabric).Workspace;

            if (!SetupEditEnvironment(pWS, fabric, null, out bIsFileBasedGDB, out bIsUnVersioned, out bUseNonVersionedDelete))
            {
                System.Windows.Forms.MessageBox.Show("The editing environment could not be initialized");
                return false;
            }

            #region Create Cadastral Job
            string sTime = "";
            if (!bIsUnVersioned && !bIsFileBasedGDB)
            {
                //see if parcel locks can be obtained on the selected parcels. First create a job.
                DateTime localNow = DateTime.Now;
                sTime = Convert.ToString(localNow);
                ICadastralJob pJob = new CadastralJob();
                pJob.Name = sTime;
                pJob.Owner = System.Windows.Forms.SystemInformation.UserName;
                pJob.Description = "Convert lines to curves";
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
            }
            #endregion

            #region Test for Edit Locks
            ICadastralFabricLocks pFabLocks = (ICadastralFabricLocks)fabric;

            //only need to get locks for parcels that have lines that are to be changed

            //IFIDSet parcelFIDs = new FIDSet();
            ILongArray affectedParcels = new LongArray();
            foreach (int i in parcelsToLock)
            {
                //parcelFIDs.Add(i);
                affectedParcels.Add(i);
            }

            if (!bIsUnVersioned && !bIsFileBasedGDB)
            {
                pFabLocks.LockingJob = sTime;
                ILongArray pLocksInConflict = null;
                ILongArray pSoftLcksInConflict = null;

                try
                {
                    pFabLocks.AcquireLocks(affectedParcels, true, ref pLocksInConflict, ref pSoftLcksInConflict);
                }
                catch (COMException pCOMEx)
                {
                    if (pCOMEx.ErrorCode == (int)fdoError.FDO_E_CADASTRAL_FABRIC_JOB_LOCK_ALREADY_EXISTS ||
                        pCOMEx.ErrorCode == (int)fdoError.FDO_E_CADASTRAL_FABRIC_JOB_CURRENTLY_EDITED)
                    {
                        System.Windows.Forms.MessageBox.Show("Edit Locks could not be acquired on all selected parcels.");
                        // since the operation is being aborted, release any locks that were acquired
                        pFabLocks.UndoLastAcquiredLocks();
                    }
                    else
                        System.Windows.Forms.MessageBox.Show(pCOMEx.Message + Environment.NewLine + Convert.ToString(pCOMEx.ErrorCode));

                    return false;
                }
            }
            #endregion

            if (editor.EditState == esriEditState.esriStateEditing)
            {
                try
                {
                    editor.StartOperation();
                }
                catch
                {
                    editor.AbortOperation();//abort any open edit operations and try again
                    editor.StartOperation();
                }
            }
            if (bUseNonVersionedDelete)
            {
                if (!StartEditing(pWS, bIsUnVersioned))
                {
                    System.Windows.Forms.MessageBox.Show("Couldn't start an edit session");
                    return false;
                }
            }

            ICadastralFabricSchemaEdit2 pSchemaEd = (ICadastralFabricSchemaEdit2)fabric;
            foreach (esriCadastralFabricTable tableType in tables)
            {
                pSchemaEd.ReleaseReadOnlyFields(fabric.get_CadastralTable(tableType), tableType);
            }
            return true;

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

    }
}
