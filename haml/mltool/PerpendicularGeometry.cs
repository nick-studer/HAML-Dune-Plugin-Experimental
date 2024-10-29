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
using ArcGIS.Core.Geometry;
using HamlProAppModule.haml.events;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.logging;
using HamlProAppModule.haml.ui;
using MLPort;
using MLPort.placement;
using Serilog;

namespace HamlProAppModule.haml.mltool
{
    public abstract class PerpendicularGeometry
    {
        protected ILogger Log => LogManager.GetLogger(GetType());
        
        protected HAMLGeometry HamlGeometry;
        protected KNNLearner Learner;
        protected Raster Raster;
        protected internal bool LabelDirSet;
        protected List<CIMGraphic> SignalGraphics;
        protected List<List<SignalPoint>> untrainedVertices;
        protected Envelope Extent;
        protected internal LeftOrRightSide LabelSide;

        public PerpendicularGeometry(Raster raster,
            KNNLearner learner, 
            Envelope extent)
        {
            Learner = learner;
            Raster = raster;
            LabelSide = LeftOrRightSide.LeftSide;
            SignalGraphics = new List<CIMGraphic>();
            untrainedVertices = new List<List<SignalPoint>>();
            Extent = extent;
        }

        protected internal abstract double CheckLabel(Geometry g, MapPoint signalLocation);
        protected internal abstract void SetLabelDir(MapPoint mp);
        protected internal abstract List<int> AutoGenerateVertices(int n);
        protected internal abstract List<int> RemoveGeneratedVertices();

        public virtual Geometry GetContourAsArcGeometry()
        {
            return HamlGeometry.GetArcGeometry();
        }
        
        public Geometry GetSearchSpacesAsArcGeometry()
        {
            return HamlGeometry.GetArcPolylineOfSearchSpaces();
        }
                
        public void MoveVertex(int index, MapPoint mp)
        {
            HamlGeometry.MoveVertex(index, mp.X, mp.Y);
        }
        
        protected internal void TrainAllUntrainedVertices()
        {
            var trainedCount = 0;
            
            foreach( List<SignalPoint> signal in untrainedVertices) {
                TrainVertex(signal);
                trainedCount++;
            }
            
            Log.Here().Debug("Trained on {@TrainedCount} vertices", trainedCount);
            
            untrainedVertices.Clear();
        }

        //Must be run on MCT.
        //Inserts a vertex on the HAML contour at the nearest location to the given point.
        //Creates a search space at that location, generates and classifies a signal, then

        protected internal virtual int InsertAndPlaceVertex(MapPoint point) {

            HamlGeometry.InsertVertex(point, out SegmentConstraint _, out int vertexIndex);
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetContourAsArcGeometry(),
                Type = HamlGraphicType.Contour
            });
            
            return vertexIndex;
        }
        
        protected internal void InsertAndPlaceVertex(MapPoint point, int vIdx) {
            GetVertices().Insert(vIdx, new Vertex(point));
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetContourAsArcGeometry(),
                Type = HamlGraphicType.Contour
            });
        }
        
        //Must be run on MCT
        //Takes a signal and analyzes each pixel's location as inside or outside the polygon, labeling accordingly.
        protected void TrainVertex(List<SignalPoint> signal) {
            List<List<Double>> featureVectors = new List<List<Double>>();
            List<List<Double>> labels = new List<List<Double>>();

            Geometry g = HamlGeometry.GetArcGeometry();

            foreach ( SignalPoint signalPoint in signal ) {
                
                MapPoint signalLocationMP = MapPointBuilderEx.CreateMapPoint(signalPoint.x, signalPoint.y);

                double label = CheckLabel(g, signalLocationMP);

                featureVectors.Add(signalPoint.featureVector);
                labels.Add(new List<Double> { label });
            }

            Learner.trainMultiple(featureVectors, labels);
            
            Log.Here().Debug("Trained on {@TrainedVertexCount} vertices", featureVectors.Count);
        }
        
        protected (double, double) GuessVertexLocation(List<SignalPoint> signalPoints)
        {
            if (LabelSide == LeftOrRightSide.LeftSide)
            {
                signalPoints.Reverse();
            }
            
            var result = Placement.binaryGuessVertexLocation(Learner, signalPoints);
            
            signalPoints.ForEach(point =>
            {
                var geoLoc = new Tuple<double, double>(point.x, point.y);
                SignalGraphics.Add(Module1.BuildSignalPointGraphic(geoLoc, point.classification));    
            });

            var placement = Raster.PixelToMap(Convert.ToInt32(result.Item1),Convert.ToInt32(result.Item2));

            return (placement.Item1, placement.Item2);
        }

        public bool IsLabelDirSet()
        {
            return LabelDirSet;
        }
       
       public List<CIMGraphic> GetSignalGraphics()
       {
           return SignalGraphics;
       }

        public void RemoveVertices(List<int> indices)
        {
            foreach (var index in indices.OrderByDescending(v => v))
            {
                RemoveVertex(index);
                Log.Here().Debug("Removed vertex at index {@Idx}", index);
            }
        }

        public void RemoveVertex(int index)
        {
            HamlGeometry.RemoveVertex(index);
            Log.Here().Debug("Removed vertex at index {@Idx}", index);
            
            GeometryChangedEvent.Publish(new GeometryChangedEventArgs
            {
                Geometry = GetContourAsArcGeometry(),
                Type = HamlGraphicType.Contour
            });
        }

        public void RemoveVertex(Vertex vertex)
        {
            HamlGeometry.GetVertices().Remove(vertex);
            Log.Here().Debug("Removed vertex {@ID}", vertex.Id);
        }

        public List<Vertex> GetVertices()
        {
            return HamlGeometry.GetVertices();
        }

        public virtual void SetExtent(Envelope newExtent)
        {
            Extent = newExtent;
            HamlGeometry.SetExtent(newExtent);
        }

        public Envelope GetExtent()
        {
            return Extent;
        }

        public HAMLGeometry GetHamlGeometry
        { 
            get => HamlGeometry;
        }


        public virtual void ResetGeometry() {}
    }
}
