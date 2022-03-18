using cAlgo.API;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SynchronizedScrolling : Indicator
    {
        private static ConcurrentDictionary<string, SymbolIndicatorInstances> _indicatorInstances = new ConcurrentDictionary<string, SymbolIndicatorInstances>();

        private SymbolIndicatorInstances _symbolIndicatorInstances;

        private DateTime _lastScrollTime;

        [Parameter("Mode", DefaultValue = Mode.All)]
        public Mode Mode { get; set; }

        protected override void Initialize()
        {
            if (_indicatorInstances.ContainsKey(SymbolName) == false)
            {
                _symbolIndicatorInstances = new SymbolIndicatorInstances();

                _indicatorInstances.AddOrUpdate(SymbolName, _symbolIndicatorInstances, (key, oldValue) => _symbolIndicatorInstances);
            }
            else
            {
                _symbolIndicatorInstances = _indicatorInstances[SymbolName];
            }

            _symbolIndicatorInstances.Add(Chart, this);

            Chart.ScrollChanged += Chart_ScrollChanged;
        }

        private void Chart_ScrollChanged(ChartScrollEventArgs obj)
        {
            var firstBarTime = obj.Chart.Bars.OpenTimes[obj.Chart.FirstVisibleBarIndex];

            if (_lastScrollTime == firstBarTime || _symbolIndicatorInstances.ScrollingChart != null) return;

            _lastScrollTime = firstBarTime;

            _symbolIndicatorInstances.ScrollingChart = obj.Chart;

            try
            {
                if (Mode == Mode.Symbol)
                {
                    ScrollCharts(_symbolIndicatorInstances, Chart, firstBarTime);
                }
                else
                {
                    foreach (var symbolInstances in _indicatorInstances)
                    {
                        if (Mode == Mode.TimeFrame)
                        {
                            ScrollCharts(symbolInstances.Value, Chart, firstBarTime, indicator => indicator.TimeFrame == TimeFrame);
                        }
                        else
                        {
                            ScrollCharts(symbolInstances.Value, Chart, firstBarTime);
                        }
                    }
                }
            }
            finally
            {
                _symbolIndicatorInstances.ScrollingChart = null;
            }
        }

        private void ScrollCharts(SymbolIndicatorInstances instances, Chart scrolledChart, DateTime firstBarTime, Func<Indicator, bool> predicate = null)
        {
            foreach (var indicator in instances.GetIndicators())
            {
                if (indicator.Chart == scrolledChart || (predicate != null && predicate(indicator) == false)) continue;

                if (indicator.Chart.Bars[0].OpenTime > firstBarTime)
                {
                    indicator.BeginInvokeOnMainThread(() => LoadeMoreBars(indicator.Chart, firstBarTime));
                }

                indicator.BeginInvokeOnMainThread(() => indicator.Chart.ScrollXTo(firstBarTime));
            }
        }

        private void LoadeMoreBars(Chart chart, DateTime firstBarTime)
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

        public override void Calculate(int index)
        {
        }
    }

    public class SymbolIndicatorInstances
    {
        private readonly ConcurrentDictionary<string, Indicator> _indicators = new ConcurrentDictionary<string, Indicator>();

        public Chart ScrollingChart { get; set; }

        public void Add(Chart chart, Indicator indicator)
        {
            var chartKey = GetChartKey(chart);

            if (GetChartKey(ScrollingChart).Equals(chartKey, StringComparison.Ordinal))
            {
                ScrollingChart = null;

                ScrollingChart.ScrollXTo(ScrollingChart.Bars.OpenTimes[ScrollingChart.FirstVisibleBarIndex]);
            }

            _indicators.AddOrUpdate(chartKey, indicator, (k, oldValue) => indicator);
        }

        public IEnumerable<Indicator> GetIndicators()
        {
            foreach (var indicatorKeyValue in _indicators)
            {
                yield return indicatorKeyValue.Value;
            }
        }

        private string GetChartKey(Chart chart)
        {
            return chart == null ? string.Empty : string.Format("{0}_{1}_{2}", chart.SymbolName, chart.TimeFrame, chart.ChartType);
        }
    }

    public enum Mode
    {
        All,
        TimeFrame,
        Symbol
    }
}