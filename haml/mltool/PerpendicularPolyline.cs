/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Geometry;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.logging;
using MLPort;
using GE = ArcGIS.Core.Geometry.GeometryEngine;

namespace HamlProAppModule.haml.mltool
{
    public class PerpendicularPolyline : PerpendicularGeometry
    {
        private bool sidePicked;
        private ProximityResult pr;
        private MapPoint tempAnchorPoint;
        private bool flipped;

        public PerpendicularPolyline(Raster raster, KNNLearner learner, HAMLPolyline polyline, Envelope extent) 
            : base(raster, learner, extent)
        {
            HamlGeometry = polyline;
            LabelDirSet = false;
            Learner = learner;
            Raster = raster;
            Extent = extent;
            SignalGraphics = new List<CIMGraphic>();
        }
        
        //Builds a PerpendicularPolygon from an Arc polygon.
        public PerpendicularPolyline(Raster raster,
            KNNLearner learner,
            Polyline arcPolyline,
            Envelope extent) : base(raster, learner, extent)
        {
            HamlGeometry = new HAMLPolyline(arcPolyline, extent);
            HamlGeometry.Raster = raster;
            
            LabelDirSet = false;
            Learner = learner;
            Raster = raster;
            Extent = extent;
            SignalGraphics = new List<CIMGraphic>();
        }

        public PerpendicularPolyline(Raster raster,
            KNNLearner learner,
            Envelope extent) : base(raster, learner, extent)
        {
            Learner = learner;
            Raster = raster;
            Extent = extent;
            SignalGraphics = new List<CIMGraphic>();
        }

        // Determines label by checking the side of the signal's MapPoint and what side of the polyline it falls on
        protected internal override double CheckLabel(Geometry g, MapPoint signalLocation)
        {
            LeftOrRightSide signalPointSide;
            GE.Instance.QueryPointAndDistance((Polyline) g, SegmentExtensionType.NoExtension, signalLocation, AsRatioOrLength.AsLength,
                out double _, out double _, out signalPointSide);
                
            return signalPointSide == LabelSide  ? 1.0 : 0.0;    
        }
        
        protected internal override List<int> AutoGenerateVertices(int n)
        {
            throw new NotImplementedException();
        }

        protected internal override List<int> RemoveGeneratedVertices()
        {
            throw new NotImplementedException();
        }

        // Calculates what side of the line is label, then constructs the polyline based on the sketched polyline
        protected internal override void SetLabelDir(MapPoint mp)
        {
            Polyline arcPolyline = (Polyline) HamlGeometry.GetArcGeometry();
            
            GE.Instance.QueryPointAndDistance(arcPolyline, SegmentExtensionType.NoExtension, mp, AsRatioOrLength.AsLength,
                out double _, out double _, out LeftOrRightSide side);

            LabelSide = side;
            LabelDirSet = true;
            
            Log.Here().Information("Label side set as {@LabelSide}", LabelSide);
        }
    }
}
