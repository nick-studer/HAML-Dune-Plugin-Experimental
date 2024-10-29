/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using OxyPlot;
using OxyPlot.Series;

namespace HamlProAppModule.haml.ui.dockpane
{

    public class VerticalBarItem : BarItem
    {
        public double dist;
        public VerticalBarItem(double x, double y, double shorelineDist, OxyColor color , int categoryIndex = -1)
        {
            dist = shorelineDist;
            Value = y;
            X = x;
            Color = color;
            CategoryIndex = categoryIndex;
        }
        
        public double X { get; set; }

        public double Dist
        {
            get => dist;
            set => dist = value;
        }
    }
}
