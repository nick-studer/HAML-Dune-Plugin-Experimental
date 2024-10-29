/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;

namespace HamlProAppModule.haml.util;

public class DebugUtil
{
    public static List<Tuple<int, int>> ConvertPolylineMpToRasterPx(Raster raster, Polyline p)
    {
        return p.Points.Select(mp => raster.MapToPixel(mp.X, mp.Y)).ToList();
    }

    public static List<Tuple<int, int>> ConvertExtentToRasterPxPoints(Raster raster, Envelope e)
    {
        List<Tuple<int, int>> ret = new List<Tuple<int, int>>();
        
        ret.Add(raster.MapToPixel(e.XMin, e.YMax));
        ret.Add(raster.MapToPixel(e.XMax, e.YMax));
        ret.Add(raster.MapToPixel(e.XMax, e.YMin));
        ret.Add(raster.MapToPixel(e.XMin, e.YMin));

        return ret;
    }
    
    public static List<Point> ConvertExtentToScreenPxPoints(Envelope e)
    {
        List<Point> ret = new List<Point>();
        
        ret.Add(MapView.Active.MapToScreen(MapPointBuilderEx.CreateMapPoint(e.XMin, e.YMax, MapView.Active.Extent.SpatialReference)));
        ret.Add(MapView.Active.MapToScreen(MapPointBuilderEx.CreateMapPoint(e.XMax, e.YMax, MapView.Active.Extent.SpatialReference)));
        ret.Add(MapView.Active.MapToScreen(MapPointBuilderEx.CreateMapPoint(e.XMax, e.YMin, MapView.Active.Extent.SpatialReference)));
        ret.Add(MapView.Active.MapToScreen(MapPointBuilderEx.CreateMapPoint(e.XMin, e.YMin, MapView.Active.Extent.SpatialReference)));

        return ret;
    }
    
    public static List<Tuple<int, int>> ConvertSegmentToRasterPxPoints(Raster raster, Segment s)
    {
        List<Tuple<int, int>> ret = new List<Tuple<int, int>>();
        
        ret.Add(raster.MapToPixel(s.StartPoint.X, s.StartPoint.Y));
        ret.Add(raster.MapToPixel(s.EndPoint.X, s.EndPoint.Y));

        return ret;
    }

    public static List<Point> ConvertSegmentToScreenPxPoints(Segment s)
    {
        List<Point> ret = new List<Point>();
        
        ret.Add(MapView.Active.MapToScreen(s.StartPoint));
        ret.Add(MapView.Active.MapToScreen(s.EndPoint));

        return ret;
    }

    public static List<Tuple<int, int>> ConvertMapPointsToRasterPoints(Raster r, List<MapPoint> points)
    {
        List<Tuple<int, int>> ret = new List<Tuple<int, int>>();
        
        points.ForEach(mp => ret.Add(r.MapToPixel(mp.X, mp.Y)));

        return ret;
    }
}
