/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ArcGIS.Core.Events;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using HamlProAppModule.haml.events;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.logging;
using HamlProAppModule.haml.ui.options;
using HamlProAppModule.haml.util;
using OxyPlot;
using OxyPlot.Axes;
using Serilog;

namespace HamlProAppModule.haml.ui.dockpane
{
     internal class ProfileDockPaneViewModel : DockPane
    {
        private static ProfileDockPaneViewModel? _pane;
        private const string DockPaneId = "ProfileDockPane";
        private const string _noProfilesMsg = "No Available Profiles";
        
        protected  ILogger Log => LogManager.GetLogger(GetType());
        private PlotModel _profileModel;
        private PlotController _controller;
        private VerticalBarSeries series;
        private Visibility _profileVisibility = Visibility.Collapsed;
        private bool _cbBermChecked;
        private bool _cbIgnoreChecked;
        private Profile _p;
        private ICommand _saveCommand;
        private ICommand _bermCommand;
        private ICommand _resetCommand;
        private ICommand _ignoreCommand;
        private Dictionary<SubscriptionToken, Action<SubscriptionToken>> _subDict;
        public static SubscriptionToken Token { get;} = ActiveProfileChangedEvent.Subscribe(Show, true);

        private static ProfileDockPaneViewModel? Pane => _pane ??= 
            FrameworkApplication.DockPaneManager.Find(DockPaneId) as ProfileDockPaneViewModel;
        
        internal static void Show(Profile? profile)
        {
            GUI.RunOnUiThread(() =>
            {
                if (Pane is null) return;
                if (profile is not null)
                {
                    Pane.Activate();
                    Pane.BuildProfileGraph(profile);
                    Pane.ProfileModel.InvalidatePlot(true);
                    Pane.ProfileVisibility = Visibility.Visible;
                    Pane.IsBermChecked = profile.IsBerm;
                    Pane.IsIgnoreChecked = profile.Ignored;
                }
                else
                {
                    Pane.ProfileModel = new PlotModel();
                    Pane.ProfileVisibility = Visibility.Collapsed;
                }
            });
        }
        
        internal static (int, int) GetGraphMinMaxX()
        {
            if (Pane is null) return (-1, -1);
            int minX = (int)Pane.ProfileModel.Axes[0].ActualMinimum;
            int maxX = (int)Pane.ProfileModel.Axes[0].ActualMaximum;

            return (minX, maxX);
        }
        
        //Both ProfileModel and Controller must be public so the xaml bindings work
        public PlotModel ProfileModel
        {
            get => _profileModel;
            set => SetProperty(ref _profileModel, value, () => ProfileModel);
        }

        public Visibility ProfileVisibility
        {
            get => _profileVisibility;
            set => SetProperty(ref _profileVisibility, value, () => ProfileVisibility);
        }
        
        public bool IsBermChecked
        {
            get => _cbBermChecked;
            set => SetProperty(ref _cbBermChecked, value, () => IsBermChecked);
        }
        
        public bool IsIgnoreChecked
        {
            get => _cbIgnoreChecked;
            set => SetProperty(ref _cbIgnoreChecked, value, () => IsIgnoreChecked);
        }
        
        public string NoProfileMsg => _noProfilesMsg;

        public PlotController Controller
        {
            get => _controller; 
            set => SetProperty(ref _controller, value, () => Controller);
        }
        
        public ICommand SaveCommand => _saveCommand ??= new RelayCommand(OnSave);
        
        public ICommand BermCommand => _bermCommand ??= new RelayCommand(OnBerm);

        public ICommand ResetCommand => _resetCommand ??= new RelayCommand(OnReset);

        public ICommand IgnoreCommand => _ignoreCommand ??= new RelayCommand(OnIgnore);

        private void OnSave()
        {
            Log.Here().Information("Save button pressed for profile {@Profile}", _p.Id);
            SaveProfileEvent.Publish(_p);
        }
        
        private void OnBerm()
        {
            _p.IsBerm = !_p.IsBerm;

            if (_p.IsBerm)
            {
                _p.RemoveAnnotation(Profile.Low);
            }
            else
            {
                _p.AddOrUpdateAnnotationPoint(Profile.Low,
                    _p.OriginalAnnoIdx[Profile.Low]);
            }
            
            Log.Here().Debug("Profile {@Profile} has changed Berm status from {@Old} to {@New}", 
                _p.Id, !_p.IsBerm, _p.IsBerm);
            
            RebuildSeries();
        }

        private void OnReset()
        {
            _p.Reset();
            Log.Here().Information("Profile {@Profile} reset to original values", _p.Id);
            ProfileModel.ResetAllAxes();
            RebuildSeries();
            SetMaxAndMin(series.XAxis, series.YAxis);
        }

        private void OnIgnore()
        {
            _p.Ignored = !_p.Ignored;
            
            Log.Here().Debug("Profile {@Profile} has changed Ignored status from {@Old} to {@New}", 
                _p.Id, !_p.Ignored, _p.Ignored);
            RebuildSeries();
        }

        private void RebuildSeries()
        {
            RebuildSeries(NoArgs.Instance);
        }
        
        private void RebuildSeries(NoArgs eventArgs)
        {
            ProfileModel.Series.Clear();
            BuildSeries();
            ProfileModel.Series.Add(series);
            _profileModel.InvalidatePlot(false);
        }

        private void BuildProfileGraph(Profile p)
        {
            _p = p;
            // Set new properties
            ProfileModel = new PlotModel();
            Controller = new PlotController();
            
            _profileModel.Background = OxyColor.Parse("#f7f8f8");
            _profileModel.PlotAreaBackground = OxyColors.White;
            
            // Set series
            BuildSeries();

            // calculate a step value for the y-axis labeling that attempts to label approx. 5 integer values
            var zVals = _p.Points.Select(p => p.Z).ToList();
            double majorStep = Math.Round((zVals.Max() - zVals.Min()) / 5d);

            majorStep = majorStep < 1 ? 1 : majorStep; // if the y value range is small, dont let the step be 0

            //Set axes for model
            var yAxis = new LinearAxis
            {
                Title = "Z",
                MajorStep = majorStep,
                Position = AxisPosition.Left
            };
            yAxis.IsZoomEnabled = false;
            
             ProfileModel.Axes.Add(new LinearAxis {Title = "Distance to Shoreline", 
                                                       Position = AxisPosition.Bottom, 
                                                       LabelFormatter = FormatDist});
             ProfileModel.Axes.Add(yAxis);
            
            ProfileModel.Series.Add(series);
            ProfileModel.InvalidatePlot(false);
            
            // For some reason the series doesn't have access to the axes at this point
            // using axes from profile model as a work around
            // todo look into when series is given axes from profile model
            SetMaxAndMin(_profileModel.Axes[0], _profileModel.Axes[1]);
            //Set controller bindings
            Controller.BindMouseDown(OxyMouseButton.Left, OxyModifierKeys.None, 1, 
                new DelegatePlotCommand<OxyMouseDownEventArgs>((v, c, e) =>
                {
                    c.AddMouseManipulator(v, new ProfileMouseManipulator(v, _p), e);
                }));
            Controller.BindMouseDown(OxyMouseButton.Middle, PlotCommands.Track);
        }

        private string FormatDist(double val)
        {
            if (val < 0 || val > _p.Points.Count - 1)
            {
                return "";
            }

            return series.Items.GetDist((int) val).ToString("#.##");
        }

        private void BuildSeries()
        {
            series = new VerticalBarSeries();

            for (var i = 0; i < _p.Points.Count; i++)
            {
                var point = _p.Points[i];

                //TODO: will we ever not have a vertex (shoreline point) once we open a graph? 
                series.Items.Add(new VerticalBarItem(i, point.Z, 
                    _p.CalcShorelineDist(i), 
                    OxyColor.Parse(Module1.GetElementHexColor(ColorSettings.GuiElement.SelectedProfile))));
            }
            
            series.Items.SetColor(_p.GetVertexPointIndex(), Module1.GetElementHexColor(ColorSettings.GuiElement.ShorelineAnnotation));

            if(_p.Classifications is not null){
                // Color the bars by classification
                for(int j=0; j < _p.Classifications.Count; j++)
                {
                    var vector = _p.Classifications[j];
                    
                    double maxVal = Double.MinValue;
                    int maxIdx = -1;
                    
                    // find the classification by finding the index with the maximum value
                    for (int i = 0; i < vector.Count; i++)
                    {
                        if (vector[i] > maxVal)
                        {
                            maxVal = vector[i];
                            maxIdx = i;
                        }
                    }

                    switch (maxIdx)
                    {
                        case 2:
                            series.Items.SetColor(j, ColorSettings.ClassificationBlue);
                            break;
                        case 1:
                            series.Items.SetColor(j, ColorSettings.ClassificationYellow);
                            break;
                        case 0:
                            series.Items.SetColor(j, ColorSettings.ClassificationPurple);
                            break;
                    }
                }
            }
            
            foreach (var kvp in _p.Annotations)
            {
                var element = kvp.Key.Equals(Profile.High)
                    ? ColorSettings.GuiElement.HighAnnotation
                    : ColorSettings.GuiElement.LowAnnotation;
                series.Items.SetColor(kvp.Value, Module1.GetElementHexColor(element));
            }
        }

        private void SetMaxAndMin(Axis xAxis, Axis yAxis)
        {
            var zVals = _p.Points.Select(p => p.Z).ToList();
            double globalMinY = (int) Math.Floor(zVals.Min() - 1);
            double globalMaxY = (int) Math.Ceiling(zVals.Max() + 1);
            var first = series.Points[0];
            var last = series.Points[series.Points.Count - 1];

            xAxis.Maximum = Math.Max(first.X, last.X);
            xAxis.Minimum = Math.Min(first.X, last.X);
            
            yAxis.Minimum = globalMinY;
            yAxis.Maximum = globalMaxY;
        }

        private void OnPointMoved(PointMovedEventArgs args)
        {
            if (args.Sender == null || args.Sender != _p || args.OldIndex < 0 || args.NewIndex < 0) return;
            series.Items.SwitchColors(args.OldIndex, args.NewIndex);
            ProfileModel.InvalidatePlot(false);
        }

        private void OnSettingsChanged(NoArgs noArgs)
        {
            if (_p is null) return;
            RebuildSeries();
        }

        protected override void OnShow(bool isVisible)
        {
            if (isVisible)
            {
                Subscribe();
            }
            else
            {
                Unsubscribe();  
            }
            base.OnShow(isVisible);
        }

        private void Subscribe()
        {
            _subDict = new Dictionary<SubscriptionToken, Action<SubscriptionToken>>
            {
                {
                    PointMovedEvent.Subscribe(OnPointMoved, true),
                    PointMovedEvent.Unsubscribe
                },
                {
                    UpdateDistEvent.Subscribe(RebuildSeries, true),
                    UpdateDistEvent.Unsubscribe
                },
                {
                    SettingsChangedEvent.Subscribe(OnSettingsChanged, true),
                    SettingsChangedEvent.Unsubscribe
                }
            };
        }

        private void Unsubscribe()
        {
            // This is only true if the dockpane is already hidden when the application starts
            if (_subDict is null) return;
            foreach (var kvp in _subDict)
            {
                kvp.Value.Invoke(kvp.Key);
            }
        }
    }
}
