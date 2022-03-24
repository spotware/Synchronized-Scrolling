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
        private static ConcurrentDictionary<string, IndicatorInstanceContainer<SynchronizedScrolling, DateTime?>> _indicatorInstances = new ConcurrentDictionary<string, IndicatorInstanceContainer<SynchronizedScrolling, DateTime?>>();

        private static int _numberOfChartsToScroll;

        private DateTime _lastScrollTime;

        private string _chartKey;

        [Parameter("Mode", DefaultValue = Mode.All)]
        public Mode Mode { get; set; }

        protected override void Initialize()
        {
            _chartKey = GetChartKey(this);

            IndicatorInstanceContainer<SynchronizedScrolling, DateTime?> oldIndicatorContainer;

            GetIndicatorInstanceContainer(_chartKey, out oldIndicatorContainer);

            //if (oldIndicatorContainer != null)
            //{
            //    if (oldIndicatorContainer.Data.HasValue)
            //    {
            //        Log("TimeToScroll: {0:dd MMM yyyy HH:mm:ss}", oldIndicatorContainer.Data.Value);
            //    }
            //    else
            //    {
            //        Log("TimeToScroll: null");
            //    }
            //}
            //else
            //{
            //    Log("No old indicator");
            //}

            _indicatorInstances.AddOrUpdate(_chartKey, new IndicatorInstanceContainer<SynchronizedScrolling, DateTime?>(this), (key, value) => new IndicatorInstanceContainer<SynchronizedScrolling, DateTime?>(this));

            if (oldIndicatorContainer != null && oldIndicatorContainer.Data.HasValue)
            {
                ScrollXTo(oldIndicatorContainer.Data.Value);
            }

            Chart.ScrollChanged += Chart_ScrollChanged;
        }

        public override void Calculate(int index)
        {
        }

        public void ScrollXTo(DateTime time)
        {
            //Log("ScrollXTo Called | {0} | {1} | {2:dd MMM yyyy HH:mm:ss}", SymbolName, TimeFrame, time);

            IndicatorInstanceContainer<SynchronizedScrolling, DateTime?> indicatorContainer;

            if (GetIndicatorInstanceContainer(_chartKey, out indicatorContainer))
            {
                indicatorContainer.Data = time;
            }

            if (Bars[0].OpenTime > time)
            {
                LoadMoreHistory();
            }
            else
            {
                //Log("Chart.ScrollXTo Called | {0} | {1} | {2:dd MMM yyyy HH:mm:ss}", SymbolName, TimeFrame, time);

                Chart.ScrollXTo(time);
            }
        }

        private void LoadMoreHistory()
        {
            //Log("Loading bars");

            var numberOfLoadedBars = Bars.LoadMoreHistory();

            if (numberOfLoadedBars == 0)
            {
                Chart.DrawStaticText("ScrollError", "Synchronized Scrolling: Can't load more data to keep in sync with other charts as more historical data is not available for this chart", VerticalAlignment.Bottom, HorizontalAlignment.Left, Color.Red);
            }
        }

        private void Chart_ScrollChanged(ChartScrollEventArgs obj)
        {
            IndicatorInstanceContainer<SynchronizedScrolling, DateTime?> indicatorContainer;

            if (GetIndicatorInstanceContainer(_chartKey, out indicatorContainer))
            {
                indicatorContainer.Data = null;
            }

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

            foreach (var indicatorContianer in _indicatorInstances)
            {
                SynchronizedScrolling indicator;

                if (indicatorContianer.Value.GetIndicator(out indicator) == false || indicator == this || (predicate != null && predicate(indicator) == false)) continue;

                toScroll.Add(indicator);
            }

            Interlocked.CompareExchange(ref _numberOfChartsToScroll, toScroll.Count, _numberOfChartsToScroll);

            //Log("Charts To Scroll: ", _numberOfChartsToScroll);

            foreach (var indicator in toScroll)
            {
                try
                {
                    //Log("Scrolling | {0} | {1} | {2} | {3:dd MMM yyyy HH:mm:ss}", indicator.SymbolName, indicator.TimeFrame, _numberOfChartsToScroll, firstBarTime);

                    indicator.ScrollXTo(firstBarTime);
                }
                catch (Exception ex)
                {
                    //Print("An instance scrolling caused exception: | {0} | {1} | {2}", indicator.SymbolName, indicator.TimeFrame, ex);

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

        private bool GetIndicatorInstanceContainer(string chartKey, out IndicatorInstanceContainer<SynchronizedScrolling, DateTime?> indicatorContainer)
        {
            if (_indicatorInstances.TryGetValue(chartKey, out indicatorContainer))
            {
                return true;
            }

            indicatorContainer = null;

            return false;
        }
    }

    public enum Mode
    {
        All,
        TimeFrame,
        Symbol
    }

    public class IndicatorInstanceContainer<T, TData> where T : Indicator
    {
        public readonly WeakReference _indicatorWeakReference;

        public IndicatorInstanceContainer(T indicator)
        {
            _indicatorWeakReference = new WeakReference(indicator);
        }

        public TData Data { get; set; }

        public bool GetIndicator(out T indicator)
        {
            if (_indicatorWeakReference.IsAlive)
            {
                indicator = (T)_indicatorWeakReference.Target;

                return true;
            }

            indicator = null;

            return false;
        }
    }
}