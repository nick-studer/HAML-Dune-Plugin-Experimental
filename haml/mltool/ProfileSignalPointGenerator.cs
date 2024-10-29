/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.logging;
using MLPort.placement;
using Serilog;

namespace HamlProAppModule.haml.mltool
{

    public class ProfileSignalPointGenerator : SignalPointGenerator
    {
        protected ILogger Log => LogManager.GetLogger(GetType());
        
        private Profile _profile;
        private int _currentIdx;

        public override bool MoveNext()
        {
            bool hasNext = _currentIdx < _profile.Points.Count;

            if (hasNext)
            {
                List<double> featureVector = new List<double>();
                var point = _profile.Points[_currentIdx];
                var dist = Math.Abs(_profile.CalcShorelineDist(_currentIdx));

                featureVector.Add(point.Z);
                featureVector.Add(dist);
                
                _current = new SignalPoint { 
                    x = point.X,
                    y = point.Y,
                    featureVector = featureVector 
                };

                _currentIdx++;
            }
            
            return hasNext;
        }

        public Profile Profile
        {
            set
            {
                Reset();
                _profile = value;
            }
        }

        public override void Reset()
        {
            _current = default;
            _currentIdx = 0;
        }

        public override void Dispose()
        {
            _current = default;
            _currentIdx = 0;
        }
    }
}
