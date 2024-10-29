/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Events;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using HamlProAppModule.haml.events;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.logging;
using HamlProAppModule.haml.mltool;
using HamlProAppModule.haml.ui.dockpane;
using HamlProAppModule.haml.util;
using GE = ArcGIS.Core.Geometry.GeometryEngine;

namespace HamlProAppModule.haml.ui.arctool
{
    public class HamlSketchProfileMapTool : HamlMapTool<ProfilePolyline>
    {
        protected List<Profile> _visibleProfiles;

        protected HamlSketchProfileMapTool()
        {
            _visibleProfiles = new List<Profile>();
            IsSketchTool = true;
            loadGeometry = false;
            SketchType = SketchGeometryType.Line;
            EsriGeometryType = esriGeometryType.esriGeometryPolyline;
            _featureClassName = Module1.DefaultPolylineFCName;
            _useLearner = true;
        }

        protected override void InitHamlTool(Envelope mapViewExtent, Geometry? arcGeometry = null)
        {
            // todo: could use some re-factoring later. possibly do all of this within the constructor for ProfilePolyline?
            Polyline p = arcGeometry as Polyline ?? throw new InvalidOperationException();
            HamlTool = new ProfilePolyline(_raster, _learner, p, mapViewExtent);
            InitLabelSides(HamlTool.GetContourAsArcGeometry() as Polyline);
            Module1.ToggleState(PluginState.InitializedState, true);
        }

        protected override void InitBaseOverlays()
        {
            var geom = HamlTool.ActiveProfile != null
                ? HamlTool.ActiveProfile.ArcTransectPolyline
                : PolylineBuilderEx.CreatePolyline();
            
            OverlayController.AddOverlay(geom,
                Module1.GetSymbolReference(HamlGraphicType.ActiveProfile), 
                HamlGraphicType.ActiveProfile);
            
            OverlayController.AddOverlay(HamlTool.GetInactiveProfilesAsArcGeometry(),
                Module1.GetSymbolReference(HamlGraphicType.ValidSearchSpace), 
                HamlGraphicType.ValidSearchSpace);
            
            OverlayController.AddOverlay(HamlTool.GetIgnoredInactiveProfilesAsArcGeometry(),
                Module1.GetSymbolReference(HamlGraphicType.IgnoredSearchSpace), 
                HamlGraphicType.IgnoredSearchSpace);
            
            OverlayController.AddOverlay(HamlTool.GetHighPointsAsMultipoint(),
                Module1.GetSymbolReference(HamlGraphicType.HighPoints), 
                HamlGraphicType.HighPoints);
            
            OverlayController.AddOverlay(HamlTool.GetLowPointsAsMultipoint(),
                Module1.GetSymbolReference(HamlGraphicType.LowPoints), 
                HamlGraphicType.LowPoints);
            
            OverlayController.AddOverlay(HamlTool.GetIgnoredHighPointsAsMultipoint(),
                Module1.GetSymbolReference(HamlGraphicType.IgnoredHighPoints), 
                HamlGraphicType.IgnoredHighPoints);
            
            OverlayController.AddOverlay(HamlTool.GetIgnoredLowPointsAsMultipoint(),
                Module1.GetSymbolReference(HamlGraphicType.IgnoredLowPoints), 
                HamlGraphicType.IgnoredLowPoints);
            
            OverlayController.AddOverlay(HamlTool.GetContourAsArcGeometry(), 
                Module1.CreateHamlPolylineSymbol().MakeSymbolReference(), HamlGraphicType.Contour);
            
            OverlayController.AddOverlay(HamlTool.GetUneditedVertexPointsAsMultiPoint(), 
                Module1.GetSymbolReference(HamlGraphicType.UneditedVertices), 
                HamlGraphicType.UneditedVertices);
            
            OverlayController.AddOverlay(HamlTool.GetEditedVertexPointsAsMultiPoint(),
                Module1.GetSymbolReference(HamlGraphicType.EditedVertices), 
                HamlGraphicType.EditedVertices);
            
            OverlayController.AddOverlay(HamlTool.GetIgnoredUneditedVertexPointsAsMultiPoint(), 
                Module1.GetSymbolReference(HamlGraphicType.IgnoredUneditedVertices), 
                HamlGraphicType.IgnoredUneditedVertices);
            
            OverlayController.AddOverlay(HamlTool.GetIgnoredEditedVertexPointsAsMultiPoint(),
                Module1.GetSymbolReference(HamlGraphicType.IgnoredEditedVertices), 
                HamlGraphicType.IgnoredEditedVertices);
        }

        /*
         * Simple progress dialog. ****Will NOT be displayed in debug mode****
         * Details for more sophisticated (% loaded) found here:
         * https://github.com/Esri/arcgis-pro-sdk-community-samples/blob/master/Framework/ProgressDialog/ProgressDialogModule.cs
         */
        private async void AnnotateSketchedVertices()
        {
            using (var progress = new ProgressDialog("Generating Profiles..."))
            {
                var status = new ProgressorSource(progress);
                progress.Show();

                await QueuedTask.Run(() =>
                {
                    var vertices = HamlTool.GetVertices();
                    for (var i = 0; i < vertices.Count; i++)
                    {
                        var v = vertices[i];

                        if (!HamlTool.VertexProfileDict.ContainsKey(v) && GE.Instance.Contains(ActiveMapView.Extent, v.GetPoint()) && v.GetConstraint() is SegmentConstraint)
                        {
                            if (!HamlTool.VertexProfileDict.ContainsKey(v) &&
                                !HamlTool.IgnoredVertexProfileDict.ContainsKey(v))
                            {
                                HamlTool.InsertProfileAndGuessPlacements(i);
                            }
                        }
                    }
                }, status.Progressor);

                progress.Hide();
            }
        }

        // Should contain keys that only pertain to all profile map tools
        protected override void OnToolKeyDown(MapViewKeyEventArgs k)
        {
            switch (k.Key)
            {
                case Key.W: // Toggle sketching
                {
                    sketching = !sketching;
                    Log.Here().Information("Sketching: {@Sketching}", sketching);
                    GUI.ShowToast(sketching ? "Sketch Enabled" : "Sketch Disabled");
                    break;
                }
                case Key.M:
                {
                    CycleProfile();
                    break;
                }
                case Key.N:
                {
                    CycleProfile(false);
                    break;
                }
                case Key.Delete:
                {
                    if (HamlTool.ActiveProfile is null) return;
                    IgnoreProfile(HamlTool.ActiveProfile);
                    UpdateVisibleProfiles();
                    break;
                }
                default:
                {
                    base.OnToolKeyDown(k);
                    break;
                }
            }
        }

        protected override void OnToolActivate()
        {
            base.OnToolActivate();

            QueuedTask.Run(() =>
            {
                GeodatabaseUtil.CreatePointDatasetAndFeatureClass(_selectedRasterLayer.GetSpatialReference());
                GeodatabaseUtil.CreateBaselineFeatureClass(_selectedRasterLayer.GetSpatialReference(), GeodatabaseUtil.BaselineFCName);    
            });
        }

        protected override void Subscribe()
        {
            base.Subscribe();
            var temp = new Dictionary<SubscriptionToken, Action<SubscriptionToken>>
            {
                {
                    SaveProfileEvent.Subscribe(VerifyAndSave),
                    SaveProfileEvent.Unsubscribe
                }
            };
            
            temp.ToList().ForEach(kvp => _subDict[kvp.Key] = kvp.Value);
        }

        protected internal void UpdateVisibleProfiles()
        {
            _visibleProfiles.Clear();

            if (HamlTool != null)
            {
                for (var i = 0; i < HamlTool.GetVertices().Count; i++)
                {
                    var profile = HamlTool.GetProfileFromVertex(HamlTool.GetVertices()[i]);

                    if (profile is null || !ViewContainsProfile(MapView.Active.Extent, profile)) continue;
                    
                    _visibleProfiles.Add(profile);
                }
            }
        }

        private bool ViewContainsProfile(Geometry extent, Profile p)
        {
            var poi = new List<MapPoint>();
            p.Annotations.Values.ToList().ForEach(index => poi.Add(p.PointAsMapPoint(index)));
            poi.Add(p.Vertex.GetPoint());

            return GeometryEngine.Instance.Intersects(extent, p.Vertex.GetConstraint().GetGeometry());
        }

        protected override void UpdateOnZoomOrPan(MapViewCameraChangedEventArgs args)
        {
            base.UpdateOnZoomOrPan(args);
            Module1.CalculateScreenBounds();
            if (HamlTool is null) return;
            UpdateVisibleProfiles();
        }

        public void CycleProfile(bool next = true)
        {
            if (_visibleProfiles.Count == 0) return;
             
            int idx;

            if (_visibleProfiles.Contains(HamlTool.ActiveProfile))
            {
                idx = _visibleProfiles.IndexOf(HamlTool.ActiveProfile);
                idx = next ? idx + 1 : idx - 1;

                if (idx == _visibleProfiles.Count)
                {
                    idx = 0;
                } else if (idx < 0)
                {
                    idx = _visibleProfiles.Count - 1;
                }
            }
            else
            {
                idx = next ? 0 : _visibleProfiles.Count - 1;
            }

            HamlTool.ActiveProfile = _visibleProfiles[idx];
            
            Log.Here().Debug("Profile {@Profile} has been set as the Active Profile", HamlTool.ActiveProfile?.Id);            
        }
        
        protected void IgnoreProfile(Profile profile)
        {
            // delete shoreline vertex and profile
            profile.Ignored = true;
            
            Log.Here().Debug("Profile {@Profile} has changed Ignored status from {@Old} to {@New}", 
                profile.Id, !profile.Ignored, profile.Ignored);
            UpdateVisibleProfiles();
        }
        
        protected void DeleteProfile(Profile profile)
        {
            // delete shoreline vertex and profile
            HamlTool.RemoveProfile(profile.Vertex);
            HamlTool.RemoveVertex(profile.Vertex);
            UpdateVisibleProfiles();
            CycleProfile();

            QueuedTask.Run(() =>
            {
                // todo: support more points beyond High and Low in a flexible way
                GeodatabaseUtil.DeleteProfilePoint("HighPoints", profile.HighOid);
                GeodatabaseUtil.DeleteProfilePoint("LowPoints", profile.LowOid);
                GeodatabaseUtil.DeleteProfilePoint("ShorelinePoints", profile.ShoreOid);
                
                if (HamlTool.GetAllProfiles().Count < 1)
                {
                    // todo: deactivate tool when the last profile is deleted
                    // note: may also need to handle cases where only one profile remains - -currently cant add a new 
                    // profile with only one current profile
                }
            });
        }

        protected override Task HandleMouseDownAsync(MapViewMouseButtonEventArgs e)
        {
            return QueuedTask.Run(() =>
            {
                if (sketching || _visibleProfiles.Count == 0) return;

                // Determine if the user has clicked near a high, low, or shoreline overlay point
                MapPoint clickPoint = MapView.Active.ClientToMap(e.ClientPoint);
                
                var lowPoints = new List<Coordinate3D>();
                var highPoints = new List<Coordinate3D>();
                var shorePoints = new List<MapPoint>();

                var profiles = _visibleProfiles.ToList();
                
                foreach (var profile in profiles)
                {
                    if (!profile.IsBerm)
                    {
                        lowPoints.Add(profile.Points[profile.LowIdx]);    
                    }
                    
                    highPoints.Add(profile.Points[profile.HiIdx]);
                    shorePoints.Add(profile.Vertex.GetPoint());
                }
                
                // build geometries out of the high, low, shore points and transect lines to determine what profile
                // was clicked along with what part of the profile the click was closest
                var lowPoly = PolylineBuilderEx.CreatePolyline(lowPoints);
                var hiPoly = PolylineBuilderEx.CreatePolyline(highPoints);
                var shorePoly = PolylineBuilderEx.CreatePolyline(shorePoints);
                var onScreenProfileTransects = PolylineBuilderEx.CreatePolyline(profiles.Select(p=>p.ArcTransectPolyline));

                // get the search results for each geometry
                ProximityResult lowSearchResult = GE.Instance.NearestVertex(lowPoly, clickPoint);
                ProximityResult highSearchResult = GE.Instance.NearestVertex(hiPoly, clickPoint);
                ProximityResult shoreSearchResult = GE.Instance.NearestVertex(shorePoly, clickPoint);
                
                // clip so that only the transects in the current mapview are part of the geometry
                ProximityResult transectsSearchResult = GE.Instance.NearestPoint(onScreenProfileTransects, clickPoint);

                // sanitize in the rare event the distance is -1, also helps w/ if/else brevity
                var lowDist = lowSearchResult.Distance >= 0 ? lowSearchResult.Distance : double.MaxValue;
                var highDist = highSearchResult.Distance >= 0 ? highSearchResult.Distance : double.MaxValue;
                var shoreDist = shoreSearchResult.Distance >= 0 ? shoreSearchResult.Distance : double.MaxValue;

                _selectedPointIndex = null;
                if (shoreSearchResult.Distance < 5 && shoreDist < lowDist && shoreDist < highDist && shoreSearchResult.PointIndex != null) 
                {
                    dragState = DragState.Shore;
                } 
                else if (lowDist < 5 && lowDist < shoreDist && lowDist < highDist && lowSearchResult.PointIndex != null)
                {
                    dragState = DragState.Low;
                } 
                else if (highDist < 5 && highSearchResult.PointIndex != null) 
                {
                    dragState = DragState.High;
                }
                
                _selectedPointIndex = transectsSearchResult.PartIndex;

                if (_selectedPointIndex.HasValue && _selectedPointIndex < profiles.Count)
                {
                    var profile = profiles[_selectedPointIndex.Value];

                    if (profile == HamlTool.ActiveProfile) return;
                    
                    HamlTool.ActiveProfile = profile;
                    UpdateVisibleProfiles();
                }
            });
        }

        protected override void OnToolMouseMoveAsync(MapViewMouseEventArgs e)
        {
            if (HamlTool == null) return;
            
            var clientPoint = MapView.Active.ClientToMap(e.ClientPoint);

            if (HamlTool.ActiveProfile != null)
            {
                var nearestIdx = HamlTool.ActiveProfile.GetNearestProfilePointIndex(clientPoint);

                if (!nearestIdx.HasValue) return;

                var idx = nearestIdx.Value;
                var (min, max) = ProfileDockPaneViewModel.GetGraphMinMaxX();
                var inBounds = min != -1 && max != -1 && idx > min &&
                               idx < max;

                if (!inBounds)
                {
                    // we have moved a point beyond the view of the graph, so limit the index to the bounds of the graph
                    idx = idx < max ? min : max;
                }

                if (dragState != DragState.None)
                {
                    if (dragState == DragState.High)
                    {
                        HamlTool.ActiveProfile.AddOrUpdateAnnotationPoint(Profile.High, idx);
                    }
                    else if (dragState == DragState.Low)
                    {
                        HamlTool.ActiveProfile.AddOrUpdateAnnotationPoint(Profile.Low, idx);
                    }
                    else if (dragState == DragState.Shore)
                    {
                        HamlTool.ActiveProfile.MoveVertex(idx);
                    }
                }
            }
            else
            {
                Log.Here().Warning("{@HamlTool} does not have an active profile", HamlTool.GetType());
                // We should not fall into this condition, but logic is in place to handle rare events
                if (!_selectedPointIndex.HasValue) return;
                int vertexIdx = _selectedPointIndex.Value;
                HamlTool.MoveVertex(vertexIdx, clientPoint);
            }
        }

        protected override void OnToolMouseUp(MapViewMouseButtonEventArgs e)
        {
            if (_selectedPointIndex is not null && HamlTool?.ActiveProfile != null)
            {
                string pointType;
                int currentIdx;
                var startPoint = HamlTool.ActiveProfile.Points[_selectedPointIndex.Value];

                switch (dragState)
                {
                    case DragState.None:
                        return;
                    case DragState.Shore:
                        pointType = "shoreline";
                        currentIdx = HamlTool.ActiveProfile.GetVertexPointIndex();
                        break;
                    case DragState.Low:
                        pointType = Profile.Low;
                        currentIdx = HamlTool.ActiveProfile.LowIdx;
                        
                        break;
                    case DragState.High:
                        pointType = Profile.High;
                        currentIdx = HamlTool.ActiveProfile.HiIdx;
                        
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (currentIdx == _selectedPointIndex.Value) return;
                var endPoint = HamlTool.ActiveProfile.Points[currentIdx];
                Log.Here().Debug(
                    "Profile {@ID} {@PointType} Point updated from idx {@StartIdx} ({@StartX},{@StartY}) to {@EndIdx} ({@EndX},{@EndY})",
                    HamlTool.ActiveProfile.Id, pointType, _selectedPointIndex.Value, Math.Round(startPoint.X,1), Math.Round(startPoint.Y,1),
                    currentIdx, Math.Round(endPoint.X,1), Math.Round(endPoint.Y,1));
            }
            
            dragState = DragState.None;
        }

        protected override async Task HandleDoubleClickAsync(MapViewMouseButtonEventArgs e)
        {
            Module1.ToggleState(PluginState.DoingOperationState, true);

            using (var progress = new ProgressDialog("Generating Profiles..."))
            {
                var status = new ProgressorSource(progress);
                progress.Show();

                await QueuedTask.Run(() =>
                {
                    MapPoint mapPoint = MapView.Active.ClientToMap(e.ClientPoint);

                    // case handling the "normal" situation: we're actively adding profiles and ML is firing
                    if (HamlTool.IsLabelDirSet())
                    {
                        OverlayController.Remove(HamlGraphicType.ProfileText);

                        GeodatabaseUtil.SaveProfilesToGdb(HamlTool.GetValidProfiles());

                        // when ML is disabled, we assume they also don't want to train on points in the meanwhile
                        if (_useLearner)
                        {
                            HamlTool.TrainAllUntrainedProfiles();
                            HamlTool.TrainAllUntrainedVertices();
                        }

                        // TODO (future task): do we want to add profiles to each vertex all at once, or trickle them in?
                        for (int i = 0; i < HamlTool.GetVertices().Count; i++)
                        {
                            Vertex v = HamlTool.GetVertices()[i];
                            if (v.GetConstraint() is SegmentConstraint && !HamlTool.VertexProfileDict.ContainsKey(v))
                            {
                                if(_useLearner)
                                {
                                    HamlTool.InsertProfileAndGuessPlacements(i);
                                }
                                else
                                {
                                    Profile p = HamlTool.InsertProfile(i);
                                    HamlTool.SetDuneAlgoPlacements(p);
                                }
                            }
                        }

                        AddVertexAndProfile(mapPoint);
                        UpdateVisibleProfiles();
                        CycleProfile();
                    }
                    else
                    {
                        RemoveLabelSideOverlays();
                        Module1.CalculateScreenBounds();
                        HamlTool.SetLabelDir(mapPoint);
                        ((HAMLPolyline) HamlTool.GetHamlGeometry).BuildConstraints();
                        AddProfiles();
                        
                        HamlTool.ActiveProfile = HamlTool.GetValidProfiles()[0];
                    }
                }, status.Progressor);
                
                progress.Hide();
            }

            Module1.ToggleState(PluginState.DoingOperationState, false);
        }

        protected void RemoveLabelSideOverlays()
        {
            OverlayController.Remove(new List<HamlGraphicType>
            {
                HamlGraphicType.LabelSideOutline,
                HamlGraphicType.LabelSideText,
                HamlGraphicType.FillSide
            });
        }
        
        protected void AddProfiles()
        {
            int max = Int32.MaxValue; // TODO: needs to be a setting to limit the number of profiles ever placed at once
            int count = 0;
                            
            for (int i = 0; i < HamlTool.GetVertices().Count; i++)
            {
                Vertex v = HamlTool.GetVertices()[i];
                if (v.GetConstraint() is SegmentConstraint && !HamlTool.VertexProfileDict.ContainsKey(v))
                {
                    if (_useLearner && HamlTool.IsProfileLearnerTrained())
                    {
                        HamlTool.InsertProfileAndGuessPlacements(i);
                    }
                    else
                    {
                        Profile p = HamlTool.InsertProfile(i);
                        HamlTool.SetDuneAlgoPlacements(p);
                    }
                    
                    if (count == max)
                    {
                        break;
                    }
                }
                else
                {
                    int x = 0;
                }
            }
            
            UpdateVisibleProfiles();
        }

        protected void AddVertexAndProfile(MapPoint mapPoint)
        {
            var idx = HamlTool.InsertAndPlaceVertex(mapPoint);
            Profile p;
            
            if (HamlTool.IsProfileLearnerTrained() && _useLearner)
            {
                p = HamlTool.InsertProfileAndGuessPlacements(idx);    
            }
            else
            {
                p = HamlTool.InsertProfile(idx);
                HamlTool.SetDuneAlgoPlacements(p);
                HamlTool.ActiveProfile = p;
            }
            
        }
        
        protected override async void OnToolMouseMove(MapViewMouseEventArgs e)
        {
            await QueuedTask.Run(() => { OnToolMouseMoveAsync(e); });
        }

        protected override Task HandleMouseUpAsync(MapViewMouseButtonEventArgs e)
        {
            return QueuedTask.Run(() =>
            {
                if (sketching) return;

                if (dragState == DragState.Shore)
                {
                    UpdateDistEvent.Publish(NoArgs.Instance);
                }
              
                // mouse up indicates that we are no longer moving a vertex/no vertex is currently selected
                dragState = DragState.None;
                _selectedPointIndex = null;
            });
        }

        protected void VerifyAndSave(Profile p)
        {
            bool ok = true;
            if (!HamlTool.VerifyPOIOrder(p))
            {
                string message = "Annotations are either out of order or on the wrong side of the shoreline. " +
                                 "Are you sure you want to continue?";
                string caption = "Annotation Warning";
                ok = GUI.ShowMessageBox(message, caption, true);
            }

            if (ok)
            {
                OnSave(p);
            }
        }
        
        protected void VerifyAndSaveProfiles(List<Profile> profiles)
        {
            bool ok = true;
            if (!profiles.Where(p => !p.Saved).ToList().TrueForAll(p => HamlTool.VerifyPOIOrder(p)))
            {
                string message = "Annotations are either out of order or on the wrong side of the shoreline. " +
                                 "Are you sure you want to continue?";
                string caption = "Annotation Warning";
                ok = GUI.ShowMessageBox(message, caption, true);
            }

            if (ok)
            {
                GeodatabaseUtil.SaveProfilesToGdb(profiles);
            }
        }

        private void OnSave(Profile p)
        {
            QueuedTask.Run(() =>
            {   
                GeodatabaseUtil.SaveProfilesToGdb(new List<Profile>{p});
                if (_useLearner)
                {
                    // HamlTool.TrainProfile(p);
                }
                else
                {
                    p.IsAwaitingTraining = false;
                }

                if (HamlTool.IsProfileLearnerTrained() && IsSketchTool)
                {
                    AnnotateSketchedVertices();
                }

                HamlTool.ReportAllChanges();
                UpdateVisibleProfiles();
            });
        }

        protected override void OnToolDeactivate()
        {
            _polySide1 = null;
            _polySide2 = null;
            _visibleProfiles.Clear();
            _thisSide = null;

            if (HamlTool is not null)
            {
                HamlTool.ActiveProfile = null;
                HamlTool.Unsubscribe();
                HamlTool = null;
            }

            base.OnToolDeactivate();
        }
    }
}
