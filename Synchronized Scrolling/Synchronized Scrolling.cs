using cAlgo.API;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SynchronizedScrolling : Indicator
    {
        private static ConcurrentDictionary<string, SymbolCharts> _symbolCharts = new ConcurrentDictionary<string, SymbolCharts>();

        private SymbolCharts _charts;

        protected override void Initialize()
        {
            if (_symbolCharts.ContainsKey(SymbolName) == false)
            {
                _charts = new SymbolCharts();

                _symbolCharts.AddOrUpdate(SymbolName, _charts, (key, oldValue) => _charts);
            }
            else
            {
                _charts = _symbolCharts[SymbolName];
            }

            _charts.Add(Chart);

            Chart.ScrollChanged += Chart_ScrollChanged;
        }

        private void Chart_ScrollChanged(ChartScrollEventArgs obj)
        {
            if (_charts.ScrollingChart != null) return;

            _charts.ScrollingChart = obj.Chart;

            try
            {
                var firstBarTime = obj.Chart.Bars.OpenTimes[obj.Chart.FirstVisibleBarIndex];

                foreach (var chart in _charts.GetCharts())
                {
                    if (chart == obj.Chart) continue;

                    if (chart.Bars[0].OpenTime > firstBarTime)
                    {
                        while (chart.Bars[0].OpenTime <= firstBarTime)
                        {
                            var numberOfLoadedBars = chart.Bars.LoadMoreHistory();

                            if (numberOfLoadedBars == 0)
                            {
                                chart.DrawStaticText("ScrollError", "Can't load more data to keep in sync with other charts as more historical data is not available for this chart", VerticalAlignment.Bottom, HorizontalAlignment.Left, Color.Red);

                                break;
                            }
                        }
                    }

                    chart.ScrollXTo(firstBarTime);
                }
            }
            finally
            {
                _charts.ScrollingChart = null;
            }
        }

        public override void Calculate(int index)
        {
        }
    }

    public class SymbolCharts
    {
        private readonly ConcurrentDictionary<string, Chart> _charts = new ConcurrentDictionary<string, Chart>();

        public Chart ScrollingChart { get; set; }

        public void Add(Chart chart)
        {
            if (GetChartKey(ScrollingChart).Equals(GetChartKey(chart), StringComparison.Ordinal))
            {
                ScrollingChart = null;

                ScrollingChart.ScrollXTo(ScrollingChart.Bars.OpenTimes[ScrollingChart.FirstVisibleBarIndex]);
            }

            _charts.AddOrUpdate(GetChartKey(chart), chart, (k, oldValue) => chart);
        }

        public IEnumerable<Chart> GetCharts()
        {
            foreach (var chartKeyValue in _charts)
            {
                var chart = chartKeyValue.Value;

                yield return chart;
            }
        }

        private string GetChartKey(Chart chart)
        {
            return chart == null ? string.Empty : string.Format("{0}_{1}_{2}_{3:o}", chart.SymbolName, chart.TimeFrame, chart.ChartType, DateTime.UtcNow);
        }
    }
}