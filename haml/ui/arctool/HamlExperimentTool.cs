/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.mltool;
using HamlProAppModule.haml.ui.experiment;
using HamlProAppModule.haml.ui.Experiment;
using HamlProAppModule.haml.util;
using Envelope = ArcGIS.Core.Geometry.Envelope;
using Feature = ArcGIS.Core.Data.Feature;
using Geometry = ArcGIS.Core.Geometry.Geometry;
using Multipoint = ArcGIS.Core.Geometry.Multipoint;
using Polyline = ArcGIS.Core.Geometry.Polyline;

namespace HamlProAppModule.haml.ui.arctool;

public class HamlExperimentTool : HamlFeatureClassMapTool
{
    protected Dictionary<Coordinate3D, Profile> candidatePoints;
    protected Dictionary<Coordinate3D, Profile> allProfiles;

    private Polyline _groundTruthFCHigh;
    private Polyline _groundTruthFCLow;
    private Polyline highFC;
    private Polyline lowFC;

    private int numManualInsertionsToFulfillFrechet = 0;

    private ProfilePolyline fineHamlTool;
    
    private static String reportDir = $"out\\{DateTime.Now.Year}-{DateTime.Now.Month}-{DateTime.Now.Day}" +
                       $"__{DateTime.Now.Hour}-{DateTime.Now.Minute}-{DateTime.Now.Second}";
    private static String rawDataDir = $"{reportDir}\\raw_data";
    private static String plotsDir = $"{reportDir}\\plots";

    protected HamlExperimentTool()
    {
        mhwChosen = true;
        _useFirstMHWPoint = false;

        QueuedTask.Run(()=>
        {
            highFC = GeodatabaseUtil.LoadBaselinePolyline("ground_high", 1) as Polyline;
            lowFC = GeodatabaseUtil.LoadBaselinePolyline("ground_low", 1) as Polyline;
            _groundTruthFCHigh = PolylineBuilderEx.CreatePolyline(highFC, ActiveMapView.Extent.SpatialReference);
            _groundTruthFCLow = PolylineBuilderEx.CreatePolyline(lowFC, ActiveMapView.Extent.SpatialReference);
        });
    }

    protected override void InitHamlTool(Envelope mapViewExtent, Geometry? arcGeometry = null)
    {
        if (arcGeometry == null) return;
        
        Module1.ToggleState(PluginState.DoingOperationState, true);
        Module1.ToggleState(PluginState.ResetGeometryState, true);
        
        var matchingPoints = GetMatchingPoints();

        List<List<Coordinate3D>> baselineExtentIntersections = FindOrthogonalIntersections(fcPoly,
            fcPoly.Points.First(), fcPoly.Length, out var dists, out var segIndices);

        _initDists = dists;
        _initSegIndices = segIndices;

        Polyline candShoreline = BuildMHWShoreline(baselineExtentIntersections);
        
        HamlTool = new ProfilePolyline(_raster, _learner, candShoreline, mapViewExtent);
        HamlTool.LabelSide = LeftOrRightSide.LeftSide; // TODO: make more flexible if needed, currently hardcoded
        HamlTool.LabelDirSet = true;
        
        candidatePoints = GetProfilesFromMatchingPairs(matchingPoints);
        allProfiles = candidatePoints;
        
        if (!InitializeFirstAndLastProfiles())
        {
            Module1.ToggleState(PluginState.DoingOperationState, false);
            Module1.ToggleState(PluginState.ResetGeometryState, false);
            return;
        }

        HamlTool.GroundTruthHigh =
            CutGroundTruthFC(_groundTruthFCHigh, HamlTool.GetAllProfiles()[0], HamlTool.GetAllProfiles()[^1]);
        HamlTool.GroundTruthLow =
            CutGroundTruthFC(_groundTruthFCLow, HamlTool.GetAllProfiles()[0], HamlTool.GetAllProfiles()[^1]);

        correctInitialProfiles();

        InitBaseOverlays();
        UpdateVisibleProfiles();
        HamlTool.ActiveProfile = HamlTool.GetValidProfiles().First(p => !p.Saved);

        Module1.ToggleState(PluginState.ContourAvailableState, true);
        Module1.ToggleState(PluginState.InitializedState, true);
        Module1.ToggleState(PluginState.ExperimentReadyState, false);

        Module1.ToggleState(PluginState.DoingOperationState, false);
    }

    Dictionary<Coordinate3D, Profile> GetProfilesFromMatchingPairs(List<List<MapPoint>> matchingPairs)
    {
        Dictionary<Coordinate3D, Profile> ret = new Dictionary<Coordinate3D, Profile>();

        foreach (List<MapPoint> pair in matchingPairs)
        {
            MapPoint highPt = pair.First();
            MapPoint lowPt = pair.Last();
            double xDiff = lowPt.X - highPt.X;
            double yDiff = lowPt.Y - highPt.Y;
            double angle = Math.Atan2(yDiff, xDiff);
            
            List<Coordinate3D> searchTransect = BuildFixedTransectCoords(lowPt, angle+Math.PI, 0, 150);
            Polyline searchPolyline = PolylineBuilderEx.CreatePolyline(searchTransect);
            Multipoint mhwMultipoint = GeometryEngine.Instance.Intersection(searchPolyline, 
                HamlTool.GetContourAsArcGeometry() as Polyline,
                GeometryDimensionType.EsriGeometry0Dimension) as Multipoint;
            if (mhwMultipoint.PointCount < 1){
                continue;
            }
            Coordinate3D mhwPoint = mhwMultipoint.Points.First().Coordinate3D;
            MapPoint mhwPointMp = MapPointBuilderEx.CreateMapPoint(mhwPoint, MapView.Active.Extent.SpatialReference);
            Vertex vertex = new Vertex(mhwPointMp);

            List<Coordinate3D> profileTransect = BuildFixedTransectCoords(mhwPointMp, angle,
                Module1.TransectLandwardLength, Module1.TransectSeawardLength);
            Profile profile = QueuedTask.Run(() =>
            {
                Profile profile = new Profile(PolylineBuilderEx.CreatePolyline(profileTransect),
                    _raster,
                    vertex,
                    HamlTool.LabelSide,
                    profileTransect);

                return profile;
            }).Result;

            profile.groundHighIdx = profile.GetNearestProfilePointIndex(highPt).Value;
            profile.groundLowIdx = profile.GetNearestProfilePointIndex(lowPt).Value;
            
            ret.Add(mhwPoint, profile);
        }
        
        return ret;
    }

    List<List<MapPoint>> GetMatchingPoints()
    {
        List<List<MapPoint>> ret = new List<List<MapPoint>>();
        
        Map map = MapView.Active.Map;
        FeatureLayer highFl = map.FindLayers("201810co_NC_1m_dhigh").FirstOrDefault() as FeatureLayer;
        FeatureClass highFc = highFl.GetFeatureClass();
        FeatureLayer lowFl = map.FindLayers("201810co_NC_1m_dlow").FirstOrDefault() as FeatureLayer;
        FeatureClass lowFc = lowFl.GetFeatureClass();
        
        QueryFilter qf = new SpatialQueryFilter()
        {
            FilterGeometry = MapView.Active.Extent,
            SpatialRelationship = SpatialRelationship.Intersects,
        };
        
        using (RowCursor cursor = highFc.Search(qf, false))
        {
            while (cursor.MoveNext())
            {
                MapPoint highGeometry = null;
                MapPoint lowGeometry = null;
                
                bool lowFound = false;
                using (Feature feature = (Feature)cursor.Current)
                {
                    highGeometry = feature.GetShape() as MapPoint;

                    var fIDidx = feature.FindField("FID");
                    var fID = feature.GetOriginalValue(fIDidx);

                    var profileIdx = feature.FindField("profile");
                    var profile = feature.GetOriginalValue(profileIdx);

                    using (RowCursor lowCursor = lowFc.Search(qf, false))
                    {
                        while (lowCursor.MoveNext() && !lowFound)
                        {
                            using (Feature lowFeature = (Feature)lowCursor.Current)
                            {
                                var lowFID = lowFeature.GetOriginalValue(fIDidx);
                                int fIDint = int.Parse(fID.ToString());
                                int lowFIDint = int.Parse(lowFID.ToString());

                                var lowProfile = lowFeature.GetOriginalValue(profileIdx);
                                int profileInt = int.Parse(profile.ToString());
                                int lowProfileInt = int.Parse(lowProfile.ToString());
                                
                                bool intEquality = Math.Abs(fIDint - lowFIDint) < 1 || 
                                                   Math.Abs(profileInt*10 - lowProfileInt) < 1;
                                
                                if (intEquality)
                                {
                                    lowGeometry = lowFeature.GetShape() as MapPoint;
                                    
                                    if (lowGeometry != null && highGeometry != null)
                                    {
                                        List<MapPoint> pair = new List<MapPoint>();
                                        pair.Add(highGeometry);
                                        pair.Add(lowGeometry);
                    
                                        ret.Add(pair);
                                    }
                                    
                                    lowFound = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        return ret;
    }

    public bool SmartInsert(bool makeCorrection = false)
    {
        Module1.StatTracker.SaveCurrentSnapshot();
        
        HamlTool.TrainAllUntrainedProfiles();

        // SmartInsert will select best candidate, insert into the tool and remove it from the list of candidates
        var insertedProfile = HamlTool.SmartInsert(candidatePoints, Module1.smartInsertionTolerance);
        
        if (insertedProfile is null) // happens when HamlTool.SmartInsert can find no suitable candidates for insertion
        {
            GUI.ShowToast("Could not find a suitable insertion point");
            return false;
        }

        UpdateVisibleProfiles();

        allProfiles[insertedProfile.Vertex.GetPoint().Coordinate3D] = insertedProfile;
        candidatePoints.Remove(insertedProfile.Vertex.GetPoint().Coordinate3D);
        foreach (Profile p in candidatePoints.Values)
        {
            p.RemoveAnnotation(Profile.High);
            p.RemoveAnnotation(Profile.Low);
        }
        
        if (makeCorrection)
        {
            HamlTool.CorrectProfilePlacements(insertedProfile, CorrectionTypeNeeded(insertedProfile));
        }
        
        UpdateVisibleProfiles();
        
        var highDist = GetDistances(AnnotationType.HIGH).Max();
        var lowDist = GetDistances(AnnotationType.LOW).Max();
        Module1.StatTracker.UpdateStat(ExperimentStat.GroundTruthFrechetDistanceHigh, highDist);
        Module1.StatTracker.UpdateStat(ExperimentStat.GroundTruthFrechetDistanceLow, lowDist);
        
        ExperimentDockPaneViewModel.Show();

        // return false to stop the calling loop when both frechet criteria are met
        // this is how we terminate the experiment when the machine incorrectly wants to continue
        return highDist > Module1.smartInsertionTolerance || lowDist > Module1.smartInsertionTolerance;
    }

    private bool InsertAtLargestDistance()
    {
        var largestDist = double.MinValue;
        var largestKvp = candidatePoints.First();
        

        foreach (var kvp in candidatePoints)
        {
            var p = kvp.Value;
            var highDist = Double.MinValue;
            var lowDist = Double.MinValue;

            var highMultipoint = GeometryEngine.Instance.Intersection(p.ArcTransectPolyline,
                HamlTool.GetHighPointsAsPolyline(),
                GeometryDimensionType.EsriGeometry0Dimension) as Multipoint;
            
            if(highMultipoint.PointCount > 0){
                var highIntersect = highMultipoint.Points.First();
                highDist = GeometryEngine.Instance.Distance(highIntersect, p.Points[p.groundHighIdx].ToMapPoint());
            }

            var lowMultipoint = GeometryEngine.Instance.Intersection(p.ArcTransectPolyline,
                HamlTool.GetLowPointsAsPolyline(),
                GeometryDimensionType.EsriGeometry0Dimension) as Multipoint;
            
            if(lowMultipoint.PointCount > 0){
                var lowIntersect = 
                    lowMultipoint.Points.First();
                lowDist = GeometryEngine.Instance.Distance(lowIntersect, p.Points[p.groundLowIdx].ToMapPoint());
            }
            

            if (Math.Max(highDist, lowDist) > largestDist)
            {
                largestDist = Math.Max(highDist, lowDist);
                largestKvp = new KeyValuePair<Coordinate3D, Profile>(kvp.Key, kvp.Value);
            }
        }

        var largestDistProfile = largestKvp.Value;
        
        if(largestDist > Module1.highTolerance){
            HamlTool.GuessProfilePlacements(largestDistProfile);
            HamlTool.InsertProfile(largestDistProfile);
            candidatePoints.Remove(largestKvp.Key);

            HamlTool.CorrectProfilePlacements(largestDistProfile, CorrectionTypeNeeded(largestDistProfile));

            var highDist = GetDistances(AnnotationType.HIGH).Max();
            var lowDist = GetDistances(AnnotationType.LOW).Max();
            Module1.StatTracker.UpdateStat(ExperimentStat.GroundTruthFrechetDistanceHigh,
                highDist);
            Module1.StatTracker.UpdateStat(ExperimentStat.GroundTruthFrechetDistanceLow,
                lowDist);

            return true;
        }

        return false;
    }

    public void PerformExperiment()
    {
        // perform machine placement until machine thinks it has met the tolerance criteria
        while(SmartInsert(true))
        {
        }

        Module1.StatTracker.SaveCurrentSnapshot();
        
        // check if the machine fulfilled its duties. if not, let the human pick the remaining insertions
        while (!FrechetCriteriaMet())
        {
            // sometimes we cant find a point to insert at even if the frechet distance is larger than tolerance,
            // due to our profile spacing restriction.
            // todo: shall we disregard the profile spacing requirement for this?
            if (InsertAtLargestDistance())
            {
                numManualInsertionsToFulfillFrechet++;
                Module1.StatTracker.SaveCurrentSnapshot();
            }
            else
            {
                break;
            }
        }
        
        CreateExperimentReport();
        GUI.ShowToast("Experiment complete");
    }

    private void SaveRawData(String dir)
    {
        SaveMeasurements();
        SaveCurves();

        void SaveMeasurements()
        {
            var output = new List<String>();
            var statTracker = Module1.StatTracker;
        
            var frechetHigh = statTracker.GetHistory(ExperimentStat.GroundTruthFrechetDistanceHigh)
                .Select(obj=>(double) obj).ToList();
            var frechetLow = statTracker.GetHistory(ExperimentStat.GroundTruthFrechetDistanceLow)
                .Select(obj=>(double) obj).ToList();

            var corrDistHigh = statTracker.GetHistory(ExperimentStat.HighPtCorrectionDistance)
                .Select(obj=>(double) obj).ToList();
            var corrDistLow = statTracker.GetHistory(ExperimentStat.LowPtCorrectionDistance)
                .Select(obj=>(double) obj).ToList();

            var meanCorrDistHigh = statTracker.GetHistory(ExperimentStat.MeanHighPtCorrectionDistance)
                .Select(obj=>(double) obj).ToList();
            var meanCorrDistLow = statTracker.GetHistory(ExperimentStat.MeanLowPtCorrectionDistance)
                .Select(obj=>(double) obj).ToList();

            output.Add("profile_added,frechet_high,frechet_low,corr_dist_high,corr_dist_low,mean_corr_dist_high,mean_corr_dist_low");
            for (int i = 0; i < statTracker.History.Count; i++)
            {
                output.Add($"{i},{frechetHigh[i]},{frechetLow[i]},{corrDistHigh[i]},{corrDistLow[i]},{meanCorrDistHigh[i]},{meanCorrDistLow[i]}");
            }
            
            File.WriteAllLines($"{dir}\\measurements.txt", output);
        }

        void SaveCurves()
        {
            var output = new List<String>();
            
            output = getCurveData(HamlTool.GroundTruthHigh);
            File.WriteAllLines($"{dir}\\ground_high.txt", output);
            
            output = getCurveData(HamlTool.GetHighPointsAsPolyline());
            File.WriteAllLines($"{dir}\\actual_high.txt", output);
            
            output = getCurveData(HamlTool.GroundTruthLow);
            File.WriteAllLines($"{dir}\\ground_low.txt", output);
            
            output = getCurveData(HamlTool.GetLowPointsAsPolyline());
            File.WriteAllLines($"{dir}\\actual_low.txt", output);
        }

        List<String> getCurveData(Polyline polyline)
        {
            var ret = new List<String>();

            var distinctPointList = polyline.Points.Distinct().ToList();
            for (int i = 0; i <distinctPointList.Count; i++)
            {
                var zVal = RasterUtil.GetZAtCoordinate(_raster,
                    new Coordinate2D(distinctPointList[i].X, distinctPointList[i].Y));
                ret.Add($"{distinctPointList[i].X},{distinctPointList[i].Y},{zVal}");
            };

            return ret;
        }
    }

    private void CreateExperimentReport()
    {
        Directory.CreateDirectory(reportDir);
        Directory.CreateDirectory(rawDataDir);
        Directory.CreateDirectory(plotsDir);
        
        SaveRawData(rawDataDir);
        SavePolylineComparisonPlots(plotsDir);
        SaveHeightProfilePlots(plotsDir);
        SaveHeightErrorPlots(plotsDir);
        ExperimentDockPaneViewModel.CompleteExperiment(plotsDir);
        
        var firstProfile = HamlTool.GetAllProfiles().First();

        List<String> heightProfileOutput = new List<string>();
        foreach (var point in firstProfile.Points)
        {
            heightProfileOutput.Add(point.Z.ToString());
        }
        File.WriteAllLines($"{rawDataDir}\\raw_height_first_profile.txt",heightProfileOutput);
        
        List<String> output = new List<string>();
        var tracker = Module1.StatTracker;

        output.Add("INFO");
        output.Add($"\tPlacement tolerance (m): {Module1.smartInsertionTolerance}");
        output.Add($"\tHigh correction tolerance (m): {Module1.highTolerance}");
        output.Add($"\tLow correction tolerance (m): {Module1.lowTolerance}");
        output.Add($"\tMHW value (m): {Module1.MeanHighWaterVal}");
        output.Add($"\tLandward transect length (m): {Module1.TransectLandwardLength}");
        output.Add($"\tSeaward transect length (m): {Module1.TransectSeawardLength}");

        output.Add("CORRECTIONS");
        output.Add("\tHigh Points");
        output.Add($"\t\tNum corrections: " +
                   $"{tracker.GetHistory(ExperimentStat.HighPtCorrectionDistance).Count(x => (double) x>0)}");
        double avgHighCorrDist = 0;
        if (tracker.GetHistory(ExperimentStat.HighPtCorrectionDistance).Any(x => (double)x != 0))
        {
            avgHighCorrDist =
                Math.Round(
                    tracker.GetHistory(ExperimentStat.HighPtCorrectionDistance).Where(x => (double)x > 0)
                        .Average(x => (double)x), 2);
        }
        output.Add($"\t\tAvg correction distance (m): " +
                   $"{avgHighCorrDist}");
        output.Add($"\t\tLargest correction distance (m): " +
                   $"{tracker.GetHistory(ExperimentStat.HighPtCorrectionDistance).Max()}"); 
        output.Add("\tLow Points");
        output.Add($"\t\tNum corrections: " +
                   $"{tracker.GetHistory(ExperimentStat.LowPtCorrectionDistance).Count(x => (double) x>0)}");
        double avgLowCorrDist = 0;
        if (tracker.GetHistory(ExperimentStat.LowPtCorrectionDistance).Any(x => (double)x != 0))
        {
            avgLowCorrDist =
                Math.Round(
                    tracker.GetHistory(ExperimentStat.LowPtCorrectionDistance).Where(x => (double)x > 0)
                        .Average(x => (double)x), 2);
        }
        output.Add($"\t\tAvg correction distance (m): " +
                   $"{avgLowCorrDist}");
        output.Add($"\t\tLargest correction distance (m): " +
                   $"{tracker.GetHistory(ExperimentStat.LowPtCorrectionDistance).Max()}");
        
        output.Add("CURVE COMPARISON");
        output.Add("\tHigh Points");
        output.Add($"\t\tNum vertices in ground truth: {HamlTool.GroundTruthHigh.PointCount}");
        output.Add($"\t\tNum vertices in actual: {HamlTool.GetHighPointsAsMultipoint().PointCount}");
        output.Add("\tLow Points");
        output.Add($"\t\tNum vertices in ground truth: {HamlTool.GroundTruthLow.PointCount}");
        output.Add($"\t\tNum vertices in actual: {HamlTool.GetLowPointsAsMultipoint().PointCount}");
        
        output.Add("ERRORS");
        output.Add("\tHigh Points");
        output.Add($"\t\tMaximum distance between actual and ground (m): " +
                   $"{Math.Round(GetDistances(AnnotationType.HIGH).Max(),2)}");
        output.Add($"\t\tAvg distance between actual and ground (m): " + 
                   $"{Math.Round(GetDistances(AnnotationType.HIGH).Average(),2)}");
        var highZErrors = GetZValErrors(AnnotationType.HIGH);
        output.Add($"\t\tAvg z-val error (m): {Math.Round(highZErrors.Average(),2)}" +
                   $"±{Math.Round(StdDev(highZErrors),3)}");
        output.Add($"\t\tLargest z-val error (m): {Math.Round(highZErrors.Max(),2)}");
        output.Add("\tLow Points");
        output.Add($"\t\tMaximum distance between actual and ground (m): " +
                   $"{Math.Round(GetDistances(AnnotationType.LOW).Max(),2)}");
        output.Add($"\t\tAvg distance between actual and ground (m): " + 
                   $"{Math.Round(GetDistances(AnnotationType.LOW).Average(),2)}");
        var lowZErrors = GetZValErrors(AnnotationType.LOW);
        output.Add($"\t\tAvg z-val error (m): {Math.Round(lowZErrors.Average(),2)}" +
                   $"±{Math.Round(StdDev(lowZErrors),3)}");
        output.Add($"\t\tLargest z-val error (m): {Math.Round(lowZErrors.Max(),2)}");

        output.Add("OVERALL");
        output.Add($"\tNum of profiles needed to reach Distance threshold: " +
                   $"{GetNumOfProfilesToSatisfyFrechetCriteria()}");
        output.Add($"\tNum of manual insertions needed to meet Distance threshold: " +
                   $"{numManualInsertionsToFulfillFrechet}");
        output.Add($"\t% reduction in vertices verified compared to ground: {GetPercentVerticesVerifiedReduction()}%");
        output.Add($"\t% reduction in vertices corrected compared to ground: {GetPercentVerticesCorrectedReduction()}%");


        File.WriteAllLines($"{reportDir}\\summary.txt", output);
    }
    
    private bool InitializeFirstAndLastProfiles()
    {
        var firstProfile = candidatePoints.First().Value;
        HamlTool.SetDuneAlgoPlacements(firstProfile);
        HamlTool.InsertProfile(0, firstProfile);

        var lastVertexIndex = candidatePoints.Count - 1;
        var lastProfile = candidatePoints.Last().Value;
        HamlTool.SetDuneAlgoPlacements(lastProfile);
        HamlTool.InsertProfile(lastVertexIndex, lastProfile);

        if (HamlTool.VertexProfileDict.Count < 2)
        {
            return false;
        }
        
        return true;
    }

    private void correctInitialProfiles()
    {
        var firstProfile = HamlTool.GetAllProfiles().First();
        var lastProfile = HamlTool.GetAllProfiles().Last();
        
        firstProfile.AddOrUpdateAnnotationPoint(Profile.High, firstProfile.groundHighIdx);
        lastProfile.AddOrUpdateAnnotationPoint(Profile.High, lastProfile.groundHighIdx);
        firstProfile.AddOrUpdateAnnotationPoint(Profile.Low, firstProfile.groundLowIdx);
        lastProfile.AddOrUpdateAnnotationPoint(Profile.Low, lastProfile.groundLowIdx);

        allProfiles[firstProfile.Vertex.GetPoint().Coordinate3D] = firstProfile;
        allProfiles[lastProfile.Vertex.GetPoint().Coordinate3D] = lastProfile;
        candidatePoints.Remove(firstProfile.Vertex.GetPoint().Coordinate3D);
        candidatePoints.Remove(lastProfile.Vertex.GetPoint().Coordinate3D);
        
        var highDist = GetDistances(AnnotationType.HIGH).Max();
        var lowDist = GetDistances(AnnotationType.LOW).Max();
        Module1.StatTracker.UpdateStat(ExperimentStat.GroundTruthFrechetDistanceHigh, highDist);
        Module1.StatTracker.UpdateStat(ExperimentStat.GroundTruthFrechetDistanceLow, lowDist);
    }

    protected override void InitBaseOverlays()
    {
        base.InitBaseOverlays();
        
        OverlayController.AddOverlay(HamlTool.GetHighPointsAsPolyline(),
            Module1.CreateHamlPolylineSymbol(CIMColor.CreateRGBColor(65,105,225)).MakeSymbolReference(), HamlGraphicType.HighContour);
        
        OverlayController.AddOverlay(HamlTool.GetLowPointsAsPolyline(),
            Module1.CreateHamlPolylineSymbol(CIMColor.CreateRGBColor(255,223,0)).MakeSymbolReference(), HamlGraphicType.LowContour);
    }
    
    protected override void OnToolKeyDown(MapViewKeyEventArgs k)
    {
        switch (k.Key)
        {
            case Key.K:
                if(HamlTool != null){
                    SmartInsert();
                }
                
                break;
        }
    }
    
    private Polyline CutGroundTruthFC(Polyline groundTruthFC, Profile leftProfile, Profile rightProfile)
    {
        var firstCut = GeometryEngine.Instance.Cut(groundTruthFC, rightProfile.ArcTransectPolyline);
        var rightRemoved = firstCut.ToList()[^1] as Polyline;
        var secondCut = GeometryEngine.Instance.Cut(rightRemoved, leftProfile.ArcTransectPolyline);
        var leftRemoved = secondCut.ToList()[0] as Polyline;

        return leftRemoved;
    }

    private void SavePolylineComparisonPlots(String dir)
    {
        List<(Polyline, string, bool)> bundleList = new List<(Polyline, string, bool)>();
        
        bundleList.Add((HamlTool.GroundTruthHigh, "crest_groundTruth", false));
        bundleList.Add((HamlTool.GetHighPointsAsPolyline(), "crest_actual", true));

        bundleList.Add((HamlTool.GroundTruthLow, "toe_groundTruth", false));
        bundleList.Add((HamlTool.GetLowPointsAsPolyline(), "toe_actual", true));

        ExperimentDockPaneViewModel.CreateAnnotationCurvePlots(bundleList, 
            dir,
            $"Digitization Comparison");
    }


    private void SaveHeightProfilePlots(String dir)
    {
        List<(List<(double,double)>, string, bool)> bundleList = new List<(List<(double,double)>, string, bool)>();
        
        bundleList.Add((GetZValData(HamlTool.GroundTruthHigh), "crest_groundTruth", false));
        bundleList.Add((GetZValData(HamlTool.GetHighPointsAsPolyline()), "crest_actual", true));

        bundleList.Add((GetZValData(HamlTool.GroundTruthLow), "toe_groundTruth", false));
        bundleList.Add((GetZValData(HamlTool.GetLowPointsAsPolyline()), "toe_actual", true));

        ExperimentDockPaneViewModel.CreateHeightProfilePlots(bundleList,
            dir,
            $"Height Profiles");
    }

    private void SaveHeightErrorPlots(String dir)
    {
        List<(List<(double,double)>, string, bool)> bundleList = new List<(List<(double,double)>, string, bool)>();

        var groundHighVals = GetZValData(HamlTool.GroundTruthHigh);
        var actualHighVals = GetZValData(HamlTool.GetHighPointsAsPolyline());
        var diffHigh = new List<(double,double)>();
        var diffHighString = new List<String>();
        // account for weird circumstances where they have different number of elements
        foreach (var groundTup in groundHighVals)
        {
            if (actualHighVals.Any(tup => Math.Abs(tup.Item1 - groundTup.Item1) < 0.1))
            {
                var tup = actualHighVals.First(tup => Math.Abs(tup.Item1 - groundTup.Item1) < 0.1);
                var x = groundTup.Item1;
                var y = Math.Round(Math.Abs(tup.Item2 - groundTup.Item2),2);
                
                diffHigh.Add((x, y));
                diffHighString.Add($"{x},{y}");
            }
        }

        var groundLowVals = GetZValData(HamlTool.GroundTruthLow);
        var actualLowVals = GetZValData(HamlTool.GetLowPointsAsPolyline());
        var diffLow = new List<(double,double)>();
        var diffLowString = new List<String>();
        foreach (var groundTup in groundLowVals)
        {
            if (actualLowVals.Any(tup => Math.Abs(tup.Item1 - groundTup.Item1) < 0.1))
            {
                var tup = actualLowVals.First(tup => Math.Abs(tup.Item1 - groundTup.Item1) < 0.1);
                var x = groundTup.Item1;
                var y = Math.Round(Math.Abs(tup.Item2 - groundTup.Item2),2);
                
                diffLow.Add((x, y));
                diffLowString.Add($"{x},{y}");
            }
        }
        
        File.WriteAllLines($"{rawDataDir}\\height_errors_crest.txt", diffHighString);
        File.WriteAllLines($"{rawDataDir}\\height_errors_toe.txt", diffLowString);

        bundleList.Add((diffHigh, "crest", true));
        bundleList.Add((diffLow, "toe", false));
        
        ExperimentDockPaneViewModel.CreateHeightErrorPlot(bundleList,
            dir,
            "Height Errors");
    }

    private AnnotationType CorrectionTypeNeeded(Profile profile)
    {
        bool highNeedsCorrection =
            GeometryEngine.Instance.Distance(profile.Points[profile.HiIdx].ToMapPoint(),
                profile.Points[profile.groundHighIdx].ToMapPoint()) >= Module1.highTolerance;
        
        bool lowNeedsCorrection = 
            GeometryEngine.Instance.Distance(profile.Points[profile.LowIdx].ToMapPoint(),
            profile.Points[profile.groundLowIdx].ToMapPoint()) >= Module1.lowTolerance;

        if (highNeedsCorrection && lowNeedsCorrection)
        {
            return AnnotationType.BOTH;
        }
        
        if (highNeedsCorrection)
        {
            return AnnotationType.HIGH;
        }

        if (lowNeedsCorrection)
        {
            return AnnotationType.LOW;
        }
        
        return AnnotationType.NEITHER;
    }

    private bool FrechetCriteriaMet()
    {
        var highDist = GetDistances(AnnotationType.HIGH).Max();
        var lowDist = GetDistances(AnnotationType.LOW).Max();

        bool criteriaMet = highDist <= Module1.highTolerance && lowDist <= Module1.lowTolerance;
        return criteriaMet;
    }

    private int GetNumOfProfilesToSatisfyFrechetCriteria()
    {
        var highFrechets = Module1.StatTracker.GetHistory(ExperimentStat.GroundTruthFrechetDistanceHigh);
        var lowFrechets = Module1.StatTracker.GetHistory(ExperimentStat.GroundTruthFrechetDistanceLow);

        for (int i = 1; i < highFrechets.Count; i++)
        {
            if ((double) highFrechets[i] < Module1.smartInsertionTolerance && (double) lowFrechets[i] < Module1.smartInsertionTolerance)
            {
                // we start with two profiles, so add 2
                return i + 1;
            }
        }
        
        var highDist = GetDistances(AnnotationType.HIGH).Max();
        var lowDist = GetDistances(AnnotationType.LOW).Max();
        
        if (highDist < Module1.smartInsertionTolerance && lowDist < Module1.smartInsertionTolerance)
        {
            // we start with two profiles, so add 2
            return HamlTool.VertexProfileDict.Count;
        }
        
        return -1;
    }

    private double GetPercentVerticesVerifiedReduction()
    {
        double numVerticesVerifiedGround = HamlTool.GroundTruthHigh.PointCount + HamlTool.GroundTruthLow.PointCount;
        double numVerticesVerifiedActual = HamlTool.GetHighPointsAsMultipoint().PointCount +
                                                HamlTool.GetLowPointsAsMultipoint().PointCount;

        return -100*Math.Round((1 - numVerticesVerifiedActual / numVerticesVerifiedGround),2);
    }

    private double GetPercentVerticesCorrectedReduction()
    {
        // should we be assuming every vertex needed correction in the ground truth?
        double numVerticesCorrectedGround = HamlTool.GroundTruthHigh.PointCount + HamlTool.GroundTruthLow.PointCount;
        double numVerticesCorrectedActual =
            Module1.StatTracker.GetHistory(ExperimentStat.HighPtCorrectionDistance).Count(x => (double)x > 0)
            + Module1.StatTracker.GetHistory(ExperimentStat.LowPtCorrectionDistance).Count(x => (double)x > 0);

        return -Math.Round(100*(1 - numVerticesCorrectedActual / numVerticesCorrectedGround),2);
    }
    
    private List<double> GetDistances(AnnotationType annotationType)
    {
        Polyline annotationPolyline = null;
        int annotationIdx = -1;
        var ret = new List<double>();
        
        if (annotationType == AnnotationType.HIGH)
        {
            annotationPolyline = HamlTool.GetHighPointsAsPolyline();
        } else if (annotationType == AnnotationType.LOW)
        {
            annotationPolyline = HamlTool.GetLowPointsAsPolyline();
        }
        
        foreach (Profile p in allProfiles.Values.ToList())
        {
            MapPoint intersectionPoint = null;
            double dist = Double.NaN;
            
            var intersectionMultipoint = GeometryEngine.Instance.Intersection(p.ArcTransectPolyline,
                annotationPolyline,
                GeometryDimensionType.EsriGeometry0Dimension) as Multipoint;

            if (annotationType == AnnotationType.HIGH)
            {
                annotationIdx = p.groundHighIdx;
            } else if (annotationType == AnnotationType.LOW)
            {
                annotationIdx = p.groundLowIdx;
            }
            
            var loc = p.Points[annotationIdx].ToMapPoint();
            
            if(intersectionMultipoint.PointCount > 0){
                intersectionPoint = intersectionMultipoint.Points.First();
                dist = GeometryEngine.Instance.Distance(intersectionPoint, loc);
            }
            
            ret.Add(dist);
        }
        
        return ret;
    }

    private List<double> GetZValErrors(AnnotationType annotationType)
    {
        if (annotationType is AnnotationType.BOTH or AnnotationType.NEITHER)
        {
            return new List<double>();
        }
        
        List<double> zValErrors = new List<double>();

        int groundIdx = 0;
        int actualIdx = 0;
        foreach (var profile in allProfiles.Values)
        {
            if (annotationType == AnnotationType.HIGH)
            {
                groundIdx = profile.groundHighIdx;
                
                var intersection =  GeometryEngine.Instance.Intersection(HamlTool.GetHighPointsAsPolyline(), profile.ArcTransectPolyline,
                    GeometryDimensionType.EsriGeometry0Dimension) as Multipoint;
                if (intersection.IsEmpty)
                {
                    continue;
                }
                actualIdx = profile.GetNearestProfilePointIndex(GeometryEngine.Instance.NearestPoint(profile.ArcTransectPolyline, intersection.Points.First()).Point).Value;
            }
            else
            {
                groundIdx = profile.groundLowIdx;
                
                var intersection =  GeometryEngine.Instance.Intersection(HamlTool.GetLowPointsAsPolyline(), profile.ArcTransectPolyline,
                    GeometryDimensionType.EsriGeometry0Dimension) as Multipoint;
                if (intersection.IsEmpty)
                {
                    continue;
                }
                actualIdx = profile.GetNearestProfilePointIndex(GeometryEngine.Instance.NearestPoint(profile.ArcTransectPolyline, intersection.Points.First()).Point).Value;
            }
            
            double groundHeight = RasterUtil.GetZAtCoordinate(_raster, profile.Points[groundIdx].ToMapPoint().Coordinate2D);
            double actualHeight = RasterUtil.GetZAtCoordinate(_raster, profile.Points[actualIdx].ToMapPoint().Coordinate2D);
            
            zValErrors.Add(Math.Abs(groundHeight - actualHeight));
        }

        return zValErrors;
    }
    
    // re-samples the given polyline based on intersections from the fine grained profile insertion points, giving back
    // the z values for each of those sample points, along with how far along the baseline that point is
    private List<(double,double)> GetZValData(Polyline polyline)
    {
        List<(double,double)> zVals = new List<(double,double)>();
        
        int actualIdx = 0;
        for (int i = 0; i < allProfiles.Values.Count; i++)
        {
            var profile = allProfiles.Values.ToList()[i];
            var intersection =  GeometryEngine.Instance.Intersection(polyline, profile.ArcTransectPolyline,
                GeometryDimensionType.EsriGeometry0Dimension) as Multipoint;
            
            if(!intersection.IsEmpty){
                zVals.Add((i*2,RasterUtil.GetZAtCoordinate(_raster, intersection.Points.First().Coordinate2D)));
            }
        }

        return zVals;
    }
    
    public static double StdDev(IEnumerable<double> values)
    {
        // ref: http://warrenseen.com/blog/2006/03/13/how-to-calculate-standard-deviation/
        double mean = 0.0;
        double sum = 0.0;
        double stdDev = 0.0;
        int n = 0;
        foreach (double val in values)
        {
            n++;
            double delta = val - mean;
            mean += delta / n;
            sum += delta * (val - mean);
        }
        if (0 < n)
            stdDev = Math.Sqrt(sum / n); // population stddev divides by n. sample stddev divides by n-1

        return stdDev;
    }

    public override void ResetGeometry()
    {
        OnToolDeactivate();
    }

    protected override void OnToolDeactivate()
    {
        HamlTool.ActiveProfile = null;
        HamlTool.ResetGeometry();
        HamlTool.ReportAllChanges();

        mhwChosen = true;
        _useFirstMHWPoint = false;
        candidatePoints = new Dictionary<Coordinate3D, Profile>();

        Module1.StatTracker = new ExperimentStatTracker();

        Module1.ToggleState(PluginState.InitializedState, false);
        Module1.ToggleState(PluginState.DoingOperationState, false);
        Module1.ToggleState(PluginState.ContourAvailableState, false);
        Module1.ToggleState(PluginState.ResetGeometryState, false);
        Module1.ToggleState(PluginState.ExperimentReadyState, true);
        
        if (HamlTool is not null)
        {
            HamlTool.ActiveProfile = null;
            HamlTool.Unsubscribe();
            HamlTool = null;
        }

        OverlayController.Dispose();
        Unsubscribe();
        _subDict.Clear();
        
        Module1.ToggleState(PluginState.DeactivatedState, true);
    }
}

public enum AnnotationType
{
    HIGH,
    LOW,
    BOTH,
    NEITHER
}
