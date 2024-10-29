/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core.Events;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Events;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using HamlProAppModule.haml.events;
using HamlProAppModule.haml.ui;
using HamlProAppModule.haml.ui.arctool;
using HamlProAppModule.haml.ui.experiment;
using HamlProAppModule.haml.ui.options;
using HamlProAppModule.haml.util;
using Newtonsoft.Json;
using Serilog;
using SF = ArcGIS.Desktop.Mapping.SymbolFactory;
using Module = ArcGIS.Desktop.Framework.Contracts.Module;
using Point = System.Windows.Point;

namespace HamlProAppModule
{
    public class Module1 : Module
    {
        private static Module1 _this = null;
        private static readonly CIMColor SignalPointColor = CIMColor.CreateRGBColor(71, 209, 67);
        private static string? _assemblyPath;

        public static dynamic activeHamlMapTool = null;
        public static Geometry loadedGeometry = null;
        public static int? loadedBitDepth = null;
        public static long? loadedOID = null;
        public static string loadedGDB = null;
        public static string loadedFC = null;
        public static string DefaultGDBPath = "";
        public static string DefaultPolylineFCName = "Curves";
        public static string DefaultPolygonFCName = "Surfaces";
        public static string DefaultPolylineSavePath = "";
        public static string DefaultPolygonSavePath = "";
        private static Layer? _selectedFeatureLayer;

        public const string SetLabelSideText = "  Double click to set landward side  ";
        public const string AddProfileText = "  Double click to add a profile  ";
        public const string SelectMHWPointText = "Double click the most seaward MHW point";
        
        private static ColorSettings _colorSettings;

        public static double MeanHighWaterVal = 0.5;
        public static double ProfileSpacing = 10.0;
        public static bool IsFixedTransect = true;
        public static int TransectLandwardLength = 125;
        public static int TransectSeawardLength = 0;
        public static float Opacity = 25f;
        public static double smartInsertionTolerance = 3 ;
        public static double highTolerance = 2.77;
        public static double lowTolerance = 2.77;

        public static ExperimentStatTracker StatTracker = new();
        
        /// <summary>
        /// Retrieve the singleton instance to this module here
        /// </summary>
        public static Module1 Current => _this ??= (Module1)FrameworkApplication.FindModule("HamlProAppModule_Module");

        public Module1()
        {
            TOCSelectionChangedEvent.Subscribe(LayerSelectionChanged);
            ActiveMapViewChangedEvent.Subscribe(MapViewChanged); ;
            LayersAddedEvent.Subscribe(LayersAdded);
            LayersRemovedEvent.Subscribe(LayersRemoved);
            MapSelectionChangedEvent.Subscribe(MapSelectionChanged);
            ProjectOpenedEvent.Subscribe(e => {
                DefaultGDBPath = e.Project.DefaultGeodatabasePath;
                DefaultPolylineSavePath = $"{DefaultGDBPath}\\{DefaultPolylineFCName}";
                DefaultPolygonSavePath = $"{DefaultGDBPath}\\{DefaultPolygonFCName}";
                CreateContourStorageFeatureClass(DefaultGDBPath, DefaultPolylineFCName, "POLYLINE");
                CreateContourStorageFeatureClass(DefaultGDBPath, DefaultPolygonFCName, "POLYGON");
            });
            ApplicationClosingEvent.Subscribe(OnApplicationClose);
            
            _colorSettings = LoadSettings();
            SettingsChangedEvent.Subscribe(OnSettingsChanged);
        }

        internal static CIMLineSymbol CreateHamlSearchSpaceSymbol(CIMColor? color = null, float opacity = 100)
        {
            color ??= GetElementCimColor(ColorSettings.GuiElement.SearchSpace, opacity);
            var lineStroke = SF.Instance.ConstructStroke(color, 0.75, SimpleLineStyle.Dash);
            
            return new CIMLineSymbol() { 
                SymbolLayers = new CIMSymbolLayer[1] { 
                    lineStroke 
                }
            };
        }

        internal static CIMSymbolReference CreateMHWPointSymbol(string hexColor, int size)
        {
            CIMColor cimColor = HexToCimColor(hexColor, 100);
            return SymbolFactory.Instance.ConstructPointSymbol(cimColor, size).MakeSymbolReference();
        }

        internal static CIMLineSymbol CreateHighlightedSearchSpaceSymbol(CIMColor? color) {
            color ??= GetElementCimColor(ColorSettings.GuiElement.SelectedProfile);
            var lineStroke = SF.Instance.ConstructStroke(color, 
                0.75, SimpleLineStyle.Dash);
            
            return new CIMLineSymbol() { 
                SymbolLayers = new CIMSymbolLayer[1] { 
                    lineStroke 
                }
            };
        }
        
        internal static CIMSymbolReference CreateLowPointSymbolReference(CIMColor? color = null, float opacity = 100)
        {
            color ??= GetElementCimColor(ColorSettings.GuiElement.LowAnnotation, opacity);
            CIMMarker marker = SF.Instance.ConstructMarker(color, 10, SimpleMarkerStyle.Circle);
            CIMPointSymbol pointSymbol = SymbolFactory.Instance.ConstructPointSymbol(marker);
            return pointSymbol.MakeSymbolReference();
        }
        
        internal static CIMSymbolReference CreateHighPointSymbolReference(CIMColor? color = null, float opacity = 100)
        {
            color ??= GetElementCimColor(ColorSettings.GuiElement.HighAnnotation, opacity);
            CIMMarker marker = SF.Instance.ConstructMarker(color, 10, SimpleMarkerStyle.Circle);
            CIMPointSymbol pointSymbol = SymbolFactory.Instance.ConstructPointSymbol(marker);
            return pointSymbol.MakeSymbolReference();
        }
        
        public async static void CreateContourStorageFeatureClass(string geodatabasePath, string featureClass, string geometry)
        {
            var exists = await QueuedTask.Run(() =>
            {
                try
                {
                    using (var geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(geodatabasePath))))
                    {
                        using (var fc = geodatabase.OpenDataset<FeatureClass>(featureClass))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    return false;
                }
            });
            if (exists)
            {
                return;
            }

            var sr = SpatialReferenceBuilder.CreateSpatialReference(4326);
            var gpParams = Geoprocessing.MakeValueArray(geodatabasePath, featureClass, geometry, null, "DISABLED", "ENABLED", sr);
            await Geoprocessing.ExecuteToolAsync("CreateFeatureClass_management", gpParams);
            var fields = new Dictionary<string, string> {
                            { "Timestamp", "DATE" },
                            { "ImageFile", "TEXT" },
                            { "BitDepth", "LONG" }
                        };
            foreach (var field in fields)
            {
                gpParams = Geoprocessing.MakeValueArray(Path.Combine(geodatabasePath, featureClass), field.Key, field.Value);
                await Geoprocessing.ExecuteToolAsync("AddField_management", gpParams);
            }
        }
        
        public static void ToggleState(PluginState pluginState, bool on)
        {
            string state = pluginState.ToString();
            
            if (on)
            {
                if (!FrameworkApplication.State.Contains(state))
                {
                    FrameworkApplication.State.Activate(state);
                }
            }
            else
            {
                if (FrameworkApplication.State.Contains(state))
                {
                    FrameworkApplication.State.Deactivate(state);
                }
            }
        }

        private void CheckForContourSelection(List<Layer> layers)
        {
            if (layers == null)
            {
                ToggleState(PluginState.ImageryLayerSelectedState, false);
                ToggleState(PluginState.PolylineContourSelectedState, false);
                ToggleState(PluginState.PolygonContourSelectedState, false);
                return;
            }

            string polylineSavePath;
            string polygonSavePath;
            if (string.IsNullOrWhiteSpace(OptionsVM.gdbPath))
            {
                polylineSavePath = DefaultPolylineSavePath;
                polygonSavePath = DefaultPolygonSavePath;
            }
            else
            {
                polylineSavePath = Path.Combine(OptionsVM.gdbPath, DefaultPolylineFCName);
                polygonSavePath = Path.Combine(OptionsVM.gdbPath, DefaultPolygonFCName);
            }
            QueuedTask.Run(() =>
            {
                var polylineLayer = layers.FirstOrDefault((lyr) => (lyr?.GetPath()?.OriginalString?.Equals(polylineSavePath)).GetValueOrDefault());
                var polygonLayer = layers.FirstOrDefault((lyr) => (lyr?.GetPath()?.OriginalString?.Equals(polygonSavePath)).GetValueOrDefault());

                if (polylineLayer != null && polylineLayer is FeatureLayer curveFL)
                {
                    if (curveFL.ShapeType == esriGeometryType.esriGeometryPolyline && curveFL.SelectionCount > 0)
                    {
                        ToggleState(PluginState.PolylineContourSelectedState, true);
                    }
                    else
                    {
                        ToggleState(PluginState.PolylineContourSelectedState, false);
                    }
                }
                else
                {
                    ToggleState(PluginState.PolylineContourSelectedState, false);
                }

                if (polygonLayer != null && polygonLayer is FeatureLayer surfaceFL)
                {
                    if (surfaceFL.ShapeType == esriGeometryType.esriGeometryPolygon && surfaceFL.SelectionCount > 0)
                    {
                        ToggleState(PluginState.PolygonContourSelectedState, true);
                    }
                    else
                    {
                        ToggleState(PluginState.PolygonContourSelectedState, false);
                    }
                }
                else
                {
                    ToggleState(PluginState.PolylineContourSelectedState, false);
                }
            });
        }
        
        public static void ResetLoadedParams()
        {
            loadedGeometry = null;
            loadedBitDepth = null;
            loadedOID = null;
            loadedGDB = null;
            loadedFC = null;
        }

        internal void LayersAdded(LayerEventsArgs e)
        {
            CheckForContourSelection(MapView.Active?.Map?.Layers?.ToList());
        }

        internal void LayersRemoved(LayerEventsArgs e)
        {
            CheckForContourSelection(MapView.Active?.Map?.Layers?.ToList());
        }

        internal void MapSelectionChanged(MapSelectionChangedEventArgs e)
        {
            CheckForContourSelection(MapView.Active?.Map?.Layers?.ToList());
            CheckFeatureClassSelection();
        }

        internal void MapViewChanged(ActiveMapViewChangedEventArgs e)
        {
            CheckForContourSelection(MapView.Active?.Map?.Layers?.ToList());
            CheckFeatureClassSelection();
        }

        internal static void CheckFeatureClassSelection()
        {
            if (MapView.Active != null)
            {
                QueuedTask.Run(() =>
                {
                    Dictionary<MapMember, List<long>>? selection = MapView.Active.Map.GetSelection().ToDictionary();
                    if (selection.Count == 1)
                    {
                        string fcName = selection.First().Key.Name;
                        FeatureLayer? featureLayer = GeodatabaseUtil.CheckSelectionType(fcName);

                        if (featureLayer is { ShapeType: esriGeometryType.esriGeometryPolyline, SelectionCount: 1 })
                        {
                            ToggleState(PluginState.PolylineSelectedState, true);
                        }
                        else
                        {
                            ToggleState(PluginState.PolylineSelectedState, false);
                        }
                    }    
                });
            }
        }

        internal static void LayerSelectionChanged(MapViewEventArgs e)
        {
            var selectedLayers = e.MapView.GetSelectedLayers();

            //Deactivate if there is no selected layer.
            if (selectedLayers.Count == 0)
            {
                ToggleState(PluginState.ImageryLayerSelectedState, false); return;
            }

            Layer selectedLayer = selectedLayers.First();

            if (selectedLayer is BasicRasterLayer == false)
            {
                ToggleState(PluginState.ImageryLayerSelectedState, false);
                return;
            }

            ToggleState(PluginState.ImageryLayerSelectedState, true);
        }

        internal static CIMSymbolReference GetSymbolReference(HamlGraphicType type, CIMColor? color = null)
        {
            CIMSymbolReference reference = null;
            
            switch (type)
            {
                case HamlGraphicType.Contour:
                    reference = CreateHamlPolylineSymbol(color).MakeSymbolReference();
                    break;
                case HamlGraphicType.ValidSearchSpace:
                    reference = CreateHamlSearchSpaceSymbol(color).MakeSymbolReference();
                    break;
                case HamlGraphicType.IgnoredSearchSpace:
                    reference = CreateHamlSearchSpaceSymbol(color, Opacity).MakeSymbolReference();
                    break;
                case HamlGraphicType.ActiveProfile:
                    reference = CreateHighlightedSearchSpaceSymbol(color).MakeSymbolReference();
                    break;
                case HamlGraphicType.UneditedVertices:
                    reference = CreateHAMLLockedSymbol(color).MakeSymbolReference();
                    break;
                case HamlGraphicType.EditedVertices:
                    reference = CreateProfileShorelineSymbol(color).MakeSymbolReference();
                    break;
                case HamlGraphicType.IgnoredEditedVertices:
                    reference = CreateProfileShorelineSymbol(color, Opacity).MakeSymbolReference();
                    break;
                case HamlGraphicType.IgnoredUneditedVertices:
                    reference = CreateHAMLLockedSymbol(color, Opacity).MakeSymbolReference();
                    break;
                case HamlGraphicType.FillSide:
                    reference = SymbolFactory.Instance
                        .ConstructPolygonSymbol(HexToCimColor("#9e9d9d", 90), SimpleFillStyle.Solid)
                        .MakeSymbolReference();
                    break;
                case HamlGraphicType.LabelSideOutline:
                    reference = BuildLabelSideOutlineGraphic();
                    break;
                case HamlGraphicType.HighPoints:
                    reference = CreateHighPointSymbolReference(color);
                    break;
                case HamlGraphicType.LowPoints:
                    reference = CreateLowPointSymbolReference(color);
                    break;
                case HamlGraphicType.IgnoredHighPoints:
                    reference = CreateHighPointSymbolReference(color, Opacity);
                    break;
                case HamlGraphicType.IgnoredLowPoints:
                    reference = CreateLowPointSymbolReference(color, Opacity);
                    break;
                case HamlGraphicType.Vertex:
                    reference = CreateProfileShorelineSymbol().MakeSymbolReference();
                    break;
            }

            return reference;
        }
        

        internal static CIMLineSymbol CreateHamlPolylineSymbol(CIMColor? color = null)
        {
            return QueuedTask.Run(() =>
            {
                color ??= GetElementCimColor(ColorSettings.GuiElement.ShorelineAnnotation);
                var stroke = SF.Instance.ConstructStroke(color, 3.0, SimpleLineStyle.Dash);

                return new CIMLineSymbol {SymbolLayers = new CIMSymbolLayer[] {stroke}};
            }).Result;
        }
        
        internal static CIMPointSymbol CreateProfileShorelineSymbol(CIMColor? color = null, float opacity = 100)
        {
            return QueuedTask.Run(() =>
            {
                color ??= GetElementCimColor(ColorSettings.GuiElement.ShorelineAnnotation, opacity);
                var marker = SF.Instance.ConstructMarker(color, 7, SimpleMarkerStyle.Square);
                
                return new CIMPointSymbol {SymbolLayers = new CIMSymbolLayer[] {marker}};
            }).Result;
        }

        internal static CIMPolygonSymbol CreateHamlPolygonSymbol()
        {
            return QueuedTask.Run(() =>
            {
                var marker = SF.Instance.ConstructMarker(GetElementCimColor(ColorSettings.GuiElement.ShorelineAnnotation),
                    7, SimpleMarkerStyle.Circle);
                var stroke = SF.Instance.ConstructStroke(GetElementCimColor(ColorSettings.GuiElement.Contour), 
                    1.0, SimpleLineStyle.Dash);

                marker.MarkerPlacement = new CIMMarkerPlacementOnVertices()
                {
                    AngleToLine = true,
                    PlaceOnEndPoints = true,
                    Offset = 0
                };
                
                return new CIMPolygonSymbol()
                {
                    SymbolLayers = new CIMSymbolLayer[2] {
                        marker, stroke
                    }
                };
            }).Result;
        }

        internal static CIMPointSymbol CreateHAMLLockedSymbol(CIMColor? color = null, float opacity = 100)
        {
            color ??= GetElementCimColor(ColorSettings.GuiElement.SavedProfile, opacity);
            return SF.Instance.ConstructPointSymbol(color, 7, SimpleMarkerStyle.Square);
        }

        public static CIMPointGraphic BuildSignalPointGraphic(Tuple<double, double> geoLoc, double c)
        {
            double r = (1 - c) * 255;
            double b = c * 255;
            
            var ptSymbol = SymbolFactory.Instance.ConstructPointSymbol(CIMColor.CreateRGBColor(r, 0, b), 5);
            
            return new CIMPointGraphic
            {
                Symbol = ptSymbol.MakeSymbolReference(),
                Location = MapPointBuilderEx.CreateMapPoint(geoLoc.Item1, geoLoc.Item2,
                    MapView.Active.Map.SpatialReference)
            };
        }
        
        public static CIMTextGraphic BuildTextOverlayGraphic(Geometry g, string text, int size)
        {
            var textSymbol = SymbolFactory.Instance.ConstructTextSymbol(ColorFactory.Instance.WhiteRGB, size, "Corbel", "Regular");
            //A balloon callout
            var balloonCallout = new CIMBalloonCallout();
            //set the callout's style
            balloonCallout.BalloonStyle = BalloonCalloutStyle.Rectangle;
            //Create a solid fill polygon symbol for the callout.
            var polySymbol = SymbolFactory.Instance.ConstructPolygonSymbol(ColorFactory.Instance.BlackRGB, SimpleFillStyle.Solid);
            //Set the callout's background to be the black polygon symbol
            balloonCallout.BackgroundSymbol = polySymbol;
            //margin inside the callout to place the text
            balloonCallout.Margin = new CIMTextMargin
            {
                Left = 5,
                Right = 5,
                Bottom = 5,
                Top = 5
            };
            //assign the callout to the text symbol's callout property
            textSymbol.Callout = balloonCallout;
            
            CIMTextGraphic labelCimTextGraphic = new CIMTextGraphic
            {
                Shape = g,
                Text = text,
                Symbol = textSymbol.MakeSymbolReference()
            };
            
            
            return labelCimTextGraphic;
        }

        /// <summary>
        /// Builds a thin envelope from the left side of the extent to the right side of the extent to contain the MHW
        /// overlay selection text. If this envelope overlaps a candidate MHW point, the envelope is moved upward in the
        /// mapview and checked for overlap. This process continues until a non-overlapping envelope is found or there is
        /// no more space to create said envelope. If no suitable envelope is found, the centroid of the mapview is returned.
        /// </summary>
        /// <param name="mhwPoints"> Candidate mhw points that are checked for overlap </param>
        /// <returns></returns>
        public static Geometry FindNonOverlappingMHWOverlayGeom(List<MapPoint> mhwPoints)
        {
            Envelope envelope = MapView.Active.Extent;
            MapPoint mpTopLeft = MapPointBuilderEx.CreateMapPoint(envelope.XMin, envelope.YMax, envelope.SpatialReference);
            MapPoint mpTopRight = MapPointBuilderEx.CreateMapPoint(envelope.XMax, envelope.YMax, envelope.SpatialReference);
            MapPoint mpBottomRight = MapPointBuilderEx.CreateMapPoint(envelope.XMax, envelope.YMin, envelope.SpatialReference);
            MapPoint mpBottomLeft = MapPointBuilderEx.CreateMapPoint(envelope.XMin, envelope.YMin, envelope.SpatialReference);
            
            Polyline leftExtent = PolylineBuilderEx.CreatePolyline(new List<MapPoint>() { mpTopLeft, mpBottomLeft });
            Polyline rightExtent = PolylineBuilderEx.CreatePolyline(new List<MapPoint>() { mpTopRight, mpBottomRight });
            
            double topAnchorDist = 1;
            double bottomAnchorDist = 0.9;
            Geometry? anchor = null;

            if (mhwPoints.Count > 0)
            {
                while (bottomAnchorDist > 0)
                {
                    List<MapPoint> polyPoints = new List<MapPoint>()
                    {
                        GeometryEngine.Instance.QueryPoint(leftExtent, SegmentExtensionType.NoExtension, topAnchorDist,
                            AsRatioOrLength.AsRatio),
                        GeometryEngine.Instance.QueryPoint(rightExtent, SegmentExtensionType.NoExtension, topAnchorDist,
                            AsRatioOrLength.AsRatio),
                        GeometryEngine.Instance.QueryPoint(rightExtent, SegmentExtensionType.NoExtension, bottomAnchorDist,
                            AsRatioOrLength.AsRatio),
                        GeometryEngine.Instance.QueryPoint(leftExtent, SegmentExtensionType.NoExtension, bottomAnchorDist,
                            AsRatioOrLength.AsRatio)
                    };

                    anchor = EnvelopeBuilderEx.CreateEnvelope(polyPoints[3], polyPoints[1]);
                    bool overlaps = mhwPoints.Any(mp => GeometryEngine.Instance.Intersects(anchor, mp));

                    if (!overlaps)
                    {
                        break;
                    }

                    anchor = null;
                    topAnchorDist -= 0.1;
                    bottomAnchorDist -= 0.1;
                }
            }

            // Use the centroid as a catch-all if suitable overlay is not found (this should never happen)
            if (anchor == null)
            {
                anchor = envelope.Center;
            }
            
            return anchor;
        }

        public static CIMSymbolReference BuildLabelSideOutlineGraphic()
        {
            CIMStroke outline = SymbolFactory.Instance.ConstructStroke(ColorFactory.Instance.GreenRGB, 4.0, SimpleLineStyle.Solid);
            
            return SymbolFactory.Instance.ConstructPolygonSymbol(
                ColorFactory.Instance.BlueRGB, SimpleFillStyle.Null, outline).MakeSymbolReference();
        }

        /// <summary>
        /// Called by Framework when ArcGIS Pro is closing
        /// </summary>
        /// <returns>False to prevent Pro from closing, otherwise True</returns>
        protected override bool CanUnload()
        {
            //TODO - add your business logic
            //return false to ~cancel~ Application close
            return true;
        }

        public static String RgbToHex(double[] rgb)
        {
            return string.Format("{0:X2}{1:X2}{2:X2}", (int) rgb[0], (int) rgb[1], (int) rgb[2]);
        }

        public static Layer? SelectedFeatureLayer
        {
            get => _selectedFeatureLayer;
            set => _selectedFeatureLayer = value;
        }

        private static ColorSettings LoadSettings()
        {
            var path = ColorSettings.FileLocation;
            if (!File.Exists(path)) return new ColorSettings();
            
            var file = File.ReadAllText(path);
            
            return JsonConvert.DeserializeObject<ColorSettings>(file, 
                       new JsonSerializerSettings {Error = (_, args) => args.ErrorContext.Handled = true}) 
                   ?? new ColorSettings();
        }

        private static Task OnApplicationClose(CancelEventArgs args)
        {
            Log.CloseAndFlush();
            return Task.FromResult(0);
        }
        
        private static int[] HexToRgb(string hex)
        {
            var color = ColorTranslator.FromHtml(hex);
            int r = Convert.ToInt16(color.R);
            int g = Convert.ToInt16(color.G);
            int b = Convert.ToInt16(color.B);

            return new []{r, g, b};
        }

        public static CIMColor HexToCimColor(string hex, double alpha = 100)
        {
            var rgb = HexToRgb(hex);
            return CIMColor.CreateRGBColor(rgb[0], rgb[1], rgb[2], alpha);
        }

        internal static CIMColor GetElementCimColor(ColorSettings.GuiElement element)
        {
            return HexToCimColor(_colorSettings.GetColor(element));
        }
        
        internal static CIMColor GetElementCimColor(ColorSettings.GuiElement element, float alpha)
        {
            var color = HexToCimColor(_colorSettings.GetColor(element));
            color.Alpha = alpha;
            return color;
        }

        internal static string GetElementHexColor(ColorSettings.GuiElement element)
        {
            return _colorSettings.GetColor(element);
        }

        internal static string GetDefaultColor(ColorSettings.GuiElement element)
        {
            return ColorSettings.GetDefaultColor(element);
        }

        internal static void UpdateColorSettings(Dictionary<ColorSettings.GuiElement, string> elementColors)
        {
            _colorSettings.Update(elementColors);
        }

        internal static void UpdateTransectConstraint(bool isFixedTransect)
        {
            IsFixedTransect = isFixedTransect;
        }
        
        internal static void UpdateTransectLandwardLength(int transectLength)
        {
            TransectLandwardLength = transectLength;
        }
        
        internal static void UpdateTransectSeawardLength(int transectLength)
        {
            TransectSeawardLength = transectLength;
        }

        internal static void UpdateMeanHighWaterPoint(double meanHighWaterPoint)
        {
            MeanHighWaterVal = meanHighWaterPoint;
        }

        internal static void UpdateProfileSpacing(double profileSpacing)
        {
            ProfileSpacing = profileSpacing;
        }

        internal static void UpdateInsertThresh(double insertThresh)
        {
            smartInsertionTolerance = insertThresh;
        }
        
        internal static void UpdateCrestThresh(double crestThresh)
        {
            highTolerance = crestThresh;
        }
        
        internal static void UpdateToeThresh(double toeThresh)
        {
            lowTolerance = toeThresh;
        }

        private static void OnSettingsChanged(NoArgs noArgs)
        {
            var json = JsonConvert.SerializeObject(_colorSettings, Formatting.Indented);
            File.WriteAllText(ColorSettings.FileLocation, json);
        }
        
        public static void CalculateScreenBounds()
        {
            ScreenWidthHeight = QueuedTask.Run(() =>
            {
                Envelope envelope = MapView.Active.Extent;
                MapPoint mpTopRight = MapPointBuilderEx.CreateMapPoint(envelope.XMax, envelope.YMax, envelope.SpatialReference);
                MapPoint mpTopLeft = MapPointBuilderEx.CreateMapPoint(envelope.XMin, envelope.YMax, envelope.SpatialReference);
                MapPoint mpBottomLeft = MapPointBuilderEx.CreateMapPoint(envelope.XMin, envelope.YMin, envelope.SpatialReference);
        
                Point topLeftScreen = MapView.Active.MapToScreen(mpTopLeft);
                Point topRightScreen = MapView.Active.MapToScreen(mpTopRight);
                Point bottomLeftScreen = MapView.Active.MapToScreen(mpBottomLeft);

                int width = Math.Max((int)topRightScreen.X, (int) topLeftScreen.X);
                int height = Math.Max((int) topLeftScreen.Y,(int) bottomLeftScreen.Y);

                return (width, height);
            }).Result;
        }
        
        public static (int Width, int Height) ScreenWidthHeight { get; private set; }

        public static string? AssemblyPath => _assemblyPath ??= GetAssemblyPath();

        private static string? GetAssemblyPath()
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(assemblyLocation);
        }
    }
}
