/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using ArcGIS.Core.Geometry;

namespace HamlProAppModule.haml.geometry {
    //FreeConstraint do not yeild signals but allow vertices to be freely moved.
    //(Not currently in use)
    class FreeConstraint : IConstraint
    {
        private MapPoint mp;
        public FreeConstraint(MapPoint mp)
        {
            this.mp = mp;
        }

        public MapPoint GetNearestPoint(double x, double y) {
            return MapPointBuilderEx.CreateMapPoint(x, y);
        }

        public MapPoint GetNearestPoint(MapPoint mp) {
            return mp;
        }

        public Geometry GetGeometry() {
            return mp;
        }

        public MapPoint GetNearestPointWithinBounds(double x, double y, double minX, double minY, double maxX, double maxY) {
            var newX = x;
            var newY = y;
            
            if (x > maxX) {
                newX = maxX;
            }
            else if (x < minX) {
                newX = minX;
            }

            if (y > maxY) {
                newY = maxY;
            }
            else if (y < minY) {
                newY = minY;
            }

            MapPoint mp = MapPointBuilderEx.CreateMapPoint(newX, newY);

            return mp;
        }
    }
}
