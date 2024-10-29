/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Geometry;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.logging;
using MLPort;
using GE = ArcGIS.Core.Geometry.GeometryEngine;

namespace HamlProAppModule.haml.mltool {

    class PerpendicularPolygon : PerpendicularGeometry{
        
        //The PerpendicularPolygon HAML Tool is the PPG tool from the sigspatial 2019 manuscript.
        //Within a GIS, search space signals should be generated within the map view upon vertex insertion.
        //This constructor will build the PerpendicularPolygon from a haml polygon.
        public PerpendicularPolygon(Raster raster, KNNLearner learner, HAMLPolygon hamlPolygon, Envelope extent) : 
            base(raster, learner, extent)
        {
            Learner = learner;
            Raster = raster;
            Extent = extent;
            HamlGeometry = hamlPolygon;
            LabelDirSet = true;
        }

        //Builds a PerpendicularPolygon from an Arc polygon.
        public PerpendicularPolygon(Raster raster, KNNLearner learner, Polygon arcPolygon, Envelope extent, bool reloading)
            : base(raster, learner, extent)
        {
            Learner = learner;
            Raster = raster;
            Extent = extent;
            HamlGeometry = new HAMLPolygon(arcPolygon, extent, reloading);
            LabelDirSet = true;
        }

        // Determines label by checking if the signal's MapPoint is within the polygon
        protected internal override double CheckLabel(Geometry g, MapPoint signalLocation)
        {
            return GE.Instance.Contains(g, signalLocation) ? 1.0 : 0.0;
        }

        //Must be run on MCT.
        //AutoGenerateVertices will choose the three longest segments of the HAML
        // contour in the current view and will insert a vertex at each of the 
        // chosen segment's midpoint.
        protected internal override List<int> AutoGenerateVertices(int n)
        {
            List<Vertex> vertices = HamlGeometry.GetVertices();
            List<(int index, double length)> segLengths = new List<(int, double)>();
            List<MapPoint> midPoints = new List<MapPoint>();

            // calculate the segment lengths
            for (int i = 0; i < vertices.Count; i++)
            {
                double d = vertices[i].DistanceSqTo(vertices[(i + 1) % vertices.Count]);
                segLengths.Add((i, d));
            }

            // sort segments by decreasing length
            segLengths.Sort((a, b) => b.length.CompareTo(a.length));

            // calculate the midpoints of the n longest segments (or all segments if less than n)
            for (int i = 0; i < Math.Min(n, segLengths.Count); i++)
            {
                int idx = segLengths[i].index;
                double midX = (vertices[idx].GetX() + vertices[(idx + 1) % vertices.Count].GetX()) / 2;
                double midY = (vertices[idx].GetY() + vertices[(idx + 1) % vertices.Count].GetY()) / 2;
                midPoints.Add(MapPointBuilderEx.CreateMapPoint(midX, midY));
            }

            // train any untrained vertices before adding the new midpoints
            TrainAllUntrainedVertices();

            Envelope mapViewExtent = EnvelopeBuilderEx.CreateEnvelope(ArcGIS.Desktop.Mapping.MapView.Active.Extent);

            // add and refine the new midpoints, processing only those midpoints in the current map view
            List<(Vertex v, double x, double y, int index)> refines = new List<(Vertex v, double x, double y, int index)>();

            foreach (MapPoint point in midPoints)
            {
                if (GE.Instance.Contains(mapViewExtent, point))
                {
                    Vertex v = HamlGeometry.InsertVertex(point, out SegmentConstraint searchSpace, out int vertexIndex);
                }
            }

            // place the refinements
            foreach ((Vertex v, double x, double y, int index) in refines)
            {
                Log.Here().Debug("HAML Message: Guessed vertex location as {#X}, {@Y}",x,y);
                v.Set(x, y);
                Log.Here().Debug("HAML Message: Moved vertex to {@X}, {@Y}",v.GetX(),v.GetY());
            }

            return refines.Select(refined => refined.index).ToList();
        }
        
        protected internal override List<int> RemoveGeneratedVertices()
        {
            throw new NotImplementedException();
        }
        
        protected internal override void SetLabelDir(MapPoint mp) {}
    }
}
