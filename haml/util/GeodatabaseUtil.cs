/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Core.Data.Exceptions;
using ArcGIS.Core.Geometry;
using ArcGIS.Core.Internal.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.ui.options;

namespace HamlProAppModule.haml.util;

public static class GeodatabaseUtil
{
    public const string ProfilePointsDatasetName = "ProfilePoints";
    public const string LowPointsFCName = "LowPoints";
    public const string HighPointsFCName = "HighPoints";
    public const string ShorelinePointsFCName = "ShorelinePoints";
    public const string BaselineFCName = "BaselinePoints";
    public const string Profile = "profile";
    public const string Ignore = "ignore";
    public const string BaselineOID = "baseline_id";
    public const string BaselineSegmentIndex = "baseline_seg_idx";
    public const string Lon = "lon";
    public const string Lat = "lat";
    public const string Easting = "easting";
    public const string Northing = "northing";
    public const string MHWVal = "mhw";
    public const string DLowX = "dlow_x";
    public const string DLowZ = "dlow_z";
    public const string DHighX = "dhigh_x";
    public const string DHighZ = "dhigh_z";
    public const string Berm = "berm";
    public const string DistanceAlongBaseline = "dist_along_baseline";
    public const string LandwardTransectLength = "land_transect_len";
    public const string SeawardTransectLength = "sea_transect_len";
    public const string OID = "objectid";
    public const string Corrected = "action";
    public const string Error = "err";
    public const string OldLat = "lat_sm";
    public const string OldLon = "lon_sm";
    public const string NewLat = "lat_raw";
    public const string NewLon = "lon_raw";
    public const string OldEasting = "easting_sm";
    public const string OldNorthing = "northing_sm";
    public const string NewEasting = "easting_raw";
    public const string NewNorthing = "northing_raw";
        
    private static Dictionary<string, string> lowFieldDict;
    private static Dictionary<string, string> highFieldDict;

    static GeodatabaseUtil()
    {
        // both high and low points share these common attributes
        var baseFieldDict = new Dictionary<string, string>
        {
            { Profile, "INTEGER" },
            { Lon, "DOUBLE" },
            { Lat, "DOUBLE" },
            { Easting, "DOUBLE" },
            { Northing, "DOUBLE" },
            { DistanceAlongBaseline, "DOUBLE"},
            { Ignore, "INTEGER"},
            { NewEasting, "DOUBLE" },
            { NewNorthing, "DOUBLE"},
            { OldEasting, "DOUBLE"},
            { OldNorthing, "DOUBLE"},
            { NewLat, "DOUBLE"},
            { NewLon, "DOUBLE"},
            { OldLat, "DOUBLE"},
            { OldLon, "DOUBLE"},
            { Corrected, "INTEGER"},
            { Error, "DOUBLE"}
        };

        lowFieldDict = new Dictionary<string, string>
        {
            {DLowX, "DOUBLE"},
            {DLowZ, "DOUBLE"}
        };
            
        highFieldDict = new Dictionary<string, string>
        {
            {DHighX, "DOUBLE"},
            {DHighZ, "DOUBLE"},
            {Berm, "INTEGER"}
        };
            
        baseFieldDict.ToList().ForEach(kvp =>
        {
            lowFieldDict[kvp.Key] = kvp.Value;
            highFieldDict[kvp.Key] = kvp.Value;
        });
    }

    public static string LoadGeometry(string geodatabase, String featureClassName, esriGeometryType geometryType)
    {
        var fcPath = Path.Combine(geodatabase, featureClassName);

        var layers = MapView.Active?.Map?.Layers;

        if (layers == null)
        {
            return
                $"No layers found. Please open a map and add the '{featureClassName}' feature class. It is located in this geodatabase: {geodatabase}";
        }

        return QueuedTask.Run(() =>
        {
            var featureLayers = layers.Where(lyr => lyr is FeatureLayer);
            if (featureLayers.Count() <= 0)
            {
                return
                    $"The '{featureClassName}' feature class is not in the map. Please add it and select a row to edit. It is located in this geodatabase: {geodatabase}";
            }

            var contourLyr =
                featureLayers.FirstOrDefault(lyr => (lyr?.GetPath()?.OriginalString?.Equals(fcPath)).Value);

            if (contourLyr == null)
            {
                return
                    $"The '{featureClassName}' feature class is not in the map. Please add it and select a row to edit. It is located in this geodatabase: {geodatabase}";
            }

            if (contourLyr is FeatureLayer fl)
            {
                if (fl.ShapeType != geometryType)
                {
                    return $"The selected feature layer is not a {geometryType} feature layer.";
                }

                if (fl.SelectionCount <= 0)
                {
                    return $"No row has been selected on the '{featureClassName}' feature layer.\n" +
                           "Please select a row, the first selection will be used.";
                }

                using (var selection = fl.GetSelection())
                {
                    var objectIDs = selection.GetObjectIDs();
                    List<long> firstOID = new List<long> { objectIDs.First() };
                    var queryFilter = new ArcGIS.Core.Data.QueryFilter()
                    {
                        ObjectIDs = firstOID
                    };
                    using (var rowCursor = fl.GetFeatureClass().Search(queryFilter))
                    {
                        rowCursor.MoveNext();
                        using (var row = rowCursor.Current)
                        {
                            if (row == null)
                            {
                                return $"No row has been selected on the '{featureClassName}' feature layer.\n" +
                                       "Please select a row, the first selection will be used.";
                            }

                            var shapeFieldIndex = row.FindField("Shape");
                            Module1.loadedGeometry = (Geometry)row.GetOriginalValue(shapeFieldIndex);
                            var bitDepthFieldIndex = row.FindField("BitDepth");
                            Module1.loadedBitDepth = (int)row.GetOriginalValue(bitDepthFieldIndex);
                            Module1.loadedOID = row.GetObjectID();
                            Module1.loadedGDB = geodatabase;
                            Module1.loadedFC = featureClassName;
                        }
                    }
                }
            }
            else
            {
                return "The selected layer is not a feature layer.\n";
            }

            return "";
        }).Result;
    }

    public static String GetGDBPath()
    {
        string gdbPath = "";
        if (string.IsNullOrWhiteSpace(Module1.loadedGDB) || string.IsNullOrWhiteSpace(Module1.loadedFC))
        {
            if (string.IsNullOrWhiteSpace(OptionsVM.gdbPath))
            {
                gdbPath = Module1.DefaultGDBPath;
            }
            else
            {
                gdbPath = OptionsVM.gdbPath;
            }
        }
        else
        {
            gdbPath = Module1.loadedGDB;
        }

        return gdbPath;
    }

    public static void CreateLineFeatureClass(string name, SpatialReference spatialReference,
        Geodatabase geodatabase)
    {
        // This static helper routine creates a FieldDescription for a GlobalID field with default values
        ArcGIS.Core.Data.DDL.FieldDescription globalIDFieldDescription = ArcGIS.Core.Data.DDL.FieldDescription.CreateGlobalIDField();

        // This static helper routine creates a FieldDescription for an ObjectID field with default values
        ArcGIS.Core.Data.DDL.FieldDescription objectIDFieldDescription = ArcGIS.Core.Data.DDL.FieldDescription.CreateObjectIDField();

        List<ArcGIS.Core.Data.DDL.FieldDescription> fieldDescriptions = new List<ArcGIS.Core.Data.DDL.FieldDescription>()
            { globalIDFieldDescription, objectIDFieldDescription };

        ShapeDescription shapeDescription = new ShapeDescription(GeometryType.Polyline, spatialReference);

        FeatureClassDescription featureClassDescription =
            new FeatureClassDescription(name, fieldDescriptions, shapeDescription);

        SchemaBuilder schemaBuilder = new SchemaBuilder(geodatabase);

        schemaBuilder.Create(featureClassDescription);

        schemaBuilder.Build();
    }

    public static bool DatasetExists(Geodatabase geodatabase, string datasetName)
    {
        try
        {
            FeatureDatasetDefinition tableDefinition =
                geodatabase.GetDefinition<FeatureDatasetDefinition>(datasetName);
            tableDefinition.Dispose();
            return true;
        }
        catch
        {
            // GetDefinition throws an exception if the definition doesn't exist
            return false;
        }
    }

    public static bool TableExists(Geodatabase geodatabase, string tableName)
    {
        try
        {
            TableDefinition tableDefinition = geodatabase.GetDefinition<TableDefinition>(tableName);
            tableDefinition.Dispose();
            return true;
        }
        catch
        {
            // GetDefinition throws an exception if the definition doesn't exist
            return false;
        }
    }

    public static async Task<int> AddProfilePoint(string featureClassName, MapPoint mp, string id,
        double zval, double shoreDist, Dictionary<string, string> fieldDict)
    {
        return await AddProfilePoint(featureClassName, mp, id, zval, shoreDist, false, fieldDict);
    }

    public static async Task<int> AddProfilePoint(string featureClassName, MapPoint mp, string id,
        double zval, double shoreDist, bool isBerm, Dictionary<string, string> fieldDict)
    {
        string gdbPath = GetGDBPath();
        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            try
            {
                using (FeatureDataset fd = geodatabase.OpenDataset<FeatureDataset>(ProfilePointsDatasetName))
                {
                    using (FeatureClass fc = fd.OpenDataset<FeatureClass>(featureClassName))
                    {
                        var fcPath = Path.Combine(gdbPath, fd.GetName(), featureClassName);
                        var definition = fc.GetDefinition();
                        if (!definition.HasZ())
                        {
                            MessageBox.Show("Feature Class must be Z Enabled.");
                            return -1;
                        }

                        bool isLow = fc.GetDefinition().GetName().Equals(LowPointsFCName);

                        string shoreDistDef = isLow ? DLowX : DHighZ;
                        string elevation = isLow ? DLowZ : DHighZ;

                        string[] requiredFields =
                            { Profile, Lon, Lat, Easting, Northing, shoreDistDef, elevation };
                        var fields = definition.GetFields();
                        var fieldNames = fields.Select(f => f.Name);
                        var missingFields = requiredFields.Where(f => !fieldNames.Contains(f));
                        if (missingFields.Count() > 0)
                        {
                            foreach (var field in missingFields)
                            {
                                if (!fieldDict.ContainsKey(field))
                                {
                                    continue;
                                }

                                var fieldType = fieldDict[field];
                                var gpParams = Geoprocessing.MakeValueArray(fcPath, field, fieldType);
                                await Geoprocessing.ExecuteToolAsync("AddField_management", gpParams);
                            }
                        }

                        int object_id = -1;
                        var eo = new EditOperation();
                        eo.Callback(context =>
                        {
                            using (var rb = fc.CreateRowBuffer())
                            using (var row = fc.CreateRow(rb))
                            {
                                context.Invalidate(row);
                                double nameVal = Int64.Parse(id.Split('_')[1]);
                                row[Profile] = nameVal;
                                row[elevation] = zval;
                                row[shoreDistDef] = shoreDist;

                                row[Easting] = mp.X;
                                row[Northing] = mp.Y;

                                // create a transform so that we can save the easting/northing as lon/lat (WGS84)
                                ProjectionTransformation transform = ProjectionTransformation.Create(
                                    MapView.Active.Extent.SpatialReference,
                                    SpatialReferences.WGS84);
                                MapPoint reproj = (MapPoint)GeometryEngine.Instance.ProjectEx(mp, transform);
                                row[Lon] = reproj.X;
                                row[Lat] = reproj.Y;

                                if (fieldDict.ContainsKey(Berm))
                                {
                                    row[Berm] = isBerm ? 1 : 0;
                                }

                                row.SetShape(mp);

                                object_id = (int)row[OID];
                            }
                        }, fc);

                        eo.Execute();

                        await Project.Current.SaveEditsAsync();
                        return object_id;
                    }
                }
            }
            catch (GeodatabaseException gdbe)
            {
                GUI.ShowToast(gdbe.Message);
            }
        }

        return -1;
    }
        
    public static void UpdateProfilePoint(string featureClassName, Geometry geometry, int oid)
    {
        string gdbPath = GetGDBPath();
        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            try
            {
                using (FeatureDataset fd = geodatabase.OpenDataset<FeatureDataset>(ProfilePointsDatasetName))
                {
                    using (FeatureClass fc = fd.OpenDataset<FeatureClass>(featureClassName))
                    {
                        var fcPath = Path.Combine(gdbPath, fd.GetName(), featureClassName);
                        var layers = MapView.Active?.Map?.Layers;
                        var featureLayers = layers.Where(lyr => lyr is FeatureLayer);
                        var featureClassLayer = featureLayers.FirstOrDefault(lyr =>
                            lyr?.GetPath() != null && lyr.GetPath().OriginalString.Equals(fcPath));
                        if (featureClassLayer == null)
                        {
                            MessageBox.Show("Unable to load " + fcPath + " to update points!");
                            return;
                        }

                        var eo = new ArcGIS.Desktop.Editing.EditOperation();
                        eo.Modify(featureClassLayer, oid, geometry);
                        eo.Execute();
                    }

                }
            }
            catch (GeodatabaseException gdbe)
            {
                MessageBox.Show(gdbe.Message);
            }
        }
    }

    public static void DeleteProfilePoint(string featureClassName, int oid)
    {
        string gdbPath = GetGDBPath();
        bool deletionResult = false;

        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            try
            {
                using (FeatureDataset fd = geodatabase.OpenDataset<FeatureDataset>(ProfilePointsDatasetName))
                {
                    using (fd.OpenDataset<FeatureClass>(featureClassName))
                    {
                        var fcPath = Path.Combine(gdbPath, fd.GetName(), featureClassName);
                        var layers = MapView.Active?.Map?.Layers;
                        var featureLayers = layers.Where(lyr => lyr is FeatureLayer);
                        var featureClassLayer = featureLayers.FirstOrDefault(lyr =>
                            lyr?.GetPath() != null && lyr.GetPath().OriginalString.Equals(fcPath));
                        if (featureClassLayer == null)
                        {
                            MessageBox.Show("Unable to load " + fcPath + " to delete point!");
                            return;
                        }

                        var eo = new EditOperation();
                        eo.Delete(featureClassLayer, oid);
                        eo.Execute();
                    }
                }
            }
            catch (GeodatabaseException gdbe)
            {
                MessageBox.Show(gdbe.Message);
            }
        }
    }
        
    public static void CreateBaselineFeatureClass(SpatialReference sr, string fcName)
    {
        string gdbPath = GetGDBPath();

        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            if (!TableExists(geodatabase, fcName))
            {
                // This static helper routine creates a FieldDescription for a GlobalID field with default values
                ArcGIS.Core.Data.DDL.FieldDescription globalIDFieldDescription = ArcGIS.Core.Data.DDL.FieldDescription.CreateGlobalIDField();

                // This static helper routine creates a FieldDescription for an ObjectID field with default values
                ArcGIS.Core.Data.DDL.FieldDescription objectIDFieldDescription = ArcGIS.Core.Data.DDL.FieldDescription.CreateObjectIDField();
                    
                ArcGIS.Core.Data.DDL.FieldDescription baselineIDFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(BaselineOID, FieldType.Integer);

                List<ArcGIS.Core.Data.DDL.FieldDescription> fieldDescriptions = new List<ArcGIS.Core.Data.DDL.FieldDescription>()
                    { globalIDFieldDescription, objectIDFieldDescription , baselineIDFieldDescription };

                ShapeDescription shapeDescription = new ShapeDescription(GeometryType.Point, sr);

                FeatureClassDescription featureClassDescription =
                    new FeatureClassDescription(fcName, fieldDescriptions, shapeDescription);

                SchemaBuilder schemaBuilder = new SchemaBuilder(geodatabase);

                schemaBuilder.Create(featureClassDescription);

                schemaBuilder.Build();
            }

            bool baselineLayerExists = false;
            foreach (var layer in MapView.Active.Map.GetLayersAsFlattenedList())
            {
                if (fcName.Equals(layer.Name))
                {
                    baselineLayerExists = true;
                }
            }

            if (!baselineLayerExists)
            {
                AddPointLayer(gdbPath, "", fcName);
            }
        }
    }

    public static void CreatePointDatasetAndFeatureClass(SpatialReference sr)
    {
        string gdbPath = GetGDBPath();

        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            SchemaBuilder schemaBuilder = new SchemaBuilder(geodatabase);

            // Create a FeatureDataset token and the dataset to hold the high and low points
            FeatureDatasetDescription featureDatasetDescription =
                new FeatureDatasetDescription(ProfilePointsDatasetName, sr);
            FeatureDatasetToken featureDatasetToken = schemaBuilder.Create(featureDatasetDescription);

            if (!DatasetExists(geodatabase, ProfilePointsDatasetName))
            {
                schemaBuilder.Build();
            }

            if (!TableExists(geodatabase, HighPointsFCName))
            {
                schemaBuilder = new SchemaBuilder(geodatabase);
                FeatureClassDescription highFcd = BuildProfilePointFeatureClasses(sr, HighPointsFCName);
                BuildSchemaFeatureClass(schemaBuilder, featureDatasetToken, highFcd);
            }
                
            // Create a FeatureClass description for the high and low points
            if (!TableExists(geodatabase, LowPointsFCName))
            {
                schemaBuilder = new SchemaBuilder(geodatabase);
                FeatureClassDescription lowFcd = BuildProfilePointFeatureClasses(sr, LowPointsFCName);
                BuildSchemaFeatureClass(schemaBuilder, featureDatasetToken, lowFcd);
            }
                
            if (!TableExists(geodatabase, ShorelinePointsFCName))
            {
                schemaBuilder = new SchemaBuilder(geodatabase);
                FeatureClassDescription shoreFcd = BuildShorelineFeatureClass(sr, ShorelinePointsFCName);
                BuildSchemaFeatureClass(schemaBuilder, featureDatasetToken, shoreFcd);
            }
        }

        bool lowPointsExists = false;
        bool highPointsExists = false;
        bool shorePointsExists = false;

        foreach (var layer in MapView.Active.Map.GetLayersAsFlattenedList())
        {
            if (LowPointsFCName.Equals(layer.Name))
            {
                lowPointsExists = true;
            }
            else if (HighPointsFCName.Equals(layer.Name))
            {
                highPointsExists = true;
            }                 
            else if (ShorelinePointsFCName.Equals(layer.Name))
            {
                shorePointsExists = true;
            }
        }

        if (!highPointsExists)
        {
            AddPointLayer(gdbPath, ProfilePointsDatasetName, HighPointsFCName);
        }

        if (!lowPointsExists)
        {
            AddPointLayer(gdbPath, ProfilePointsDatasetName, LowPointsFCName);
        }
            
        if (!shorePointsExists)
        {
            AddPointLayer(gdbPath, ProfilePointsDatasetName, ShorelinePointsFCName);
        }
    }

    public static async void AddPointLayer(string gdbPath, string datasetName, string layerName)
    {
        Uri uri = new Uri(Path.Combine(gdbPath, datasetName, layerName));
        await QueuedTask.Run(() => LayerFactory.Instance.CreateLayer(uri, MapView.Active.Map));
    }

    private static void BuildSchemaFeatureClass(SchemaBuilder schemaBuilder, FeatureDatasetToken token,
        FeatureClassDescription fcDesc)
    {
        // Create a FeatureClass inside a FeatureDataset
        schemaBuilder.Create(new FeatureDatasetDescription(token), fcDesc);

        // Build status
        bool buildStatus = schemaBuilder.Build();

        // Build errors
        if (!buildStatus)
        {
            IReadOnlyList<string> errors = schemaBuilder.ErrorMessages;

            if (errors.Count > 0)
            {
                String message = "Errors when making feature class for " + fcDesc.Name;
                GUI.ShowToast(message);
            }
        }
    }

    private static FeatureClassDescription BuildShorelineFeatureClass(SpatialReference sr, string featureClassName)
    {
        ArcGIS.Core.Data.DDL.FieldDescription globalIDFieldDescription = ArcGIS.Core.Data.DDL.FieldDescription.CreateGlobalIDField();
        ArcGIS.Core.Data.DDL.FieldDescription objectIDFieldDescription = ArcGIS.Core.Data.DDL.FieldDescription.CreateObjectIDField();
        ArcGIS.Core.Data.DDL.FieldDescription nameFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Profile, FieldType.Integer);
        ArcGIS.Core.Data.DDL.FieldDescription northingFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Northing, FieldType.Double);
        ArcGIS.Core.Data.DDL.FieldDescription eastingFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Easting, FieldType.Double);
        ArcGIS.Core.Data.DDL.FieldDescription latFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Lon, FieldType.Double);
        ArcGIS.Core.Data.DDL.FieldDescription lonFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Lat, FieldType.Double);
        ArcGIS.Core.Data.DDL.FieldDescription mhwFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(MHWVal, FieldType.Double);
        ArcGIS.Core.Data.DDL.FieldDescription landTransectLenFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(LandwardTransectLength, FieldType.Integer);
        ArcGIS.Core.Data.DDL.FieldDescription seaTransectLenFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(SeawardTransectLength, FieldType.Integer);
            
        List<ArcGIS.Core.Data.DDL.FieldDescription> fieldDescriptions = new List<ArcGIS.Core.Data.DDL.FieldDescription>()
        {
            globalIDFieldDescription,
            objectIDFieldDescription,
            nameFieldDescription,
            lonFieldDescription,
            latFieldDescription,
            eastingFieldDescription,
            northingFieldDescription,
            mhwFieldDescription,
            landTransectLenFieldDescription,
            seaTransectLenFieldDescription
        };
            
        ShapeDescription shapeDescription = new ShapeDescription(GeometryType.Point, sr);
        shapeDescription.HasZ = true;

        return new FeatureClassDescription(featureClassName, fieldDescriptions, shapeDescription);
    }

    private static FeatureClassDescription BuildProfilePointFeatureClasses(SpatialReference spatialReference,
        String featureClassName)
    {
        // TODO: ADD z_error, start_date, end_date. Need to talk with sponsor to determine how to move forward.

        ArcGIS.Core.Data.DDL.FieldDescription globalIDFieldDescription = ArcGIS.Core.Data.DDL.FieldDescription.CreateGlobalIDField();
        ArcGIS.Core.Data.DDL.FieldDescription objectIDFieldDescription = ArcGIS.Core.Data.DDL.FieldDescription.CreateObjectIDField();
        ArcGIS.Core.Data.DDL.FieldDescription nameFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Profile, FieldType.Integer);
        ArcGIS.Core.Data.DDL.FieldDescription distFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(DistanceAlongBaseline, FieldType.Double);
        ArcGIS.Core.Data.DDL.FieldDescription ignoreFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Ignore, FieldType.Integer);
        ArcGIS.Core.Data.DDL.FieldDescription baselineOidFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(BaselineOID, FieldType.Integer);
        ArcGIS.Core.Data.DDL.FieldDescription baselineSegmentIndexFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(BaselineSegmentIndex, FieldType.Integer);

        ArcGIS.Core.Data.DDL.FieldDescription shorelineDistXFieldDescription;
        ArcGIS.Core.Data.DDL.FieldDescription elevationFieldDescription;
        ArcGIS.Core.Data.DDL.FieldDescription? bermFieldDescription = null;
        if (featureClassName.Equals(LowPointsFCName))
        {
            shorelineDistXFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(DLowX, FieldType.Double);
            elevationFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(DLowZ, FieldType.Double);
        }
        else
        {
            shorelineDistXFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(DHighX, FieldType.Double);
            elevationFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(DHighZ, FieldType.Double);
            bermFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Berm, FieldType.Integer);
        }

        // Geo-coord fields
        ArcGIS.Core.Data.DDL.FieldDescription northingFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Northing, FieldType.Double);
        ArcGIS.Core.Data.DDL.FieldDescription eastingFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Easting, FieldType.Double);
        ArcGIS.Core.Data.DDL.FieldDescription latFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Lon, FieldType.Double);
        ArcGIS.Core.Data.DDL.FieldDescription lonFieldDescription = new ArcGIS.Core.Data.DDL.FieldDescription(Lat, FieldType.Double);

        List<ArcGIS.Core.Data.DDL.FieldDescription> fieldDescriptions = new List<ArcGIS.Core.Data.DDL.FieldDescription>()
        {
            globalIDFieldDescription,
            objectIDFieldDescription,
            nameFieldDescription,
            lonFieldDescription,
            latFieldDescription,
            eastingFieldDescription,
            northingFieldDescription,
            shorelineDistXFieldDescription,
            elevationFieldDescription,
            distFieldDescription,
            ignoreFieldDescription,
            baselineOidFieldDescription,
            baselineSegmentIndexFieldDescription
        };

        if (bermFieldDescription is not null) fieldDescriptions.Add(bermFieldDescription);

        ShapeDescription shapeDescription = new ShapeDescription(GeometryType.Point, spatialReference);
        shapeDescription.HasZ = true;

        return new FeatureClassDescription(featureClassName, fieldDescriptions, shapeDescription);
    }

    public static async Task<List<Geometry>> BuildFromFeatureClass(FeatureClass fc, bool inView)
    {
        return await QueuedTask.Run(() =>
        {
            try
            {
                using (fc)
                {
                    SpatialQueryFilter spatialQuery;
                    if (inView)
                    {
                        spatialQuery = new SpatialQueryFilter()
                        {
                            FilterGeometry = MapView.Active.Extent,
                            SpatialRelationship = SpatialRelationship.Intersects,
                        };
                    }
                    else
                    {
                        spatialQuery = null;
                    }

                    List<Geometry> features = new List<Geometry>();

                    using (RowCursor rc = fc.Search(spatialQuery, false))
                    {
                        while (rc.MoveNext())
                        {
                            using (Feature feature = (Feature)rc.Current)
                            {
                                if (feature.GetShape() is MapPoint)
                                {
                                    Geometry geom = feature.GetShape();
                                    features.Add(MapPointBuilder.CreateMapPoint(geom as MapPoint,
                                        geom.SpatialReference));
                                }
                                else if (feature.GetShape() is Polyline)
                                {
                                    Polyline featurePoly = feature.GetShape() as Polyline;
                                    features.Add(featurePoly);
                                }
                                else
                                {
                                    // TODO: do we need to add the ability to load a polygon?
                                }
                            }
                        }
                    }

                    return features;
                }
            }
            catch (GeodatabaseException exObj)
            {
                //TODO: serilog? 
                string message = exObj.Message;
                return null;
            }
        }) ?? throw new InvalidOperationException();
    }

    public static esriGeometryType GetShapeType(String layerName)
    {
        FeatureLayer? fl = MapView.Active.Map.FindLayers(layerName).First() as FeatureLayer;
        return fl != null ? fl.ShapeType : esriGeometryType.esriGeometryNull;
    }

    public static FeatureClass? GetFeatureClassFromFeatureLayer(String fcName)
    {
        FeatureLayer? fl = MapView.Active.Map.FindLayers(fcName).First() as FeatureLayer;
        return fl != null ? fl.GetFeatureClass() : null;
    }

    public static FeatureLayer? CheckSelectionType(string fcName)
    {
        IEnumerable<Layer> layers = MapView.Active.Map.Layers.Where(layer => layer is FeatureLayer);
            
        return QueuedTask.Run(() =>
        {
            foreach (FeatureLayer featureLayer in layers)
            {
                if (featureLayer.Name.Equals(fcName))
                {
                    return featureLayer;
                }
            }

            return null;
                
        }).Result;
    }

    /// <summary>
    /// Loads a geometry based on a feature classes name and said geometry's OID
    /// </summary>
    /// <param name="fcName"></param>
    /// <param name="oid"></param>
    /// <returns></returns>
    public static Geometry? GetFeatureFromOID(string fcName, long oid)
    {
        string gdbPath = GetGDBPath();
        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            try
            {
                using (FeatureClass fc = geodatabase.OpenDataset<FeatureClass>(fcName))
                {
                    QueryFilter queryFilter = new QueryFilter();
                    using (var rowCursor = fc.Search(queryFilter))
                    {
                        while (rowCursor.MoveNext())
                        {
                            using (var row = rowCursor.Current)
                            {
                                if (row != null)
                                { 
                                    var shapeFieldIndex = row.FindField("Shape");
                                    long rowOid = row.GetObjectID();

                                    if (rowOid == oid)
                                    {
                                        return row.GetOriginalValue(shapeFieldIndex) as Geometry;
                                    }
                                }
                            }    
                        }
                    } 
                }
            }
            catch (GeodatabaseException gdbe)
            {
                MessageBox.Show("Unable to load .");
            }
        }

        return null;
    }
        
    public static async Task<int> SaveMapPoint(MapPoint point, string fcName, long? baselineOID = null)
    {
        string gdbPath = GetGDBPath();

        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            try
            {
                using (FeatureClass fc = geodatabase.OpenDataset<FeatureClass>(fcName))
                {
                    int object_id = -1;
                    var eo = new EditOperation();
                    eo.Callback(context =>
                    {
                        using (var rb = fc.CreateRowBuffer())
                        using (var row = fc.CreateRow(rb))
                        {
                            context.Invalidate(row);
                            row.SetShape(MapPointBuilder.CreateMapPoint(point.X, point.Y, point.SpatialReference));

                            if (baselineOID != null)
                            {
                                row[BaselineOID] = (int) baselineOID.Value;
                            }
                                
                            object_id = (int)row[OID];
                            row.Store();
                        }
                    }, fc);

                    eo.Execute();

                    await Project.Current.SaveEditsAsync();
                    return object_id;
                }
                    
            }
            catch (GeodatabaseException gdbe)
            {
                GUI.ShowToast(gdbe.Message);
            }
        }
            
        return -1;
    }
        
    public static async void UpdateBaselinePoint(long oid, MapPoint point)
    {
        string gdbPath = GetGDBPath();
        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            try
            {
                using FeatureClass fc = geodatabase.OpenDataset<FeatureClass>(BaselineFCName);
                var fcPath = Path.Combine(gdbPath, BaselineFCName);
                var layers = MapView.Active?.Map?.Layers;
                var featureLayers = layers.Where(lyr => lyr is FeatureLayer);
                var featureClassLayer = featureLayers.FirstOrDefault(lyr =>
                    lyr?.GetPath() != null && lyr.GetPath().OriginalString.Equals(fcPath));
                if (featureClassLayer == null)
                {
                    MessageBox.Show("Unable to load " + fcPath + " to update points!");
                    return;
                }
                    
                var eo = new EditOperation();
                eo.Modify(featureClassLayer, oid, point);
                await eo.ExecuteAsync();
                await Project.Current.SaveEditsAsync();
            }
            catch (GeodatabaseException gdbe)
            {
                MessageBox.Show(gdbe.Message);
            }
        }
    }

    public static Tuple<long, MapPoint>? LoadBaselinePoint(long queryBaselineOID)
    {
        string gdbPath = GetGDBPath();
        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            try
            {
                using (FeatureClass fc = geodatabase.OpenDataset<FeatureClass>(BaselineFCName))
                {
                    QueryFilter queryFilter = new QueryFilter();
                        
                    using (var rowCursor = fc.Search(queryFilter))
                    {
                        while (rowCursor.MoveNext())
                        {
                            using (var row = rowCursor.Current)
                            {
                                if (row != null)
                                {
                                    long oid = row.GetObjectID();
                                    int baselineOIDIdx = row.FindField(BaselineOID);
                                    long val = (int) row.GetOriginalValue(baselineOIDIdx);

                                    if (val == queryBaselineOID)
                                    {
                                        var shapeFieldIndex = row.FindField("Shape");
                                            
                                        return new Tuple<long, MapPoint>(oid,
                                            (MapPoint)row.GetOriginalValue(shapeFieldIndex));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (GeodatabaseException gdbe)
            {
                MessageBox.Show("No saved baseline point, starting from beginning of feature class.");
            }
        }

        return null;
    }
        
    /// <summary>
    /// Loads a geometry based on a feature classes name and said geometry's OID
    /// </summary>
    /// <param name="fcName"></param>
    /// <param name="oid"></param>
    /// <returns></returns>
    public static Geometry? LoadBaselinePolyline(string fcName, long oid)
    {
        string gdbPath = GetGDBPath();
        using (Geodatabase geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
        {
            try
            {
                using (FeatureClass fc = geodatabase.OpenDataset<FeatureClass>(fcName))
                {
                    QueryFilter queryFilter = new QueryFilter();
                    using (var rowCursor = fc.Search(queryFilter))
                    {
                        while (rowCursor.MoveNext())
                        {
                            using (var row = rowCursor.Current)
                            {
                                if (row != null)
                                {
                                    var shapeFieldIdx = row.FindField("Shape");
                                    var lenFieldIdx = row.FindField("LZN");
                                    long rowOid = row.GetObjectID();

                                    if (rowOid == oid)
                                    {
                                        return row.GetOriginalValue(shapeFieldIdx) as Geometry;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (GeodatabaseException gdbe)
            {
                //TODO: add log
                int asdf = 0;
            }
        }

        return null;
    }
        
    public static async void SaveProfilesToGdb(List<Profile> profiles)
    {
        var highAdds = new List<FeatureClassEntry>();
        var lowAdds = new List<FeatureClassEntry>();
        var shoreAdds = new List<FeatureClassEntry>();
        var highUpdates = new Dictionary<int, FeatureClassEntry>();
        var lowUpdates = new Dictionary<int, FeatureClassEntry>();
        var shoreUpdates = new Dictionary<int, FeatureClassEntry>();
        var newProfiles = new List<Profile>();
            
        var transform = ProjectionTransformation.Create(
            MapView.Active.Extent.SpatialReference,
            SpatialReferences.WGS84);

        var newOrEdited = profiles.Where(p => !p.Saved || p.Edited).ToList();
        newOrEdited.ForEach(p =>
        {
            var sharedDoubleFields = new Dictionary<string, double>
            {
                {DistanceAlongBaseline, p.Vertex.DistAlongBaseline}
            };
                
            var sharedIntegerFields = new Dictionary<string, int>
            {
                {Profile, int.Parse(p.Id.Split('_')[1])},
                {Ignore,  p.Ignored ? 1 : 0},
                {BaselineOID, p.Vertex.BaselineOid},
                {BaselineSegmentIndex, p.Vertex.BaselineSegIdx}
            };
                
            // handle low points
            if (!p.IsBerm)
            {
                var lowMp = p.PointAsMapPoint(p.LowIdx);
                var lowReprojected = (MapPoint) GeometryEngine.Instance.ProjectEx(lowMp, transform);
                    
                var lowDoubleFields = new Dictionary<string, double>
                {
                    {DLowX, p.CalcShorelineDist(p.LowIdx)},
                    {DLowZ, lowMp.Z},
                    {Lon, lowReprojected.X},
                    {Lat, lowReprojected.Y},
                    {Easting, lowMp.X},
                    {Northing, lowMp.Y}
                };

                var lowIntegerFields = new Dictionary<string, int>(sharedIntegerFields);
                    
                sharedDoubleFields.ToList().ForEach(kvp => lowDoubleFields.Add(kvp.Key, kvp.Value));

                var lowEntry = new FeatureClassEntry
                {
                    DoubleFields = lowDoubleFields,
                    IntegerFields = lowIntegerFields,
                    Shape = lowMp
                };

                if (p.Saved)
                {
                    lowUpdates.Add(p.LowOid, lowEntry);
                }
                else
                {
                    lowAdds.Add(lowEntry);
                }
            }
                
            // handle high points
            var mp = p.PointAsMapPoint(p.HiIdx);
            var reproj = (MapPoint) GeometryEngine.Instance.ProjectEx(mp, transform);
                    
            var doubleFields = new Dictionary<string, double>
            {
                {DHighX, p.CalcShorelineDist(p.HiIdx)},
                {DHighZ, mp.Z},
                {Lon, reproj.X},
                {Lat, reproj.Y},
                {Easting, mp.X},
                {Northing, mp.Y}
            };

            var integerFields = new Dictionary<string, int>(sharedIntegerFields) {{Berm, p.IsBerm ? 1 : 0}};

            sharedDoubleFields.ToList().ForEach(kvp => doubleFields.Add(kvp.Key, kvp.Value));

            var fce = new FeatureClassEntry
            {
                DoubleFields = doubleFields,
                IntegerFields = integerFields,
                Shape = mp
            };

            if (p.Saved)
            {
                highUpdates.Add(p.HighOid, fce);
            }
            else
            {
                highAdds.Add(fce);
                newProfiles.Add(p);
            }
                
            // handle shore points
            mp = p.Points[p.GetVertexPointIndex()].ToMapPoint();
            reproj = (MapPoint) GeometryEngine.Instance.ProjectEx(mp, transform);
                
            var shorelineIntFields = new Dictionary<string, int>()
            {
                {Profile, int.Parse(p.Id.Split('_')[1])},
                {LandwardTransectLength, Module1.TransectLandwardLength},
                {SeawardTransectLength, Module1.TransectSeawardLength}
            };
                
            var shorelineDoubleFields = new Dictionary<string, double>
            {
                {MHWVal, mp.Z},
                {Lon, reproj.X},
                {Lat, reproj.Y},
                {Easting, mp.X},
                {Northing, mp.Y}
            };
                
            var shoreEntry = new FeatureClassEntry
            {
                DoubleFields = shorelineDoubleFields,
                IntegerFields = shorelineIntFields,
                Shape = mp
            };

            if (p.Saved)
            {
                shoreUpdates.Add(p.ShoreOid, shoreEntry);
            }
            else
            {
                shoreAdds.Add(shoreEntry);
            }
        });

        List<int> highOids;
        List<int> lowOids;
        List<int> shoreOids; // TODO: do we need this?
        using var geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(GetGDBPath())));
        try
        {
            using var fd = geodatabase.OpenDataset<FeatureDataset>(ProfilePointsDatasetName);
            using var highFc = fd.OpenDataset<FeatureClass>(HighPointsFCName);
                
            highOids = InsertFeatureClassRows(highFc, highAdds).Result;
            if (highUpdates.Count > 0)
            {
                UpdateFeatureClassRows(highFc, highUpdates);    
            }

            using var lowFc = fd.OpenDataset<FeatureClass>(LowPointsFCName);
            lowOids = InsertFeatureClassRows(lowFc, lowAdds).Result;
            if (lowUpdates.Count > 0)
            {
                UpdateFeatureClassRows(lowFc, lowUpdates);
            }
                
            using var shoreFc = fd.OpenDataset<FeatureClass>(ShorelinePointsFCName);
            shoreOids = InsertFeatureClassRows(shoreFc, shoreAdds).Result;
            if (shoreUpdates.Count > 0)
            {
                UpdateFeatureClassRows(shoreFc, shoreUpdates);
            }

            await Project.Current.SaveEditsAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        var bermCount = 0;
        for (int i = 0; i < highOids.Count; i++)
        {
            var p = newProfiles[i];
            p.HighOid = highOids[i];
            p.ShoreOid = shoreOids[i];
            if (p.IsBerm)
            {
                bermCount++;
            }
            else
            {
                p.LowOid = lowOids[i - bermCount];
            }
        }
            
        newOrEdited.ForEach(p => p.SetOriginalValues());
    }

    public static async Task<List<int>> InsertFeatureClassRows(FeatureClass fc, List<FeatureClassEntry> entries)
    {
        var oids = new List<int>();

        var eo = new EditOperation();
        eo.Callback(context =>
        {
            oids = entries.Select(entry =>
            {
                using var rb = fc.CreateRowBuffer();
                using var row = fc.CreateRow(rb);

                foreach (var kvp in entry.DoubleFields)
                {
                    row[kvp.Key] = kvp.Value;
                }

                foreach (var kvp in entry.IntegerFields)
                {
                    row[kvp.Key] = kvp.Value;
                }

                row.SetShape(entry.Shape);
                context.Invalidate(row);
                row.Store();

                return (int) row[OID];
            }).ToList();
        }, fc);
            
        await eo.ExecuteAsync();

        return oids;
    }

    public static async void UpdateFeatureClassRows(FeatureClass fc, Dictionary<int, FeatureClassEntry> entries)
    {
        var oids = entries.Keys.Select(Convert.ToInt64).ToList();

        if (oids.Count > 0)
        {
            var eo = new EditOperation();
    
            eo.Callback(context =>
            {
                var filter = new QueryFilter
                {
                    ObjectIDs = oids
                };
                using var rc = fc.Search(filter, false);

                while (rc.MoveNext())
                {
                    using var row = (Feature)rc.Current;
                    context.Invalidate(row);
                    var entry = entries[(int)row[OID]];

                    foreach (var kvp in entry.DoubleFields)
                    {
                        row[kvp.Key] = kvp.Value;
                    }

                    foreach (var kvp in entry.IntegerFields)
                    {
                        row[kvp.Key] = kvp.Value;
                    }

                    row.SetShape(entry.Shape);
                    row.Store();
                    context.Invalidate(row);
                }
            }, fc);
            
            await eo.ExecuteAsync();
            await Project.Current.SaveEditsAsync();
        }
    }
    
    public static Dictionary<string, ProfilePointGDBData> LoadExistingPoint(Geodatabase gdb, String fdName, 
        string fcName, SpatialQueryFilter sqf, HashSet<string> currentToolProfileIds)
    { 
        Dictionary<string, ProfilePointGDBData> ret = new Dictionary<string, ProfilePointGDBData>();
        
        try
        {
            using (FeatureDataset fd = gdb.OpenDataset<FeatureDataset>(fdName))
            using (FeatureClass fc = fd.OpenDataset<FeatureClass>(fcName))
            {
                using (var rowCursor = fc.Search(sqf))
                {
                    while (rowCursor.MoveNext())
                    {
                        using (var row = rowCursor.Current)
                        {
                            if (row != null)
                            {
                                var profileNameIndex = row.FindField(Profile);
                                string profileId = (string)row.GetOriginalValue(profileNameIndex);
     
                                if (!currentToolProfileIds.Contains(profileId))
                                {
                                    if (fcName.Equals(BuildGDBTableName(HighPointsFCName)))
                                    {
                                        ret.Add(profileId, new AnnotationPointGDBData(row));
                                    }
                                    else if (fcName.Equals(BuildGDBTableName(LowPointsFCName)))
                                    {
                                        ret.Add(profileId, new AnnotationPointGDBData(row));
                                    }
                                }
                                else
                                {
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (GeodatabaseException gdbe)
        {
                
        }
     
        return ret;
    } 
    
    static string BuildGDBTableName(string fcName)
    {
        return "C:\\Users\\nstuder\\Documents\\ArcGIS\\Projects\\experiment\\experiment.gdb" + "_" + fcName;
    }
}

public class AnnotationPointGDBData : ProfilePointGDBData
{
    private double lon_sm;
    private double lat_sm;
    private double easting_sm;
    private double northing_sm;
    private double dlow_x;
    private double dlow_z;
    private double lon_raw;
    private double lat_raw;
    private double easting_raw;
    private double northing_raw;
    private double xraw;
    private double zraw;
    private int action;
    private double err;
        
    public AnnotationPointGDBData(Row row) : base(row)
    {
        lon_sm = (double)row.GetOriginalValue(row.FindField("lon_sm"));
        lat_sm = (double)row.GetOriginalValue(row.FindField("lat_sm"));
        easting_sm = (double)row.GetOriginalValue(row.FindField("easting_sm"));
        northing_sm = (double)row.GetOriginalValue(row.FindField("northing_sm"));
        dlow_x = (double)row.GetOriginalValue(row.FindField("dlow_x"));
        dlow_z = (double)row.GetOriginalValue(row.FindField("dlow_z"));
        lon_raw = (double)row.GetOriginalValue(row.FindField("lon_raw"));
        lat_raw = (double)row.GetOriginalValue(row.FindField("lat_raw"));
        easting_raw = (double)row.GetOriginalValue(row.FindField("easting_raw"));
        northing_raw = (double)row.GetOriginalValue(row.FindField("northing_raw"));
        xraw = (double)row.GetOriginalValue(row.FindField("xraw"));
        zraw = (double)row.GetOriginalValue(row.FindField("zraw"));
        action = (int)row.GetOriginalValue(row.FindField("action"));
        err = (double)row.GetOriginalValue(row.FindField("err"));
    }

    public double LonSm => lon_sm;
    public double LatSm => lat_sm;
    public double EastingSm => easting_sm;
    public double NorthingSm => northing_sm;
    public double DlowX => dlow_x;
    public double DlowZ => dlow_z;
    public double LonRaw => lon_raw;
    public double LatRaw => lat_raw;
    public double EastingRaw => easting_raw;
    public double NorthingRaw => northing_raw;
    public double Xraw => xraw;
    public double Zraw => zraw;
    public int Action => action;
    public double Err => err;
}

public class ProfilePointGDBData
{
    private MapPoint mp;
    private int oid;

    public ProfilePointGDBData(Row row)
    {
        oid = (int)row.GetObjectID();
        int shapeFieldIndex = row.FindField("Shape");
        mp = MapPointBuilder.CreateMapPoint((MapPoint)row.GetOriginalValue(shapeFieldIndex),
            MapView.Active.Extent.SpatialReference);
    }

    public MapPoint Mp
    {
        get => mp;
        set => mp = value ?? throw new ArgumentNullException(nameof(value));
    }

    public int Oid
    {
        get => oid;
        set => oid = value;
    }
}
