/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using HamlProAppModule.haml.ui.Experiment;

namespace HamlProAppModule.haml.ui.experiment;

public class ExperimentStatTracker
{
    private List<Snapshot> _history;
    private Dictionary<ExperimentStat, object> _currentStats;

    public ExperimentStatTracker()
    {
        _history = new List<Snapshot>();
        _currentStats = new Dictionary<ExperimentStat, object>();
        Enum.GetValues<ExperimentStat>()
            .ToList()
            .ForEach( v => _currentStats.Add(v, v.DefaultValue()));
    }
    
    public Snapshot CreateSnapshot()
    {
        var currentSnapshot = new Snapshot
        {
            Stats = _currentStats.ToDictionary(kvp=>kvp.Key, kvp=>kvp.Value)
        };
        
        // Update Cumulative and Mean Stats
        if(History.Count > 0){
            // ML Placement Count
            int increment = (bool) _currentStats[ExperimentStat.PlacementIsMl] ? 1 : 0;
            currentSnapshot.Stats[ExperimentStat.CumulativeMLPlacements] =
                (int)GetHistory(ExperimentStat.CumulativeMLPlacements)[^1] + increment;
            
            // Unnecessary Insertion Count
            increment = (bool) _currentStats[ExperimentStat.InsertionWasUnnecessary] ? 1 : 0;
            currentSnapshot.Stats[ExperimentStat.CumulativeUnnecessaryInsertions] =
                (int)GetHistory(ExperimentStat.CumulativeUnnecessaryInsertions)[^1] + increment;

            // High Point Absolute Correction Distance
            // Cumulative
            currentSnapshot.Stats[ExperimentStat.CumulativeHighPtCorrectionDistance] =
                Math.Abs((double) GetHistory(ExperimentStat.CumulativeHighPtCorrectionDistance)[^1]) +
                Math.Abs((double) _currentStats[ExperimentStat.HighPtCorrectionDistance]);
            // Mean
            currentSnapshot.Stats[ExperimentStat.MeanHighPtCorrectionDistance] =
                Math.Round(Math.Abs((double) GetHistory(ExperimentStat.CumulativeHighPtCorrectionDistance)[^1]) / History.Count,2);
            
            // Low Point Absolute Correction Distance
            // Cumulative
            currentSnapshot.Stats[ExperimentStat.CumulativeLowPtCorrectionDistance] =
                Math.Abs((double) GetHistory(ExperimentStat.CumulativeLowPtCorrectionDistance)[^1]) +
                Math.Abs((double) _currentStats[ExperimentStat.LowPtCorrectionDistance]);
            // Mean
            currentSnapshot.Stats[ExperimentStat.MeanLowPtCorrectionDistance] =
                Math.Round(Math.Abs((double) GetHistory(ExperimentStat.CumulativeLowPtCorrectionDistance)[^1]) / History.Count,2);
        }

        return currentSnapshot;
    }

    // Snapshot the current state of the experiment before new profiles are inserted
    public void SaveCurrentSnapshot()
    {
        _history.Add(CreateSnapshot());
        RestoreDefaultValues();
    }
    
    public List<Snapshot> History => _history;

    public Snapshot Latest => _history.Last();

    public Snapshot Current => CreateSnapshot();

    public void UpdateStat(ExperimentStat statType, object value)
    {
        if (_currentStats.ContainsKey(statType))
        {
            if (value is int or double or long or float && statType.DefaultValue() is double)
            {
                _currentStats[statType] = Convert.ToDouble(value);
            } else if (value.GetType() == statType.DefaultValue().GetType())
            {
                _currentStats[statType] = value;
            }
        }
        
        ExperimentDockPaneViewModel.Show();
    }
    
    public List<T> GetHistory<T>(ExperimentStat stat) => _history.Select(snap => 
        (T)snap.Stats[stat]).ToList();
    
    public List<object> GetHistory(ExperimentStat stat) => _history.Select(snap => 
        snap.Stats[stat]).ToList();
    

    // Reset all of the per-profile stats
    private void RestoreDefaultValues()
    {
        _currentStats.Keys.ToList().ForEach(k => _currentStats[k] = k.DefaultValue());
        
        if(History.Count > 0)
        {
            _currentStats[ExperimentStat.GroundTruthFrechetDistanceHigh] =
                Latest.Stats[ExperimentStat.GroundTruthFrechetDistanceHigh];
            _currentStats[ExperimentStat.GroundTruthFrechetDistanceLow] =
                Latest.Stats[ExperimentStat.GroundTruthFrechetDistanceLow];
        }
    }
}
