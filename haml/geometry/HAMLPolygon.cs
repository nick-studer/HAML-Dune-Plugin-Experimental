/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Geometry;
using GE = ArcGIS.Core.Geometry.GeometryEngine;

namespace HamlProAppModule.haml.geometry {
    public class HAMLPolygon : HAMLGeometry
    {

        //Initializes a HAML Polygon given an Arc Polygon.
        //To create a polygon from the template, some simplifications are enforced.
        //The convex hull of the given template is used to simplify the template.
        //A pole is marked at the centroid of the convex hull.
        //This will minimize the chances that deformations cause non-simple polygons
        // while best-guessing well-formed search spaces.
        public HAMLPolygon(Polygon arcPolygon, Envelope extent, bool reloading) : base(extent)
        {
            Polygon template = null;
            if (reloading)
            {
                template = arcPolygon;
            }
            else
            {
                template = GE.Instance.ConvexHull(arcPolygon) as Polygon;
            }

            if (template != null)
            {
                MapPoint pole = GE.Instance.Centroid(template);

                foreach (MapPoint templateVertexCoordinate in template.Points.Take(template.PointCount - 1))
                {
                    SegmentConstraint searchSpace = GetConstraint(pole, templateVertexCoordinate);
                    vertices.Add(new Vertex(templateVertexCoordinate, searchSpace));
                }
            }
        }
        
        // Finds nearest point on segment in polygon, then creates constraint orthogonal to the the original segment 
        public override SegmentConstraint BuildConstraint(Segment domainSegment, MapPoint clickPoint, IList<Segment> segmentIList, out double domainRatio)
        {
            GE.Instance.QueryPointAndDistance(domainSegment, SegmentExtensionType.NoExtension, clickPoint, AsRatioOrLength.AsRatio,
                out domainRatio, out _, out _);
            
            return GetConstraint(segmentIList, domainSegment, domainRatio);
        }
        

        public override Geometry GetArcGeometry()
        {
            return PolygonBuilderEx.CreatePolygon(GetMapPoints());
        }
    }
}
