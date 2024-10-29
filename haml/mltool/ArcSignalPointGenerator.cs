/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.geometry;
using HamlProAppModule.haml.logging;
using HamlProAppModule.haml.util;
using MLPort.placement;
using Serilog;

namespace HamlProAppModule.haml.mltool {
    
    //Responsible for grabbing the features associated with pixels under the search space.
    //Signals must be properly oriented such that the first point lies within the polygon
    // upon the time of insertion.
    public class ArcSignalPointGenerator : SignalPointGenerator
    {
        protected ILogger Log => LogManager.GetLogger(GetType());

        protected Raster raster;
        private SegmentConstraint _constraint;
        private Envelope _extent;
        private LineSegment _domainSegment;
        private double _domainWalk;

        public ArcSignalPointGenerator(Raster raster) {
            FindMinMaxPxVals();
            this.raster = raster;
        }

        private async void FindMinMaxPxVals()
        {
            await QueuedTask.Run(() =>
            {
                //TODO: increase robustness, should not just find the first raster and get its stats
                //Accessing the raster layer
                var lyr = MapView.Active.Map.GetLayersAsFlattenedList().OfType<BasicRasterLayer>().FirstOrDefault();
                //Getting the colorizer
                var colorizer = lyr.GetColorizer() as CIMRasterStretchColorizer;
                //Accessing the statistics
                var stats = colorizer.StretchStats;
                var max = stats.max;
                var min = stats.min;

                if (min.HasValue && max.HasValue)
                {
                    pixelMin = min.Value;
                    pixelMax = max.Value;
                }
                else
                {
                    (decimal pixelMinDecimal, decimal pixelMaxDecimal) = RasterUtil.GetMinMaxPixelValues(raster.GetPixelType());
                    
                    pixelMin = Decimal.ToDouble(pixelMinDecimal);
                    pixelMax = Decimal.ToDouble(pixelMaxDecimal);
                    
                    Log.Here().Information("Using min/max decimal values for pixel min/max");
                }
            });
        }

        public ArcSignalPointGenerator(Raster raster, double pixelMin, double pixelMax)
        {
            this.raster = raster;
            this.pixelMin = pixelMin;
            this.pixelMax = pixelMax;
        }

        //Must be run on MCT.
        //Gets the signal for a given search space.
        //Signals, unlike Segments, are cut at the current mapview bounds.
        public override bool MoveNext()
        {
            if (_domainWalk < _domainSegment.Length)
            {
                //This stride does not sample pixels evenly as the Bresenham algorithm would.
                //However, it fits within a geospatial context to occasionally skip pixels and
                // sample at standard distances.
                Tuple<double, double> cellSize = raster.GetMeanCellSize();
                double stride = cellSize.Item1 + cellSize.Item2;
                
                MapPoint p = GeometryEngine.Instance.QueryPoint(_domainSegment,
                    SegmentExtensionType.NoExtension,
                    _domainWalk,
                    AsRatioOrLength.AsLength);

                List<double> featureVector = new List<double>();
                Tuple<int, int> pixelLocation = raster.MapToPixel(p.X, p.Y);
                
                for ( int i = 0; i < raster.GetBandCount(); i++ ) {
                    //So very wonky and compute instensive.  But is there any better way?
                    //Need something like a PixelBlock but for a given geometry rather than an OBB.
                    object pixelValue = raster.GetPixelValue(i, pixelLocation.Item1, pixelLocation.Item2);

                    if (pixelValue == null)
                    {
                        pixelValue = pixelMin;
                    }
                    
                    if ( pixelValue != null ) {
                        decimal valueDecimal = Decimal.Parse(pixelValue.ToString(), NumberStyles.Float);
                        featureVector.Add((Decimal.ToDouble(valueDecimal) - pixelMin)/(pixelMax - pixelMin));
                    } else {
                        Log.Here().Warning("Encountered null pixel value on band {@BandIdx} at ({@X}, {@Y})",
                            raster.GetBand(i).ToString(), pixelLocation.Item1, pixelLocation.Item2);
                        
                        //This should rarely occur.  Usually happens at raster edges.
                        featureVector.Add(0.0);
                    }
                }
                
                _current = new SignalPoint { 
                    x = pixelLocation.Item1,
                    y = pixelLocation.Item2,
                    featureVector = featureVector 
                };

                _domainWalk += stride;

                return true;
            }
            else
            {
                return false;
            }
        }

        public override void Reset()
        {
            _current = default;
            _domainWalk = 0.0;
        }

        public override void Dispose()
        {
            _current = default;
            _domainWalk = 0.0;
        }
        
        private void UpdateDomainSegment()
        {
            if (_constraint is null || _extent is null) return;
            
            //TODO: At some point we should check to see why this is necessary on the WorldView image.
            Polyline searchSpaceProjected =
                PolylineBuilderEx.CreatePolyline(Constraint.GetGeometry() as Polyline, MapView.Active.Map.SpatialReference);

            Envelope extentProjected = EnvelopeBuilderEx.CreateEnvelope(Extent, MapView.Active.Map.SpatialReference);

            Polyline? domain = GeometryEngine.Instance.Intersection(searchSpaceProjected, extentProjected) as Polyline;

            if (domain != null && domain.Points.Count >= 2)
            {
                _domainSegment = LineBuilderEx.CreateLineSegment(domain.Points.First(), domain.Points.Last());
                _domainWalk = 0.0;
            }
        }

        public SegmentConstraint Constraint
        {
            get => _constraint;
            
            set
            {
                _constraint = value;
                UpdateDomainSegment();
            }
        }

        public Envelope Extent
        {
            get => _extent;
            
            set
            {
                _extent = value;
                UpdateDomainSegment();
            } 
        }
    }
}
