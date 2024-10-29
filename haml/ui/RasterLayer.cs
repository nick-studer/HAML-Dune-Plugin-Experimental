/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using ArcGIS.Desktop.Mapping;

namespace HamlProAppModule.haml.ui
{
    internal struct RasterLayerSettings
    {
        internal Layer Layer { get; }
        internal int MaxBitDepth { get; }
        internal int UserDefinedBitDepth { get; set; }

        public RasterLayerSettings(Layer layer, int maxBitDepth, int userDefinedBitDepth)
        {
            Layer = layer;
            MaxBitDepth = maxBitDepth;
            UserDefinedBitDepth = userDefinedBitDepth;
        }
    }
}
