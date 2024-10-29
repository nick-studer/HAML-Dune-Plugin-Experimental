/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.mltool;

namespace HamlProAppModule.haml.ui.arctool
{
    // Enables the polyline sketch tool    
    class HamlSketchPolylineMapTool : HamlMapTool<PerpendicularPolyline>
    {
        HamlSketchPolylineMapTool() : base()
        {
            IsSketchTool = true;
            loadGeometry = false;
            SketchType = SketchGeometryType.Line;
            EsriGeometryType = esriGeometryType.esriGeometryPolyline;
            _featureClassName = Module1.DefaultPolylineFCName;
        }
        
        protected override void InitBaseOverlays()
        {
            // Currently, this class does not have any overlays
        } 

        protected override void InitHamlTool(Envelope mapViewExtent, Geometry? arcGeometry = null)
        {
            if (arcGeometry is Polyline polyline)
            {
                HamlTool = new PerpendicularPolyline(_raster, _learner, polyline, mapViewExtent);
            } 
        }
    }
}
