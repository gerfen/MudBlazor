﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor.EnhanceChart.Internal;
using MudBlazor.Utilities;

namespace MudBlazor.EnhanceChart
{
    record MudEnchancedBarChartSnapShot(Double Margin, Double Padding);

    /// <summary>
    /// The bar chart offering rich interactions
    /// </summary>
    public partial class MudEnhancedBarChart : ICollection<MudEnhancedBarDataSet>, ICollection<IYAxis>, ISnapshot<MudEnchancedBarChartSnapShot>
    {
        #region Fields

        private List<SvgLine> _lines = new();
        private List<SvgText> _labels = new();
        private List<SvgBarRepresentation> _bars = new();

        private List<MudEnhancedBarDataSet> _dataSets = new();
        private List<IYAxis> _yaxes = new();
        private MudEnhancedBarChartXAxis _xAxis;
        private Dictionary<IDataSeries, BarChartToolTipInfo> currentToolTips = new();

        #endregion

        /// <summary>
        /// The "parent" Chart of this BarChart
        /// </summary>
        [CascadingParameter] MudEnhancedChart Chart { get; set; }

        /// <summary>
        /// The Datasets that should be rendered
        /// </summary>
        [Parameter] public RenderFragment DataSets { get; set; }

        /// <summary>
        /// The y axes (possible multiple) that should be rendered   
        /// </summary>
        [Parameter] public RenderFragment YAxes { get; set; } = DefaultYAxesFragment;

        /// <summary>
        /// The x axis (only a single one) that should be rendered 
        /// </summary>
        [Parameter] public RenderFragment XAxis { get; set; } = DefaultXAxisFragment;

        /// <summary>
        /// A action that is called before drawing instruction are created. Can be used to manipulte data before drawing
        /// </summary>
        [Parameter] public Action<MudEnhancedBarChart> BeforeCreatingInstructionCallBack { get; set; }

        /// <summary>
        /// the margin between the bars and axis. The value is the relativ value. For instance 2.0 means two percent of the avaible space
        /// </summary>
        [Parameter] public Double Margin { get; set; } = 2.0;


        /// <summary>
        /// The padding between two series. For instance 2.0 means two percent of the avaible space
        /// </summary>
        [Parameter] public Double Padding { get; set; } = 3.0;

        /// <summary>
        /// Callback for changes about the legend. This is invoked for instance if a new series is added
        /// </summary>
        [Parameter] public EventCallback<ChartLegendInfo> LegendInfoChanged { get; set; }

        /// <summary>
        /// The data for the legend
        /// </summary>
        public ChartLegendInfo LegendInfo => new ChartLegendInfo(_dataSets.Select(x => new ChartLegendInfoGroup(x.Name,
            x.Select(y => new ChartLegendInfoSeries(y.Name, "#" + (String)y.Color, y.IsEnabled, y)),
            true)));


        #region Tooltips and legend

        internal void AddTooltip(BarChartToolTipInfo barChartToolTipInfo, IDataSeries series)
        {
            currentToolTips.Add(series, barChartToolTipInfo);
            Chart.UpdateTooltip(currentToolTips.Values);
        }

        internal void RemoveTooltip(IDataSeries series)
        {
            currentToolTips.Remove(series);
            Chart.UpdateTooltip(currentToolTips.Values);
        }

        private void InvokeLegendChanged()
        {
            var info = LegendInfo;
            LegendInfoChanged.InvokeAsync(info);
            Chart?.UpdateLegend(info);
        }

        #endregion

        #region Updates from children

        protected internal void DataSetUpdated(MudEnhancedBarDataSet barChartSeries)
        {
            if (_dataSets.Contains(barChartSeries) == false)
            {
                _dataSets.Add(barChartSeries);
            }

            InvokeLegendChanged();
            CreateDrawingInstruction();
        }

        protected internal void SeriesAdded(MudEnhancedBarChartSeries _)
        {
            CreateDrawingInstruction();
            InvokeLegendChanged();
        }

        protected internal void SetSeriesAsExclusivelyActive(IDataSeries inputSeries)
        {
            foreach (var set in _dataSets)
            {
                foreach (var series in set)
                {
                    if (inputSeries == series)
                    {
                        series.SetAsActive();
                    }
                    else
                    {
                        series.SetAsInactive();
                    }
                }
            }

            SoftRedraw();
        }

        protected internal void SetAllSeriesAsActive()
        {
            foreach (var set in _dataSets)
            {
                foreach (var series in set)
                {
                    series.SetAsActive();
                }
            }

            SoftRedraw();
        }

        protected internal void DataSetCleared(MudEnhancedBarDataSet _)
        {
            CreateDrawingInstruction();
            InvokeLegendChanged();
        }

        protected internal void DataSeriesRemoved(MudEnhancedBarChartSeries _)
        {
            CreateDrawingInstruction();
            InvokeLegendChanged();
        }

        protected internal void SeriesUpdated(MudEnhancedBarDataSet _, MudEnhancedBarChartSeries __)
        {
            InvokeLegendChanged();
            CreateDrawingInstruction();
        }

        #region From Axes

        protected internal void AxesUpdated(MudEnhancedNumericLinearAxis _)
        {
            CreateDrawingInstruction();
        }

        protected internal void MajorTickChanged(IYAxis axe, MudEnhancedTick _)
        {
            CreateDrawingInstruction();
        }

        protected internal void MinorTickChanged(IYAxis axe, MudEnhancedTick _)
        {
            CreateDrawingInstruction();
        }

        internal void XAxesUpdated(MudEnhancedBarChartXAxis barChartXAxes)
        {
            if (_xAxis == null)
            {
                _xAxis = barChartXAxes;
            }
            else if (_xAxis != barChartXAxes)
            {
                _xAxis = barChartXAxes;
            }

            CreateDrawingInstruction();
        }

        #endregion

        #endregion

        #region Drawing

        protected internal void SoftRedraw()
        {
            StateHasChanged();
        }

        public void ForceRedraw()
        {
            CreateDrawingInstruction();
        }

        private void CreateDrawingInstruction()
        {
            BeforeCreatingInstructionCallBack?.Invoke(this);

            if (_dataSets.Count == 0 || _dataSets.Sum(x => x.Count) == 0 || _dataSets.Sum(x => x.Sum(y => y.Points.Count)) == 0 || _yaxes.Count == 0)
            {
                return;
            }

            _bars.Clear();
            _labels.Clear();
            _lines.Clear();

            Dictionary<MudEnhancedBarDataSet, IYAxis> dataSetAxisMapper = new();

            foreach (var set in _dataSets)
            {
                IYAxis axis = set.Axis;
                if (set.Axis == null)
                {
                    axis = _yaxes[0];
                }

                axis.ProcessDataSet(set);
                dataSetAxisMapper.Add(set, axis);
            }

            Int32 amountOfSeries = _dataSets.Sum(x => x.Count(y => y.IsEnabled == true));

            Double xPerLabel = 100.0 / _xAxis.Labels.Count;
            Double labelThickness = xPerLabel - Padding;
            Double subSpacePerSeriesPerLabel = labelThickness / amountOfSeries;
            Double barThickness = subSpacePerSeriesPerLabel - Margin;

            Double currentX = 0.0;

            TransformMatrix2D matrix = TransformMatrix2D.Identity;
            switch (_xAxis.Placement)
            {
                case XAxisPlacement.Bottom:
                    matrix = TransformMatrix2D.TranslateY(_xAxis.Margin + _xAxis.Height) * TransformMatrix2D.ScalingY((100 - _xAxis.Margin - _xAxis.Height) / 100);
                    break;
                case XAxisPlacement.Top:
                    matrix = TransformMatrix2D.ScalingY((100 - _xAxis.Margin - _xAxis.Height) / 100);
                    break;
                case XAxisPlacement.None:
                    matrix = TransformMatrix2D.Identity;
                    break;
                default:
                    break;
            }

            foreach (var item in dataSetAxisMapper.Values)
            {
                item.CalculateTicks();

                switch (item.Placement)
                {
                    case YAxisPlacement.Left:
                        matrix = TransformMatrix2D.TranslateX(item.Margin + item.LabelSize) * TransformMatrix2D.ScalingX((100 - item.Margin - item.LabelSize) / 100) * matrix;
                        break;
                    case YAxisPlacement.Rigth:
                        matrix = TransformMatrix2D.ScalingX((100 - item.Margin - item.LabelSize) / 100) * matrix;
                        break;
                    case YAxisPlacement.None:
                    default:
                        //matrix = TransformMatrix2D.Identity * matrix;
                        continue;
                }
            }

            matrix = TransformMatrix2D.MirrorYAxis * TransformMatrix2D.Translate(0, -100) * matrix;

            Double xForLine = 0.0;
            Double deltaForXGridline = 100.0 / _xAxis.Labels.Count;
            for (int labelIndex = 0; labelIndex < _xAxis.Labels.Count; labelIndex++)
            {
                Double subX = currentX + Padding / 2.0;
                foreach (var set in _dataSets)
                {
                    var axisHelper = dataSetAxisMapper[set].GetTickInfo();

                    foreach (var series in set)
                    {
                        if (series.IsEnabled == false) { continue; }

                        Double value = 0.0;
                        if (labelIndex < series.Points.Count)
                        {
                            value = series.Points[labelIndex];
                        }

                        Double height = (value / axisHelper.Max) * 100.0;
                        if (height > 100.0)
                        {
                            height = 100.0;
                        }

                        SvgBarRepresentation bar = new SvgBarRepresentation
                        {
                            P1 = matrix * new Point2D(subX, 0),
                            P2 = matrix * new Point2D(subX, height),
                            P3 = matrix * new Point2D(subX + barThickness, height),
                            P4 = matrix * new Point2D(subX + barThickness, 0),
                            Fill = series.Color,
                            Series = series,
                            XLabel = _xAxis.Labels[labelIndex],
                            YValue = value,
                        };

                        _bars.Add(bar);

                        subX += subSpacePerSeriesPerLabel;
                    }
                }

                currentX += xPerLabel;
                if (_xAxis.ShowGridLines == true)
                {
                    SvgLine line = new SvgLine(
                        matrix * new Point2D(xForLine, 0),
                        matrix * new Point2D(xForLine, 100),
                        _xAxis.GridLineThickness,
                        (String)_xAxis.GridLineColor,
                        new CssBuilder("mud-enhanced-chart-x-axis-grid-line").AddOnlyWhenNotEmptyClass(_xAxis.GridLineCssClass).Build()
                        );
                    _lines.Add(line);
                }

                xForLine += deltaForXGridline;
            }

            if (_xAxis.ShowGridLines == true)
            {
                _lines.Add(new SvgLine(
                        matrix * new Point2D(100, 0),
                        matrix * new Point2D(100, 100),
                        _xAxis.GridLineThickness,
                        (String)_xAxis.GridLineColor,
                        new CssBuilder("mud-enhanced-chart-x-axis-grid-line").AddOnlyWhenNotEmptyClass(_xAxis.GridLineCssClass).Build()
                        ));
            }

            if (_xAxis.Placement != XAxisPlacement.None)
            {
                Double marginLeftFromXAxis = dataSetAxisMapper.Values.Where(x => x.Placement == YAxisPlacement.Left)
                     .Select(x => x.Margin + x.LabelSize)
                     .Sum();

                Double marginRigthFromXAxis = dataSetAxisMapper.Values.Where(x => x.Placement == YAxisPlacement.Rigth)
                     .Select(x => x.Margin + x.LabelSize)
                     .Sum();

                Double spacePerXAxisLabel = (100.00 - marginRigthFromXAxis - marginLeftFromXAxis) / _xAxis.Labels.Count;

                Double x = marginLeftFromXAxis + (spacePerXAxisLabel / 2);

                foreach (var item in _xAxis.Labels)
                {
                    Point2D labelLeftCorner = new Point2D(x,
                        (_xAxis.Placement == XAxisPlacement.Bottom) ? (100.00 - _xAxis.Height / 2) : _xAxis.Height / 2);
                    _labels.Add(new SvgText
                    {
                        Class = new CssBuilder("mud-enhanced-chart-x-axis-label").AddOnlyWhenNotEmptyClass(_xAxis.LabelCssClass).Build(),
                        Length = spacePerXAxisLabel,
                        Value = item,
                        Height = _xAxis.Height,
                        X = labelLeftCorner.X,
                        Y = labelLeftCorner.Y
                    });

                    x += spacePerXAxisLabel;

                }
            }

            foreach (var axisHelper in dataSetAxisMapper.Values)
            {
                if (axisHelper.Placement == YAxisPlacement.None)
                {
                    continue;
                }

                var tickInfo = axisHelper.GetTickInfo();

                Double marginFromXAxis = _xAxis.Placement == XAxisPlacement.None ? 0.0 : _xAxis.Margin + _xAxis.Height;

                Double x = axisHelper.Placement == YAxisPlacement.Left ? axisHelper.LabelSize : 100 - axisHelper.LabelSize;

                Double y = 100.0;
                if (_xAxis.Placement == XAxisPlacement.Bottom)
                {
                    y -= marginFromXAxis;
                }

                Double deltaYPerTick = (100.0 - marginFromXAxis) / (tickInfo.MajorTickAmount - 1);
                Double tickValue = tickInfo.StartTickValue;
                Int32 startIndex = _labels.Count;

                Double deltaPerMajorGridLine = 100.0 / (tickInfo.MajorTickAmount - 1);
                Double deltaPerMinorGridLine = deltaPerMajorGridLine / (tickInfo.MinorTickAmount + 1);

                Double lineY = 0.0;

                for (int i = 0; i < tickInfo.MajorTickAmount; i++)
                {
                    Point2D labelLeftCorner = new Point2D(x, y);

                    _labels.Add(new SvgText
                    {
                        Class =  new CssBuilder("mud-enhanced-chart-y-axis-major-label").AddOnlyWhenNotEmptyClass(axisHelper.LabelCssClass).Build(),
                        Value = Math.Round(tickValue, 6).ToString(),
                        Height = 3,
                        X = labelLeftCorner.X,
                        Y = labelLeftCorner.Y,
                        TextPlacement = axisHelper.Placement == YAxisPlacement.Left ? "end" : "start",
                    });

                    y -= deltaYPerTick;
                    tickValue += tickInfo.MajorTickNumericValue;

                    if (axisHelper.MajorTickInfo?.ShowGridLines == true)
                    {
                        SvgLine line = new SvgLine(
                          matrix * new Point2D(0, lineY),
                          matrix * new Point2D(100, lineY),
                          axisHelper.MajorTickInfo.GridLineThickness,
                          axisHelper.MajorTickInfo.GridLineColor,
                          new CssBuilder("mud-enhanced-chart-y-axis-major-grid-line").AddOnlyWhenNotEmptyClass(axisHelper.MajorTickInfo.GridLineCssClass).Build()
                          );
                        _lines.Add(line);
                    }

                    if (axisHelper.MinorTickInfo?.ShowGridLines == true)
                    {
                        if (lineY < 100.0)
                        {
                            Double minorLineY = deltaPerMinorGridLine;
                            while (minorLineY < deltaPerMajorGridLine)
                            {
                                SvgLine line = new SvgLine(
                                 matrix * new Point2D(0, minorLineY + lineY),
                                 matrix * new Point2D(100, minorLineY + lineY),
                                 axisHelper.MinorTickInfo.GridLineThickness,
                                 axisHelper.MinorTickInfo.GridLineColor,
                                 new CssBuilder("mud-enhanced-chart-y-axis-minor-grid-line").AddOnlyWhenNotEmptyClass(axisHelper.MinorTickInfo.GridLineCssClass).Build()
                                 );
                                _lines.Add(line);

                                minorLineY += deltaPerMinorGridLine;
                            }
                        }
                    }

                    lineY += deltaPerMajorGridLine;
                }

                _labels[startIndex].Baseline = "text-after-edge";
                _labels[_labels.Count - 1].Baseline = "text-before-edge";
            }

            StateHasChanged();
        }

        #endregion

        #region Dataset ICollection Member

        public void Add(MudEnhancedBarDataSet item)
        {
            if (_dataSets.Contains(item) == false)
            {
                _dataSets.Add(item);
                InvokeLegendChanged();
                CreateDrawingInstruction();
            }
        }

        public bool Remove(MudEnhancedBarDataSet item)
        {
            if (_dataSets.Contains(item) == true)
            {
                _dataSets.Remove(item);
                InvokeLegendChanged();

                CreateDrawingInstruction();
                return true;
            }

            return false;
        }

        void ICollection<MudEnhancedBarDataSet>.Clear()
        {
            _dataSets.Clear();
            CreateDrawingInstruction();
        }

        public void Clear()
        {
            _dataSets.Clear();
            _yaxes.Clear();
            CreateDrawingInstruction();
        }

        Int32 ICollection<MudEnhancedBarDataSet>.Count => _dataSets.Count;
        Boolean ICollection<MudEnhancedBarDataSet>.IsReadOnly => false;
        public bool Contains(MudEnhancedBarDataSet item) => _dataSets.Contains(item);
        public void CopyTo(MudEnhancedBarDataSet[] array, int arrayIndex) => _dataSets.CopyTo(array, arrayIndex);
        public IEnumerator<MudEnhancedBarDataSet> GetEnumerator() => _dataSets.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _dataSets.GetEnumerator();

        #endregion

        #region YAxes ICollection Member

        public void Add(IYAxis item)
        {
            if (_yaxes.Contains(item) == false)
            {
                _yaxes.Add(item);
                CreateDrawingInstruction();
            }
        }

        public Boolean Remove(IYAxis item)
        {
            if (_yaxes.Contains(item) == true)
            {
                _yaxes.Remove(item);
                CreateDrawingInstruction();
                return true;
            }

            return false;
        }

        void ICollection<IYAxis>.Clear()
        {
            _yaxes.Clear();
            CreateDrawingInstruction();
        }

        Int32 ICollection<IYAxis>.Count => _yaxes.Count;
        Boolean ICollection<IYAxis>.IsReadOnly => false;

        public Boolean Contains(IYAxis item) => _yaxes.Contains(item);
        public void CopyTo(IYAxis[] array, int arrayIndex) => _yaxes.CopyTo(array, arrayIndex);
        IEnumerator<IYAxis> IEnumerable<IYAxis>.GetEnumerator() => _yaxes.GetEnumerator();

        #endregion

        #region DefaultRenderFragments

        public static void DefaultXAxisFragment(RenderTreeBuilder builder)
        {
            builder.OpenComponent(1, typeof(MudEnhancedBarChartXAxis));
            builder.CloseComponent();
        }

        public static void DefaultYAxesFragment(RenderTreeBuilder builder)
        {
            builder.OpenComponent(1, typeof(MudEnhancedNumericLinearAxis));
            builder.CloseComponent();
        }

        #endregion

        protected override void OnParametersSet()
        {
            base.OnParametersSet();

            ISnapshot<MudEnchancedBarChartSnapShot> _this = this;

            if (_this.SnapshotHasChanged(true) == true)
            {
                CreateDrawingInstruction();
            }
        }

        MudEnchancedBarChartSnapShot ISnapshot<MudEnchancedBarChartSnapShot>.OldSnapshotValue { get; set; }
        MudEnchancedBarChartSnapShot ISnapshot<MudEnchancedBarChartSnapShot>.CreateSnapShot() => new MudEnchancedBarChartSnapShot(Margin, Padding);

      
    }
}
