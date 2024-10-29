/*
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
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Events;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using HamlProAppModule.haml.events;
using HamlProAppModule.haml.logging;
using HamlProAppModule.haml.mltool;
using HamlProAppModule.haml.ui.options;
using HamlProAppModule.haml.util;
using MLPort;
using Serilog;
using GE = ArcGIS.Core.Geometry.GeometryEngine;
using MessageBox = ArcGIS.Desktop.Framework.Dialogs.MessageBox;

namespace HamlProAppModule.haml.ui.arctool
{

    public enum PluginState
    {
        ImageryLayerSelectedState,
        PolylineSelectedState,
        PolylineContourSelectedState,
        PolygonContourSelectedState,
        InitializedState,
        UndoAvailableState, // ESRI team undo, may be useful in the future
        UndoRefineState,
        ContourAvailableState,
        DoingOperationState,
        EnableLearnerState,
        DisableLearnerState,
        ResetGeometryState,
        DeactivatedState,
        ExperimentReadyState
    }
    
    public enum DragState
    {
        None,
        Shore,
        Low,
        High
    }
    
    // Responsible for all control of HAML contouring interfaces
     public abstract class HamlMapTool<T> : MapTool where T : PerpendicularGeometry
    {
        protected  ILogger Log => LogManager.GetLogger(GetType());
        protected Dictionary<SubscriptionToken, Action<SubscriptionToken>> _subDict;
        protected OverlayController OverlayController;
        
        // TODO: once we get rid of other tools, this will likely be fine to live here
        protected List<MapPoint> candMHWPoints;

        protected bool _useFirstMHWPoint; // [I wanna go fast! -RB] Used for finding the best MHW point 
        
        // vars to store the geometries and OIDS of baseline data
        protected MapPoint? _baselinePoint;
        protected long? _baselinePointOID;
        
        protected Polyline? fcPoly;
        protected int? _baselineOID;

        // used to determine if user has either selected a MHW point or there was only one candidate MHW point, so 
        // initialization was automatic

        protected DragState dragState;

        protected bool _showSearchSpaces = true;
        private bool _showSignalSpaces;
        private List<IDisposable> signalOverlays;
        
        protected BasicRasterLayer _selectedRasterLayer;
        
        protected bool sketching;
        protected bool loadGeometry;
        protected bool mhwChosen;
        protected bool initialized;

        protected string _featureClassName = "";
        
        Stack<Action> undoStack;

        protected static int? _selectedPointIndex;
        protected static object _lock;

        //Careful with these references
        protected KNNLearner _learner;
        protected int _maxLearnerTreeSize = 10000;
        protected bool _useLearner;
        protected Raster _raster;
        protected double? _minPixelValue;
        protected double? _maxPixelValue;

        // variables to handle setting the label side before adding profiles
        
        protected bool? _thisSide;
        protected Polygon _polySide1;
        protected Polygon _polySide2;
        
        protected abstract void InitHamlTool(Envelope mapExtent, Geometry? arcGeometry = null);
        protected abstract void InitBaseOverlays();
        //
        protected HamlMapTool()
        {
            IsSketchTool = false;
            SketchOutputMode = SketchOutputMode.Map;
            Module1.activeHamlMapTool = this;
            EsriGeometryType = esriGeometryType.esriGeometryPolygon;
            
            Log.Here().Information("Tool Created");
        }

        protected virtual void UpdateOnZoomOrPan(MapViewCameraChangedEventArgs args)
        {
            if (HamlTool != null)
            {
                var newExtent = args.MapView.Extent;
                HamlTool.SetExtent(EnvelopeBuilderEx.CreateEnvelope(newExtent));
                
                Log.Here().Information("Mapview extent's (XMax, YMax) and (XMin, YMin) was changed to "+
                                "({@XMax}, {@YMax}) and ({@XMin}, {@YMin})",
                    Math.Round(newExtent.XMax,1), Math.Round(newExtent.YMax, 1), 
                    Math.Round(newExtent.XMin, 1), Math.Round(newExtent.YMin, 1));
            }
        }

        private void UpdateSignalLayer()
        {
            if (_showSignalSpaces)
            {
                List<CIMGraphic> signalGraphics = HamlTool.GetSignalGraphics();

                foreach (CIMGraphic graphic in signalGraphics)
                {
                    signalOverlays.Add(AddOverlay(graphic));
                }
            }
        }

        private static void AbortToolActivation(String message)
        {
            MessageBox.Show(message);
            FrameworkApplication.SetCurrentToolAsync(null);
        }

        protected override Task OnToolActivateAsync(bool active)
        {
            _lock = new object();
            if (loadGeometry)
            {
                LoadGeometry();
            }
            else
            {
                Module1.loadedGeometry = null;
                Module1.loadedBitDepth = null;
                Module1.loadedOID = null;
            }
            
            return QueuedTask.Run(async () =>
            {
                OnToolActivate();
            }).ContinueWith(t =>
            {
                if (IsSketchTool && Module1.loadedGeometry != null)
                    SetCurrentSketchAsync(Module1.loadedGeometry);
            });
        }

        protected virtual void OnToolActivate()
        {
            Module1.ToggleState(PluginState.InitializedState, false);
            Module1.ToggleState(PluginState.DeactivatedState, false);
            Module1.activeHamlMapTool = this;
            OverlayController = new OverlayController(AddOverlay, UpdateOverlay);
            signalOverlays = new List<IDisposable>();
            undoStack = new Stack<Action>();
            
            if (MapView.Active == null) return;
            
            _selectedRasterLayer = CheckSelectedRasterLayer();
            
            CalcStats();

            //Make sure the source raster's projection matches that of the map view
            Raster sourceRaster = InitRasterData();
            
            if (MapView.Active.Map.SpatialReference.Name != sourceRaster.GetSpatialReference().Name)
            {
                sourceRaster.SetSpatialReference(MapView.Active.Map.SpatialReference);
            }

            //Create the ML implementation
            LabelFeature labelFeature = new LabelFeature("Shoreline Label");
            List<Feature> features = new List<Feature>();

            for (int i = 0; i < sourceRaster.GetBandCount(); i++)
            {
                features.Add(new LabelFeature(sourceRaster.GetBand(i).GetName()));
            }

            ClassificationLabel label = new ClassificationLabel(labelFeature, features);
            _learner = new KNNLearner(label, DistanceMetric.EuclideanSquaredDistance, 8);
            Module1.ToggleState(PluginState.DisableLearnerState, true);

            _raster = sourceRaster;

            if (IsSketchTool)
            {
                sketching = true;
            }
            else // currently the FC tool is the only non-sketch tool
            {
                Dictionary<MapMember, List<long>>? selection = MapView.Active.Map.GetSelection().ToDictionary();
                
                if (selection.Count > 1)
                {
                    GUI.ShowMessageBox("Too many features selected. Tool initialization requires one feature to be selected.",
                        "Initialization Error", false);
                    
                    FrameworkApplication.SetCurrentToolAsync(null);
                    return;
                }
                
                // load the feature class geometry and make sure that it is actually a geom
                string fcName = selection.First().Key.Name;
                _baselineOID = (int) selection.First().Value[0];
                fcPoly = GeodatabaseUtil.GetFeatureFromOID(fcName, _baselineOID.Value) as Polyline;

                if (fcPoly is null)
                {
                    GUI.ShowMessageBox("Unable to load feature, is selected feature a polyline?" +
                                       " Tool initialization requires a polyline.",
                        "Initialization Error", false);
                    
                    FrameworkApplication.SetCurrentToolAsync(null);
                    return;
                }

                if (!fcPoly.SpatialReference.Wkid.Equals(ActiveMapView.Extent.SpatialReference.Wkid))
                {
                    GUI.ShowMessageBox("Feature class geometry CRS does not match the mapview's CRS. Consider reprojecting.",
                        "Initialize Error", false);
                    
                    FrameworkApplication.SetCurrentToolAsync(null);
                    return;
                }

                LoadBaselinePoint();
                double startDist = CalcStartDist();
                List<Coordinate3D>? initTransect =  FindBaselineTransect(fcPoly, startDist, out _);
                
                if (initTransect == null)
                {
                    // the starting point of the baseline (saved or initial) does not intersect the current mapview
                    // kill the tool, then let the user pan to a new area
                    GUI.ShowMessageBox("Initial baseline point transect does not intersect mapview! " +
                                       "Consider panning to a new location.",
                        "Initialize Warning", false);
                    
                    FrameworkApplication.SetCurrentToolAsync(null);
                    return;
                }
                
                double walkPos = startDist;
                double step = 1.0;

                candMHWPoints = new List<MapPoint>();
                bool inView = true;
                
                // walk along the baseline until at least one MHW point is found or we run out of baseline
                FindCandMHWPoints(walkPos, step);
                
                if (candMHWPoints.Count == 1) // only one candidate point, so use it to init the tool
                {
                    mhwChosen = true;
                    _useFirstMHWPoint = true;
                    InitHamlTool(MapView.Active.Extent, fcPoly);
                    
                    if (HamlTool != null)
                    {
                        InitBaseOverlays();
                        Module1.ToggleState(PluginState.ContourAvailableState, true);
                    }
                    else
                    {
                        // This case should never happen, but this allows graceful failure
                        GUI.ShowMessageBox("Could not initialize tool.",
                            "Initialization Error", false);
                        
                        FrameworkApplication.SetCurrentToolAsync(null);
                        return;
                    }
                }
                else
                {
                    GUI.ShowMessageBox("Could not find any MHW points to initialize the tool." +
                                       " Consider changing the MHW value.",
                        "Initialization Error", false);
                    
                    FrameworkApplication.SetCurrentToolAsync(null);
                    return;
                }
            }

            var initExtent = MapView.Active.Extent;
            Subscribe();
            Log.Here().Information("Tool Activated using {@Raster}", _selectedRasterLayer.Name);
            Log.Here().Information("Initial mapview extent's (XMax, YMax) and (XMin, YMin) is "+
                            "({@XMax}, {@YMax}) and ({@XMin}, {@YMin})",
                Math.Round(initExtent.XMax,1), Math.Round(initExtent.YMax,1), Math.Round(initExtent.XMin,1), Math.Round(initExtent.YMin,1));
        }

        private double CalcStartDist()
        {
            double ret;
            if (_baselinePoint != null)
            {
                GeometryEngine.Instance.QueryPointAndDistance(fcPoly, SegmentExtensionType.NoExtension, _baselinePoint, 
                    AsRatioOrLength.AsLength, out ret, out _, out _);
            }
            else
            {
                GeometryEngine.Instance.QueryPointAndDistance(fcPoly, SegmentExtensionType.NoExtension, fcPoly.Points[0], 
                    AsRatioOrLength.AsLength, out ret, out _, out _);    
            }

            return ret;
        }

        protected internal void FindCandMHWPoints(double walkPos, double step)
        {
            while (candMHWPoints.Count == 0 && walkPos <= fcPoly.Length)
            {
                var transectIntersect = FindBaselineTransect(fcPoly, walkPos, out _);
                    
                if (transectIntersect == null)
                {
                    break;
                }

                _baselinePoint = GE.Instance.MovePointAlongLine(fcPoly, walkPos, false, 0, SegmentExtensionType.NoExtension);
                Multipoint multipoint = MultipointBuilderEx.CreateMultipoint(transectIntersect);
                ProximityResult pr = GeometryEngine.Instance.NearestPoint(multipoint, _baselinePoint);

                if (pr.PointIndex != null)
                {
                    List<Coordinate3D> candMHWCoords = HamlFeatureClassMapTool.FindMHWPoints(transectIntersect, Module1.MeanHighWaterVal, fcPoly);
                    candMHWCoords.ForEach(coord => candMHWPoints.Add(coord.ToMapPoint(ActiveMapView.Extent.SpatialReference)));
                }

                walkPos += step;
            }
        }

        private void LoadBaselinePoint()
        {
            QueuedTask.Run(() =>
            {
                if (_baselineOID != null)
                {
                    Tuple<long, MapPoint>? baselinePointInfo = GeodatabaseUtil.LoadBaselinePoint(_baselineOID.Value);
                    
                    if (baselinePointInfo is not null)
                    {
                        _baselinePointOID = baselinePointInfo.Item1;
                        _baselinePoint = baselinePointInfo.Item2;
                    }
                }
            });
        }

        protected internal void ShowSelectMHWPointOverlay()
        {
            Geometry labelAnchor = Module1.FindNonOverlappingMHWOverlayGeom(candMHWPoints);
            OverlayController.AddOverlay(Module1.BuildTextOverlayGraphic(GeometryEngine.Instance.Centroid(labelAnchor), Module1.SelectMHWPointText, 22),
                HamlGraphicType.SelectMHWPointText);
        }
        
        /// <summary>
        /// Finds orthogonal transects along a polyline via generating an arbitrarily large normal in both directions
        /// of the baseline. This is then clipped by the mapview extent, creating a transect for the given mapview.
        ///
        /// The AcrPro intersect method seems to be broken, so this method returns the intersection point of the transect
        /// with the baseline along with the transect points.
        /// </summary>
        /// <param name="p"></param>
        /// <param name="walkPos"></param> Distance along the polyline to find the first point
        /// <param name="step"></param> Distance from the first point to get the second point, building the segment
        /// <param name="segIdx"></param> Returns the segment index of the polyline we're querying
        /// <returns> Tuple of MapPoint (Item1) and list of MapPoint, where the list represents the transect MapPoints that
        /// intersect the baseline at MapPoint (Item1). 
        /// </returns>
        protected List<Coordinate3D>? FindBaselineTransect(Polyline p, double walkPos, out int? segIdx)
        {
            // TODO: this should likely be based on MapPoints, not distance along the polyline
            MapPoint curr = GeometryEngine.Instance.QueryPoint(p,
                SegmentExtensionType.NoExtension,
                walkPos, AsRatioOrLength.AsLength);
            ProximityResult pr = GeometryEngine.Instance.NearestPoint(fcPoly, curr);
            segIdx = pr.SegmentIndex;

            if (pr.SegmentIndex.HasValue)
            {
                Segment seg = fcPoly.Parts[0][pr.SegmentIndex.Value];
                MapPoint p1 = seg.StartPoint;
                MapPoint p2 = seg.EndPoint;
                double segAngle = GeomUtil.CalcAngleBetweenPoints(p1, p2);
                double normalAngleRads =  segAngle + (Math.PI / 2);
                double antiAngle = segAngle - Math.PI / 2;
                List<MapPoint> offsetPoints = new List<MapPoint>();
                
                offsetPoints.Add(GeometryEngine.Instance.ConstructPointFromAngleDistance(curr, normalAngleRads, 200));
                offsetPoints.Add(GeometryEngine.Instance.ConstructPointFromAngleDistance(curr, antiAngle, 200));
                Polyline giantTransect = PolylineBuilderEx.CreatePolyline(offsetPoints);

                return RasterUtil.CreateIntersectionMapCoordinates(_raster, giantTransect, out _);
            }
            
            return null;
        }

        protected List<Coordinate3D>? BuildFixedTransectCoords(MapPoint mp, double transectAngle, double landwardDist, double seawardDist)
        {
            List<Coordinate3D> ret = QueuedTask.Run(() => {
                
                MapPoint endPoint1 = GeometryEngine.Instance.ConstructPointFromAngleDistance(mp,
                    transectAngle, landwardDist);
                
                GeometryEngine.Instance.QueryPointAndDistance(HamlTool.GetContourAsArcGeometry() as Polyline, SegmentExtensionType.NoExtension, endPoint1,
                    AsRatioOrLength.AsLength, out _, out _, out LeftOrRightSide ep1side);

                Polyline fixedTransectPoly;
                if (HamlTool.LabelSide.Equals(ep1side))
                {
                    MapPoint seawardPoint = GeometryEngine.Instance.ConstructPointFromAngleDistance(mp,
                        transectAngle + Math.PI, seawardDist);
                    fixedTransectPoly = PolylineBuilderEx.CreatePolyline(new List<MapPoint>()
                        { endPoint1, seawardPoint });    
                }
                else
                {
                    MapPoint endPoint2 = GeometryEngine.Instance.ConstructPointFromAngleDistance(mp,
                        transectAngle + Math.PI, landwardDist);
                    
                    MapPoint seawardPoint = GeometryEngine.Instance.ConstructPointFromAngleDistance(mp,
                        transectAngle, seawardDist);
                    
                    fixedTransectPoly = PolylineBuilderEx.CreatePolyline(new List<MapPoint>()
                        { endPoint2, seawardPoint });
                }
            
                return RasterUtil.CreateIntersectionMapCoordinates(_raster, fixedTransectPoly, out _);

            }).Result;

            for (int i = 0; i < ret.Count; i++)
            {
                Coordinate3D coord = ret[i];
                ret[i] = new Coordinate3D(coord.X,coord.Y,RasterUtil.GetZAtCoordinate(_raster, new Coordinate2D(coord.X, coord.Y)));
            }
            
            return ret;
        }
        
        protected internal void ShowCandidateMHWPoints(List<MapPoint> points)
        {
            OverlayController.AddOverlay(points[0],
                Module1.CreateMHWPointSymbol(ColorSettings.DefaultBlue, 10), 
                HamlGraphicType.CandHighMHWPoint);
            
            OverlayController.AddOverlay(points[1],
                Module1.CreateMHWPointSymbol(ColorSettings.DefaultMagenta, 10), 
                HamlGraphicType.CandLowMHWPoint);
        }

        private async void CalcStats()
        {
            await QueuedTask.Run(() =>
            {
                //Accessing the raster layer
               
                //Getting the colorizer
                //TODO: raster on reset, how to handle?
                var colorizer = _selectedRasterLayer.GetColorizer() as CIMRasterStretchColorizer;
                //Accessing the statistics

                if (colorizer != null)
                {
                    var stats = colorizer.StretchStats;
                    _maxPixelValue = stats.max;
                    _maxPixelValue = stats.min;
                }
            });
        }

        private Raster InitRasterData()
        {
            Raster derivativeRaster = _selectedRasterLayer.GetRaster();
            RasterDataset rasterDataset = derivativeRaster.GetRasterDataset() as RasterDataset;
            Raster sourceRaster = rasterDataset.CreateFullRaster();
            
            if (OptionsVM.rasterLayerSettings.ContainsKey(_selectedRasterLayer))
            {
                BitDepth = OptionsVM.rasterLayerSettings[_selectedRasterLayer].UserDefinedBitDepth;
            }

            if (Module1.loadedBitDepth.HasValue)
            {
                BitDepth = Module1.loadedBitDepth.Value;
            }

            //Get the pixel depth for the dataset
            var pixelType = sourceRaster.GetPixelType();
            decimal minPixelValue, maxPixelValue;
            if (BitDepth == 0)
            {
                
                (minPixelValue, maxPixelValue) = RasterUtil.GetMinMaxPixelValues(pixelType);
                BitDepth = (int) Math.Round((Math.Log((double) maxPixelValue) / Math.Log(2)));
                if (BitDepth % 2 != 0)
                {
                    BitDepth++;
                }
            }
            else
            {
                (minPixelValue, maxPixelValue) = RasterUtil.GetMinMaxPixelValues(pixelType, BitDepth);
            }

            if (Decimal.Equals(minPixelValue, maxPixelValue))
            {
                AbortToolActivation("The pixel types of the raster are either unknown or complex\n" +
                                    "HAML requires either integer or floating point pixel types.");
                return null;
            }

            Log.Here().Debug("Pixel max/min: " + _minPixelValue + " " + _maxPixelValue);

            return sourceRaster;
        }
        
        private BasicRasterLayer CheckSelectedRasterLayer()
        {
            IReadOnlyList<Layer> selectedLayerList = MapView.Active.GetSelectedLayers();

            //Deactivate if there is no selected layer.
            if (selectedLayerList.Count == 0)
            {
                AbortToolActivation("No Layer Selected.\nPlease select a raster layer for HAML.");
                return null;
            }

            Layer selectedLayer = selectedLayerList.First();

            if (selectedLayer is BasicRasterLayer == false)
            {
                AbortToolActivation("The selected layer is not a basic raster layer.\n" +
                                    "HAML requires basic raster layers for operation.");
                return null;
            }

            return selectedLayer as BasicRasterLayer;
        }

        private void LoadGeometry()
        {
            string gdbPath = "";
            if (string.IsNullOrWhiteSpace(OptionsVM.gdbPath))
            {
                gdbPath = Module1.DefaultGDBPath;
            }
            else
            {
                gdbPath = OptionsVM.gdbPath;
            }

            var error = GeodatabaseUtil.LoadGeometry(gdbPath, _featureClassName, EsriGeometryType);
            if (error is null)
            {
                AbortToolActivation("failed to load geometry");
            }
        }

        //Allow for garbage collection when tool is deactivated.
        // TODO: This is called when the catalog is viewed. Will we need to fool with catalog and come back to tool?
        protected override Task OnToolDeactivateAsync(bool hasMapViewChanged)
        {
            return QueuedTask.Run(() =>
            {
                if (Module1.activeHamlMapTool != this) return;
                lock (_lock)
                {
                    OnToolDeactivate();
                }
            });
        }

        protected virtual void Subscribe()
        {
            _subDict = new Dictionary<SubscriptionToken, Action<SubscriptionToken>>
            {
                {
                    SettingsChangedEvent.Subscribe(OnSettingsChanged), 
                    SettingsChangedEvent.Unsubscribe
                },
                {
                    MapViewCameraChangedEvent.Subscribe(UpdateOnZoomOrPan),
                    MapViewCameraChangedEvent.Unsubscribe
                }
            };
        }
        
        protected void Unsubscribe()
        {
            if (_subDict is null) return;
            foreach (var kvp in _subDict)
            {
                kvp.Value.Invoke(kvp.Key);
            }
            
        }
        
        protected void OnSettingsChanged(NoArgs noArgs)
        {
            if (HamlTool is ProfilePolyline tool)
            {
                QueuedTask.Run(() =>tool.ReportAllChanges(null, true));
            }
        }

        protected virtual void OnToolDeactivate()
        {
            _lock = null;
            sketching = false;
            Module1.ResetLoadedParams();
            undoStack.Clear();
            _selectedRasterLayer = null;
            _baselineOID = null;
            _baselinePointOID = null;
            _baselinePoint = null;
            fcPoly = null;
                    
            Module1.ToggleState(PluginState.UndoRefineState, false);
            Module1.ToggleState(PluginState.ContourAvailableState, false);
            Module1.ToggleState(PluginState.ResetGeometryState, false);

            Module1.ToggleState(PluginState.EnableLearnerState, false);
            Module1.ToggleState(PluginState.DisableLearnerState, false);

            signalOverlays.ForEach(d => d.Dispose());
            signalOverlays.Clear();
            Unsubscribe();
            _subDict.Clear();
            OverlayController.Dispose();
            Module1.ToggleState(PluginState.DeactivatedState, true);
            Log.Here().Information("Tool Deactivated");
        }

        protected override void OnToolKeyDown(MapViewKeyEventArgs k)
        {
            switch (k.Key)
            {
                // Toggle search space on/off
                case Key.H:
                {
                    _showSearchSpaces = !_showSearchSpaces;

                    Log.Here().Information("Showing Search Spaces: {@ShowSearchSpaces}", _showSearchSpaces);
                    if (!_showSearchSpaces)
                    {
                        OverlayController.AddExemption(HamlGraphicType.ValidSearchSpace);
                    }
                    else
                    {
                        OverlayController.RemoveExemption(HamlGraphicType.ValidSearchSpace);
                        InitBaseOverlays();
                    }
                    
                    break;
                }
                // Toggle signals on/off
                case Key.S:
                {
                    _showSignalSpaces = !_showSignalSpaces;
                    Log.Here().Information("Showing Signal Spaces: {@ShowSignalSpaces}", _showSignalSpaces);

                    if (!_showSignalSpaces)
                    {
                        foreach (IDisposable disposable in signalOverlays)
                        {
                            disposable.Dispose();
                        }
                    }

                    QueuedTask.Run(UpdateSignalLayer);
                    break;
                }
            }
        }

        protected override void OnToolMouseDown(MapViewMouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !sketching)
            {
                e.Handled = true;
            }
        }

        protected override Task HandleMouseDownAsync(MapViewMouseButtonEventArgs e)
        {
            return QueuedTask.Run(() =>
            {
                if (sketching) return;

                MapPoint clickPoint = MapView.Active.ClientToMap(e.ClientPoint);
                
                ProximityResult search = 
                    GE.Instance.NearestVertex(HamlTool.GetContourAsArcGeometry(), clickPoint);
                //TODO: Arc Pro 2.6 should allow retrieval of the geometry objects from the graphics layer of the overlay.
                //      The graphics layer would also allow for multiple geometries (ie. multiple vertices).
                //      Those routines should enable some form of getting the geometry that the user has clicked.
                    
                // must determine if user clicked near a vertex
                // get click point and nearest point in pixel space to avoid scale issues
                MapPoint nearestPoint = search.Point;
                    
                if ( RasterUtil.GetPixelDistance(_raster, clickPoint, nearestPoint) < 5)
                {
                    if (search.PointIndex != null)
                    {
                        int idx = search.PointIndex.Value;
                        _selectedPointIndex = idx;
                    }
                }
            });
        }

        protected override void OnToolMouseUp(MapViewMouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !sketching)
            {
                e.Handled = true;
            }
        }

        protected override Task HandleMouseUpAsync(MapViewMouseButtonEventArgs e)
        {
            return QueuedTask.Run(() =>
            {
                if (sketching) return;

                // mouse up indicates that we are no longer moving a vertex/no vertex is currently selected
                
                _selectedPointIndex = null;
            });
        }
        
        // For some reason, this method is not structured/named similarly to mouseUp, doubleClick, etc. 
        protected override async void OnToolMouseMove(MapViewMouseEventArgs e)
        {
            if (sketching) return;
            
            if (_selectedPointIndex != null)
            {
                dragState = DragState.Shore;
                await QueuedTask.Run(() => { OnToolMouseMoveAsync(e); });
            }
        }

        // Due to naming conventions of overriden methods, the async logic is found in OnToolMouseMove, which references
        // this method.
        // In short, this method should be called in a QueuedTask.
        protected virtual void OnToolMouseMoveAsync(MapViewMouseEventArgs e)
        {
            lock (_lock)
            {
                if (_selectedPointIndex.HasValue)
                {
                    MapPoint clientPoint = MapView.Active.ClientToMap(e.ClientPoint);
                    HamlTool.MoveVertex(_selectedPointIndex.Value, clientPoint);
                }
            }
        }

        protected override void OnToolDoubleClick(MapViewMouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !sketching)
            {
                e.Handled = true;
            }
        }

        protected override async Task HandleDoubleClickAsync(MapViewMouseButtonEventArgs e)
        {
            Module1.ToggleState(PluginState.DoingOperationState, true);
            await QueuedTask.Run(() =>
            {
                MapPoint mapPoint = MapView.Active.ClientToMap(e.ClientPoint);

                if (!sketching)
                {
                    Log.Here().Debug("HAML Message: Training, Inserting, and Placing.");
                    HamlTool.TrainAllUntrainedVertices();
                    HamlTool.InsertAndPlaceVertex(mapPoint);
                }
            });
            Module1.ToggleState(PluginState.DoingOperationState, false);
        }

        public virtual void ResetGeometry()
        {
                Log.Here().Information("{@HamlTool} learner has been reset", HamlTool.GetType());
        }

        protected override async Task<bool> OnSketchCompleteAsync(Geometry arcGeometry)
        {
            if (!Validate(arcGeometry)) return false;
            
            sketching = false;

            Module1.ToggleState(PluginState.DoingOperationState, true);

            var res = await QueuedTask.Run(() =>
            {
                lock (_lock)
                {
                    InitHamlTool(EnvelopeBuilderEx.CreateEnvelope(MapView.Active.Extent), arcGeometry);
                    InitBaseOverlays();
                }

                Module1.ToggleState(PluginState.ContourAvailableState, true);

                return true;
            });

            Module1.ToggleState(PluginState.DoingOperationState, false);
            Log.Here().Information("Sketch completed");
            return res;
        }

        protected List<Geometry>? SplitMapviewByPolyline(Polyline p)
        {
            Envelope extent = MapView.Active.Extent;
            Polyline extentPoly = PolylineBuilderEx.CreatePolyline(PolygonBuilderEx.CreatePolygon(extent), MapView.Active.Map.SpatialReference);
            Polygon extentPolygon = PolygonBuilderEx.CreatePolygon(extent);
            
            List<Geometry>? cuts = GeometryEngine.Instance.Cut(extentPolygon, p) as List<Geometry>;

            if (cuts == null || cuts.Count == 0)
            {
                p = GE.Instance.Extend(p, extentPoly, ExtendFlags.RelocateEnds);    
                cuts = GeometryEngine.Instance.Cut(extentPolygon, p) as List<Geometry>;
            }
            
            return cuts;
        }
        
        protected virtual bool Validate(Geometry g)
        {
            if (g.PointCount < 2) // handle case for invalid polyline vertex count
            {
                MessageBox.Show("Curves must have at least two vertices!", "Sketch Error", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.None);
                return false;
            }
            
            return true;
        }

        protected void InitLabelSides(Polyline polyline)
        {
            // TODO: get centroid of seaward side to act as anchor for MHW placement
            List<Geometry>? sides = SplitMapviewByPolyline(polyline);
            _polySide1 = sides?[0] as Polygon ?? throw new InvalidOperationException();
            _polySide2 = sides[1] as Polygon ?? throw new InvalidOperationException();
        }

        public async void ResetTool()
        {
            Log.Here().Information("Resetting {@HamlMapTool}", GetType());
            await OnToolDeactivateAsync(false);
            await OnToolActivateAsync(false);
        }
        
        private bool ShowConfirmResetToolMessageBox()
        {
            var message = "Are you sure you want to reset the tool?\n\n" +
                          "You will lose any unsaved progress.";
            var dialogResult = MessageBox.Show(message,
                "Tool Reset",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Exclamation);

            return dialogResult == MessageBoxResult.OK;
        }
        
        protected void AddUndoAction(Action action)
        {
            undoStack.Push(action);
            if (undoStack.Count > 0)
            {
                Module1.ToggleState(PluginState.UndoAvailableState, true);
            }
            else
            {
                Module1.ToggleState(PluginState.UndoAvailableState, false);
            }
        }

        // TODO: any undo capabilities *must* update ML backend
        public async void UndoLastAction()
        {
            Module1.ToggleState(PluginState.DoingOperationState, true);

            if (undoStack.Count > 0)
            {
                await QueuedTask.Run(() =>
                {
                    undoStack.Pop().Invoke();
                });
            }

            Module1.ToggleState(PluginState.DoingOperationState, false);

            if (undoStack.Count > 0)
            {
                Module1.ToggleState(PluginState.UndoAvailableState, true);
            }
            else
            {
                Module1.ToggleState(PluginState.UndoAvailableState, false);
            }
        }

        public T HamlTool { get; set; }

        public int BitDepth { get; set; }

        public esriGeometryType EsriGeometryType { get; set; }

        public MapPoint? TemplarBaselinePoint
        {
            get => _baselinePoint;
            set => _baselinePoint = value;
        }

        public long? TemplarBaselineOid
        {
            get => _baselinePointOID;
            set => _baselinePointOID = value;
        }

        public int? BaselineOid
        {
            get => _baselineOID;
            set => _baselineOID = value;
        }

        public void SetUseLearner(bool useLearner)
        {
            _useLearner = useLearner;
            Log.Here().Information("Using Learner: {@UseLearner}", _useLearner);

            if (useLearner) // If we're enabling the tool, disable the enable button and enable the disable button
            {
                Module1.ToggleState(PluginState.EnableLearnerState, false);
                Module1.ToggleState(PluginState.DisableLearnerState, true);
            }
            else
            {
                Module1.ToggleState(PluginState.EnableLearnerState, true);
                Module1.ToggleState(PluginState.DisableLearnerState, false);
            }
        }
    }
}
