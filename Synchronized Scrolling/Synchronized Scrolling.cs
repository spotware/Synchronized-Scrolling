using cAlgo.API;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.FileSystem)]
    public class SynchronizedScrolling : Indicator
    {
        private static ConcurrentDictionary<string, WeakReference> _indicatorInstances = new ConcurrentDictionary<string, WeakReference>();

        private static int _numberOfChartsToScroll;

        private DateTime _lastScrollTime;

        private string _chartKey;

        [Parameter("Mode", DefaultValue = Mode.All)]
        public Mode Mode { get; set; }

        public DateTime? TimeToScroll { get; set; }

        protected override void Initialize()
        {
            _chartKey = GetChartKey(this);

            SynchronizedScrolling oldIndicatr;

            if (GetIndicator(_chartKey, out oldIndicatr))
            {
                TimeToScroll = oldIndicatr.TimeToScroll;

                if (TimeToScroll.HasValue)
                {
                    Log("TimeToScroll: {0:dd MMM yyyy HH:mm:ss}", TimeToScroll.Value);
                }
                else
                {
                    Log("TimeToScroll: null");
                }
            }
            else
            {
            }

            var weakReference = new WeakReference(this);

            _indicatorInstances.AddOrUpdate(_chartKey, weakReference, (key, value) => weakReference);

            if (TimeToScroll.HasValue)
            {
                ScrollXTo(TimeToScroll.Value);
            }

            Chart.ScrollChanged += Chart_ScrollChanged;
        }

        public override void Calculate(int index)
        {
        }

        public void ScrollXTo(DateTime time)
        {
            TimeToScroll = time;

            Log("ScrollXTo Called | {0} | {1} | {2:dd MMM yyyy HH:mm:ss}", SymbolName, TimeFrame, time);

            LoadMoreHistory(time);

            Log("Chart.ScrollXTo Called | {0} | {1} | {2:dd MMM yyyy HH:mm:ss}", SymbolName, TimeFrame, time);

            Chart.ScrollXTo(time);
        }

        private void LoadMoreHistory(DateTime time)
        {
            if (Bars[0].OpenTime > time)
            {
                var numberOfLoadedBars = Bars.LoadMoreHistory();

                if (numberOfLoadedBars == 0)
                {
                    Chart.DrawStaticText("ScrollError", "Synchronized Scrolling: Can't load more data to keep in sync with other charts as more historical data is not available for this chart", VerticalAlignment.Bottom, HorizontalAlignment.Left, Color.Red);
                }
                else
                {
                    Log("Loading bars");
                }
            }
        }

        private void Chart_ScrollChanged(ChartScrollEventArgs obj)
        {
            TimeToScroll = null;

            if (_numberOfChartsToScroll > 0)
            {
                Interlocked.Decrement(ref _numberOfChartsToScroll);

                return;
            }

            var firstBarTime = obj.Chart.Bars.OpenTimes[obj.Chart.FirstVisibleBarIndex];

            if (_lastScrollTime == firstBarTime) return;

            _lastScrollTime = firstBarTime;

            switch (Mode)
            {
                case Mode.Symbol:
                    ScrollCharts(firstBarTime, indicator => indicator.SymbolName.Equals(SymbolName, StringComparison.Ordinal));
                    break;

                case Mode.TimeFrame:
                    ScrollCharts(firstBarTime, indicator => indicator.TimeFrame == TimeFrame);
                    break;

                default:
                    ScrollCharts(firstBarTime);
                    break;
            }
        }

        private void ScrollCharts(DateTime firstBarTime, Func<Indicator, bool> predicate = null)
        {
            var toScroll = new List<SynchronizedScrolling>(_indicatorInstances.Values.Count);

            foreach (var indicatorWeakReference in _indicatorInstances)
            {
                if (indicatorWeakReference.Value.IsAlive == false) continue;

                var indicator = (SynchronizedScrolling)indicatorWeakReference.Value.Target;

                if (indicator == this || (predicate != null && predicate(indicator) == false)) continue;

                toScroll.Add(indicator);
            }

            Interlocked.CompareExchange(ref _numberOfChartsToScroll, toScroll.Count, _numberOfChartsToScroll);

            Print("Charts To Scroll: ", _numberOfChartsToScroll);

            foreach (var indicator in toScroll)
            {
                try
                {
                    Print("Scrolling | {0} | {1} | {2} | {3:dd MMM yyyy HH:mm:ss}", indicator.SymbolName, indicator.TimeFrame, _numberOfChartsToScroll, firstBarTime);

                    indicator.ScrollXTo(firstBarTime);
                }
                catch (Exception ex)
                {
                    Log("An instance scrolling caused exception: | {0} | {1} | {2}", indicator.SymbolName, indicator.TimeFrame, ex);

                    Interlocked.Decrement(ref _numberOfChartsToScroll);
                }
            }
        }

        private void Log(string format, params object[] args)
        {
            var log = string.Format(format, args);

            var logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "cAlgo", "logs");

            if (Directory.Exists(logFolder) == false) Directory.CreateDirectory(logFolder);

            var logFilePath = Path.Combine(logFolder, string.Format("{0}.txt", _chartKey));

            File.AppendAllLines(logFilePath, new string[] { log });
        }

        private string GetChartKey(SynchronizedScrolling indicator)
        {
            return string.Format("{0}_{1}_{2}", indicator.SymbolName, indicator.TimeFrame, indicator.Chart.ChartType);
        }

        private bool GetIndicator(string chartKey, out SynchronizedScrolling indicator)
        {
            WeakReference weakReference;

            if (_indicatorInstances.TryGetValue(chartKey, out weakReference) && weakReference.IsAlive)
            {
                indicator = (SynchronizedScrolling)weakReference.Target;

                return true;
            }

            indicator = null;

            return false;
        }
    }

    public enum Mode
    {
        All,
        TimeFrame,
        Symbol
    }
}