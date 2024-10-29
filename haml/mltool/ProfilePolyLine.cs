/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Events;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.events;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.logging;
using HamlProAppModule.haml.ui;
using HamlProAppModule.haml.ui.arctool;
using HamlProAppModule.haml.ui.Experiment;
using HamlProAppModule.haml.util;
using MLPort;
using MLPort.kdtree;
using MLPort.placement;
using MLPort.placement.options;
using GE = ArcGIS.Core.Geometry.GeometryEngine;

namespace HamlProAppModule.haml.mltool
{
    // Creates profiles along a polyline based on a user's click point 
    public class ProfilePolyline : PerpendicularPolyline
    {
        protected Dictionary<SubscriptionToken, Action<SubscriptionToken>> _subDict;
        private KNNLearner _profileLearner;
        private Profile? _activeProfile;
        private Raster _raster;
        private bool _insertProfiles;
        private int _pointsTrainedSinceOptimization;
        private bool _readyForLearnerOptimization;
        private SpatialReference _reference;
        
        private Polyline _groundTruthSegmentHigh;
        private Polyline _groundTruthSegmentLow;

        public ProfilePolyline(Raster raster, KNNLearner learner, Polyline polyline, Envelope extent) 
            : base(raster, learner, polyline, extent)
        {
            _raster = raster;
            _reference = raster.GetSpatialReference();
            _profileLearner = InitMultiClassKNNLearner(_raster);
            LabelDirSet = false;
            HamlGeometry = new HAMLPolyline(polyline, extent);
            HamlGeometry.Raster = raster;
            _insertProfiles = true;
            VertexProfileDict = new Dictionary<Vertex, Profile>();
            IgnoredVertexProfileDict = new Dictionary<Vertex, Profile>();
            
            _subDict = new Dictionary<SubscriptionToken, Action<SubscriptionToken>>
            {
                {
                    PointMovedEvent.Subscribe(OnPointMoved), 
                    PointMovedEvent.Unsubscribe
                },
                {
                    IgnoreProfileEvent.Subscribe(OnIgnoreProfile),
                    IgnoreProfileEvent.Unsubscribe
                }
            };
            
            _pointsTrainedSinceOptimization = 0;
            _readyForLearnerOptimization = false;

        }

        private MultiClassKNNLearner InitMultiClassKNNLearner(Raster raster)
        {
            List<Feature> features = new List<Feature>();

            for (int i = 0; i < raster.GetBandCount(); i++)
            {
                features.Add(new LabelFeature(raster.GetBand(i).GetName()));
            }
            
            LabelFeature profileLabelFeature = new LabelFeature("Profile Label");
            ClassificationLabel profileClassLabel = new ClassificationLabel(profileLabelFeature, features, 3);
            return new MultiClassKNNLearner(profileClassLabel, DistanceMetric.EuclideanSquaredDistance, 9,
                ScoringMetric.DualInverseDistanceWeights);
        }

        protected internal IEnumerable<Profile> VerifyAllProfiles()
        {
            foreach(KeyValuePair<Vertex, Profile> entry in VertexProfileDict)
            {
                if (!entry.Value.Saved && !VerifyPOIOrder(entry.Value))
                {
                    yield return entry.Value;
                }
            }
        }
        
        protected internal bool VerifyPOIOrder(Profile p)
        {
            GE.Instance.QueryPointAndDistance((Multipart)GetContourAsArcGeometry(), SegmentExtensionType.NoExtension, p.PointAsMapPoint(p.HiIdx), AsRatioOrLength.AsLength,
                out double _, out double _, out LeftOrRightSide highSide);
            
            if (p.IsBerm)
            {
                Log.Here().Debug("Berm detected on the {@HighSide} of the shoreline with label side {@LabelSide}"
                , highSide, LabelSide);
                
                return highSide == LabelSide;
            }
            
            if (p.LowIdx < 0 || p.HiIdx < 0)
            {
                return false;
            }
            
            // Currently, the order of annotation points will always have the low point closer to the shoreline
            // than the high point. Also must make sure that the annotation points are on the label side
            var shoreline = p.Vertex.GetPoint().Coordinate3D;

            var lowPoint = p.Points[p.LowIdx];
            var highPoint = p.Points[p.HiIdx];

            double lowDist = GeomUtil.CalcDist(shoreline, lowPoint, MapView.Active.Map.SpatialReference);
            double highDist = GeomUtil.CalcDist(shoreline, highPoint, MapView.Active.Map.SpatialReference);
        
            GE.Instance.QueryPointAndDistance((Multipart)GetContourAsArcGeometry(), SegmentExtensionType.NoExtension, p.PointAsMapPoint(p.LowIdx), AsRatioOrLength.AsLength,
                out double _, out double _, out LeftOrRightSide lowSide);

            var ret = lowDist < highDist && lowSide == LabelSide && highSide == LabelSide;

            if (!ret)
            {
                SetDuneAlgoPlacements(p);
                Log.Here().Warning("Out of order annotations in profile at index {@Idx} with vertex ({@X}, {@Y}). " +
                                   "High point: idx={@HighIdx} ({@HighX}, {@HighY}) on {@HighSide}. " +
                                   "Low point: idx={@LowIdx} ({@LowX}, {@LowY}) on {@LowSide}. " +
                                   "Label side: {@LabelSide}",
                    p.GetVertexPointIndex(), p.Points[p.GetVertexPointIndex()].X, p.Points[p.GetVertexPointIndex()].Y
                    , p.HiIdx, Math.Round(p.Points[p.HiIdx].X,1), Math.Round(p.Points[p.HiIdx].Y,1), highSide
                    , p.LowIdx, Math.Round(p.Points[p.LowIdx].X,1), Math.Round(p.Points[p.LowIdx].Y,1), lowSide
                    , LabelSide);
            }

            return true;
            //return ret;
        }

        private bool VerifyMLPlacement(Profile p)
        {
            return VerifyHighPointIsHigh(p, 0.6)
                   && VerifyHighPointHigherThanLowPoint(p, 0.10)
                   && VerifyAllPlacementsAboveSealevel(p)
                   && VerifyPOIOrder(p);
        }
        
        /// <summary>
        /// Verifies that the high point is placed at or above a certain height threshold, given by a percentage of the
        /// maximum height in the profile. Because some profiles have "false" dunes with higher peaks, we allow the
        /// dune peaks to be below the maximum height. This method just ensures that the high point is not *too* low. For
        /// example, the default value of 0.5 means that the high point cannot be placed lower than half of the maximum
        /// height value on the profile.
        /// </summary>
        /// <param name="p"> The height profile of the transect </param>
        /// <param name="heightPercentageThreshold"> The height threshold, given as a percentage, which the high point
        /// must be above to satisfy the verification. </param>
        /// <returns> true if the high point's height is above the height threshold, false otherwise. </returns>
        private bool VerifyHighPointIsHigh(Profile p, double heightPercentageThreshold = 0.5)
        {
            var maxHeight = p.Points.Select(pt=> pt.Z).Max();
            var thresholdHeight = heightPercentageThreshold * maxHeight;
            var placementHeight = p.Points[p.HiIdx].Z;
            
            var ret = placementHeight >= thresholdHeight;

            if (!ret)
            {
                Log.Here().Information("ML placement failed high point height check. Height threshold: {@Threshold}, Placement height: {@Placement}",
                    thresholdHeight, placementHeight);
            }

            return ret;
        }
        
        /// <summary>
        /// Verifies that the high point is placed higher than the low point, as a percentage of the highest point in
        /// the profile.
        /// </summary>
        /// <param name="p"> The height profile of the transect </param>
        /// <param name="heightMarginPercentage"> The height margin threshold, given as a percentage of the maximum
        /// height of the signal, which the high point's height must be greater than the low point's height. For
        /// example, the default value of 0 means that the high point only needs to be above the low point by any amount.
        /// A value of 0.10 would mean that the high point must be higher than the low point by 10% of the maximum
        /// height of the profile.</param>
        /// <returns> true if the high point's height is sufficiently higher than the low point, false otherwise. </returns>
        private bool VerifyHighPointHigherThanLowPoint(Profile p, double heightMarginPercentage = 0)
        {
            // todo: we could store the profile's max height when the profile is created, which would be used by these two checks and also maybe algo placement for performance increase
            var maxHeight = p.Points.Select(pt=> pt.Z).Max();
            
            if (heightMarginPercentage is < 0 or >= 1)
            {
                Log.Here().Error("Invalid height margin {@Margin} specified for high point placement", heightMarginPercentage);
                return false;
            }

            var thresholdHeight = (1 + heightMarginPercentage * maxHeight) * p.Points[p.LowIdx].Z;
            var placementHeight = p.Points[p.HiIdx].Z;
            
            var ret = placementHeight > thresholdHeight;
            
            if (!ret)
            {
                Log.Here().Information("ML placement failed high > low check. Height threshold: {@Threshold}, Placement height: {@Actual}",
                    thresholdHeight, placementHeight);
            }

            return ret;
        }
        
        /// <summary>
        /// Verifies that both the high point and the low point are placed at or above sea level
        /// </summary>
        /// <param name="p"> The height profile of the transect </param>
        /// <returns> true if both high and low point placements are above sea level, false otherwise. </returns>
        private bool VerifyAllPlacementsAboveSealevel(Profile p)
        {
            var highPlacementHeight = p.Points[p.HiIdx].Z;
            var lowPlacementHeight = p.Points[p.LowIdx].Z;
            
            var ret = highPlacementHeight > 0 && lowPlacementHeight > 0;

            if (!ret)
            {
                Log.Here().Information("ML placement failed above sea level check. High placement height: {@High}, Low placement height: {@Low}",
                    highPlacementHeight, lowPlacementHeight);
            }
            
            return ret;
        }
        
        protected internal void TrainAllUntrainedProfiles()
        {
            List<Profile> trainedProfiles = new List<Profile>();
            
            GetValidProfiles().FindAll(p => !p.IsBerm && p.IsAwaitingTraining)
                .AsParallel().AsOrdered()
                .Select(p => (p, GetSignalPoints(p)))
                .ToList()
                .ForEach(tup =>
                {
                    var p = tup.p;
                    var points = tup.Item2;
                    TrainProfile(points, 
                        new List<int> {p.Annotations[Profile.High], p.Annotations[Profile.Low]},
                            p);
                    
                    trainedProfiles.Add(p);
                });

            if (trainedProfiles.Count > 0)
            {
                Log.Here().Debug("Trained on profiles: {@TrainedProfiles}", trainedProfiles.Select(p=>p.Id));
            }
            else
            {
                Log.Here().Debug("Found no valid profiles to train on");
            }
        }

        public List<Profile> GetValidProfiles()
        {
            return VertexProfileDict.Values.ToList();
        }
        
        public List<Profile> GetIgnoredProfiles()
        {
            return IgnoredVertexProfileDict.Values.ToList();
        }
        
        public List<Profile> GetAllProfiles()
        {
            var ret = VertexProfileDict.Values.ToList();
            ret.AddRange(IgnoredVertexProfileDict.Values.ToList());

            return ret;
        }
        
        protected internal List<SignalPoint> GetSignalPoints(Profile p)
        {
            p.RemoveNaN();
            
            var profileSignalGenerator = new SignalGenerator()
                .AddOption(new StandardDeviationOption(0, 3, 2))
                .AddOption(new MeanOption(0, 3, 2))
                .AddOption(new DifferentiationOption(0))
                .AddOption(new DifferentiationOption(0, 2));
            var profileSignalPointGenerator = new ProfileSignalPointGenerator
            {
                Profile = p
            };

            Module1.ToggleState(PluginState.DisableLearnerState, true);

            return profileSignalGenerator.GetSignal(profileSignalPointGenerator);
        }
        
        private void TrainProfile(List<SignalPoint> signal, List<int> classIndices, Profile p)
        {
            GeometryEngine.Instance.QueryPointAndDistance((Polyline)GetContourAsArcGeometry(), SegmentExtensionType.NoExtension, p.PointAsMapPoint(0), AsRatioOrLength.AsLength,
                out double _, out double _, out LeftOrRightSide side);
            
            // Label is on right side of signal/profile
            // Need to reverse order of classes to Low, Hi
            if (side != LabelSide)
            {
                classIndices.Reverse();
            }
            
            var featureVectors = new List<List<double>>();
            var labels = new List<List<double>>();

            var startingIdx = 0;
            for (int i = 0; i < classIndices.Count; i++)
            {
                int classIndex = classIndices[i];
                for (int j = startingIdx; j < signal.Count; j++)
                {
                    if (j <= classIndex)
                    {
                        featureVectors.Add(signal[j].featureVector);
                        labels.Add(new List<double> {i});
                    }
                    else
                    {
                        startingIdx = j;
                        break;
                    }
                }
            }

            for (int i = startingIdx; i < signal.Count; i++)
            {
                featureVectors.Add(signal[i].featureVector);
                labels.Add(new List<Double> {classIndices.Count});
            }

            int pointsBefore = _profileLearner.getDatasetSize();
            
            _profileLearner.trainMultipleSampled(featureVectors, labels, 
                new List<int> {p.HiIdx, p.LowIdx});
            
            _pointsTrainedSinceOptimization += _profileLearner.getDatasetSize() - pointsBefore;

            // we train in profile-sized batches, so _pointsTrainedSinceOptimization may be over the limit by the size
            // of a profile
            if (_pointsTrainedSinceOptimization > 2000)
            {
                _readyForLearnerOptimization = true;
            }

            p.IsAwaitingTraining = false;
        }

        public override Geometry GetContourAsArcGeometry()
        {
            return HamlGeometry.GetArcGeometry();
        }

        public Profile? ActiveProfile
        {
            get => _activeProfile;
            set
            {
                _activeProfile = value;
                
                ReportSearchSpaceChanges();
                
                ActiveProfileChangedEvent.Publish(_activeProfile);
            }
        }

        public bool ToggleMode()
        {
            _insertProfiles = !_insertProfiles;
            return _insertProfiles;
        }

        protected internal Profile InsertProfile(int vertexIdx, List<Coordinate3D>? points = null)
        {
            var vertex = GetVertices()[vertexIdx];
            var p = new Profile(vertex.GetConstraint().GetGeometry() as Polyline, Raster, vertex,  LabelSide, points);
            VertexProfileDict.Add(vertex, p);
            
            ReportVertexChanges();
            ReportSearchSpaceChanges();

            return p;
        }

        protected internal void InsertProfile(int vertexIdx, Profile profile)
        {
            var vertex = GetVertices()[vertexIdx];
            VertexProfileDict.Add(vertex, profile);

            ReportVertexChanges();
            ReportSearchSpaceChanges();
        }

        protected internal void RemoveProfile(Vertex profileVertex)
        {
            if (VertexProfileDict.ContainsKey(profileVertex))
            {
                Log.Here().Debug("Removing profile {@Profile}",
                    VertexProfileDict[profileVertex].Id);
                VertexProfileDict.Remove(profileVertex);
            } else if (IgnoredVertexProfileDict.ContainsKey(profileVertex))
            {
                Log.Here().Debug("Removing profile {@Profile} from Ignored vertices",
                    IgnoredVertexProfileDict[profileVertex].Id);
                IgnoredVertexProfileDict.Remove(profileVertex);
            }
            else
            {
                Log.Here().Warning("Failed to remove profile vertex {@ID} at {@X}, {@Y}",
                    profileVertex.Id, profileVertex.GetX(), profileVertex.GetY());
            }
            
            ReportAllChanges();
        }
        
        /// <summary>
        /// Creates profile at the declared index along the HAML tool geometry
        /// </summary> 
        /// <param name="idx"></param> index of the HAML geometry to add the profile
        /// <returns></returns>
        protected internal Profile InsertProfileAndGuessPlacements(int idx)
        {
            return InsertProfileAndGuessPlacements(idx, null);
        }

        /// <summary>
        /// Creates profile at the declared index along the HAML tool geometry
        /// </summary>
        /// <param name="idx"></param> index of the HAML geometry to add the profile
        /// <param name="coords"></param> list of points making up the profile's constraint
        /// <returns></returns>
        protected internal Profile InsertProfileAndGuessPlacements(int idx, List<Coordinate3D>? coords)
        {
            var p = InsertProfile(idx, coords);
            GuessProfilePlacements(p);

            return p;
        }
        
        public void GuessProfilePlacements(Profile p)
        {
            var signalPoints = GetSignalPoints(p);
            var vecs = signalPoints.Select(p => p.featureVector).ToList();
            var classifications = _profileLearner.ClassifyParallel(vecs);
            p.Classifications = classifications;
            
            var indices = new List<int>();
            // TODO: make this more robust for different orientations of the profile. the point is to make sure
            // todo: the signal points are starting in the water
            signalPoints.Reverse();
            var guesses = GuessProfilePointLocations(signalPoints);
            guesses.Reverse();
            signalPoints.Reverse();


            // If ML placement doesn't return enough placements we'll create a default guess
            if (guesses.Count < 2)
            {
                Log.Here().Warning("ML failed to create two placements. Making default guesses");
                
                // for (int i = 0; i < 2; i++)
                // {
                //     indices.Add(SanitizeGuess(indices.Count, p, -1));
                // }

                SetDuneAlgoPlacements(p);
                p.SetOriginalValues();
                return;
            }
            else
            {
                guesses.ForEach(point =>
                {
                    MapPoint mp = MapPointBuilderEx.CreateMapPoint(point.Item1, point.Item2, _reference);
                    ProximityResult pr = GeometryEngine.Instance.NearestVertex(p.ArcTransectPolyline, mp);
                    int? searchIdxNullable = pr.PointIndex;

                    if (searchIdxNullable.HasValue)
                    {
                        int searchIdx = searchIdxNullable.Value;

                        indices.Add(searchIdx);
                    }
                });
            }
            
            GeometryEngine.Instance.QueryPointAndDistance((Polyline)GetContourAsArcGeometry(), SegmentExtensionType.NoExtension, p.PointAsMapPoint(0), AsRatioOrLength.AsLength,
                out double _, out double _, out LeftOrRightSide side);
            // Label is on right side of signal/profile
            // Order of classes - Low, Hi
            if (side != LabelSide)
            {
                p.AddOrUpdateAnnotationPoint(Profile.High, indices[1]);
                p.AddOrUpdateAnnotationPoint(Profile.Low, indices[0]);
            }
            // Label is on left side of signal/profile
            // Order of classes - Hi, Low
            else
            {
                p.AddOrUpdateAnnotationPoint(Profile.High, indices[0]);
                p.AddOrUpdateAnnotationPoint(Profile.Low, indices[1]);
            }
            
            int maxZIdx = GeomUtil.GetMaxZIdxNaive(p);
            p.AddOrUpdateAnnotationPoint(Profile.High, maxZIdx);
            
            p.SetOriginalValues();
        }

        private List<(double, double)> GuessProfilePointLocations(List<SignalPoint> points)
        {
            return Placement.GuessDuneVertexLocationParallel(_profileLearner, points, 3);
        }

        private int SanitizeGuess(int idxCount, Profile p, int segIdx)
        {
            int idx = segIdx;
            int vIndex = p.GetVertexPointIndex();
            GeometryEngine.Instance.QueryPointAndDistance((Polyline)GetContourAsArcGeometry(), SegmentExtensionType.NoExtension, p.PointAsMapPoint(0), AsRatioOrLength.AsLength,
                out double _, out double _, out LeftOrRightSide side);
            int buffer;

            // Label is on right side of signal/profile
            // Order of classes - Low, Hi
            if (side != LabelSide)
            {
                buffer = idxCount == 0 ? 10 : 20;
                if (segIdx == -1 || segIdx < vIndex)
                {
                    idx = vIndex + buffer;
                }
                
            }
            // Label is on left side of signal/profile
            // Order of classes - Hi, Low
            else
            {
                buffer = idxCount == 0 ? 20 : 10;
                if (segIdx == -1 || segIdx > vIndex)
                {
                    idx = vIndex - buffer;
                }
            }
            
            if (idx < 0)
            {
                Log.Here().Debug("Placement guess sanitized from index {@Old} to {@New}"
                    , idx, 0);
                idx = 0;
            }

            if (idx > p.Points.Count - 1)
            {
                Log.Here().Debug("Placement guess sanitized from index {@Old} to {@New}"
                    , idx, p.Points.Count - 1);
                idx = p.Points.Count - 1;
            }
            
            return idx;
        }

        public bool SetDuneAlgoPlacements(Profile p)
        {
            int maxZIdx = GeomUtil.GetMaxZIdxNaive(p);
            int duneCurveIdx = GeomUtil.CalcLowPointViaSlope(p, maxZIdx);

            if (duneCurveIdx != -1)
            {
                p.AddOrUpdateAnnotationPoint(Profile.High, maxZIdx);
                p.AddOrUpdateAnnotationPoint(Profile.Low, duneCurveIdx);
                p.SetOriginalValues();
                
                // This flag is initialized to true (we assume ML placement firstly), so hopefully having this false
                // flag here is enough to handle all cases where algo placement takes over.
                Module1.StatTracker.UpdateStat(ExperimentStat.PlacementIsMl, false);
                
                return true;
            }
            
            Log.Here().Debug("Did not place a low point for profile with profile vertex {@ID} at {@X}, {@Y}"
                ,p.GetVertexPointIndex()
                , p.Points[p.GetVertexPointIndex()].X
                , p.Points[p.GetVertexPointIndex()].Y);
            return false;
        }
        
        public bool IsProfileLearnerTrained()
        {
            // todo: do we want a way to parameterize this?
            return _profileLearner.GetKDTree().getNumInstances() > 1000;
        }

        public Dictionary<Vertex, Profile> VertexProfileDict { get; }
        
        public Dictionary<Vertex, Profile> IgnoredVertexProfileDict { get; }

        public Profile? GetProfileFromVertex(Vertex v)
        {
            if (VertexProfileDict.ContainsKey(v))
            {
                return VertexProfileDict[v];
            }
            
            if (IgnoredVertexProfileDict.ContainsKey(v))
            {
                return IgnoredVertexProfileDict[v];
            }

            return null;
        }
        
        private void IgnoreProfile(Vertex vertex)
        {
            if (!VertexProfileDict.ContainsKey(vertex))
            {
                return;
            }

            var profile = VertexProfileDict[vertex];
            VertexProfileDict.Remove(vertex);
            IgnoredVertexProfileDict.Add(vertex, profile);
            
        }
        
        public void ReinstateProfile(Vertex vertex)
        {
            if (!IgnoredVertexProfileDict.ContainsKey(vertex))             {
                return;
            }
            
            var profile = IgnoredVertexProfileDict[vertex];
            IgnoredVertexProfileDict.Remove(vertex);
            VertexProfileDict.Add(vertex, profile);
            
        }

        // Returns all profiles that are not the currently selected profile
        public Geometry GetInactiveProfilesAsArcGeometry()
        {
            var inactiveProfileLines = (from p in GetValidProfiles() 
                where ActiveProfile is not null && ActiveProfile != p 
                select p.ArcTransectPolyline).ToList();

            return PolylineBuilderEx.CreatePolyline(inactiveProfileLines);
        }
        
        // Returns all ignored profiles that are not the currently selected profile
        public Geometry GetIgnoredInactiveProfilesAsArcGeometry()
        {
            var inactiveProfileLines = (from p in GetIgnoredProfiles() 
                where ActiveProfile is not null && ActiveProfile != p 
                select p.ArcTransectPolyline).ToList();

            return PolylineBuilderEx.CreatePolyline(inactiveProfileLines);
        }
        
        public Multipoint GetUneditedVertexPointsAsMultiPoint()
        {
            var points = VertexProfileDict.Values.Where(p => !p.Edited && p.Saved).Select(p => p.Vertex.GetPoint()).ToList();
            return MultipointBuilderEx.CreateMultipoint(points, Extent.SpatialReference);
        }
        
        public Multipoint GetEditedVertexPointsAsMultiPoint()
        {
            var points = VertexProfileDict.Values.Where(p => p.Edited || !p.Saved).Select(p => p.Vertex.GetPoint()).ToList();
            return MultipointBuilderEx.CreateMultipoint(points, Extent.SpatialReference);
        }
        
        public Multipoint GetIgnoredUneditedVertexPointsAsMultiPoint()
        {
            var points = IgnoredVertexProfileDict.Values.Where(p => !p.Edited && p.Saved).Select(p => p.Vertex.GetPoint()).ToList();
            return MultipointBuilderEx.CreateMultipoint(points, Extent.SpatialReference);
        }
        
        public Multipoint GetIgnoredEditedVertexPointsAsMultiPoint()
        {
            var points = IgnoredVertexProfileDict.Values.Where(p => p.Edited || !p.Saved).Select(p => p.Vertex.GetPoint()).ToList();
            return MultipointBuilderEx.CreateMultipoint(points, Extent.SpatialReference);
        }

        public Multipoint GetHighPointsAsMultipoint()
        {
            return MultipointBuilderEx.CreateMultipoint(GetValidProfiles()
                .Where(p => p.HiIdx > -1)
                .Select(p => p.Points[p.HiIdx])
                .ToList(), Extent.SpatialReference);
        }

        public Polyline GetHighPointsAsPolyline()
        {
            //TODO: investigate duplicate vertices being added
            List<Coordinate3D> orderedPoints = new List<Coordinate3D>();
            
            foreach (var vertex in HamlGeometry.GetVertices())
            {
                if(VertexProfileDict.ContainsKey(vertex)){
                    var profile = VertexProfileDict[vertex];
                    var hiIdx = profile.HiIdx;

                    if (hiIdx == -1)
                    {
                        return PolylineBuilderEx.CreatePolyline(GetHighPointsAsMultipoint());
                    }
                    
                    orderedPoints.Add(profile.Points[hiIdx]);
                }
            }
            
            return PolylineBuilderEx.CreatePolyline(orderedPoints);
        }
        
        public Multipoint GetLowPointsAsMultipoint()
        {
            return MultipointBuilderEx.CreateMultipoint(GetValidProfiles()
                .Where(p => p.LowIdx > -1)
                .Select(p => p.Points[p.LowIdx])
                .ToList(), Extent.SpatialReference);
        }
        
        // todo: remove this and combine code with GetHighPointsAsPolyline
        public Polyline GetLowPointsAsPolyline()
        {
            //TODO: investigate duplicate vertices being added
            List<Coordinate3D> orderedPoints = new List<Coordinate3D>();
            
            foreach (var vertex in HamlGeometry.GetVertices())
            {
                if (VertexProfileDict.ContainsKey(vertex))
                {
                    var profile = VertexProfileDict[vertex];
                    var lowIdx = profile.LowIdx;

                    if (lowIdx == -1)
                    {
                        return PolylineBuilderEx.CreatePolyline(GetLowPointsAsMultipoint());
                    }

                    orderedPoints.Add(profile.Points[lowIdx]);
                }
            }
            
            return PolylineBuilderEx.CreatePolyline(orderedPoints);
        }
        
        public Multipoint GetIgnoredHighPointsAsMultipoint()
        {
            return MultipointBuilderEx.CreateMultipoint(GetIgnoredProfiles()
                .Where(p => p.HiIdx > -1)
                .Select(p => p.Points[p.HiIdx])
                .ToList(), Extent.SpatialReference);
        }
        
        public Multipoint GetIgnoredLowPointsAsMultipoint()
        {
            return MultipointBuilderEx.CreateMultipoint(GetIgnoredProfiles()
                .Where(p => p.LowIdx > -1)
                .Select(p => p.Points[p.LowIdx])
                .ToList(), Extent.SpatialReference);
        }

        private void OnPointMoved(PointMovedEventArgs args)
        {
            switch (args.Type)
            {
                case HamlGraphicType.HighPoints:
                    if (args.NewIndex != args.OldIndex)
                    {
                        var origHighIdx = args.Sender.OriginalAnnoIdx[Profile.High];
                        var newHighIdx = args.NewIndex;
                        
                        if(origHighIdx != -1 && newHighIdx != -1){
                        
                            var origHighPt = args.Sender.Points[origHighIdx].ToMapPoint();
                            var newHighPt = args.Sender.Points[newHighIdx].ToMapPoint();

                            var correctionDistance = Math.Round(GeometryEngine.Instance.Distance(origHighPt, newHighPt), 2);
                            
                            Module1.StatTracker.UpdateStat(ExperimentStat.HighPtCorrectionDistance, correctionDistance);
                        }
                    }

                    ReportHighPointChanges();
                    ReportVertexChanges();
                    break;
                case HamlGraphicType.LowPoints:
                    if (args.NewIndex != args.OldIndex && args.NewIndex != -1 && args.OldIndex != -1)
                    {
                        var origLowIdx = args.Sender.OriginalAnnoIdx[Profile.Low];
                        var newLowIdx = args.NewIndex;

                        if (origLowIdx != -1 && newLowIdx != -1)
                        {

                            var origLowPt = args.Sender.Points[origLowIdx].ToMapPoint();
                            var newLowPt = args.Sender.Points[newLowIdx].ToMapPoint();

                            var correctionDistanceLow =
                                Math.Round(GeometryEngine.Instance.Distance(origLowPt, newLowPt), 2);

                            Module1.StatTracker.UpdateStat(ExperimentStat.LowPtCorrectionDistance,
                                correctionDistanceLow);
                        }
                    }

                    ReportLowPointChanges();
                    ReportVertexChanges();
                    break;
                case HamlGraphicType.Vertex:
                    ReportVertexChanges();
                    break;
                default:
                    return;
            }
        }
        
        public override void ResetGeometry()
        {
            QueuedTask.Run(async () =>
            {
                var beforeTreeSize = GetProfileLearnerSize();

                _profileLearner = InitMultiClassKNNLearner(_raster);
                _pointsTrainedSinceOptimization = 0;
                _readyForLearnerOptimization = false;
                Module1.ToggleState(PluginState.ResetGeometryState, false);

                var afterTreeSize = GetProfileLearnerSize();
                
                Log.Here().Debug("Reset learner from {@BeforeTreeSize} instances to {@AfterTreeSize}",
                    beforeTreeSize, afterTreeSize);
            });
        }
        
        protected internal int GetProfileLearnerSize()
        {
            return _profileLearner.getDatasetSize();
        }

        public void OptimizeLearner()
        {
            try
            {
                _profileLearner.optimize();
                _pointsTrainedSinceOptimization = 0;
                _readyForLearnerOptimization = false;
                Log.Here().Debug("Optimized learner with {@TreeSize} instances."
                    , GetProfileLearnerSize());
            }
            catch(Exception e)
            {
                Log.Here().Error("Failed to optimize learner");
            }
        }

        public bool ReadyForLearnerOptimization()
        {
            return _readyForLearnerOptimization;
        }

        private void OnIgnoreProfile(Profile p)
        {
            if (p.Ignored)
            {
                IgnoreProfile(p.Vertex);    
            }
            else
            {
                ReinstateProfile(p.Vertex);
            }
            
            ReportAllChanges();
        }

        public void ReportAllChanges(CIMColor? color = null, bool updateSymbolReference = false)
        {
            ReportSearchSpaceChanges(color, updateSymbolReference);
            ReportVertexChanges(color, updateSymbolReference);
            ReportHighPointChanges(color, updateSymbolReference);
            ReportLowPointChanges(color, updateSymbolReference);
        }

        private void ReportSearchSpaceChanges(CIMColor? color = null, bool updateSymbolReference = false)
        {
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetInactiveProfilesAsArcGeometry(),
                Type = HamlGraphicType.ValidSearchSpace,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.ValidSearchSpace, color) : null
            });
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetIgnoredInactiveProfilesAsArcGeometry(),
                Type = HamlGraphicType.IgnoredSearchSpace,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.IgnoredSearchSpace, color) : null
            });
                
            if (ActiveProfile is null) return;
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = ActiveProfile.ArcTransectPolyline,
                Type = HamlGraphicType.ActiveProfile,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.ActiveProfile, color) : null
            });
        }

        private void ReportVertexChanges(CIMColor? color = null, bool updateSymbolReference = false)
        {
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetContourAsArcGeometry(),
                Type = HamlGraphicType.Contour,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.Contour, color) : null
            });
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
                {
                    Geometry = GetHighPointsAsPolyline(),
                    Type = HamlGraphicType.HighContour
                }
            );
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
                {
                    Geometry = GetLowPointsAsPolyline(),
                    Type = HamlGraphicType.LowContour
                }
            );

            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetEditedVertexPointsAsMultiPoint(),
                Type = HamlGraphicType.EditedVertices,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.EditedVertices, color) : null
            });
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetUneditedVertexPointsAsMultiPoint(),
                Type = HamlGraphicType.UneditedVertices,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.UneditedVertices, color) : null
            });
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetIgnoredEditedVertexPointsAsMultiPoint(),
                Type = HamlGraphicType.IgnoredEditedVertices,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.IgnoredEditedVertices, color) : null
            });
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetIgnoredUneditedVertexPointsAsMultiPoint(),
                Type = HamlGraphicType.IgnoredUneditedVertices,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.IgnoredUneditedVertices, color) : null
            });

            ExperimentDockPaneViewModel.Show();
        }

        private void ReportLowPointChanges(CIMColor? color = null, bool updateSymbolReference = false)
        {
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetLowPointsAsMultipoint(),
                Type = HamlGraphicType.LowPoints,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.LowPoints, color) : null
            });
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetIgnoredLowPointsAsMultipoint(),
                Type = HamlGraphicType.IgnoredLowPoints,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.IgnoredLowPoints, color) : null
            });
        }

        private void ReportHighPointChanges(CIMColor? color = null, bool updateSymbolReference = false)
        {
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetHighPointsAsMultipoint(),
                Type = HamlGraphicType.HighPoints,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.HighPoints, color) : null
            });
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetIgnoredHighPointsAsMultipoint(),
                Type = HamlGraphicType.IgnoredHighPoints,
                Reference = updateSymbolReference ? Module1.GetSymbolReference(HamlGraphicType.IgnoredHighPoints, color) : null
            });
        }

        public void Unsubscribe()
        {
            if (_subDict is null) return;
            foreach (var kvp in _subDict)
            {
                kvp.Value.Invoke(kvp.Key);
            }

            _subDict.Clear();
        }
        
        protected internal override int InsertAndPlaceVertex(MapPoint point)
        {
            HamlGeometry.InsertVertex(point, out _, out var vertexIndex);
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetContourAsArcGeometry(),
                Type = HamlGraphicType.Contour
            });
            
            return vertexIndex;
        }

        public Profile SmartInsert(Dictionary<Coordinate3D, Profile> candidatePoints, double tolerance)
        {
            if (candidatePoints.Count < 1)
            {
                return null;
            }
            
            Profile ret = null;
            
            Module1.ToggleState(PluginState.DoingOperationState, true);
            
            // the keys for the ProfileDists dictionary are tuples because we need the original Coordinate3D to re-use
            // as a key later
            var candidateProfileDists = new Dictionary<Tuple<Profile, Coordinate3D>, double>();
            var highLine = GetHighPointsAsPolyline(); // TODO: store these for later
            var lowLine = GetLowPointsAsPolyline(); // TODO: store these for later
            
            Dictionary<Coordinate3D, Profile> coarseSelectedCandidatePointsList = GetCoarseSelectedCandidatePoints(candidatePoints, candidatePoints.Count);
            if (coarseSelectedCandidatePointsList.Count < 1)
            {
                return null; //experiment ends when no more candidate points can be found
            }
            
            // This creates a dictionary of each profile and the sum of their greatest changes to the high and low lines
            coarseSelectedCandidatePointsList.ToList().ForEach(kvp =>
            {
                var p = kvp.Value;
                GuessProfilePlacements(p);
                
                GeometryEngine.Instance.QueryPointAndDistance(highLine, SegmentExtensionType.NoExtension,
                    p.PointAsMapPoint(p.HiIdx), AsRatioOrLength.AsLength, out _, out var highDist, out _);
                GeometryEngine.Instance.QueryPointAndDistance(lowLine, SegmentExtensionType.NoExtension,
                    p.PointAsMapPoint(p.LowIdx), AsRatioOrLength.AsLength, out _, out var lowDist, out _);

                var largerDist = Math.Max(highDist,lowDist);

                // If total dist is inside the tolerance then the profile is deemed redundant and not added
                if (largerDist > tolerance)
                {
                    candidateProfileDists.Add(new Tuple<Profile, Coordinate3D>(p,kvp.Key), largerDist);
                }
            });

            if (candidateProfileDists.Any())
            {
                var largestChangeProfile = candidateProfileDists.OrderByDescending(x => x.Value).First().Key.Item1;

                var chosenProfile = largestChangeProfile;
                
                var highIntersectionPoint = GeometryEngine.Instance.Intersection(GetHighPointsAsPolyline(), chosenProfile.ArcTransectPolyline, GeometryDimensionType.EsriGeometry0Dimension);
                var highDist = GeometryEngine.Instance.Distance(chosenProfile.Points[chosenProfile.groundHighIdx].ToMapPoint(), highIntersectionPoint);
                var lowIntersectionPoint = GeometryEngine.Instance.Intersection(GetLowPointsAsPolyline(), chosenProfile.ArcTransectPolyline, GeometryDimensionType.EsriGeometry0Dimension);
                var lowDist = GeometryEngine.Instance.Distance(chosenProfile.Points[chosenProfile.groundLowIdx].ToMapPoint(), lowIntersectionPoint);
                if (highDist + lowDist <= tolerance)
                {
                    Module1.StatTracker.UpdateStat(ExperimentStat.InsertionWasUnnecessary, true);
                }

                InsertProfile(chosenProfile);
                ret = chosenProfile;
            }
            else
            {
                // create a new copy of the original candidate points list that has had the previously-tried candidates removed
                var reducedCandidatePointsList = candidatePoints
                    .Where(kvp => !coarseSelectedCandidatePointsList.ContainsKey(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                
                ret = SmartInsert(reducedCandidatePointsList, tolerance);
            }

            Module1.ToggleState(PluginState.DoingOperationState, false);

            return ret;
        }

        public void CorrectProfilePlacements(Profile profile, AnnotationType annotationType)
        {
            switch(annotationType)
            {
                case(AnnotationType.HIGH):
                    profile.AddOrUpdateAnnotationPoint(Profile.High, profile.groundHighIdx);
                    break;
                case(AnnotationType.LOW):
                    profile.AddOrUpdateAnnotationPoint(Profile.Low, profile.groundLowIdx);
                    break;
                case(AnnotationType.BOTH):
                    profile.AddOrUpdateAnnotationPoint(Profile.High, profile.groundHighIdx);
                    profile.AddOrUpdateAnnotationPoint(Profile.Low, profile.groundLowIdx);
                    break;
                case(AnnotationType.NEITHER):
                    break;
            }
        }

        private Dictionary<Coordinate3D, Profile> GetCoarseSelectedCandidatePoints(Dictionary<Coordinate3D, Profile> candidatePoints, int count)
        {
            // if candidate points list is empty, early return an empty copy. this condition should terminate the experiment
            if (candidatePoints.Count < 1)
            {
                return new Dictionary<Coordinate3D, Profile>();
            }
            
            var candidatePointsCopy = candidatePoints.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            // if the requested count is equal to or larger than the number of entries in candidate points list, return all
            if (count >= candidatePoints.Count)
            {
                return candidatePointsCopy;
            }
            
            // otherwise, randomly remove entries from the copied list until we have the correct number
            Random r = new Random();
            while (candidatePointsCopy.Count > count)
            {
                int randInt = r.Next(0, candidatePoints.Count);

                var selectedEntry = candidatePoints.ElementAt(randInt);
                candidatePointsCopy.Remove(selectedEntry.Key);
            }

            return candidatePointsCopy;
        }

        public void InsertProfile(Profile p)
        {
            var vertex = GetVertices()[InsertAndPlaceVertex(p.Vertex.GetPoint())];
            p.Vertex = vertex;
            
            VertexProfileDict.Add(vertex, p);
            ActiveProfile = p;
            ReportAllChanges();
        }

        public Polyline GroundTruthHigh
        {
            get => _groundTruthSegmentHigh;
            set => _groundTruthSegmentHigh = value;
        }

        public Polyline GroundTruthLow
        {
            get => _groundTruthSegmentLow;
            set => _groundTruthSegmentLow = value;
        }
    }
}
