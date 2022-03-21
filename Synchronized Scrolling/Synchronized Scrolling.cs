using cAlgo.API;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Threading;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SynchronizedScrolling : Indicator
    {
        private static ConcurrentDictionary<string, SynchronizedScrolling> _indicatorInstances = new ConcurrentDictionary<string, SynchronizedScrolling>();

        private static int _numberOfChartsToScroll;

        private DateTime _lastScrollTime;

        private string _chartKey;

        [Parameter("Mode", DefaultValue = Mode.All)]
        public Mode Mode { get; set; }

        protected override void Initialize()
        {
            _chartKey = string.Format("{0}_{1}_{2}", SymbolName, TimeFrame, Chart.ChartType);

            if (_indicatorInstances.ContainsKey(_chartKey) == false)
            {
                _indicatorInstances.AddOrUpdate(_chartKey, this, (key, value) => this);
            }

            Chart.ScrollChanged += Chart_ScrollChanged;
        }

        public void ScrollXTo(DateTime dateTime)
        {
            Print("ScrollXTo Called | ", SymbolName, " | ", TimeFrame);
            BeginInvokeOnMainThread(() =>
            {
                LoadMoreHistory(dateTime);

                Chart.ScrollXTo(dateTime);
            });
        }

        private void LoadMoreHistory(DateTime dateTime)
        {
            while (Bars[0].OpenTime > dateTime)
            {
                var numberOfLoadedBars = Bars.LoadMoreHistory();

                if (numberOfLoadedBars == 0)
                {
                    Chart.DrawStaticText("ScrollError", "Can't load more data to keep in sync with other charts as more historical data is not available for this chart", VerticalAlignment.Bottom, HorizontalAlignment.Left, Color.Red);

                    break;
                }
            }
        }

        private void Chart_ScrollChanged(ChartScrollEventArgs obj)
        {
            if (_numberOfChartsToScroll > 0)
            {
                Print("_numberOfChartsToScroll > 0 | ", _numberOfChartsToScroll);

                Interlocked.Decrement(ref _numberOfChartsToScroll);

                return;
            }

            var firstBarTime = obj.Chart.Bars.OpenTimes[obj.Chart.FirstVisibleBarIndex];

            if (_lastScrollTime == firstBarTime) return;

            _lastScrollTime = firstBarTime;

            switch (Mode)
            {
                case Mode.Symbol:
                    ScrollCharts(Chart, firstBarTime, indicator => indicator.SymbolName.Equals(SymbolName, StringComparison.Ordinal));
                    break;

                case Mode.TimeFrame:
                    ScrollCharts(Chart, firstBarTime, indicator => indicator.TimeFrame == TimeFrame);
                    break;

                default:
                    ScrollCharts(Chart, firstBarTime);
                    break;
            }
        }

        private void ScrollCharts(Chart scrolledChart, DateTime firstBarTime, Func<Indicator, bool> predicate = null)
        {
            var toScroll = new List<SynchronizedScrolling>(_indicatorInstances.Values.Count);

            foreach (var indicator in _indicatorInstances.Values)
            {
                if (indicator.Chart == scrolledChart || (predicate != null && predicate(indicator) == false)) continue;

                toScroll.Add(indicator);
            }

            Interlocked.CompareExchange(ref _numberOfChartsToScroll, toScroll.Count, _numberOfChartsToScroll);

            foreach (var indicator in toScroll)
            {
                Print("Scrolling | {0} | {1}", indicator.SymbolName, _numberOfChartsToScroll);

                indicator.ScrollXTo(firstBarTime);
            }
        }

        public override void Calculate(int index)
        {
        }
    }

    public enum Mode
    {
        All,
        TimeFrame,
        Symbol
    }
}