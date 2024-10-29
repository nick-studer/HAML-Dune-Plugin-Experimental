/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using GE = ArcGIS.Core.Geometry.GeometryEngine;

namespace HamlProAppModule.haml.geometry
{
    public abstract class HAMLGeometry
    {
        protected List<Vertex> vertices;
        protected Envelope extent;
        protected Raster raster;

        public HAMLGeometry(Envelope extent)
        {
            vertices = new List<Vertex>();
            this.extent = extent;
        }

        //Takes a vertex's insertion point and carves out a search space for it.
        //The search space is cut by the map view and the HAML contour.
        public virtual Vertex InsertVertex(MapPoint clickPoint, out SegmentConstraint constraint, out int vertexIndex) {
            Multipart multipart = GetArcGeometry() as Multipart;
            ProximityResult pr = GeometryEngine.Instance.NearestPoint(multipart, clickPoint);
            constraint = null;
            Vertex newVertex = null;
            vertexIndex = -1;
        
            int? segmentIndex = pr.SegmentIndex;
            
            if ( segmentIndex.HasValue ) {
                //CJM: Arc SDK makes it wonky to get segments by index.
                vertexIndex = segmentIndex.Value + 1;
                
                GE.Instance.QueryPointAndDistance(multipart,
                    SegmentExtensionType.NoExtension, clickPoint,
                    AsRatioOrLength.AsRatio,
                    out var domainRatio, out _, out _);

                if (domainRatio == 0 || domainRatio == 1)
                {
                    vertexIndex = domainRatio == 1 ? vertices.Count : 0;  
                }
                
                newVertex = new Vertex(clickPoint);
                vertices.Insert(vertexIndex, newVertex);
            }

            return newVertex;
        }
        
        public Vertex CreateVertex(MapPoint clickPoint, out IConstraint constraint, out int vertexIndex, bool buildConstraint=true) {
            Multipart multipart = GetArcGeometry() as Multipart;
            ProximityResult pr = GeometryEngine.Instance.NearestPoint(multipart, clickPoint);
            constraint = null;
            Vertex newVertex = null;
            vertexIndex = -1;
        
            int? segmentIndex = pr.SegmentIndex;
            
            if ( segmentIndex.HasValue ) {
                //CJM: Arc SDK makes it wonky to get segments by index.
                vertexIndex = segmentIndex.Value + 1;
                ICollection<Segment> segmentCollection = new List<Segment>();
                multipart.GetAllSegments(ref segmentCollection);
                IList<Segment> segmentIList = segmentCollection as IList<Segment>;

                Segment domainSegment = segmentIList[segmentIndex.Value];
                
                //Create the cut set
                segmentIList.RemoveAt(segmentIndex.Value);

                double domainRatio;
                if (buildConstraint)
                {
                    constraint = BuildConstraint(domainSegment, clickPoint, segmentIList, out domainRatio);
                }
                else
                {
                    constraint = new FreeConstraint(clickPoint);
                    GE.Instance.QueryPointAndDistance(domainSegment,
                        SegmentExtensionType.NoExtension, clickPoint,
                        AsRatioOrLength.AsRatio,
                        out domainRatio, out _, out _);
                }
                
                if (domainRatio == 0 || domainRatio == 1)
                {
                    vertexIndex = domainRatio == 1 ? vertices.Count : 0;  
                }
                
                newVertex = new Vertex(clickPoint, constraint);
            }

            return newVertex;
        }
        
        public void RemoveVertex(int vertexIndex)
        {
            vertices.RemoveAt(vertexIndex);
        }

        public abstract SegmentConstraint BuildConstraint(Segment domainSegment, MapPoint clickPoint, IList<Segment> segmentIList, out double domainRatio);
        
        //Moves the vertex as close to x,y within the vertex's search space.
        public void MoveVertex(int index, double x, double y)
        {
            MapPoint mp = MapPointBuilderEx.CreateMapPoint(x, y);
            vertices[index].Set(mp);
        }

        //Creates a search space starting at pole, intersecting the given point,
        // and extending to the given extent.
        protected SegmentConstraint GetConstraint(MapPoint pole, MapPoint vertexLocation)
        {
            LineSegment unextendedSegment = LineBuilderEx.CreateLineSegment(pole, vertexLocation, MapView.Active.Map.SpatialReference);
            Polyline unextendedPolyline = PolylineBuilderEx.CreatePolyline(unextendedSegment, MapView.Active.Map.SpatialReference);
            Polyline envelopePolyline = PolylineBuilderEx.CreatePolyline(PolygonBuilderEx.CreatePolygon(extent), MapView.Active.Map.SpatialReference);
            Polyline segment = GE.Instance.Extend(unextendedPolyline, envelopePolyline, ExtendFlags.RelocateEnds | ExtendFlags.NoExtendAtFrom);
            segment = GE.Instance.SimplifyPolyline(segment, SimplifyType.Network);

            return new SegmentConstraint(segment.Points.First(), segment.Points.Last());
        }

        //Creates a search space given a segment and a ratio-based location within the segment.
        //The search space is then cut by the given list of segments.
        protected SegmentConstraint GetConstraint(IList<Segment> cuts, Segment domain, double ratio)
        {
            LineSegment arcNormal = GE.Instance.QueryNormal(domain, SegmentExtensionType.NoExtension, ratio, AsRatioOrLength.AsRatio, 0.5 * domain.Length);
            Polyline arcNormalPolyline = PolylineBuilderEx.CreatePolyline(arcNormal, MapView.Active.Map.SpatialReference);
            Polyline envelopePolyline = PolylineBuilderEx.CreatePolyline(PolygonBuilderEx.CreatePolygon(extent), MapView.Active.Map.SpatialReference);
            Polyline domainPolyline = PolylineBuilderEx.CreatePolyline(domain, MapView.Active.Map.SpatialReference);
            Polyline constraint = GE.Instance.Extend(arcNormalPolyline, envelopePolyline, ExtendFlags.RelocateEnds);
            constraint = GE.Instance.SimplifyPolyline(constraint, SimplifyType.Network);
            
            foreach (LineSegment arcSegment in cuts)
            {
                Polyline cutter = PolylineBuilderEx.CreatePolyline(arcSegment, constraint.SpatialReference);
                List<Geometry> cutGeometry = GE.Instance.Cut(constraint, cutter) as List<Geometry>;

                if (cutGeometry.Count() > 1)
                {
                    constraint = GE.Instance.Intersects((Polyline)cutGeometry[0], domainPolyline) == true
                               ? (Polyline)cutGeometry[0] : (Polyline)cutGeometry[1];
                }
            }

            Debug.WriteLine("HAML message: SearchSpace start is " + constraint.Points.First().X + "," + constraint.Points.First().Y);

            return new SegmentConstraint(constraint.Points.Last(), constraint.Points.First());
        }
        
		public abstract Geometry GetArcGeometry();

        public List<MapPoint> GetMapPoints()
        {

            return vertices.Select(l => FixInvalidZ(l.GetPoint())).ToList();
        }

        protected MapPoint FixInvalidZ(MapPoint mp)
        {
            var coord3d = mp.Coordinate3D;
            if (double.IsNaN(coord3d.Z))
            {
                coord3d.Z = 0;
                mp = coord3d.ToMapPoint(mp.SpatialReference);
            }

            return mp;
        }

        public List<Vertex> GetVertices()
        {

            return vertices;
        }

        public Polyline GetArcPolylineOfSearchSpaces()
        {
            List<Polyline> constraintPolylines = new List<Polyline>();

            foreach (Vertex v in vertices)
            {
                if (v.GetConstraint() is SegmentConstraint)
                {
                    constraintPolylines.Add(v.GetConstraint().GetGeometry() as Polyline);    
                }
            }

            return PolylineBuilderEx.CreatePolyline(constraintPolylines);
        }

        public Raster Raster
        {
            get => raster;
            set => raster = value;
        }

        public void SetExtent(Envelope newExtent)
        {
            extent = newExtent;
        }
    }
}
