/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.ui.arctool;

namespace HamlProAppModule.haml.util;

public static class GeomUtil
{
    //GeometryEngine.Instance.Distance method has an issue with spatial references, must build "fresh"
    //map points, even though the original map points have the same SR
    public static double CalcDist(Coordinate3D mp1, Coordinate3D mp2, SpatialReference spatialReference)
    {
        return GeometryEngine.Instance.Distance(mp1.ToMapPoint(spatialReference), mp2.ToMapPoint(spatialReference));
    }
    
    public static double Slope(double x1, double y1, double x2, double y2) {
        var ret = y2-y1;
        ret /= x2-x1;
        return Math.Abs(ret);
    }
    
    public static double SignedSlope(double x1, double y1, double x2, double y2) {
        var ret = y2-y1;
        ret /= x2-x1;
        return ret;
    }

    // Looks for the largest z value along profile p, on the labelside of the given polyline
    public static int GetMaxZIdxNaive(Profile p)
    {
        //Fancier ways to do this, but most either iterate over the data twice or aren't any faster (possibly slower)
        double maxZ = double.MinValue;
        int maxZIdx = -1;

        var points = p.Points.ToList(); 
        
        for (int i = 0; i < points.Count; i++)
        {
            var mp = points[i];
            if (points[i].Z > maxZ)
            {
                maxZIdx = i;
                maxZ = mp.Z;
            }                        
        }
        
        return maxZIdx;
    }
    
    // Looks for the largest z value along profile p, on the labelside of the given polyline
    public static int GetMaxZIdxViaOnLabelSide(Profile p, Polyline polyline, LeftOrRightSide labelSide)
    {
        //Fancier ways to do this, but most either iterate over the data twice or aren't any faster (possibly slower)
        double maxZ = double.MinValue;
        int maxZIdx = -1;

        var points = p.Points.ToList();
        int vertPointIdx = p.GetVertexPointIndex();

        if (vertPointIdx > 0)
        {
            vertPointIdx -= 1;
        }

        bool labelSideIsScreenLeft = GeomUtil.LabelSideLeftOfVertex(p.Points[vertPointIdx].ToMapPoint(), polyline, labelSide);
        
        if (labelSideIsScreenLeft)
        {
            points = points.Take(vertPointIdx).ToList();
        }
        else
        {
            points = points.Skip(vertPointIdx).ToList();
        }
        
        for (int i = 0; i < points.Count; i++)
        {
            var mp = points[i];
            if (points[i].Z > maxZ)
            {
                maxZIdx = labelSideIsScreenLeft ? i : i+vertPointIdx;
                maxZ = mp.Z;
            }                        
        }
        
        // We restrict the maxZIdx to be no closer to the shoreline than 2 elements away, to leave room for the
        // low point to be placed. This is necessary to avoid crashing and should only happen with bad data or bad
        // choice of label side
        if (labelSideIsScreenLeft)
        {
            maxZIdx = maxZIdx > vertPointIdx - 2 ? vertPointIdx - 2 : maxZIdx;
        }
        else
        {
            maxZIdx = maxZIdx < vertPointIdx + 2 ? vertPointIdx + 2 : maxZIdx;
        }
        
        return maxZIdx;
    }


    // Utilize the fact that esri indexes elements from left (on the physical screen) to right, and top to bottom
    // to see if the label side is on screen left or not. This is used to account for the fact that the orientation of
    // polylines changes the esri interpretation of "left" and "right"
    public static bool LabelSideLeftOfVertex(MapPoint mp, Polyline polyline, LeftOrRightSide labelSide)
    {
        GeometryEngine.Instance.QueryPointAndDistance(polyline, SegmentExtensionType.NoExtension, 
            mp, AsRatioOrLength.AsLength, out double _, out double _, 
            out LeftOrRightSide leftElementSide);
        
        return leftElementSide == labelSide;
    }
        
    public static int CalcLowPointViaSlope(Profile p, int maxIndex)
    {
        int vertIdx = p.GetVertexPointIndex();
        double largestChange = 0;

        // if the labelside is screen left, we search upwards from the high point. otherwise, we search downwards.
        int walkDirection = maxIndex > vertIdx ? -1 : 1;
        int idxOfLargestSlopeChange = maxIndex + walkDirection*10;
        
        bool inRange = true;
        int i = maxIndex + walkDirection * 3;
        while(inRange)
        {
            // the negative of walk direction is the direction towards the high point
            double prevSlope = CalcSlopeBetweenPoints(p, i - walkDirection, -walkDirection);
            double currSlope = CalcSlopeBetweenPoints( p, i, -walkDirection);

            if (double.IsNaN(prevSlope)|| double.IsNaN(currSlope))
            {
                return -1;
            }

            double slopeDx = walkDirection*(currSlope - prevSlope);

            // since the walk is performed in the direction away from the high point, we want to find areas where the 
            // dune is sloping downwards, particularly where the dune falls sharply but then begins to level off
            if (slopeDx > largestChange && walkDirection*currSlope < 0)
            {
                largestChange = slopeDx;
                idxOfLargestSlopeChange = i;
            }

            if (walkDirection == 1)
            {
                inRange = i < vertIdx;
            }
            else
            {
                inRange = vertIdx < i;
            }

            i += walkDirection;
        }
        
        // make sure the low point doesnt get placed on top of or on the other side of the shore vertex
        if (walkDirection == 1)
        {
            idxOfLargestSlopeChange = idxOfLargestSlopeChange > vertIdx - 1 ? vertIdx - 1 : idxOfLargestSlopeChange;
        }
        else
        {
            idxOfLargestSlopeChange = idxOfLargestSlopeChange < vertIdx + 1 ? vertIdx + 1 : idxOfLargestSlopeChange;
        }

        // make sure that the low index is not on the wrong side of the shoreline vertex
        if (vertIdx > maxIndex && idxOfLargestSlopeChange >= vertIdx)
        {
            idxOfLargestSlopeChange = vertIdx - 1;
        }
        else if (maxIndex > vertIdx && idxOfLargestSlopeChange <= vertIdx)
        {
            idxOfLargestSlopeChange = vertIdx + 1;
        }
        
        return idxOfLargestSlopeChange;
    }

    private static double CalcSlopeBetweenPoints(Profile p, int idx, int walkDirection)
    {
        int currIdx = idx;
        int nextIdx = idx + walkDirection;

        double slope;
        if (nextIdx < p.Points.Count && nextIdx > 0)
        {
            var curr = p.Points[currIdx];
            var next = p.Points[nextIdx];

            slope = SignedSlope(currIdx, curr.Z, nextIdx, next.Z);    
        }
        else
        {
            slope = double.NaN;
        }
        
        
        return slope;
    }

    // Calculates the unnormalized dot product formed by the two vectors v1->v2 and v2->v3. Positive values of this
    // indicate that the two vectors share a common component (they point at least partially in the same direction)
    public static double CalcTwoSegmentDotProductUnnormalized(Vertex v1, Vertex v2, Vertex v3)
    {
        double ret = 0;

        List<Double> segmentVector1 = new List<double>();
        List<Double> segmentVector2 = new List<double>();

        // x components
        segmentVector1.Add(v2.GetX() - v1.GetX());
        segmentVector2.Add(v3.GetX() - v2.GetX());

        // y components
        segmentVector1.Add(v2.GetY() - v1.GetY());
        segmentVector2.Add(v3.GetY() - v2.GetY());

        for (int i = 0; i < segmentVector1.Count; i++)
        {
            ret += segmentVector1[i] * segmentVector2[i];
        }

        return ret;
    }
    
    public static Envelope? BuildPointExtent(MapPoint mp)
    {
        double len = MapView.Active.Extent.Length;
        double width = MapView.Active.Extent.Width;
        double dist = len > width ? len : width; 
        Geometry ptBuffer = GeometryEngine.Instance.Buffer(mp, dist);

        Polygon? buffer = ptBuffer as Polygon;

        if (buffer != null)
        {
            return EnvelopeBuilderEx.CreateEnvelope(buffer.Extent);    
        }

        return null;
    }

    public static double CalcAngleBetweenPoints(MapPoint p1, MapPoint p2)
    {
        double xDiff = p2.X - p1.X;
        double yDiff = p2.Y - p1.Y;
        return Math.Atan2(yDiff, xDiff);
    }

    /// <summary>
    /// Uses Douglas-Peucker algorithm to simplify a curve using a specified distance threshold.
    ///
    /// This is currently used to create simplified versions of ground truth curves, to serve as comparison to the
    /// curves produced by our iterative human-machine team process.
    ///
    /// NOTE: the returned curve has no restrictions on how dense the vertices can be. Input curves that vary
    /// rapidly/sharply may result in simplified curves that have vertices more closely packed than our minimum profile
    /// spacing for machine insertion. To address this, you should make sure that the input curve has been properly
    /// re-sampled first.
    /// </summary>
    /// <param name="polyline">The input polyline to be simplified</param>
    /// <param name="tolerance">The maximum acceptable error tolerance</param>
    /// <returns>A simplified polyline that has the fewest vertices needed to approximate the original polyline while
    /// respecting the specified error tolerance.</returns>
    public static Polyline SimplifyPolyline(Polyline polyline, double tolerance)
    {
        if (polyline.Points.Count < 2) {
            Module1.ToggleState(PluginState.DoingOperationState, false);
            Module1.ToggleState(PluginState.ResetGeometryState, false);
            throw new ArgumentOutOfRangeException("Not enough points to simplify");
        }

        List<MapPoint> outputMapPoints = new List<MapPoint>();
        Simplify(polyline, tolerance, outputMapPoints);

        return PolylineBuilderEx.CreatePolyline(outputMapPoints, polyline.SpatialReference);

        void Simplify(Polyline polyline, double tolerance, List<MapPoint> output)
        {
            // Find the point with the maximum distance from line between the start and end
            double dmax = 0.0;
            int index = 0;
            int end = polyline.Points.Count - 1;
            for (int i = 1; i < end; ++i) {
                double d = PerpendicularDistance(polyline.Points[i], polyline.Points[0], polyline.Points[end]);
                if (d > dmax) {
                    index = i;
                    dmax = d;
                }
            }

            // If max distance is greater than epsilon, recursively simplify
            if (dmax > tolerance) {
                List<MapPoint> recResults1 = new List<MapPoint>();
                List<MapPoint> recResults2 = new List<MapPoint>();
                Polyline firstLine = PolylineBuilderEx.CreatePolyline(polyline.Points.Take(index + 1).ToList()
                                                                        , polyline.SpatialReference);
                Polyline lastLine = PolylineBuilderEx.CreatePolyline(polyline.Points.Skip(index).ToList()
                                                                        , polyline.SpatialReference);
                Simplify(firstLine, tolerance, recResults1);
                Simplify(lastLine, tolerance, recResults2);

                // build the result list
                output.AddRange(recResults1.Take(recResults1.Count - 1));
                output.AddRange(recResults2);
                if (output.Count < 2) throw new Exception("Problem assembling output");
            }
            else {
                // Just return start and end points
                output.Clear();
                output.Add(polyline.Points[0]);
                output.Add(polyline.Points[^1]);
            }
        }
        
        double PerpendicularDistance(MapPoint pt, MapPoint lineStart, MapPoint lineEnd) {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;

            // Normalize
            double mag = Math.Sqrt(dx * dx + dy * dy);
            if (mag > 0.0) {
                dx /= mag;
                dy /= mag;
            }
            double pvx = pt.X - lineStart.X;
            double pvy = pt.Y - lineStart.Y;

            // Get dot product (project pv onto normalized direction)
            double pvdot = dx * pvx + dy * pvy;

            // Scale line direction vector and subtract it from pv
            double ax = pvx - pvdot * dx;
            double ay = pvy - pvdot * dy;

            return Math.Sqrt(ax * ax + ay * ay);
        }
    }

    // Should yield a Polyline with points:
    // (0, 0, 0)
    // (2, -0.1, 0)
    // (3, 5, 0)
    // (7, 9, 0)
    // (9, 9, 0)
    public static void SimplifyTest(SpatialReference spatialReference)
    {
        List<MapPoint> pointList = new List<MapPoint>() {
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(0.0,0.0,0.0)),
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(1.0,0.1,0.0)),
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(2.0,-0.1,0.0)),
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(3.0,5.0,0.0)),
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(4.0,6.0,0.0)),
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(5.0,7.0,0.0)),
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(6.0,8.1,0.0)),
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(7.0,9.0,0.0)),
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(8.0,9.0,0.0)),
            MapPointBuilderEx.CreateMapPoint(new Coordinate3D(9.0,9.0,0.0))
        };
        Polyline testPolyline = PolylineBuilderEx.CreatePolyline(pointList, spatialReference);
        
        List<MapPoint> pointListOut = new List<MapPoint>();
        Polyline outputPolyline = SimplifyPolyline(testPolyline, 1.0);
        List<MapPoint> outPoints = new List<MapPoint>();
        foreach (MapPoint mp in outputPolyline.Points)
        {
            outPoints.Add(mp);
        }
        int test = 0;
    }
}
