/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Collections.Generic;
using ArcG = ArcGIS.Core.Geometry;

namespace HamlProAppModule.haml.geometry {
    
    //Represents the search space of a vertex.
    //Constrains a vertex to a line segment.
    public class SegmentConstraint : IConstraint {
        ArcG.Polyline segment;

        public SegmentConstraint(ArcG.MapPoint p1, ArcG.MapPoint p2) {
            segment = ArcG.PolylineBuilderEx.CreatePolyline(new List<ArcG.MapPoint> {p1, p2});
        }
        
        public SegmentConstraint(List<ArcG.MapPoint> points) {
            segment = ArcG.PolylineBuilderEx.CreatePolyline(points);
        }

        public ArcG.MapPoint GetNearestPoint(ArcG.MapPoint mp) {
            return ArcG.GeometryEngine.Instance.NearestPoint(GetGeometry(), mp).Point;
        }

        public ArcG.MapPoint GetNearestPoint(double x, double y) {
            return GetNearestPoint(ArcG.MapPointBuilderEx.CreateMapPoint(x, y));
        }

        public ArcG.Geometry GetGeometry() {
            return segment;
        }

        public ArcG.MapPoint GetNearestPointWithinBounds(double x, double y, double minX, double minY, double maxX, double maxY) {
            ArcG.Envelope bound = ArcG.EnvelopeBuilderEx.CreateEnvelope(minX, minY, maxX, maxY);
            ArcG.Polyline boundedSegment = ArcG.GeometryEngine.Instance.Intersection(segment, bound) as ArcG.Polyline;
            ArcG.MapPoint point = ArcG.MapPointBuilderEx.CreateMapPoint(x, y);
            
            return ArcG.GeometryEngine.Instance.NearestPoint(boundedSegment, point).Point;
        }
    }
}
