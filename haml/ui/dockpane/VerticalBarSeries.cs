/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using OxyPlot;
using OxyPlot.Series;

// Modified class from https://github.com/mugiseyebrows/VerticalBarChart/blob/master/VerticalBarChart/StretchyLinearBarSeries.cs
namespace HamlProAppModule.haml.ui.dockpane
{

    /// <summary>
    /// Represents a series to display bars in a linear axis
    /// </summary>
    public class VerticalBarSeries : DataPointSeries
    {
        /// <summary>
        /// The rendered rectangles.
        /// </summary>
        private readonly List<OxyRect> rectangles = new List<OxyRect>();

        /// <summary>
        /// The indexes matching rendered rectangles.
        /// </summary>
        private readonly List<int> rectanglesPointIndexes = new List<int>();

        /// <summary>
        /// The default color.
        /// </summary>
        private OxyColor defaultColor;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearBarSeries" /> class.
        /// </summary>
        public VerticalBarSeries()
        {
            FillColor = OxyColors.Automatic;
            BarWidth = 5;
            StrokeColor = OxyColors.Black;
            StrokeThickness = 0;
            TrackerFormatString = DefaultTrackerFormatString;
            NegativeFillColor = OxyColors.Undefined;
            NegativeStrokeColor = OxyColors.Undefined;
            Items = new VerticalBarSeriesItems(this);
        }

        /// <summary>
        /// Gets or sets the color of the interior of the bars.
        /// </summary>
        /// <value>The color.</value>
        public OxyColor FillColor { get; set; }

        /// <summary>
        /// Gets or sets the width of the bars.
        /// </summary>
        /// <value>The width of the bars.</value>
        public double BarWidth { get; set; }

        /// <summary>
        /// Gets or sets the thickness of the curve.
        /// </summary>
        /// <value> The stroke thickness.</value>
        public double StrokeThickness { get; set; }

        /// <summary>
        /// Gets or sets the color of the border around the bars.
        /// </summary>
        /// <value>The color of the stroke.</value>
        public OxyColor StrokeColor { get; set; }

        /// <summary>
        /// Gets or sets the color of the interior of the bars when the value is negative.
        /// </summary>
        /// <value>The color.</value>
        public OxyColor NegativeFillColor { get; set; }

        /// <summary>
        /// Gets or sets the color of the border around the bars when the value is negative.
        /// </summary>
        /// <value>The color of the stroke.</value>
        public OxyColor NegativeStrokeColor { get; set; }

        /// <summary>
        /// Gets the actual color.
        /// </summary>
        /// <value>The actual color.</value>
        public OxyColor ActualColor
        {
            get
            {
                return FillColor.GetActualColor(defaultColor);
            }
        }

        /// <summary>
        /// Gets the nearest point.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <param name="interpolate">interpolate if set to <c>true</c> .</param>
        /// <returns>A TrackerHitResult for the current hit.</returns>
        public override TrackerHitResult GetNearestPoint(ScreenPoint point, bool interpolate)
        {
            var rectangleIndex = FindRectangleIndex(point);
            if (rectangleIndex < 0)
            {
                return null;
            }

            var rectangle = rectangles[rectangleIndex];
            if (!rectangle.Contains(point))
            {
                return null;
            }

            var pointIndex = rectanglesPointIndexes[rectangleIndex];
            var dataPoint = ActualPoints[pointIndex];
            var item = GetItem(pointIndex);

            // Format: {0}\n{1}: {2}\n{3}: {4}
            var trackerParameters = new[]
            {
                Title,
                XAxis.Title ?? "X",
                XAxis.GetValue(Items.GetDist(pointIndex)), 
                YAxis.Title ?? "Y", 
                YAxis.GetValue(Math.Round(dataPoint.Y, 2))
            };

            var text = StringHelper.Format(ActualCulture, TrackerFormatString, item, trackerParameters);

            return new TrackerHitResult
            {
                Series = this,
                DataPoint = dataPoint,
                Position = point,
                Item = item,
                Index = pointIndex,
                Text = text,
            };
        }

        /// <inheritdoc/>
        public override void Render(IRenderContext rc)
        {
            rectangles.Clear();
            rectanglesPointIndexes.Clear();

            var actualPoints = ActualPoints;
            if (actualPoints == null || actualPoints.Count == 0)
            {
                return;
            }

            VerifyAxes();

            RenderBars(rc, actualPoints);
        }

        /// <summary>
        /// Renders the legend symbol for the line series on the
        /// specified rendering context.
        /// </summary>
        /// <param name="rc">The rendering context.</param>
        /// <param name="legendBox">The bounding rectangle of the legend box.</param>
        public override void RenderLegend(IRenderContext rc, OxyRect legendBox)
        {
            var xmid = (legendBox.Left + legendBox.Right) / 2;
            var ymid = (legendBox.Top + legendBox.Bottom) / 2;
            var height = (legendBox.Bottom - legendBox.Top) * 0.8;
            var width = height;
            rc.DrawRectangle(
                new OxyRect(xmid - (0.5 * width), ymid - (0.5 * height), width, height),
                GetSelectableColor(ActualColor),
                StrokeColor,
                StrokeThickness,
                EdgeRenderingMode);
        }

        /// <summary>
        /// Sets default values from the plot model.
        /// </summary>
        protected override void SetDefaultValues()
        {
            if (FillColor.IsAutomatic())
            {
                defaultColor = PlotModel.GetDefaultColor();
            }
        }

        /// <summary>
        /// Updates the axes to include the max and min of this series.
        /// </summary>
        protected override void UpdateAxisMaxMin()
        {
            base.UpdateAxisMaxMin();
            
            // May not need this part. Not part of OG class method
            XAxis.Include(0 - 0.5);
            XAxis.Include(Points.Count - 0.5);
            
            YAxis.Include(0.0);
        }

        /// <summary>
        /// Find the index of a rectangle that contains the specified point.
        /// </summary>
        /// <param name="point">the target point</param>
        /// <returns>the rectangle index</returns>
        public int FindRectangleIndex(ScreenPoint point)
        {
            IComparer<OxyRect> comparer;
            if (this.IsTransposed())
            {
                comparer = ComparerHelper.CreateComparer<OxyRect>(
                    (x, y) =>
                        {
                            if (x.Bottom < point.Y)
                            {
                                return 1;
                            }

                            if (x.Top > point.Y)
                            {
                                return -1;
                            }

                            return 0;
                        });
            }
            else
            {
                comparer = ComparerHelper.CreateComparer<OxyRect>(
                    (x, y) =>
                        {
                            if (x.Right < point.X)
                            {
                                return -1;
                            }

                            if (x.Left > point.X)
                            {
                                return 1;
                            }

                            return 0;
                        });
            }

            return rectangles.BinarySearch(0, rectangles.Count, new OxyRect(), comparer);
        }

        /// <summary>
        /// Renders the series bars.
        /// </summary>
        /// <param name="rc">The rendering context.</param>
        /// <param name="actualPoints">The list of points that should be rendered.</param>
        private void RenderBars(IRenderContext rc, List<DataPoint> actualPoints)
        {
            var widthVector = this.Orientate(new ScreenVector(XAxis.Scale / 2, 0));
            
            for (var pointIndex = 0; pointIndex < actualPoints.Count; pointIndex++)
            {
                var actualPoint = actualPoints[pointIndex];
                if (!IsValidPoint(actualPoint))
                {
                    continue;
                }

                var screenPoint = Transform(actualPoint) - widthVector;
                var basePoint = Transform(new DataPoint(actualPoint.X, 0)) + widthVector;
                var rectangle = new OxyRect(basePoint, screenPoint);
                rectangles.Add(rectangle);
                rectanglesPointIndexes.Add(pointIndex);

                var barColors = GetBarColors(actualPoint.Y);
                var fillColor = Items.GetColor(pointIndex);

                rc.DrawRectangle(
                    rectangle, 
                    fillColor, 
                    barColors.StrokeColor, 
                    StrokeThickness, 
                    EdgeRenderingMode.GetActual(EdgeRenderingMode.Adaptive));
            }
        }

        /// <summary>
        /// Gets the colors used to draw a bar.
        /// </summary>
        /// <param name="y">The point y value</param>
        /// <returns>The bar colors</returns>
        private BarColors GetBarColors(double y)
        {
            var positive = y >= 0.0;
            var fillColor = (positive || NegativeFillColor.IsUndefined()) ? GetSelectableFillColor(ActualColor) : NegativeFillColor;
            var strokeColor = (positive || NegativeStrokeColor.IsUndefined()) ? StrokeColor : NegativeStrokeColor;

            return new BarColors(fillColor, strokeColor);
        }

        /// <summary>
        /// Stores the colors used to draw a bar.
        /// </summary>
        private struct BarColors
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="BarColors" /> struct.
            /// </summary>
            /// <param name="fillColor">The fill color</param>
            /// <param name="strokeColor">The stroke color</param>
            public BarColors(OxyColor fillColor, OxyColor strokeColor) : this()
            {
                FillColor = fillColor;
                StrokeColor = strokeColor;
            }

            /// <summary>
            /// Gets the fill color.
            /// </summary>
            public OxyColor FillColor { get; private set; }

            /// <summary>
            /// Gets the stroke color.
            /// </summary>
            public OxyColor StrokeColor { get; private set; }
        }

        public VerticalBarSeriesItems Items { get; private set; }

        public class VerticalBarSeriesItems
        {
            private List<VerticalBarItem> items;
            public VerticalBarSeriesItems(VerticalBarSeries series)
            {
                Series = series;
                items = new List<VerticalBarItem>();
            }
            public VerticalBarSeries Series { get; set; }

            public void Add(VerticalBarItem item)
            {
                Series.Points.Add(new DataPoint(item.X, item.Value));
                items.Add(item);
            }

            public OxyColor GetColor(int idx)
            {
                return items[idx].Color;
            }

            public void SetColor(int idx, OxyColor color)
            {
                items[idx].Color = color;
            }
            
            public void SetColor(int idx, string hex)
            {
                items[idx].Color = OxyColor.Parse(hex);
            }

            public double GetDist(int idx)
            {
                // TODO: Sometimes the graph does not update its items quickly enough, but this is imperceptible to the user
                // TODO: and does not affect the backend data. A more formal approach (lock?) could be added in the future
                if (idx > items.Count)
                {
                    return 0.0;
                }
                
                return items[idx].dist;
            }
            public void SwitchColors(int idx1, int idx2)
            {
                (items[idx1].Color, items[idx2].Color) = (items[idx2].Color, items[idx1].Color);
            }

            public void AddRange(IEnumerable<VerticalBarItem> items)
            {
                foreach(var item in items)
                {
                    Add(item);
                }
            }

            public void Clear()
            {
                Series.Points.Clear();
            }

            public int Count => Series.Points.Count;
        }
    }
}
