/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using ArcGIS.Desktop.Framework.Threading.Tasks;
using HamlProAppModule.haml.mltool;
using HamlProAppModule.haml.ui.arctool;
using HamlProAppModule.haml.util;

namespace HamlProAppModule.haml.ui.button
{
    class SaveGeometry : ArcGIS.Desktop.Framework.Contracts.Button
    {
        protected override void OnClick()
        {
            // TODO: can we do this async?
            if (Module1.activeHamlMapTool != null)
            {
                HamlMapTool<ProfilePolyline> tool = Module1.activeHamlMapTool as HamlMapTool<ProfilePolyline>;

                // save the profiles
                if (tool is null)
                {
                    GUI.ShowMessageBox("Could not save profiles!", "Save Error!", false);
                }
                else
                {
                    QueuedTask.Run(() =>
                    {
                        GeodatabaseUtil.SaveProfilesToGdb(tool.HamlTool.GetAllProfiles());
                        
                        if (tool is HamlFeatureClassMapTool)
                        {
                    
                            ((HamlFeatureClassMapTool)tool).UpdateBaselinePoint();
                            ((HamlFeatureClassMapTool)tool).SaveBaselinePoint();
                            ((HamlFeatureClassMapTool)tool).HamlTool.ReportAllChanges();
                            ((HamlFeatureClassMapTool)tool).UpdateVisibleProfiles();
                        }
                    });
                }
            }
        }
    }
}
