/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.events;
using HamlProAppModule.haml.ui;
using HamlProAppModule.haml.util;

namespace HamlProAppModule.haml.geometry
{
    public class Profile
    {
        public const string High = "high";
        public const string Low = "low";
        private bool _isBerm;
        private bool _isAwaitingTraining;
        private bool _ignored;

        private int _vertexPointIdx;
        private List<int> _nonNaNIndices = new List<int>();
        private LeftOrRightSide _labelSide;
        
        private Polyline _arcTransectPolyline;
        public Polyline ArcTransectPolyline => _arcTransectPolyline ??= GetArcTransectPolyline();
        private Polyline p;
        private Raster r;
        private SpatialReference _reference;

        private List<List<double>> classifications; 
        
        // data structures to store points of the profile in various aspects
        public ReadOnlyCollection<Coordinate3D> Points => _points ??= GetPoints();
        private ReadOnlyCollection<Coordinate3D>? _points;

        private int _originalShorelineIdx;

        public int groundHighIdx { get; set; }
        public int groundLowIdx { get; set; }

        public Profile(Polyline p, Raster r, Vertex v, LeftOrRightSide labelSide, List<Coordinate3D>? points = null)
        {
            _labelSide = labelSide;
            _vertexPointIdx = -1;
            Vertex = v;
            Id = CreateProfileName();

            // find the intersection points, then create an arcpro polyline containing said points
            this.p = p;
            this.r = r;
            _reference = QueuedTask.Run(()=>r.GetSpatialReference()).Result;

            if (points != null)
            {
                _points = points.AsReadOnly();
            }

            Annotations = new Dictionary<string, int>();
            OriginalAnnoIdx = new Dictionary<string, int>
            {
                {High, -1},
                {Low, -1}
            };
            _isBerm = false;
            _isAwaitingTraining = true;

            LowOid = -1;
            HighOid = -1;
            ShoreOid = -1;
        }

        private static string CreateProfileName()
        {
            String prefix = "profile_";
            String suffix = DateTime.Now.ToString("hmmssff");
            String ret = prefix + suffix;
            return ret;
        }

        public MapPoint PointAsMapPoint(int idx)
        {
            return Points[idx].ToMapPoint(MapView.Active.Map.SpatialReference);
        }

        private ReadOnlyCollection<Coordinate3D> GetPoints()
        {
            if (_points == null)
            {
                _points = RasterUtil.CreateIntersectionMapCoordinates(r, p, out _nonNaNIndices).AsReadOnly();
            }

            return _points;
        }

        private Polyline GetArcTransectPolyline()
        {
            var sr = _reference;
            return PolylineBuilderEx.CreatePolyline(Points, sr);
        }

        public Vertex Vertex { get; set; }

        public double CalcShorelineDist(int idx)
        {
            var ret = Math.Round(GeomUtil.CalcDist(Points[idx], Vertex.GetPoint().Coordinate3D, Vertex.GetPoint().SpatialReference), 2);
            
            // the goal is to have landward-side values have negative distance in the profile view
            if (idx > _vertexPointIdx && _labelSide == LeftOrRightSide.LeftSide
                || idx < _vertexPointIdx && _labelSide == LeftOrRightSide.RightSide)
            {
                ret = -ret;
            }

            return ret;
        }
        
        // Adds or updates an annotation point and sends a property change message
        public void AddOrUpdateAnnotationPoint(string id, int idx)
        {
            // Update annotation if index doesn't belong to another annotation
            if (Annotations.ContainsKey(id) && !Annotations.Values.Contains(idx))
            {
                var oldIdx = Annotations[id]; 
                var newIdx = Annotations[id] = idx;
                PointMovedEvent.Publish(new PointMovedEventArgs
                {
                    Sender = this,
                    NewIndex = newIdx,
                    OldIndex = oldIdx,
                    Type = id == High ? HamlGraphicType.HighPoints : HamlGraphicType.LowPoints
                });
            }
            else if (!Annotations.Values.Contains(idx))
            {
                Annotations.Add(id, idx);
                PointMovedEvent.Publish(new PointMovedEventArgs
                {
                    Sender = this,
                    NewIndex = -1,
                    OldIndex = -1,
                    Type = id == High ? HamlGraphicType.HighPoints : HamlGraphicType.LowPoints
                });
            }
        }

        public void RemoveAnnotation(string id)
        {
            if (Annotations.Remove(id))
            {
                PointMovedEvent.Publish(new PointMovedEventArgs
                {
                    NewIndex = -1,
                    OldIndex = -1,
                    Type = id == High ? HamlGraphicType.HighPoints : HamlGraphicType.LowPoints
                });
            }
        }

        // We know the profile has been saved if either of these are set
        public bool Saved => LowOid != -1 || HighOid != -1;

        public int GetVertexPointIndex()
        {
            if (_vertexPointIdx >= 0) return _vertexPointIdx;
            
            var result = GeometryEngine.Instance.NearestVertex(ArcTransectPolyline, Vertex.GetPoint())
                .PointIndex;
            _vertexPointIdx = result ?? -1;

            return _vertexPointIdx;
        }

        public void MoveVertex(int newIdx)
        {
            MoveVertex(newIdx, false);
        }

        private void MoveVertex(int newIDx, bool reset)
        {
            // We don't want to move a vertex on top of an annotation
            if (Annotations.Values.Contains(newIDx)) return;
            var oldIdx = _vertexPointIdx;
            var p = Points[newIDx]; 
            Vertex.Set(p.ToMapPoint(MapView.Active.Map.SpatialReference), false);
            _vertexPointIdx = newIDx;
            
            if (reset) return;
            
            PointMovedEventArgs args = new PointMovedEventArgs(oldIdx, _vertexPointIdx)
            {
                Sender = this,
                OldIndex = oldIdx,
                NewIndex = _vertexPointIdx,
                Type = HamlGraphicType.Vertex
            };
            PointMovedEvent.Publish(args);
        }

        public void SetOriginalValues()
        {
            OriginalAnnoIdx.Clear();
            foreach (var keyValuePairs in Annotations)
            {
                OriginalAnnoIdx[keyValuePairs.Key] = keyValuePairs.Value;
            }

            _originalShorelineIdx = GetVertexPointIndex();

            OriginalBerm = IsBerm;
            OriginalIgnored = Ignored;
        }

        public void Reset()
        {
            Annotations.Clear();
            foreach (var kvp in OriginalAnnoIdx)
            {
                AddOrUpdateAnnotationPoint(kvp.Key, kvp.Value);
            }

            MoveVertex(_originalShorelineIdx, true);
        }

        // todo make this readonly. Only profile should be able to edit it.
        public Dictionary<string, int> Annotations { get; }

        // object id of row in database
        public int LowOid { get; set; }
        public int HighOid { get; set; }
        public int ShoreOid { get; set; }
        public bool Edited => LowIdx != OriginalAnnoIdx[Low]
                              || HiIdx != OriginalAnnoIdx[High]
                              || _vertexPointIdx != _originalShorelineIdx
                              || OriginalIgnored != Ignored
                              || OriginalBerm != IsBerm;

        public int LowIdx => Annotations.ContainsKey(Low) ? Annotations[Low] : -1;

        public int HiIdx => Annotations.ContainsKey(High) ? Annotations[High] : -1;

        public string Id { get; }

        public int? GetNearestProfilePointIndex(MapPoint queryPoint)
        {
            return GeometryEngine.Instance.NearestVertex(ArcTransectPolyline, queryPoint).PointIndex;
        }
        public bool IsBerm
        {
            get => _isBerm;
            set => _isBerm = value;
        }

        // TODO: In the future, we might replace this flag with a running list of profiles remaining to be trained,
        // TODO: for performance reasons.
        public bool IsAwaitingTraining
        {
            get => _isAwaitingTraining;
            set => _isAwaitingTraining = value;
        }

        public Dictionary<string, int> OriginalAnnoIdx { get; }

        public bool Ignored
        {
            get => _ignored;
            set
            {
                _ignored = value;
                IgnoreProfileEvent.Publish(this);
            }
        }
        
        public bool OriginalIgnored { get; set; }
        
        public bool OriginalBerm { get; set; }

        public void RemoveNaN()
        {
            if (_nonNaNIndices.Count < 1)
            {
                RasterUtil.CreateIntersectionMapCoordinates(r, p, out _nonNaNIndices).AsReadOnly();
            }
            
            var pointsWithoutNaN = new List<Coordinate3D>();

            for (int i = 0; i < _nonNaNIndices.Count; i++)
            {
                if (_nonNaNIndices[i] < _points.Count && _nonNaNIndices[i] >= 0)
                {
                    pointsWithoutNaN.Add(_points[_nonNaNIndices[i]]);
                }
            }

            _points = pointsWithoutNaN.AsReadOnly();
        }
        public List<List<double>> Classifications { get => classifications; set => classifications = value; }
    }
}
