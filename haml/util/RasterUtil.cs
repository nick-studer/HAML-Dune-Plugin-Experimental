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
using ArcGIS.Desktop.Framework.Threading.Tasks;

namespace HamlProAppModule.haml.util;

public static class RasterUtil
{
    public static double GetPixelDistance(Raster r, MapPoint mp1, MapPoint mp2)
    {
        var (x1, y1) = r.MapToPixel(mp1.X, mp1.Y);

        var (x2, y2) = r.MapToPixel(mp2.X, mp2.Y);

        return Math.Sqrt((y1 - y2) * (y1 - y2) + (x1 - x2) * (x1 - x2));
    }

    public static List<Coordinate3D> CreateIntersectionMapCoordinates(Raster r, Polyline p, out List<int> nonNaNIndices)
    {
        // get all (x,y) MapPoints that fall on the constraint
        Polyline test = GeometryEngine.Instance.DensifyByLength(p, 0.25) as Polyline;
        List<Coordinate3D> ret = new List<Coordinate3D>();
        nonNaNIndices = new List<int>();


        Tuple<int,int> prevPoint = null;
        
        foreach (var testPoint in test.Points)
        {
            Tuple<int, int> rasterPx = QueuedTask.Run(()=> r.MapToPixel(testPoint.X, testPoint.Y)).Result;
            if (!rasterPx.Equals(prevPoint))
            {
                var obj = QueuedTask.Run(() => r.GetPixelValue(0, rasterPx.Item1, rasterPx.Item2)).Result;

                float zValue;
                if (obj is not float f)
                {
                    zValue = Convert.ToSingle(obj);
                }
                else
                {
                    zValue = f;
                    nonNaNIndices.Add(ret.Count);

                }

                ret.Add(new Coordinate3D(testPoint.X, testPoint.Y, zValue));
            }

            prevPoint = rasterPx;
        }
        
        return ret;
    }

    //The ArcPro SDK makes it really tough to get the NITF metadata I need
    // (or the process is undocumented).  The returned values are not the true
    // min/max pixel values.
    //E.g. WorldView imagery has 11BPP, but this method will return a max pixel value
    // of ushort.MaxValue.
    public static (decimal, decimal) GetMinMaxPixelValues(RasterPixelType pixelType) {
        switch( pixelType ) {
            case RasterPixelType.UNKNOWN:
                return (new Decimal(0), new Decimal(0));
            case RasterPixelType.U1:
                return (new Decimal(0), new Decimal(1));
            case RasterPixelType.U2:
                return (new Decimal(0), new Decimal(3));
            case RasterPixelType.U4:
                return (new Decimal(0), new Decimal(16));
            case RasterPixelType.UCHAR:
                return (new Decimal(byte.MinValue), new Decimal(byte.MaxValue));
            case RasterPixelType.CHAR:
                return (new Decimal(sbyte.MinValue), new Decimal(sbyte.MaxValue));
            case RasterPixelType.USHORT:
                return (new Decimal(ushort.MinValue), new Decimal(ushort.MaxValue));
            case RasterPixelType.SHORT:
                return (new Decimal(short.MinValue), new Decimal(short.MaxValue));
            case RasterPixelType.ULONG:
                return (new Decimal(ulong.MinValue), new Decimal(ulong.MaxValue));
            case RasterPixelType.LONG:
                return (new Decimal(long.MinValue), new Decimal(long.MaxValue));
            case RasterPixelType.FLOAT:
                //TODO: figure out a better way to handle this
                return (new Decimal(long.MinValue), new Decimal(long.MaxValue));
            case RasterPixelType.DOUBLE:
                return (new Decimal(double.MinValue), new Decimal(double.MaxValue));
            case RasterPixelType.COMPLEX:
                return (new Decimal(0), new Decimal(0));
            case RasterPixelType.DCOMPLEX:
                return (new Decimal(0), new Decimal(0));
            case RasterPixelType.CSHORT:
                return (new Decimal(0), new Decimal(0));
            case RasterPixelType.CLONG:
                return (new Decimal(0), new Decimal(0));
        }

        return (new Decimal(0), new Decimal(0));
    }
    
    public static (decimal, decimal) GetMinMaxPixelValues(RasterPixelType pixelType, int bitDepth) {
        var maxPixelValue = new Decimal(Math.Pow(2, bitDepth) - 1);
        switch (pixelType)
        {
            case RasterPixelType.CHAR:
            case RasterPixelType.SHORT:
            case RasterPixelType.LONG:
            case RasterPixelType.FLOAT:
            case RasterPixelType.DOUBLE:
                var half = (maxPixelValue + 1) / 2;
                maxPixelValue = half - 1;
                var minPixelValue = half * -1;
                return (minPixelValue, maxPixelValue);
            case RasterPixelType.UNKNOWN:
            case RasterPixelType.COMPLEX:
            case RasterPixelType.DCOMPLEX:
            case RasterPixelType.CSHORT:
            case RasterPixelType.CLONG:
                return (new Decimal(0), new Decimal(0));
            case RasterPixelType.U1:
            case RasterPixelType.U2:
            case RasterPixelType.U4:
            case RasterPixelType.UCHAR:
            case RasterPixelType.USHORT:
            case RasterPixelType.ULONG:
            default:
                return (new Decimal(0), maxPixelValue);
        }
    }

        public static double CalcPolygonAverageZ(Raster r, Polygon poly)
        {
            List<double> zList = new List<double>();
            
            for (int i = 0; i<poly.Points.Count; i++)
            {
                var mapPixels = r.MapToPixel(poly.Points[i].X, poly.Points[i].Y);
                var obj = r.GetPixelValue(0, mapPixels.Item1, mapPixels.Item2);

                float zValue;
                if (obj is not float f)
                {
                    zValue = Convert.ToSingle(obj);
                }
                else
                {
                    zValue = f;
                }
                
                zList.Add(zValue);
            }

            return zList.Average();
        }
        
    public static List<Coordinate3D> DomainWalk(Raster raster, Polyline line, out List<int> nonNaNIndices)
    {
        var coords = new List<Coordinate3D>();
        var mapPoints = new List<MapPoint>();
        nonNaNIndices = new List<int>();

        var lineProjected = PolylineBuilderEx.CreatePolyline(line, QueuedTask.Run(raster.GetSpatialReference).Result);
        
        var domainSegment = LineBuilderEx.CreateLineSegment(lineProjected.Points.First(), lineProjected.Points.Last());
        var domainLength = 0.0;

        //This stride does not sample pixels evenly as the Bresenham algorithm would.
        //However, it fits within a geospatial context to occasionally skip pixels and
        // sample at standard distances.
        var cellSize = QueuedTask.Run(raster.GetMeanCellSize).Result;
        var stride = 0.25*(cellSize.Item1 + cellSize.Item2);

        while (domainLength < domainSegment.Length)
        {
            var p = GeometryEngine.Instance.QueryPoint(domainSegment,
                SegmentExtensionType.NoExtension,
                domainLength,
                AsRatioOrLength.AsLength);

            mapPoints.Add(p);
            domainLength += stride;
        }
        
        MapPoint? prevMapPoint = null;
        Tuple<int, int>? prevPixel = null;

        foreach (MapPoint mapPoint in mapPoints)
        {
            // find nearest point on the profile line to make a smooth profile
            var nearestPoint = GeometryEngine.Instance.NearestPoint(lineProjected, mapPoint).Point;

            Tuple<int, int> rasterPx = QueuedTask.Run(() => raster.MapToPixel(nearestPoint.X, nearestPoint.Y)).Result;

            if (prevPixel is null && prevMapPoint is null)
            {
                prevPixel = rasterPx;
                prevMapPoint = nearestPoint;
            }
            else
            {
                if (!prevPixel.Equals(rasterPx))
                {
                    var obj = QueuedTask.Run(() => raster.GetPixelValue(0, prevPixel.Item1, prevPixel.Item2)).Result;

                    float zValue;
                    if (obj is not float f)
                    {
                        zValue = Convert.ToSingle(obj);
                    }
                    else
                    {
                        zValue = f;
                        nonNaNIndices.Add(coords.Count);
                    }

                    coords.Add(new Coordinate3D(prevMapPoint.X, prevMapPoint.Y, zValue));
                }

                prevPixel = rasterPx;
                prevMapPoint = nearestPoint;
            }
        }

        return coords;
    }

    public static double GetZAtCoordinate(Raster r, Coordinate2D coordinate)
    {
        var mapPixels = QueuedTask.Run(()=> r.MapToPixel(coordinate.X, coordinate.Y)).Result;
        var obj = QueuedTask.Run(()=>r.GetPixelValue(0, mapPixels.Item1, mapPixels.Item2)).Result;

        float zValue;
        if (obj is not float f)
        {
            zValue = Convert.ToSingle(obj);
        }
        else
        {
            zValue = f;
        }

        return zValue;
    }
}
