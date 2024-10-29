/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Collections.Generic;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using GE = ArcGIS.Core.Geometry.GeometryEngine;

namespace HamlProAppModule.haml.geometry
{
    public class HAMLPolyline : HAMLGeometry
    {
        public HAMLPolyline(Polyline arcPolyline, Envelope extent) : base(extent)
        {
            if (arcPolyline != null)
            {
            	Polyline template = arcPolyline;
	            foreach (MapPoint templateVertexCoordinate in template.Points)
	            {
	                vertices.Add(new Vertex(templateVertexCoordinate));
	            }
            }
        }
        
        protected internal void BuildConstraint(Vertex v, int vIdx)
        {
            if (vIdx == 0)
            {
                v.SetConstraint(BuildConstraint(vIdx, vIdx + 1)); // we are dealing with an endpoint
            }
            else if (vIdx == vertices.Count - 1) // we are dealing with an endpoint
            {
                v.SetConstraint(BuildConstraint(vIdx, vIdx - 1));
            }
            else // we are not dealing with an endpoint
            {
                v.SetConstraint(BuildConstraint(vIdx - 1, vIdx, vIdx + 1));
            }
        }

        // Walks over the initially sketched polyline and creates search spaces/constraints for each index
        protected internal void BuildConstraints()
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                Vertex v = vertices[i];
                if (v.GetConstraint() is FreeConstraint && GeometryEngine.Instance.Within(v.GetPoint(), MapView.Active.Extent))
                {
                    BuildConstraint(v, i);
                }
            }
        }

        // Creates the search space for the vertices in between the first and last vertices. A line is created between  
        // the neighboring vertices, then its normal is calculated, which is the constraint for the middle vertex. 
        protected SegmentConstraint BuildConstraint(int v1Idx, int v2Idx, int v3Idx)
        {
            MapPoint prev = vertices[v1Idx].GetPoint();
            MapPoint curr = vertices[v2Idx].GetPoint();
            MapPoint next = vertices[v3Idx].GetPoint();
            return BuildConstraint(prev, curr, next);
        }
        
        public SegmentConstraint BuildConstraint(MapPoint prev, MapPoint curr, MapPoint next)
        {
            // create segment connecting previous and next
            Segment prevToNext = LineBuilderEx.CreateLineSegment(prev, next, MapView.Active.Map.SpatialReference);
            GE.Instance.QueryPointAndDistance(prevToNext, SegmentExtensionType.NoExtension, curr,
                AsRatioOrLength.AsRatio, out double distanceAlongCurve, out _, out _);

            return BuildConstraint(prevToNext, distanceAlongCurve);
        }
        
        // Creates search space in for the first and last vertices of the sketched polyline. The orthogonal line is
        // calculated, then is set to the constraint of v1IDx
        public SegmentConstraint BuildConstraint(int v1Idx, int v2Idx)
        {
            MapPoint curr = vertices[v1Idx].GetPoint();
            MapPoint next = vertices[v2Idx].GetPoint();
            
            // Create segment between curr and next vertices
            Segment seg = LineBuilderEx.CreateLineSegment(curr, next, MapView.Active.Map.SpatialReference);

            return BuildConstraint(seg);
        }

        private SegmentConstraint BuildConstraint(Segment seg, double distAlongCurve = 0)
        {
            // Build extent polyline to extend search spaces to
            Polyline envelopePolyline = PolylineBuilderEx.CreatePolyline(PolygonBuilderEx.CreatePolygon(extent),
                MapView.Active.Map.SpatialReference);
            
            // TODO: hardcoded normal length, should not be an issue unless user is unconventionally zoomed in 
            LineSegment arcNormal = GE.Instance.QueryNormal(seg, SegmentExtensionType.NoExtension, distAlongCurve,
                AsRatioOrLength.AsRatio, 10); 

            // get normal of prev --> next segment 
            Polyline arcNormalPolyline = PolylineBuilderEx.CreatePolyline(arcNormal, MapView.Active.Map.SpatialReference);
            
            // Extend normal to bounds of imageview
            Polyline constraintPolyline =
                GE.Instance.Extend(arcNormalPolyline, envelopePolyline, ExtendFlags.RelocateEnds);
            constraintPolyline = GE.Instance.SimplifyPolyline(constraintPolyline, SimplifyType.Network);
            
            return new SegmentConstraint(constraintPolyline.Points[0], constraintPolyline.Points[1]);
        }
        
        // Finds nearest point on segment in polygon, then creates constraint orthogonal to the the original segment. If
        // nearest point is on edge of polyline, the polyline is then extended based on the last existing segment 
        // extending to the mapview window, then creating a search space orthogonal to said extension.
        public override SegmentConstraint BuildConstraint(Segment domainSegment, MapPoint clickPoint, IList<Segment> segmentIList, out double domainRatio)
        {
            Segment baseSegment = domainSegment;
            MapPoint nearestDomainPoint = GE.Instance.QueryPointAndDistance(baseSegment,
                SegmentExtensionType.NoExtension, clickPoint,
                AsRatioOrLength.AsRatio,
                out domainRatio, out _, out _);
            
            if (domainRatio == 0 || domainRatio == 1) // handles case of extending the polyline
            {
                //extend the polyline to the edge of the map extent, then find the nearest point
                Polyline unextendedPolyline =
                    PolylineBuilderEx.CreatePolyline(domainSegment, MapView.Active.Map.SpatialReference);
                Polyline envelopePolyline = PolylineBuilderEx.CreatePolyline(PolygonBuilderEx.CreatePolygon(extent),
                    MapView.Active.Map.SpatialReference);
                Polyline extendedPolyline =
                    GE.Instance.Extend(unextendedPolyline, envelopePolyline, ExtendFlags.RelocateEnds);
                extendedPolyline = GE.Instance.SimplifyPolyline(extendedPolyline, SimplifyType.Network);

                // find the nearest point to the extension
                MapPoint extNearestPoint = GE.Instance.QueryPointAndDistance(extendedPolyline,
                    SegmentExtensionType.NoExtension, clickPoint,
                    AsRatioOrLength.AsRatio,
                    out _, out _, out _);
                
                if (domainRatio == 1) // Initialize base segment depending on which side of the line is extending
                {
                    baseSegment =
                        LineBuilderEx.CreateLineSegment(nearestDomainPoint, extNearestPoint,
                            MapView.Active.Map.SpatialReference);
                }
                else if (domainRatio == 0)
                {
                    baseSegment =
                        LineBuilderEx.CreateLineSegment(extNearestPoint, nearestDomainPoint,
                            MapView.Active.Map.SpatialReference);

                }
                
                return GetConstraint(segmentIList, baseSegment, domainRatio);  
            }
            
            return GetConstraint(segmentIList, baseSegment, domainRatio);  
        }
        
        public override Geometry GetArcGeometry()
        {
            return PolylineBuilderEx.CreatePolyline(GetMapPoints(), MapView.Active.Map.SpatialReference);
        }
    }
}
