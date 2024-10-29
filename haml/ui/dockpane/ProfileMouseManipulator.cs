/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using HamlProAppModule.haml.events;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.logging;
using OxyPlot;
using Serilog;

namespace HamlProAppModule.haml.ui.dockpane
{
    internal class ProfileMouseManipulator : MouseManipulator
    {
        protected  ILogger Log => LogManager.GetLogger(GetType());
        private readonly Profile _profile;
        private readonly VerticalBarSeries? _series;
        
        private List<int> _displayIndices;
        private bool _isShoreline;
        private string _id;
        private int _currentIdx;
        private int startingIndex;

        public ProfileMouseManipulator(IPlotView plotView, Profile p) : base(plotView)
        {
            _profile = p;
            _series = PlotView.ActualModel.Series.First() as VerticalBarSeries;
        }

        public override void Started(OxyMouseEventArgs e)
        {
            base.Started(e);
            
            // Use find rectangle index to allow the user to click-to-snap on white space
            var result = _series?.FindRectangleIndex(e.Position);
            if (result is null or < 0) return;

            _currentIdx = result.Value;

            GenerateDisplayIndicesList();

            _isShoreline = _currentIdx == _profile.GetVertexPointIndex();

            Delta(e);
        }

        public override void Delta(OxyMouseEventArgs e)
        {
            base.Delta(e);
            e.Handled = true;
            
            if (_series is null || _displayIndices is null) return;
            
            var maxX = (int)_series.XAxis.ActualMaximum;
            var minX = (int)_series.XAxis.ActualMinimum;
            
            // Use find rectangle index so the user can drag through whitespace
            var result = _series?.FindRectangleIndex(e.Position);
            if (result is null or < 0) return;
            
            var idx = result.Value;

            // we do not allow switching beyond the current xAxis bounds.
            if (idx < minX || idx > maxX) return;
            
            _id = GetClosestAnnotation();
            startingIndex = _profile.Annotations[_id];

            if (!_isShoreline)
            {
                _profile.AddOrUpdateAnnotationPoint(_id, idx);
            } else if (_isShoreline)
            {
                _profile.MoveVertex(idx);
            }

            _displayIndices.Remove(_currentIdx);
            _currentIdx = idx;
            _displayIndices.Add(_currentIdx);
                
            PlotView.InvalidatePlot(false);
        }

        public override void Completed(OxyMouseEventArgs e)
        {
            base.Completed(e);
            e.Handled = true;
            var shouldLog = startingIndex != _currentIdx;
            var startPoint = _profile.Points[startingIndex];
            var endPoint = _profile.Points[_currentIdx];
            var pointType = _isShoreline ? "shoreline" : _id;
            
            if (shouldLog)
            {
                if (_isShoreline) UpdateDistEvent.Publish(NoArgs.Instance);
                
                Log.Here().Debug(
                    "Profile {@ID} {@PointType} Point updated from idx {@StartIdx} ({@StartX},{@StartY}) to {@EndIdx} ({@EndX},{@EndY})",
                    _profile.Id, pointType, startingIndex, startPoint.X.ToString("F1"), startPoint.Y.ToString("F1"),
                    _currentIdx, endPoint.X.ToString("F1"), endPoint.Y.ToString("F1"));
            } 
            
            PlotView.InvalidatePlot();
        }

        private void GenerateDisplayIndicesList()
        {
            _displayIndices = new List<int>();
            foreach (var kvp in _profile.Annotations)
            {
                _displayIndices.Add(kvp.Value);
            }
            
            _displayIndices.Add(_profile.GetVertexPointIndex());
        }

        private string GetClosestAnnotation()
        {
            var dist = int.MaxValue;
            var smallestDist = int.MaxValue;
            string ret = String.Empty;

            foreach (var kvp in _profile.Annotations)
            {
                dist = Math.Abs(_currentIdx - kvp.Value);

                if (dist < smallestDist)
                {
                    smallestDist = dist;
                    ret = kvp.Key;
                }
            }

            return ret;
        }
    }
}
