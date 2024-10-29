/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Collections.Generic;

namespace HamlProAppModule.haml.ui.Experiment;

public static class ExperimentStatExtension
{
    private static Dictionary<ExperimentStat, object> _defaultValues = new()
    {
        {ExperimentStat.PlacementIsMl, true},
        {ExperimentStat.HighPtCorrectionDistance, 0.0},
        {ExperimentStat.LowPtCorrectionDistance, 0.0},
        {ExperimentStat.GroundTruthFrechetDistanceHigh, 0.0},
        {ExperimentStat.GroundTruthFrechetDistanceLow, 0.0},
        {ExperimentStat.CumulativeMLPlacements, 0},
        {ExperimentStat.CumulativeHighPtCorrectionDistance, 0.0},
        {ExperimentStat.CumulativeLowPtCorrectionDistance, 0.0},
        {ExperimentStat.MeanHighPtCorrectionDistance, 0.0},
        {ExperimentStat.MeanLowPtCorrectionDistance, 0.0},
        {ExperimentStat.InsertionWasUnnecessary, false},
        {ExperimentStat.CumulativeUnnecessaryInsertions, 0}
    };

    public static object DefaultValue(this ExperimentStat stat) => _defaultValues[stat];
}
