/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Internal.Mapping.Locate;
using HamlProAppModule.haml.util;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace HamlProAppModule.haml.ui.Experiment;

public class ExperimentDockPaneViewModel : DockPane
{
    private static ExperimentDockPaneViewModel? _pane;
    private const string DockPaneId = "ExperimentDockPane";
    private Dictionary<ExperimentStat, object> _stats;
    private PlotModel _history;
    private List<ExperimentStat> _plotOptions;
    private ExperimentStat _stat1;
    private ExperimentStat _stat2;

    public Dictionary<ExperimentStat, object> Stats
    {
        get => _stats;
        set => SetProperty(ref _stats, value, () => Stats);
    }
    
    public PlotModel History
    {
        get => _history;
        set => SetProperty(ref _history, value, () => History);
    }
    
    public List<ExperimentStat> PlotOptions
    {
        get => _plotOptions;
        set => SetProperty(ref _plotOptions, value, () => PlotOptions);
    }
    
    public ExperimentStat Stat1
    {
        get => _stat1;
        set
        {
            SetProperty(ref _stat1, value, () => Stat1);
            OnStatSelected();
        }
    }

    public ExperimentStat Stat2
    {
        get => _stat2;
        set
        {
            SetProperty(ref _stat2, value, () => Stat2);
            OnStatSelected();
        }
    }

    public static ExperimentDockPaneViewModel? Pane => _pane ??= 
        FrameworkApplication.DockPaneManager.Find(DockPaneId) as ExperimentDockPaneViewModel;
    
    internal static void Show()
    {
        if (Pane is null || Module1.StatTracker.History.Count < 1) return;
        Pane.Stats = Module1.StatTracker.Current.Stats;

        GUI.RunOnUiThread(() =>
        {
            Pane.Activate();
            Pane.OnStatSelected();
        });
    }

    public ExperimentDockPaneViewModel()
    {
        PlotOptions = Enum.GetValues<ExperimentStat>()
            .Where(e => e.DefaultValue() is not bool).ToList();
        if (Stat1 != ExperimentStat.PlacementIsMl && Stat2 != ExperimentStat.PlacementIsMl) return;
        Stat1 = ExperimentStat.CumulativeHighPtCorrectionDistance;
        Stat2 = ExperimentStat.CumulativeLowPtCorrectionDistance;
    }

    private void BuildHistoryPlot(ExperimentStat stat1, ExperimentStat stat2)
    {
        if (stat1.DefaultValue() is bool || stat2.DefaultValue() is bool) return;
        History = new PlotModel();
        History.Background = OxyColors.White;

        List<double> data1 = new List<double>();
        List<double> data2 = new List<double>();
        data1 = Module1.StatTracker.GetHistory<double>(stat1);
        data2 = Module1.StatTracker.GetHistory<double>(stat2);

        var maxRange = data2.Max() > data1.Max() ? data2.Max()+5 : data1.Max()+5;
        var minRange = data2.Min() < data1.Min() ? data2.Min()-5 : data1.Min()-5;
        
        History.Legends.Add(new Legend
        {
            LegendTitle = "Legend",
            LegendPosition = LegendPosition.BottomRight
        });
        
        History.Axes.Add(new LinearAxis
        {
            Title = "Profiles Inserted",
            Position = AxisPosition.Bottom,
            Minimum = 1,
            Maximum = data1.Count
        });
        
        History.Axes.Add(new LinearAxis
        {
            Title = Enum.GetName(stat1),
            Position = AxisPosition.Left,
            Key = "Left",
            Minimum = minRange,
            Maximum = maxRange
        });
        
        History.Axes.Add(new LinearAxis
        {
            Title = Enum.GetName(stat2),
            Position = AxisPosition.Right,
            Key = "Right",
            Minimum = minRange,
            Maximum = maxRange
        });
        
        History.Series.Add(BuildSeries(data1, "Left"));
        History.Series.Add(BuildSeries(data2, "Right"));
        
        History.InvalidatePlot(false);
    }
    
    private LineSeries BuildSeries(List<double> data, string yAxisKey)
    {
        var series = new LineSeries();
        data.ForEachWithIndex((d, i) => series.Points.Add(new DataPoint(i, d)));
        series.YAxisKey = yAxisKey;
        series.Color = yAxisKey.Equals("Left") ? OxyColors.Green : OxyColors.Red;
        series.Title = yAxisKey.Equals("Left") ? Enum.GetName(Stat1) : Enum.GetName(Stat2);
        return series;
    }
    
    private void OnStatSelected()
    {
        if(_stats != null && _stats.Any()){
            BuildHistoryPlot(Stat1, Stat2);
        }
    }

    public static void Save()
    {
        if (Pane is null || Module1.StatTracker.History.Count < 1) return;
        //TODO
        Pane.SavePlot();
    }

    private void SavePlot()
    {
        var pngExporter = new PngExporter { Width = 1200, Height = 800 }; //TODO: maybe make this into an experiment option
        pngExporter.ExportToFile(History, "out\\plot.png"); //TODO: put experiment outputs into unique folders
    }

    public static void CompleteExperiment(String dir)
    {
        if (Pane is null || Module1.StatTracker.History.Count < 1) return;

        Pane.SaveConclusionPlots(dir);
    }

    private void SaveConclusionPlots(String dir)
    {
        var pngExporter = new PngExporter { Width = 1200, Height = 800 };

        Stat1 = ExperimentStat.MeanHighPtCorrectionDistance;
        Stat2 = ExperimentStat.MeanLowPtCorrectionDistance;
        BuildHistoryPlot(Stat1, Stat2);
        pngExporter.ExportToFile(History, $"{dir}\\mean correction distance.png");
        
        Stat1 = ExperimentStat.HighPtCorrectionDistance;
        Stat2 = ExperimentStat.LowPtCorrectionDistance;
        BuildHistoryPlot(Stat1, Stat2);
        pngExporter.ExportToFile(History, $"{dir}\\correction distance.png");
        
        Stat1 = ExperimentStat.GroundTruthFrechetDistanceHigh;
        Stat2 = ExperimentStat.GroundTruthFrechetDistanceLow;
        BuildHistoryPlot(Stat1, Stat2);
        pngExporter.ExportToFile(History, $"{dir}\\frechet.png");
    }

    public static void CreateAnnotationCurvePlots(List<(Polyline, string, bool)> bundleList, String dir, String plotName)
    {
        List<(List<(double, double)>, string, bool)> plotBundle = new List<(List<(double, double)>, string, bool)>();
        
        List<double> individualMaxX = new List<double>();
        List<double> individualMinX = new List<double>();
        List<double> individualMaxY = new List<double>();
        List<double> individualMinY = new List<double>();

        // process into raw data
        foreach ((Polyline, string, bool) bundle in bundleList)
        {
            Polyline polyline = bundle.Item1;
            string name = bundle.Item2;
            bool useMarkers = bundle.Item3;

            List<(double, double)> rawData = new List<(double, double)>();

            foreach (var mapPoint in polyline.Points)
            {
                rawData.Add((mapPoint.X, mapPoint.Y));
            }
            
            individualMaxX.Add(rawData.Max(tup=>tup.Item1)); // extract the maxX from this set
            individualMinX.Add(rawData.Min(tup=>tup.Item1));
            individualMaxY.Add(rawData.Max(tup=>tup.Item2));
            individualMinY.Add(rawData.Min(tup=>tup.Item2));
            
            // shouldnt need Distinct here.. but dupe vertices are coming from GetHighPointsAsPolyline
            plotBundle.Add((rawData.Distinct().ToList(), name, useMarkers)); 
        }
        
        double dataMaxX = individualMaxX.Max()+25;
        double dataMinX = individualMinX.Min()-25;
        double dataMaxY = individualMaxY.Max()+25;
        double dataMinY = individualMinY.Min()-25;
        PlotModel plotModel = new PlotModel();
        plotModel.Title = plotName;
        plotModel.TitleFontSize = 24;
        plotModel.Background = OxyColors.White;

        plotModel.Axes.Add(new LinearAxis
        {
            Title = "X",
            Position = AxisPosition.Bottom,
            Minimum = dataMinX,
            Maximum = dataMaxX,
            IsAxisVisible = false
        });
        
        plotModel.Axes.Add(new LinearAxis
        {
            Title = "Y",
            Position = AxisPosition.Left,
            Key = "Left",
            Minimum = dataMinY,
            Maximum = dataMaxY,
            IsAxisVisible = false
        });
        
        plotModel.Legends.Add(new Legend
        {
            LegendTitle = "Legend",
            LegendFontSize = 20,
            LegendPosition = LegendPosition.BottomRight
        });
        
        foreach (var bundle in plotBundle)
        {
            List<(double,double)> rawData = bundle.Item1;
            string name = bundle.Item2;
            bool useMarkers = bundle.Item3;
            
            var series = new LineSeries();
            
            if(useMarkers){
                series.StrokeThickness = 1;
                if (name.Contains("optimal"))
                {
                    series.MarkerType = MarkerType.Diamond;
                    series.MarkerSize = 7;
                }else{
                    series.MarkerType = MarkerType.Circle;
                    series.MarkerSize = 5;
                }
            }
            else
            {
                series.StrokeThickness = 3;
            }
            series.Title = $"{name} vertices={rawData.Count}";
            
            rawData.ForEach(tup => series.Points.Add(new DataPoint(tup.Item1, tup.Item2)));
            plotModel.Series.Add(series);
        }
        
        var pngExporter = new PngExporter { Width = 1200, Height = 800};
        pngExporter.ExportToFile(plotModel, $"{dir}\\{plotName}.png");
    }

    public static void CreateHeightProfilePlots(List<(List<(double,double)>, string, bool)> bundleList, String dir, String plotName)
    {
        List<(List<(double,double)>, string, bool)> plotBundle = new List<(List<(double,double)>, string, bool)>();
        
        List<double> individualMaxZ = new List<double>();

        // process into raw data
        foreach ((List<(double,double)>, string, bool) bundle in bundleList)
        {
            List<(double,double)> rawData = bundle.Item1;
            string name = bundle.Item2;
            bool useMarkers = bundle.Item3;

            individualMaxZ.Add(rawData.Max(tup => tup.Item2));

            // shouldnt need Distinct here.. but dupe vertices are coming from GetHighPointsAsPolyline
            plotBundle.Add((rawData.Distinct().ToList(), name, useMarkers)); 
        }
        
        double dataMaxZ = individualMaxZ.Max()+0.25;
        double dataMaxX = bundleList.First().Item1.Last().Item1 + 20;

        PlotModel plotModel = new PlotModel();
        plotModel.Title = plotName;
        plotModel.TitleFontSize = 24;
        plotModel.Background = OxyColors.White;

        plotModel.Axes.Add(new LinearAxis
        {
            Title = "Across Shore Distance (m)",
            Position = AxisPosition.Bottom,
            FontSize = 20,
            Minimum = 0,
            Maximum = dataMaxX
        });
        
        plotModel.Axes.Add(new LinearAxis
        {
            Title = "Elevation (m)",
            Position = AxisPosition.Left,
            Key = "Left",
            FontSize = 20,
            Minimum = 0,
            Maximum = dataMaxZ
        });
        
        plotModel.Legends.Add(new Legend
        {
            LegendTitle = "Legend",
            LegendFontSize = 20,
            LegendPosition = LegendPosition.BottomRight
        });
        
        foreach (var bundle in plotBundle)
        {
            List<(double,double)> rawData = bundle.Item1;
            string name = bundle.Item2;
            bool useMarkers = bundle.Item3;
            
            var series = new LineSeries();
            
            if(useMarkers){
                series.StrokeThickness = 1;
                series.MarkerType = MarkerType.Circle;
                series.MarkerSize = 3;
            }
            else
            {
                series.StrokeThickness = 3;
            }
            series.Title = $"{name}";
            
            for (int i = 0; i < rawData.Count; i++)
            {
                series.Points.Add(new DataPoint(rawData[i].Item1, rawData[i].Item2));
            }
            plotModel.Series.Add(series);
        }
        
        var pngExporter = new PngExporter { Width = 1200, Height = 800};
        pngExporter.ExportToFile(plotModel, $"{dir}\\{plotName}.png");
    }

    public static void CreateHeightErrorPlot(List<(List<(double,double)>, string, bool)> bundleList, String dir, String plotName)
    {
        List<(List<(double,double)>, string, bool)> plotBundle = new List<(List<(double,double)>, string, bool)>();
        
        List<double> individualMaxZ = new List<double>();

        // process into raw data
        foreach ((List<(double,double)>, string, bool) bundle in bundleList)
        {
            List<(double,double)> rawData = bundle.Item1;
            string name = bundle.Item2;
            bool useMarkers = bundle.Item3;

            individualMaxZ.Add(rawData.Max(tup => tup.Item2));

            // shouldnt need Distinct here.. but dupe vertices are coming from GetHighPointsAsPolyline
            plotBundle.Add((rawData.Distinct().ToList(), name, useMarkers)); 
        }
        
        double dataMaxX = bundleList.First().Item1.Last().Item1;

        PlotModel plotModel = new PlotModel();
        plotModel.Title = plotName;
        plotModel.TitleFontSize = 24;
        plotModel.Background = OxyColors.White;

        plotModel.Axes.Add(new LinearAxis
        {
            Title = "Distance along shore (m)",
            Position = AxisPosition.Bottom,
            Key = "Bottom",
            FontSize = 20,
            Minimum = 0,
            Maximum = dataMaxX
        });

        plotModel.Axes.Add(new LinearAxis
        {
            Title = "Elevation Error(m)",
            Position = AxisPosition.Left,
            Key = "Left",
            FontSize = 20,
            Minimum = 0,
            Maximum = 1.5
        });
        
        plotModel.Legends.Add(new Legend
        {
            LegendTitle = "Legend",
            LegendFontSize = 20,
            LegendPosition = LegendPosition.TopRight
        });
        
        foreach (var bundle in plotBundle)
        {
            List<(double,double)> rawData = bundle.Item1;
            string name = bundle.Item2;
            bool useMarkers = bundle.Item3;
            
            var series = new LineSeries();

            series.StrokeThickness = 3;
            
            if(useMarkers){
                series.Color = OxyColor.FromRgb(0,255,0);
            }
            else
            {
                series.Color =  OxyColor.FromRgb(255,0,0);
            }
            series.Title = $"{name}";
            
            for (int i = 0; i < rawData.Count; i++)
            {
                series.Points.Add(new DataPoint(rawData[i].Item1, rawData[i].Item2));
            }
            plotModel.Series.Add(series);
        }

        var halfMeterGuideSeries = new LineSeries();
        halfMeterGuideSeries.StrokeThickness = 0.5;
        halfMeterGuideSeries.LineStyle = LineStyle.Dash;
        halfMeterGuideSeries.Color = OxyColor.FromRgb(0,0,0);
        var oneMeterGuideSeries = new LineSeries();
        oneMeterGuideSeries.StrokeThickness = 0.5;
        oneMeterGuideSeries.LineStyle = LineStyle.Dash;
        oneMeterGuideSeries.Color = OxyColor.FromRgb(0,0,0);
        for (int i = 0; i < plotBundle.First().Item1.Count; i++)
        {
            halfMeterGuideSeries.Points.Add(new DataPoint(plotBundle.First().Item1[i].Item1, 0.5));
            oneMeterGuideSeries.Points.Add(new DataPoint(plotBundle.First().Item1[i].Item1, 1));
        }
        plotModel.Series.Add(halfMeterGuideSeries);
        plotModel.Series.Add(oneMeterGuideSeries);
        
        var pngExporter = new PngExporter { Width = 1200, Height = 800};
        pngExporter.ExportToFile(plotModel, $"{dir}\\{plotName}.png");
    }
}
