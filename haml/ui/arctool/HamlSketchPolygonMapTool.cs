/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Windows;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.mltool;
using MessageBox = ArcGIS.Desktop.Framework.Dialogs.MessageBox;

namespace HamlProAppModule.haml.ui.arctool
{
    // Enables the polygon sketch tool
    class HamlSketchPolygonMapTool : HamlMapTool<PerpendicularPolygon>
    {
        HamlSketchPolygonMapTool() : base()
        {
            IsSketchTool = true;
            loadGeometry = false;
            SketchType = SketchGeometryType.Polygon;
            EsriGeometryType = esriGeometryType.esriGeometryPolygon;
            _featureClassName = Module1.DefaultPolygonFCName;
        }

        protected override void InitHamlTool(Envelope mapViewExtent,Geometry? arcGeometry = null)
        {
            HamlTool = new PerpendicularPolygon(_raster, _learner, arcGeometry as Polygon, mapViewExtent, false);
        }

        protected override void InitBaseOverlays()
        {
            // Currently, this class does not have any overlays
        }

        protected override bool Validate(Geometry g)
        {
            if (g.PointCount < 3) // handle case for invalid polygon vertex count
            {
                MessageBox.Show("Surfaces must have at least three vertices!", "Sketch Error", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.None);
                return false;
            }

            return true;
        }
    }
}
