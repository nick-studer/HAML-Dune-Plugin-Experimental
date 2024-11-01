﻿/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.mltool;
using HamlProAppModule.haml.ui.options;
using HamlProAppModule.haml.util;

namespace HamlProAppModule.haml.ui.arctool;

public class HamlFeatureClassMapTool : HamlSketchProfileMapTool
{
    protected double extendDist;
    protected bool _initProfilesPlaced;
    protected MapPoint? _hoveredMHWPoint;
    protected MapPoint? _resetPoint;
    protected Dictionary<Vertex, List<Coordinate3D>> _initBaselineExtentIntersections;
    protected List<double> _initDists;
    protected List<int> _initSegIndices;

    protected HamlFeatureClassMapTool()
    {
        IsSketchTool = false;
        loadGeometry = false;
        SketchType = SketchGeometryType.Line;
        EsriGeometryType = esriGeometryType.esriGeometryPolyline;
        _featureClassName = Module1.DefaultPolylineFCName;
        extendDist = 50; // TODO: should probably be a parameter MUST BE BIGGER THAN step!
        _initProfilesPlaced = false;
    }

    protected override void InitHamlTool(Envelope mapViewExtent, Geometry? arcGeometry = null)
    {
        if (arcGeometry == null || _baselinePoint == null) return;
        
        // The reset point is only set (ie null) when the tool is first activated. If the user resets the tool, the 
        // reset point should not change
        _resetPoint ??= _baselinePoint;
            
        // Get orthogonal intersections from the baseline and the current extent
        var tempIntersections = FindOrthogonalIntersections(fcPoly, _baselinePoint, 
            extendDist, out var initDists, out var segIndices);

        _initDists = initDists;
        _initSegIndices = segIndices;
            
        // build shoreline based on these intersections and the MHW value
        Polyline shoreline = BuildMHWShoreline(tempIntersections);

        // Tool is being initialized in a mapview that has poor starting conditions 
        if (shoreline.Points.Count <= 1)
        {
            ShowExtendErrorMessage();
            FrameworkApplication.SetCurrentToolAsync(null);
        }
            
        HamlTool = new ProfilePolyline(_raster, _learner, shoreline, mapViewExtent);
        
        var intersectionPoint = GeometryEngine.Instance.Intersection(
            PolylineBuilderEx.CreatePolyline(tempIntersections.Last(), fcPoly.SpatialReference),
            fcPoly, GeometryDimensionType.EsriGeometry0Dimension) as Multipoint;

        _baselinePoint = intersectionPoint.Points.First();

        _initBaselineExtentIntersections = new Dictionary<Vertex, List<Coordinate3D>>();
        
        // Assign baseline seg idx to vertices so we can separate them
        for (int i = 0; i < HamlTool.GetVertices().Count; i++)
        {
            var vertex = HamlTool.GetVertices()[i];
            vertex.BaselineOid = _baselineOID.Value;
            vertex.BaselineSegIdx = _initSegIndices[i];
            _initBaselineExtentIntersections.Add(vertex, tempIntersections[i]);
        }
        
        Dictionary<int, List<Vertex>> vertexSegs = new Dictionary<int, List<Vertex>>();
        
        // Separate vertices into lists based on their seg idx
        HamlTool.GetVertices().ForEach(v =>
        {
            if (!vertexSegs.ContainsKey(v.BaselineSegIdx))
            {
                vertexSegs.Add(v.BaselineSegIdx, new List<Vertex>());
            }
            
            vertexSegs[v.BaselineSegIdx].Add(v);
        });

        // Each list of vertices should be in the correct order for that set
        // Once we can get a correctly ordered set into the haml tool we can then insert the rest
        // and have them correctly placed
        var list = vertexSegs.ToList().First(kvp => kvp.Value.Count > 1);
        HamlTool.GetVertices().Clear();
        HamlTool.GetVertices().AddRange(list.Value);
        vertexSegs.Remove(list.Key);
        vertexSegs.Values.ToList().ForEach(l => l.ForEach(v => HamlTool.InsertAndPlaceVertex(v.GetPoint())));

        InitBaseOverlays();
        Module1.ToggleState(PluginState.ContourAvailableState, true);
    }

    protected override void OnToolDeactivate()
    {
        mhwChosen = false;
        _initProfilesPlaced = false;
        initialized = false;
        _baselinePoint = _resetPoint;
        base.OnToolDeactivate();
    }

    protected override async void OnToolMouseMove(MapViewMouseEventArgs e)
    {
        if (!mhwChosen)
        {
            await QueuedTask.Run(() =>
            {
                MapPoint clientPoint = MapView.Active.ClientToMap(e.ClientPoint);

                // must set which cand MHW point is the best point
                if (candMHWPoints.Count > 1)
                {
                    double bestDist = Double.MaxValue;
                    MapPoint? bestPoint = null;

                    bool nearby = false;
                    candMHWPoints.ForEach(mp =>
                    {
                        double dist = Math.Abs(GeometryEngine.Instance.Distance(clientPoint, mp));

                        if (dist < 10 && dist < bestDist)
                        {
                            bestDist = dist;
                            bestPoint = mp;
                            nearby = true;
                        }
                    });

                    // if the mouse isn't near any of the cand MHW points, clear the overlay
                    if (!nearby)
                    {
                        _hoveredMHWPoint = null;
                        OverlayController.Remove(HamlGraphicType.HoveredMHWPoint);
                        bestPoint = null;
                    }

                    // if the mouse is near a candidate point, update the overlay
                    if ((bestPoint != null && bestPoint != _hoveredMHWPoint) || _hoveredMHWPoint == null)
                    {
                        _hoveredMHWPoint = bestPoint;

                        String color = candMHWPoints.IndexOf(_hoveredMHWPoint) == 0
                            ? ColorSettings.DefaultBlue
                            : ColorSettings.DefaultMagenta;
                        
                        OverlayController.AddOverlay(_hoveredMHWPoint,
                            Module1.CreateMHWPointSymbol(color, 20),
                            HamlGraphicType.HoveredMHWPoint);
                    }
                }
            });
        }
        else
        {
            base.OnToolMouseMove(e);    
        }
    }

    protected override async Task HandleDoubleClickAsync(MapViewMouseButtonEventArgs e)
    {
        if (mhwChosen || candMHWPoints.Count < 1) return;
        
        // There were more than one candidate MHW Points, so the user must double click to select one of them 
        // before the tool is initialized
        QueuedTask.Run(() =>
        {
            Module1.ToggleState(PluginState.DoingOperationState, true);
            
            MapPoint mapPoint = MapView.Active.ClientToMap(e.ClientPoint);
            
            SetMHWPointAndClearOverlays(mapPoint);

            //TODO: need to see how many points are in the tool, else the "choose label sides" is borked
            // due to only one point being in the tool
            if (HamlTool == null)
            {
                InitHamlTool(MapView.Active.Extent, fcPoly);
            }
            
            // detect special case when reset was clicked before the user saved the first refine
            if (_initProfilesPlaced && HamlTool.GetVertices().Count() == 1)
            {
                RecoverFromResetBeforeInitialization();
            }

            AutoSetLabelSide();

            if (!initialized)
            {
                InitializeShoreline();
            }
                
            UpdatePluginStates();
        });
        
        void SetMHWPointAndClearOverlays(MapPoint mapPoint)
        {
            double bestDist = double.MaxValue;
            _useFirstMHWPoint = true;
            
            candMHWPoints.ForEach(mp =>
            {
                double dist = Math.Abs(GeometryEngine.Instance.Distance(mapPoint, mp));
                    
                if (dist < 10 && dist < bestDist)
                {
                    bestDist = dist;

                    if (!mp.Equals(candMHWPoints[0]))
                    {
                        _useFirstMHWPoint = false;
                    }
                }
            });
            
            mhwChosen = true;
                
            OverlayController.Remove(HamlGraphicType.SelectMHWPointText);
            OverlayController.Remove(HamlGraphicType.CandHighMHWPoint);
            OverlayController.Remove(HamlGraphicType.CandLowMHWPoint);
            OverlayController.Remove(HamlGraphicType.HoveredMHWPoint);
            candMHWPoints.Clear();
        }
        
        // extend the shoreline w/o adding profiles and essentially act full re-initialization of the
        // tool w/o losing whatever (few) points are currently active on the screen.
        void RecoverFromResetBeforeInitialization()
        {
            _initProfilesPlaced = false;
                            
            // Get orthogonal intersections from the baseline and the current extent
            List<List<Coordinate3D>> baselineExtentIntersections = FindOrthogonalIntersections(fcPoly, 
                _baselinePoint, extendDist, out var dists, out var segIndices);
            
            // build shoreline based on these intersections and the MHW value
            Polyline shoreline = BuildMHWShoreline(baselineExtentIntersections);

            int insertIdx = HamlTool.GetVertices().Count;
            for (int i = 0; i < shoreline.Points.Count; i++)
            {
                Vertex vertex = new Vertex(shoreline.Points[i]);
                List<Coordinate3D> points = baselineExtentIntersections[i];
                LineSegment line = LineBuilderEx.CreateLineSegment(points[0], points[points.Count - 1]);
                SegmentConstraint sc = new SegmentConstraint(line.StartPoint, line.EndPoint);
                vertex.SetConstraint(sc);
                vertex.DistAlongBaseline = dists[i];
                vertex.BaselineOid = _baselineOID.Value;
                vertex.DistAlongBaseline = segIndices[i];
                HamlTool.GetVertices().Insert(insertIdx, vertex);
                insertIdx++;
            }
        }

        // use the average Z values of the sides created by splitting the mapview with the candidate shoreline to pick
        // a label side
        void AutoSetLabelSide()
        {
            InitLabelSides(HamlTool.GetContourAsArcGeometry() as Polyline);
                        
            var side1ZAverage = RasterUtil.CalcPolygonAverageZ(_raster,_polySide1);
            var side2ZAverage = RasterUtil.CalcPolygonAverageZ(_raster,_polySide2);
            var mapPointOfCentroidOfHighZArea = side1ZAverage > side2ZAverage ? _polySide1.Extent.Center : _polySide2.Extent.Center;
            HamlTool.SetLabelDir(mapPointOfCentroidOfHighZArea);
            HamlTool.ReportAllChanges(null, true);
        }

        // add the first real profile to the candidate shoreline
        void InitializeShoreline()
        {
            using (var progress = new ProgressDialog("Initializing Shoreline..."))
            {
                var status = new ProgressorSource(progress);
                progress.Show();
                
                InitializeProfileConstraints();
            
                QueuedTask.Run(() =>
                {
                    VerifyAndSaveProfiles(HamlTool.GetValidProfiles());
                    Profile p = HamlTool.InsertProfile(0);
                    HamlTool.SetDuneAlgoPlacements(p);
                    UpdateVisibleProfiles();
                    HamlTool.ActiveProfile = p;
                    initialized = true;

                }, status.Progressor);

                progress.Hide();
            }    
        }

        void UpdatePluginStates()
        {
            Module1.ToggleState(PluginState.ContourAvailableState, true);
            Module1.ToggleState(PluginState.InitializedState, true);

            if (HamlTool.GetAllProfiles().Count > 1)
            {
                Module1.ToggleState(PluginState.UndoRefineState, true);
            }
        
            Module1.ToggleState(PluginState.ResetGeometryState, true);

            Module1.ToggleState(PluginState.DoingOperationState, false);
        }
    }

    /// <summary>
    /// Handles special case of initializing the constraints of the init vertices/profiles
    /// </summary>
    protected void InitializeProfileConstraints()
    {
        // Set constraints for each vertex based on the previously calculated intersections
        for (int i = 0; i < HamlTool.GetVertices().Count; i++)
        {
            var vertex = HamlTool.GetVertices()[i];
            
            LineSegment transect;
            if (!Module1.IsFixedTransect)
            {
                List<Coordinate3D> baselinePoints = _initBaselineExtentIntersections[vertex];
                transect = LineBuilderEx.CreateLineSegment(baselinePoints[0], baselinePoints[baselinePoints.Count - 1]);
            }
            else
            {
                List<Coordinate3D> transectPoints = BuildFixedTransect(vertex.GetPoint(), _initBaselineExtentIntersections[vertex]);
                transect = QueuedTask.Run(() => LineBuilderEx.CreateLineSegment(transectPoints[0], transectPoints[transectPoints.Count - 1])).Result;
            }
            
            SegmentConstraint sc = new SegmentConstraint(transect.StartPoint, transect.EndPoint);
            vertex.SetConstraint(sc);
            vertex.DistAlongBaseline = _initDists[i];
            vertex.BaselineOid = _baselineOID.Value;
            vertex.BaselineSegIdx = _initSegIndices[i];
        }

        
    }
    
    public static List<Coordinate3D> FindMHWPoints(List<Coordinate3D> points, double queryVal, Polyline baseline)
    {
        List<Coordinate3D> ret = new List<Coordinate3D>();
        List<int> shorelineIndices = FindMHWIndex(points, queryVal, 0, points.Count -1, baseline);

        if (shorelineIndices.Count > 0)
        {
            shorelineIndices.ForEach(idx =>
            {
                if (idx >= 0 && idx < points.Count - 1)
                {
                    ret.Add(points[idx]);  
                }
            });
        }

        // if more than one MHW point is found, only keep the first
        if (ret.Count > 1)
        {
            ret = new List<Coordinate3D> {ret.First()};
        }

        return ret;
    }

    //TODO: currently walks along the baseline, building transects. DOES NOT take into account vertices/segments of the baseline!
    public List<List<Coordinate3D>> FindOrthogonalIntersections(Polyline p, MapPoint walkStart, double walkEnd, 
        out List<double> distances, out List<int> segIndices, double step = Double.NaN)
    {
        if (double.IsNaN(step))
        {
            step = Module1.ProfileSpacing;
        }
        
        // TODO: currently starts at beginning of baseline, will need to check where to actually start if the user is resuming work
        double walkDist = 0;
        distances = new List<double>();
        segIndices = new List<int>();
        
        GeometryEngine.Instance.QueryPointAndDistance(fcPoly, SegmentExtensionType.NoExtension, walkStart,
            AsRatioOrLength.AsLength, out double walkPos, out _, out _);

        var intersections = new List<List<Coordinate3D>>();
        {

            // TODO: initial walk distance should likely NOT be very long (first two points (2x step dist), perhaps?)
            while (walkDist < walkEnd && walkPos <= p.Length)
            {
                var transect = FindBaselineTransect(p, walkPos, out var segIdx);

                if (transect != null)
                {
                    intersections.Add(transect);
                    distances.Add(walkPos);
                    segIndices.Add(segIdx.Value);
                } 
                walkDist += step;
                walkPos += step;
            
            }
        }
        
        return intersections;
    }
    
    public override void ResetGeometry()
    {
        switch (ShowConfirmResetGeometryMessageBox())
        {
            case(MessageBoxResult.Yes):
                QueuedTask.Run(() =>
                {
                    if (_initProfilesPlaced)
                    {
                        VerifyAndSaveProfiles(HamlTool.GetValidProfiles());
                    }
                    
                    UpdateBaselinePoint();
                    SaveBaselinePoint();
                });

                HamlTool.ActiveProfile = null;
                HamlTool.ResetGeometry();
                break;
            case(MessageBoxResult.No):
                
                if (HamlTool.GetAllProfiles().Count == 1)
                {
                    ResetTool();
                    return;
                }
                
                RemoveGeneratedVertices();
                HamlTool.ActiveProfile = null;
                HamlTool.ResetGeometry();
                
                break;
            case(MessageBoxResult.Cancel):
                return;
        }

        mhwChosen = false;
        HamlTool.LabelDirSet = false;
        
        Module1.ToggleState(PluginState.UndoRefineState, false);
        Module1.ToggleState(PluginState.ContourAvailableState, false);

        QueuedTask.Run(() =>
        {
            HamlTool.ReportAllChanges(Module1.HexToCimColor(ColorSettings.DefaultGrey, 50), true);
            GeometryEngine.Instance.QueryPointAndDistance(fcPoly, SegmentExtensionType.NoExtension, _baselinePoint, AsRatioOrLength.AsLength,
                out double walkPos, out _, out _);
            FindCandMHWPoints(walkPos, 1.0);

            if (candMHWPoints.Count > 1)
            {
                ShowCandidateMHWPoints(candMHWPoints);
                ShowSelectMHWPointOverlay();    
            }
            else
            {
                _useFirstMHWPoint = true;
                mhwChosen = true;
            }
        });
    }
    
    protected Polyline BuildMHWShoreline(List<List<Coordinate3D>> baselineIntersections)
    {
        List<MapPoint> mhwPoints = new List<MapPoint>();
        foreach (var intersection in baselineIntersections)
        {
            if (intersection is not null)
            {
                List<Coordinate3D> candMHWCoords = FindMHWPoints(intersection, Module1.MeanHighWaterVal, fcPoly);

                if (candMHWCoords.Count == 1)
                {
                    mhwPoints.Add(candMHWCoords[0].ToMapPoint());
                }
                else if (candMHWCoords.Count > 1)
                {
                    MapPoint bestPoint = _useFirstMHWPoint ? candMHWCoords[0].ToMapPoint(MapView.Active.Extent.SpatialReference) : candMHWCoords[1].ToMapPoint(MapView.Active.Extent.SpatialReference);
                    mhwPoints.Add(bestPoint);
                }
                else if (candMHWCoords.Count == 0 && HamlTool != null && HamlTool.GetVertices().Count > 0)
                {
                    // no MHW value was found (possibly a transect over a no-data section) or inlet, so place the new
                    // shoreline vertex next to the last "sane" vertex
                    ProximityResult pr  = GeometryEngine.Instance.NearestVertex(
                        PolylineBuilderEx.CreatePolyline(intersection, MapView.Active.Extent.SpatialReference),
                        HamlTool.GetVertices()[HamlTool.GetVertices().Count - 1].GetPoint());

                    if (pr.PointIndex.HasValue)
                    {
                        mhwPoints.Add(intersection[pr.PointIndex.Value].ToMapPoint());
                    }
                }
                else if (mhwPoints.Count > 0)
                {
                    ProximityResult pr  = GeometryEngine.Instance.NearestVertex(
                        PolylineBuilderEx.CreatePolyline(intersection, MapView.Active.Extent.SpatialReference),
                        mhwPoints[mhwPoints.Count - 1]);
                    
                    if (pr.PointIndex.HasValue)
                    {
                        mhwPoints.Add(intersection[pr.PointIndex.Value].ToMapPoint());
                    }
                }
            }
        }

        if (mhwPoints.Count != baselineIntersections.Count)
        {
            UpdateTemplarBaselinePoint(baselineIntersections.Count - mhwPoints.Count);
        }
        
        return PolylineBuilderEx.CreatePolyline(mhwPoints);;
    }
    
    public static List<int> FindMHWIndex(List<Coordinate3D> points, double mhwShorelineVal, int startIdx, int endIdx, Polyline baseline)
    {
        List<int> ret = new List<int>();
        int numCont = 0;
        if (startIdx < endIdx)
        {
            for (int i = startIdx; i < endIdx - 1; i++)
            {
                Coordinate3D currCoord = points[i];
                Coordinate3D nextCoord = points[i+1];
                
                double currZ = currCoord.Z;
                double nextZ = nextCoord.Z;

                double threshold = 0.1;
                if ((currZ == 0 || nextZ == 0) && Math.Abs(nextZ - currZ) > threshold) //NaN values create discontinuities so check for them and filter those false discontinuities out
                {
                    numCont++;
                    continue;
                }

                if (currZ < mhwShorelineVal && nextZ >= mhwShorelineVal || nextZ < mhwShorelineVal && currZ >= mhwShorelineVal)
                {
                    double currZSeaDist = GeometryEngine.Instance.Distance(currCoord.ToMapPoint(), baseline);
                    double nextZSeaDist = GeometryEngine.Instance.Distance(nextCoord.ToMapPoint(), baseline);

                    if (currZSeaDist < nextZSeaDist)
                    {
                        ret.Add(i);
                    }
                    else
                    {
                        ret.Add(i+1);
                    }
                }
            }
        }
        
        if (ret.Count == 0)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Math.Abs(points[i].Z - mhwShorelineVal) < 0.5) 
                {
                    // TODO: is it ok to have a case where the MHW value is technically not found, so add a close value?
                    ret.Add(i);
                }
            }
        }
        
        if (ret.Count > 2)
        {
            ret = new List<int> { ret.First(), ret.Last() };
        }
        
        return ret;
    }

    protected void ExtendShoreline()
    {
        QueuedTask.Run(() =>
        {
            UpdateBaselinePoint();

            if (_initProfilesPlaced)
            {
                VerifyAndSaveProfiles(HamlTool.GetValidProfiles());
                SaveBaselinePoint();
            }

            if (_useLearner)
            {
                if (HamlTool.ReadyForLearnerOptimization())
                {
                    HamlTool.OptimizeLearner();
                }

                if (HamlTool.GetProfileLearnerSize() < _maxLearnerTreeSize)
                {
                    HamlTool.TrainAllUntrainedProfiles();
                }
            }

            if (_initProfilesPlaced)
            {
                List<List<Coordinate3D>> baselineExtentIntersections = FindOrthogonalIntersections(fcPoly,
                    _baselinePoint, extendDist, out var dists, out var segIndices);
                Polyline candShoreline = BuildMHWShoreline(baselineExtentIntersections);

                if (candShoreline.Points.Any())
                {
                    for (int i = 0; i < candShoreline.Points.Count; i++)
                    {
                        MapPoint shorelinePoint = candShoreline.Points[i];
                        List<Coordinate3D> baselineCoords = baselineExtentIntersections[i];

                        if (!Module1.IsFixedTransect)
                        {
                            AddVertexAndProfile(shorelinePoint, baselineCoords, dists[i], segIndices[i]);
                        }
                        else
                        {
                            List<Coordinate3D> transectCoords = BuildFixedTransect(shorelinePoint, baselineCoords);
                            AddVertexAndProfile(candShoreline.Points[i], transectCoords, dists[i], segIndices[i]);
                        }
                    }
                }
                else
                {
                    ShowExtendErrorMessage();
                }
            }
            else
            {
                // This only happens once, since we technically already have the made the search spaces for each profile,
                // but we only displayed the first profile for the user to annotate. Could likely be tightened up.
                _initProfilesPlaced = true;
                AddProfiles();
            }

            UpdateVisibleProfiles();
            HamlTool.ActiveProfile = HamlTool.GetValidProfiles().First(p => !p.Saved);
            Module1.ToggleState(PluginState.UndoRefineState, true);
        });
    }
    

    internal void SaveBaselinePoint()
    {
        if (TemplarBaselinePoint != null)
        {
            QueuedTask.Run(() =>
            {
                if (TemplarBaselineOid is null or -1)
                {
                    TemplarBaselineOid = GeodatabaseUtil
                        .SaveMapPoint(TemplarBaselinePoint, GeodatabaseUtil.BaselineFCName, BaselineOid).Result;
                }
                else
                {
                    GeodatabaseUtil.UpdateBaselinePoint(TemplarBaselineOid.Value, TemplarBaselinePoint);
                }
            });
        }
    }

    internal void UpdateBaselinePoint()
    {
        List<Profile> profiles = HamlTool.GetAllProfiles();
        
        var dist = profiles.Select(p => p.Vertex.DistAlongBaseline).Max();
        _baselinePoint = GeometryEngine.Instance.QueryPoint(fcPoly, SegmentExtensionType.NoExtension, 
            dist + Module1.ProfileSpacing, AsRatioOrLength.AsLength);
    }

    protected List<Coordinate3D> BuildFixedTransect(MapPoint shorelinePoint, List<Coordinate3D> baselineTransectCoords)
    {
        Polyline baselineTransect =
            PolylineBuilderEx.CreatePolyline(baselineTransectCoords);
                                
        // angle could be stored when finding the baseline intersections, but would require
        // several out vars
        MapPoint p1 = baselineTransect.Points.First();
        MapPoint p2 = baselineTransect.Points.Last();
        double xDiff = p2.X - p1.X;
        double yDiff = p2.Y - p1.Y;
        double angle = Math.Atan2(yDiff, xDiff);

        // find the shoreline transect. while it seems inefficient to sample the shoreline transect
        // after sampling the baseline transect, their is no guarantee that the baseline transect
        // captures what the analyst is interested in (ie zoomed in at a low scale)
        return BuildFixedTransectCoords(shorelinePoint, angle, Module1.TransectLandwardLength, Module1.TransectSeawardLength);
    }

    protected void AddVertexAndProfile(MapPoint mp, List<Coordinate3D> coords, double dist, int segIdx)
    {
        // TODO: since we are always working CCW, we *should* always be extending in the same direction...need to make sure, though
        var verts = HamlTool.GetVertices();
        var vIdx = HamlTool.InsertAndPlaceVertex(mp);
        
        LineSegment line = LineBuilderEx.CreateLineSegment(
            coords[0].ToMapPoint(ActiveMapView.Extent.SpatialReference),
            coords[coords.Count - 1].ToMapPoint(ActiveMapView.Extent.SpatialReference));
        SegmentConstraint sc = new SegmentConstraint(line.StartPoint, line.EndPoint);
        
        var currentVertex = HamlTool.GetVertices()[vIdx];

        currentVertex.SetConstraint(sc);
        currentVertex.DistAlongBaseline = dist;
        currentVertex.BaselineOid = _baselineOID.Value;
        currentVertex.BaselineSegIdx = segIdx;
        
                            
        if (HamlTool.IsProfileLearnerTrained() && _useLearner)
        {
            HamlTool.InsertProfileAndGuessPlacements(vIdx, coords);
        }
        else
        {
            Profile p = HamlTool.InsertProfile(vIdx);
            HamlTool.SetDuneAlgoPlacements(p);
        }
    }

    // Checks the shoreline angles created by the current vertex and its previous two to determine if a sharp change in
    // the shoreline geometry (over 90 degrees from seg to seg) has occured. Returns true when a sharp angle is detected.
    protected bool DetectSharpShorelineAngles(int vertexIndex)
    {
        if (vertexIndex < 2)
        {
            return false;
        }

        var verts = HamlTool.GetVertices();
        var dotProduct =
            GeomUtil.CalcTwoSegmentDotProductUnnormalized(verts[vertexIndex], verts[vertexIndex - 1], verts[vertexIndex - 2]);

        return dotProduct <= 0;
    }

    protected void ShowExtendErrorMessage()
    {
        GUI.ShowMessageBox("Not enough space to extend tool!" ,"Extend Error!", false);
    }

    protected void UpdateTemplarBaselinePoint(int stepback)
    {
        GeometryEngine.Instance.QueryPointAndDistance(fcPoly, SegmentExtensionType.NoExtension, _baselinePoint,
            AsRatioOrLength.AsLength, out double walkPos, out _, out _);

        walkPos -= Module1.ProfileSpacing * stepback; // TODO: why 2x?

        _baselinePoint = GeometryEngine.Instance.QueryPoint(fcPoly, SegmentExtensionType.NoExtension, walkPos, AsRatioOrLength.AsLength);
    }

    public bool InitProfilesPlaced
    {
        get => _initProfilesPlaced;
        set => _initProfilesPlaced = value;
    }

    protected override void UpdateOnZoomOrPan(MapViewCameraChangedEventArgs args)
    {
        if (HamlTool != null)
        {
            base.UpdateOnZoomOrPan(args);
        }
    }

    public void AutoGenerateVertices(int x) // todo: remove or implement unused argument
    { 
        ExtendShoreline();
    }

    public void RemoveGeneratedVertices()
    {
        int removeCount = (int) Math.Ceiling(extendDist / Module1.ProfileSpacing);
        for (int i = removeCount; i > 0; i--)
        {
            if (HamlTool.GetAllProfiles().Count > 1)
            {
                DeleteProfile(HamlTool.GetAllProfiles().Last());    
            }
            else
            {
                Module1.ToggleState(PluginState.UndoRefineState, false);
            }
        }
        
        Vertex v = HamlTool.GetVertices()[HamlTool.GetVertices().Count - 1];
        HamlTool.ActiveProfile = HamlTool.GetProfileFromVertex(v);

        if (HamlTool.GetVertices().Count == 1)
        {
            QueuedTask.Run(() =>
            {
                // Get orthogonal intersections from the baseline and the current extent
                var tempIntersections = FindOrthogonalIntersections(fcPoly, _baselinePoint, 
                    extendDist, out var initDists, out var segIndices);

                _initDists = initDists;
                _initSegIndices = segIndices;
            
                // build shoreline based on these intersections and the MHW value
                Polyline shoreline = BuildMHWShoreline(tempIntersections);

                // Tool is being initialized in a mapview that has poor starting conditions 
                if (shoreline.Points.Count <= 1)
                {
                    ShowExtendErrorMessage();
                    FrameworkApplication.SetCurrentToolAsync(null);
                }

                for (int i = 0; i < shoreline.Points.Count; i++)
                {
                    HamlTool.InsertAndPlaceVertex(shoreline.Points[i], HamlTool.GetVertices().Count);
                }

                _initProfilesPlaced = false;
            });
        }
        
        HamlTool.ReportAllChanges();
        UpdateBaselinePoint();
        SaveBaselinePoint();
    }

    protected override void OnToolKeyDown(MapViewKeyEventArgs k)
    {
        switch (k.Key)
        {
            case Key.K:
                AutoGenerateVertices(3); // todo: remove or implement unused argument
                break;
            default:
                base.OnToolKeyDown(k);
                break;
        }
    }
    
    protected internal MessageBoxResult ShowConfirmResetGeometryMessageBox()
    {
        String message = "This will allow you to set a new mean high water point and label side, and will also set the machine learning tool to \"cold start\" conditions.\n\n"+
                         "You may have unsaved profiles from previous Refines. Would you like to save any unsaved profiles before the reset?\n\n"+
                         "NOTE: this will NOT remove or reset any saved profiles or their vertices";
        MessageBoxResult dialogResult = MessageBox.Show(message,
            "Confirm ML Tool Reset",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Exclamation);

        return dialogResult;
    }
}
