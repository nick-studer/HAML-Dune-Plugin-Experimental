/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Windows.Controls;
using System.Windows.Input;
using HamlProAppModule.haml.ui.arctool;

namespace HamlProAppModule.haml.ui.dockpane;

public partial class ProfileDockPaneView : UserControl
{
    public ProfileDockPaneView()
    {
        InitializeComponent();
    }
    
            
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.M:
                if (Module1.activeHamlMapTool is HamlSketchProfileMapTool)
                {
                    ((HamlSketchProfileMapTool) Module1.activeHamlMapTool).CycleProfile();
                }
                break;
            case Key.N:
                if (Module1.activeHamlMapTool is HamlSketchProfileMapTool)
                {
                    ((HamlSketchProfileMapTool) Module1.activeHamlMapTool).CycleProfile(false);
                }
                break;
            case Key.K:
                if (Module1.activeHamlMapTool is HamlFeatureClassMapTool)
                {
                    ((HamlFeatureClassMapTool) Module1.activeHamlMapTool).AutoGenerateVertices(3); //todo: remove or implement unused argument
                }
                break;
        }

        e.Handled = true;
    }
    
}
