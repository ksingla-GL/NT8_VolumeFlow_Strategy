#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using System.Windows;
using System.Windows.Media;
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.TTW
{
    /// <summary>
    /// TTWVolumeFlow - Detects significant volume spikes and marks potential stop/reversal zones.
    /// Optional: ATR Trailing Stop Filter, Alerts, Session Filter, Multi-Series Protection, ATR Stop Plots.
    /// OPTIMIZED VERSION - M3 with Advanced Volume Analysis
    /// </summary>
    [Description("Detects significant volume spikes and marks potential stop/reversal zones. M3 with advanced volume analysis.")]
    public class TTWVolumeFlowOptimized : Indicator
    {
        // M3 Enums - replaced with constants for NinjaScript compatibility
        // VolumeReferenceType: 0=SMA, 1=EMA, 2=Highest, 3=TrimmedMean, 4=Median
        // SpikeDetectionMode: 0=None, 1=Multiplier, 2=ZScore
        #region Private Variables
        // Indicators
        private SMA volumeSMA;
        private ATR atr;
        private ATR atrTrailing;

        // Font
        private NinjaTrader.Gui.Tools.SimpleFont labelFont;

        // ATR Trailing Stop series (Harry's approach)
        private Series<double> preliminaryTrend;
        private Series<double> trend;
        private Series<double> currentStopLong;
        private Series<double> currentStopShort;

        // Alert State (-1 bear, 0 none, 1 bull)
        private Series<int> signalState;

        // Session bar counter
        private int sessionBarCount;

        // ATR Stop tracking
        private bool stoppedOut;
        private double trailingAmount;

        // Version string
        private string versionString = "v3.0";

        // M3 FUNCTIONAL ENRICHMENT - Volume Analysis
        private Series<double> referenceVolume;
        private EMA volumeEMA;
        private MAX volumeMAX;
        private StdDev volumeStdDevIndicator;
        private List<double> volumeBuffer;
        private Series<bool> spikeState;
        private Series<int> lastBullSignalBar, lastBearSignalBar;
        private double cachedReferenceVolume;
        private double cachedVolumeZScore;
        private bool isSpikeDetected, isNormalizedVolume;
        
        // M3 Drawing constants
        private const string SPIKE_BULL = "SpikeBull";
        private const string SPIKE_BEAR = "SpikeBear";

        // PERFORMANCE OPTIMIZATIONS - Cached values
        private double cachedVolumeSMA;
        private double cachedATR;
        private double cachedATRTrailing;
        private double cachedCurrentVolume;
        private bool cacheValid;
        private int lastBarCached = -1;

        // Cached bar metrics
        private double cachedOpen;
        private double cachedClose;
        private double cachedHigh;
        private double cachedLow;
        private double cachedBarHeight;

        // Drawing optimization
        private Dictionary<int, string[]> activeDrawings;
        private const string BULL_SIGNAL = "BullVF";
        private const string BEAR_SIGNAL = "BearVF";
        private const string BULL_LABEL = "BullLbl";
        private const string BEAR_LABEL = "BearLbl";

        // PERFORMANCE TESTING - REMOVE AFTER OPTIMIZATION VERIFICATION
        private System.Diagnostics.Stopwatch perfTimer;
        private long totalExecutionTime;
        private int executionCount;
        private long peakMemory;
        private long startMemory;
        private DateTime testStartTime;
        #endregion

        #region Properties
        // --- Main Parameters ---
        [NinjaScriptProperty]
        [Range(1.0, double.MaxValue)]
        [Display(Name = "Volume Multiplier", Description = "Multiplier for volume spike detection", Order = 1, GroupName = "01. Core Parameters")]
        public double VolumeMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Volume Period", Description = "Period for average volume calculation (SMA)", Order = 2, GroupName = "01. Core Parameters")]
        public int VolumePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Description = "Period for ATR calculation", Order = 3, GroupName = "01. Core Parameters")]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Multiplier", Description = "Price confirmation via ATR distance", Order = 4, GroupName = "01. Core Parameters")]
        public double AtrMultiplier { get; set; }

        // --- Spacing Parameters ---
        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Arrow Offset Factor", Description = "Symbol distance relative to bar height (0.1-5.0)", Order = 1, GroupName = "02. Spacing")]
        public double ArrowOffsetFactor { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Volume Offset Factor", Description = "Vertical offset of volume label relative to bar height (0.1-5.0)", Order = 2, GroupName = "02. Spacing")]
        public double VolumeOffsetFactor { get; set; }

        // --- Display Parameters ---
        [NinjaScriptProperty]
        [Display(Name = "Show Volume Label", Description = "Display volume label", Order = 1, GroupName = "03. Display")]
        public bool ShowLabel { get; set; }

        [NinjaScriptProperty]
        [Range(0, 4)]
        [Display(Name = "Symbol Type", Description = "0=Arrow, 1=Diamond, 2=Square, 3=Triangle, 4=Line", Order = 2, GroupName = "03. Display")]
        public int SymbolType { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Symbol Size", Description = "Symbol size (Line type uses this for width)", Order = 3, GroupName = "03. Display")]
        public int SymbolSize { get; set; }

        // --- Visual Colors ---
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bullish Color", Description = "Color for bullish stops", Order = 1, GroupName = "04. Visual")]
        public Brush BullishColor { get; set; }
        [Browsable(false)]
        public string BullishColorSerializable { get { return Serialize.BrushToString(BullishColor); } set { BullishColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bearish Color", Description = "Color for bearish stops", Order = 2, GroupName = "04. Visual")]
        public Brush BearishColor { get; set; }
        [Browsable(false)]
        public string BearishColorSerializable { get { return Serialize.BrushToString(BearishColor); } set { BearishColor = Serialize.StringToBrush(value); } }

        // --- Label Appearance ---
        [NinjaScriptProperty]
        [Display(Name = "Label Font Family", Description = "Font family for labels", Order = 1, GroupName = "05. Label Appearance")]
        public string LabelFontFamily { get; set; }

        [NinjaScriptProperty]
        [Range(8, 24)]
        [Display(Name = "Label Font Size", Description = "Font size", Order = 2, GroupName = "05. Label Appearance")]
        public int LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Label Font Bold", Description = "Bold font", Order = 3, GroupName = "05. Label Appearance")]
        public bool LabelFontBold { get; set; }

        // --- ATR Trailing Stop ---
        [NinjaScriptProperty]
        [Display(Name = "Enable ATR Trailing Stop Filter", Description = "Filter volume signals using ATR Trailing", Order = 1, GroupName = "06. ATR Trailing Stop")]
        public bool EnableAtrTrailingFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Trailing Period", Description = "Period for ATR Trailing calculation", Order = 2, GroupName = "06. ATR Trailing Stop")]
        public int AtrTrailingPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Trailing Multiplier", Description = "Multiplier for ATR Trailing", Order = 3, GroupName = "06. ATR Trailing Stop")]
        public double AtrTrailingMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Stop Line", Description = "Display ATR trailing stop line", Order = 4, GroupName = "06. ATR Trailing Stop")]
        public bool ShowStopLine { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "ATR Long Stop Color", Description = "Color for long stop line", Order = 5, GroupName = "06. ATR Trailing Stop")]
        public Brush AtrLongStopColor { get; set; }
        [Browsable(false)]
        public string AtrLongStopColorSerializable { get { return Serialize.BrushToString(AtrLongStopColor); } set { AtrLongStopColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "ATR Short Stop Color", Description = "Color for short stop line", Order = 6, GroupName = "06. ATR Trailing Stop")]
        public Brush AtrShortStopColor { get; set; }
        [Browsable(false)]
        public string AtrShortStopColorSerializable { get { return Serialize.BrushToString(AtrShortStopColor); } set { AtrShortStopColor = Serialize.StringToBrush(value); } }

        // --- M3 Volume Analysis ---
        [NinjaScriptProperty]
        [Range(0, 4)]
        [Display(Name = "Volume Reference Type", Description = "0=SMA, 1=EMA, 2=Highest, 3=TrimmedMean, 4=Median", Order = 1, GroupName = "07. Volume Analysis")]
        public int VolumeRefType { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2)]
        [Display(Name = "Spike Detection Mode", Description = "0=None, 1=Multiplier, 2=ZScore", Order = 2, GroupName = "07. Volume Analysis")]
        public int SpikeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5.0)]
        [Display(Name = "Z-Score Threshold", Description = "Z-Score threshold for spike detection", Order = 3, GroupName = "07. Volume Analysis")]
        public double ZScoreThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Volume Normalization", Description = "Normalize volume for cross-instrument comparison", Order = 4, GroupName = "07. Volume Analysis")]
        public bool EnableVolumeNormalization { get; set; }

        // --- M3 Signal Management ---
        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Min Bars Between Signals", Description = "Minimum bars between signals (debounce)", Order = 1, GroupName = "08. Signal Management")]
        public int MinBarsBetweenSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Spike Markers", Description = "Display spike detection markers", Order = 2, GroupName = "08. Signal Management")]
        public bool ShowSpikeMarkers { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Spike Color", Description = "Color for spike markers", Order = 3, GroupName = "08. Signal Management")]
        public Brush SpikeColor { get; set; }
        [Browsable(false)]
        public string SpikeColorSerializable { get { return Serialize.BrushToString(SpikeColor); } set { SpikeColor = Serialize.StringToBrush(value); } }

        // --- Advanced / Filters ---
        [NinjaScriptProperty]
        [Display(Name = "Process Secondary Series", Description = "Process additional data series", Order = 1, GroupName = "09. Advanced")]
        public bool ProcessSecondarySeries { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Ignore First Bars of Session", Description = "Ignore first X bars of each session", Order = 2, GroupName = "09. Advanced")]
        public int IgnoreFirstBarsOfSession { get; set; }

        // --- Alerts ---
        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Description = "Alert on new signal (once per bar)", Order = 1, GroupName = "10. Alerts")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert Sound", Description = "Sound file (e.g., Alert1.wav)", Order = 2, GroupName = "10. Alerts")]
        public string AlertSound { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Spike Alert Sound", Description = "Sound file for spike alerts", Order = 3, GroupName = "10. Alerts")]
        public string SpikeAlertSound { get; set; }

        // --- Version ---
        [XmlIgnore]
        [Display(Name = "Release and date", Description = "Release version and date", Order = 0, GroupName = "11. Version")]
        public string VersionString
        {
            get { return versionString; }
            set {; }
        }

        // Public access to trend for strategies
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Trend
        {
            get { return trend; }
        }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TTWVolumeFlowOptimized";
                Description = "Detects significant volume spikes; optional with ATR Trailing Stop Filter.";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // Defaults
                VolumeMultiplier = 1.5;
                VolumePeriod = 20;
                AtrPeriod = 14;
                AtrMultiplier = 0.75;
                ArrowOffsetFactor = 0.2;
                VolumeOffsetFactor = 0.3;
                ShowLabel = true;
                SymbolType = 0;
                SymbolSize = 3;
                BullishColor = Brushes.Green;
                BearishColor = Brushes.Red;

                LabelFontFamily = "Arial";
                LabelFontSize = 12;
                LabelFontBold = true;

                EnableAtrTrailingFilter = true;  // Default to true per client request
                AtrTrailingPeriod = 20;    // Client specified default
                AtrTrailingMultiplier = 3.5;   // Client specified default
                ShowStopLine = true;
                AtrLongStopColor = Brushes.DodgerBlue;
                AtrShortStopColor = Brushes.OrangeRed;

                ProcessSecondarySeries = false;
                IgnoreFirstBarsOfSession = 0;

                EnableAlerts = false;
                AlertSound = "Alert1.wav";

                // M3 Defaults (backward compatible)
                VolumeRefType = 0;  // SMA - Keep SMA for output parity
                SpikeMode = 1;  // Multiplier - Keep existing logic
                ZScoreThreshold = 2.5;
                EnableVolumeNormalization = false;
                MinBarsBetweenSignals = 0;  // Disabled by default
                ShowSpikeMarkers = false;
                SpikeColor = Brushes.Orange;
                SpikeAlertSound = "Alert2.wav";

                // Initialize collections
                activeDrawings = new Dictionary<int, string[]>(256);

                // Plots
                AddPlot(new Stroke(BullishColor, 2), PlotStyle.Dot, "BullishVolumeFlow");
                AddPlot(new Stroke(BearishColor, 2), PlotStyle.Dot, "BearishVolumeFlow");
                AddPlot(new Stroke(AtrLongStopColor, 2), PlotStyle.Line, "StopLine");
            }
            else if (State == State.Configure)
            {
                volumeSMA = SMA(Volume, VolumePeriod);
                atr = ATR(AtrPeriod);
                atrTrailing = (AtrTrailingPeriod != AtrPeriod) ? ATR(AtrTrailingPeriod) : atr;
                
                // M3: Initialize volume reference indicators
                volumeEMA = EMA(Volume, VolumePeriod);
                volumeMAX = MAX(Volume, VolumePeriod);
                volumeStdDevIndicator = StdDev(Volume, VolumePeriod);
            }
            else if (State == State.DataLoaded)
            {
                labelFont = new NinjaTrader.Gui.Tools.SimpleFont(LabelFontFamily, LabelFontSize) { Bold = LabelFontBold };

                // Use optimized MaximumBarsLookBack settings
                preliminaryTrend = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                trend = new Series<double>(this, MaximumBarsLookBack.Infinite);
                currentStopLong = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                currentStopShort = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                signalState = new Series<int>(this, MaximumBarsLookBack.TwoHundredFiftySix);

                // M3: Initialize series and collections
                referenceVolume = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                spikeState = new Series<bool>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                lastBullSignalBar = new Series<int>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                lastBearSignalBar = new Series<int>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                volumeBuffer = new List<double>(VolumePeriod);

                // Set stop line plot color dynamically
                Plots[2].Brush = AtrLongStopColor;

                // PERFORMANCE TESTING INIT
                perfTimer = new System.Diagnostics.Stopwatch();
                startMemory = GC.GetTotalMemory(true);
                testStartTime = DateTime.Now;
                Print("=== PERFORMANCE TEST STARTED (OPTIMIZED M3 v3.0) ===");
            }
            else if (State == State.Terminated)
            {
                // PERFORMANCE TESTING OUTPUT
                if (executionCount > 0)
                {
                    Print("=== PERFORMANCE TEST RESULTS (OPTIMIZED M3 v3.0) ===");
                    Print($"Test Duration: {(DateTime.Now - testStartTime).TotalSeconds:F2} seconds");
                    Print($"Total Bars Processed: {executionCount}");
                    Print($"Average Time per Bar: {totalExecutionTime / executionCount / 10} microseconds");
                    Print($"Peak Memory Usage: {peakMemory / 1024.0 / 1024.0:F2} MB");
                    Print($"Final Memory Usage: {(GC.GetTotalMemory(false) - startMemory) / 1024.0 / 1024.0:F2} MB");
                    Print("=============================================");
                }

                // Cleanup
                labelFont = null;
                preliminaryTrend = null;
                trend = null;
                currentStopLong = null;
                currentStopShort = null;
                signalState = null;
                
                // M3: Cleanup new series
                referenceVolume = null;
                spikeState = null;
                lastBullSignalBar = null;
                lastBearSignalBar = null;
                volumeBuffer?.Clear();
                volumeBuffer = null;
                
                activeDrawings?.Clear();
                activeDrawings = null;
            }
        }

        protected override void OnBarUpdate()
        {
            // START PERFORMANCE MEASUREMENT
            perfTimer.Restart();
            long memBefore = GC.GetTotalMemory(false);

            // Multi-Series: only process primary series if not allowed
            if (!ProcessSecondarySeries && BarsInProgress != 0)
                return;

            // Initialize trend on first bars
            if (CurrentBar < 2)
            {
                preliminaryTrend[0] = 1.0;
                trend[0] = 1.0;
                currentStopLong[0] = Close[0] - TickSize * 2;
                currentStopShort[0] = Close[0] + TickSize * 2;
                Values[2][0] = currentStopLong[0];
                PlotBrushes[2][0] = AtrLongStopColor;
                return;
            }

            // Session counting - optimized
            if (IsFirstTickOfBar)
            {
                if (Bars.IsFirstBarOfSession)
                    sessionBarCount = 0;
                sessionBarCount++;
                cacheValid = false; // Invalidate cache on new bar
                stoppedOut = false; // Reset stopped out flag
                
                // M3: Fix debounce persistence - carry forward last signal bars
                if (CurrentBar > 0)
                {
                    lastBullSignalBar[0] = lastBullSignalBar[1];
                    lastBearSignalBar[0] = lastBearSignalBar[1];
                }
            }

            // Minimum bars check
            int requiredBars = Math.Max(VolumePeriod, Math.Max(AtrPeriod, AtrTrailingPeriod));

            // Keep the Harry ladder running on EVERY bar >= 2 so [1] is always valid
            UpdateATRTrailingStop();

            if (CurrentBar < requiredBars || sessionBarCount <= IgnoreFirstBarsOfSession)
            {
                // Hide signals during warm-up; line will still draw (next edit)
                SetPlotValues(double.NaN, double.NaN);
                UpdateStopLinePlot();
                CleanupBarDrawings(CurrentBar);
                RecordPerformanceMetrics(memBefore);
                return;
            }

            // Cache values once per bar
            UpdateCachedValues();

            // ALWAYS calculate ATR Trailing Stop (for the line display)
            UpdateATRTrailingStop();

            // Early exit checks with cached values
            if (cachedReferenceVolume <= 0 || double.IsNaN(cachedReferenceVolume))
            {
                SetPlotValues(double.NaN, double.NaN);
                UpdateStopLinePlot();
                CleanupBarDrawings(CurrentBar);
                RecordPerformanceMetrics(memBefore);
                return;
            }

            // M3: Spike detection logic
            bool isVolumeSpike = false;
            switch (SpikeMode)
            {
                case 0: // None
                    isVolumeSpike = true; // Always process signals
                    break;
                case 1: // Multiplier
                    isVolumeSpike = cachedCurrentVolume >= cachedReferenceVolume * VolumeMultiplier;
                    break;
                case 2: // ZScore
                    isVolumeSpike = Math.Abs(cachedVolumeZScore) >= ZScoreThreshold;
                    break;
            }

            // Store spike state
            isSpikeDetected = isVolumeSpike && SpikeMode != 0;
            spikeState[0] = isSpikeDetected;

            bool isBullishStop = false;
            bool isBearishStop = false;

            if (isVolumeSpike)
            {
                double atrThreshold = AtrMultiplier * cachedATR;
                isBullishStop = (cachedClose > cachedOpen + atrThreshold);
                isBearishStop = (cachedClose < cachedOpen - atrThreshold);

                // M3: Signal debounce mechanism
                if (MinBarsBetweenSignals > 0)
                {
                    if (isBullishStop && CurrentBar - lastBullSignalBar[0] < MinBarsBetweenSignals)
                        isBullishStop = false;
                    if (isBearishStop && CurrentBar - lastBearSignalBar[0] < MinBarsBetweenSignals)
                        isBearishStop = false;
                }

                // Apply ATR Trailing Filter if enabled (only affects signals, not line display)
                if (EnableAtrTrailingFilter && CurrentBar > 2)
                {
                    double currentTrend = trend[0];
                    if (currentTrend > 0.5)
                        isBearishStop = false; // No bearish signals in uptrend
                    else if (currentTrend < -0.5)
                        isBullishStop = false; // No bullish signals in downtrend
                }

                // Update last signal bars
                if (isBullishStop) lastBullSignalBar[0] = CurrentBar;
                if (isBearishStop) lastBearSignalBar[0] = CurrentBar;
            }

            // Set plot values for signals
            SetPlotValues(
                isBullishStop ? cachedLow - 2 * TickSize : double.NaN,
                isBearishStop ? cachedHigh + 2 * TickSize : double.NaN
            );

            // Update stop line display (decoupled from filter)
            UpdateStopLinePlot();

            // M3: Enhanced drawing logic with spike visualization
            if (isBullishStop || isBearishStop || (ShowSpikeMarkers && isSpikeDetected))
            {
                ManageSignalDrawings(CurrentBar, isBullishStop, isBearishStop, isSpikeDetected);
            }
            else
            {
                CleanupBarDrawings(CurrentBar);
            }

            // M3: Enhanced alerts with spike differentiation
            if (IsFirstTickOfBar && EnableAlerts)
            {
                int newState = isBullishStop ? 1 : (isBearishStop ? -1 : 0);
                if (newState != 0 && CurrentBar > 0 && signalState[1] != newState)
                {
                    bool isSpike = isSpikeDetected && (isBullishStop || isBearishStop);
                    string alertType = isSpike ? "Spike" : "Normal";
                    string soundFile = isSpike ? SpikeAlertSound : AlertSound;
                    
                    Alert($"TTWVolumeFlow_{alertType}_{(newState == 1 ? "Bull" : "Bear")}_{CurrentBar}",
                          Priority.Medium,
                          $"TTWVolumeFlow {alertType} {(newState == 1 ? "Bullish" : "Bearish")} Signal",
                          soundFile, 0, null, null);
                }
                
                // Spike-only alerts
                if (ShowSpikeMarkers && isSpikeDetected && !isBullishStop && !isBearishStop)
                {
                    Alert($"TTWVolumeFlow_SpikeOnly_{CurrentBar}", Priority.Low,
                          "TTWVolumeFlow Volume Spike Detected", SpikeAlertSound, 0, null, null);
                }
                
                signalState[0] = newState;
            }

            // END PERFORMANCE MEASUREMENT
            RecordPerformanceMetrics(memBefore);
        }

        #region Optimized Helper Methods

        private void UpdateCachedValues()
        {
            if (!cacheValid || lastBarCached != CurrentBar)
            {
                cachedVolumeSMA = volumeSMA[0];
                cachedATR = atr[0];
                cachedCurrentVolume = Volume[0];
                cachedOpen = Open[0];
                cachedClose = Close[0];
                cachedHigh = High[0];
                cachedLow = Low[0];

                // Calculate bar height once with fallback
                cachedBarHeight = cachedHigh - cachedLow;
                if (cachedBarHeight <= TickSize * 0.5)
                    cachedBarHeight = TickSize * 2.0;

                if (atrTrailing != atr)
                    cachedATRTrailing = atrTrailing[0];
                else
                    cachedATRTrailing = cachedATR;

                // M3: Update reference volume and Z-Score
                UpdateReferenceVolume();
                
                // M3: Z-Score calculation using selected baseline for consistency
                if (volumeStdDevIndicator[0] > 1e-9)
                    cachedVolumeZScore = (cachedCurrentVolume - cachedReferenceVolume) / volumeStdDevIndicator[0];
                else
                    cachedVolumeZScore = 0;

                cacheValid = true;
                lastBarCached = CurrentBar;
            }
        }

        private void UpdateReferenceVolume()
        {
            switch (VolumeRefType)
            {
                case 0: // SMA
                    cachedReferenceVolume = volumeSMA[0];
                    break;
                case 1: // EMA
                    cachedReferenceVolume = volumeEMA[0];
                    break;
                case 2: // Highest
                    cachedReferenceVolume = volumeMAX[0];
                    break;
                case 3: // TrimmedMean
                    cachedReferenceVolume = CalculateTrimmedMean();
                    break;
                case 4: // Median
                    cachedReferenceVolume = CalculateMedian();
                    break;
                default:
                    cachedReferenceVolume = volumeSMA[0];
                    break;
            }
            referenceVolume[0] = cachedReferenceVolume;
        }


        private double CalculateTrimmedMean()
        {
            if (CurrentBar < VolumePeriod) return volumeSMA[0];
            
            volumeBuffer.Clear();
            for (int i = 0; i < VolumePeriod; i++)
                volumeBuffer.Add(Volume[i]);
            
            volumeBuffer.Sort();
            int trimCount = (int)(VolumePeriod * 0.1);  // Remove top/bottom 10%
            double sum = 0;
            int count = VolumePeriod - (2 * trimCount);
            
            for (int i = trimCount; i < VolumePeriod - trimCount; i++)
                sum += volumeBuffer[i];
            
            return count > 0 ? sum / count : volumeSMA[0];
        }

        private double CalculateMedian()
        {
            if (CurrentBar < VolumePeriod) return volumeSMA[0];
            
            volumeBuffer.Clear();
            for (int i = 0; i < VolumePeriod; i++)
                volumeBuffer.Add(Volume[i]);
            
            volumeBuffer.Sort();
            int mid = VolumePeriod / 2;
            
            if (VolumePeriod % 2 == 0)
                return (volumeBuffer[mid - 1] + volumeBuffer[mid]) / 2;
            else
                return volumeBuffer[mid];
        }

        private void UpdateATRTrailingStop()
        {
            if (IsFirstTickOfBar)
            {
                // Calculate trailing amount
                double offset = Math.Max(TickSize, atrTrailing[1]);
                trailingAmount = AtrTrailingMultiplier * offset;

                // Update stops based on trend (Harry's step-ladder approach)
                if (preliminaryTrend[1] > 0.5) // Uptrend
                {
                    // Long stop can only move up or stay the same
                    currentStopLong[0] = Math.Max(currentStopLong[1], Math.Min(Close[1] - trailingAmount, Close[1] - TickSize));
                    currentStopShort[0] = Close[1] + trailingAmount;
                }
                else // Downtrend
                {
                    // Short stop can only move down or stay the same
                    currentStopShort[0] = Math.Min(currentStopShort[1], Math.Max(Close[1] + trailingAmount, Close[1] + TickSize));
                    currentStopLong[0] = Close[1] - trailingAmount;
                }
            }

            // Check for trend reversal (only one reversal per bar allowed)
            if (Calculate == Calculate.OnPriceChange && !stoppedOut)
            {
                if (preliminaryTrend[1] > 0.5 && Low[0] < currentStopLong[0])
                {
                    preliminaryTrend[0] = -1.0;
                    stoppedOut = true;
                }
                else if (preliminaryTrend[1] < -0.5 && High[0] > currentStopShort[0])
                {
                    preliminaryTrend[0] = 1.0;
                    stoppedOut = true;
                }
                else
                {
                    preliminaryTrend[0] = preliminaryTrend[1];
                }
            }
            else if (Calculate == Calculate.OnBarClose)
            {
                if (preliminaryTrend[1] > 0.5 && Close[0] < currentStopLong[0])
                    preliminaryTrend[0] = -1.0;
                else if (preliminaryTrend[1] < -0.5 && Close[0] > currentStopShort[0])
                    preliminaryTrend[0] = 1.0;
                else
                    preliminaryTrend[0] = preliminaryTrend[1];
            }

            // Update confirmed trend
            if (Calculate == Calculate.OnBarClose)
                trend[0] = preliminaryTrend[0];
            else if (IsFirstTickOfBar)
                trend[0] = preliminaryTrend[1];
            else
                trend[0] = preliminaryTrend[0];
        }

        private void UpdateStopLinePlot()
        {
            // ALWAYS show stop line when ShowStopLine is true, regardless of filter
            if (ShowStopLine && CurrentBar >= 2)
            {
                if (trend[0] > 0.5)
                {
                    Values[2][0] = currentStopLong[0];
                    PlotBrushes[2][0] = AtrLongStopColor;
                }
                else if (trend[0] < -0.5)
                {
                    Values[2][0] = currentStopShort[0];
                    PlotBrushes[2][0] = AtrShortStopColor;
                }
                else
                {
                    // Neutral trend - use previous stop
                    Values[2][0] = preliminaryTrend[0] > 0 ? currentStopLong[0] : currentStopShort[0];
                    PlotBrushes[2][0] = preliminaryTrend[0] > 0 ? AtrLongStopColor : AtrShortStopColor;
                }
            }
            else
            {
                Values[2][0] = double.NaN;
                PlotBrushes[2][0] = Brushes.Transparent;
            }
        }

        private void SetPlotValues(double bull, double bear)
        {
            Values[0][0] = bull;
            Values[1][0] = bear;
        }

        private void ManageSignalDrawings(int barIndex, bool isBullish, bool isBearish, bool isSpike)
        {
            string[] tags = GetOrCreateDrawingTags(barIndex);

            if (isBullish)
            {
                double symbolY = cachedLow - (cachedBarHeight * ArrowOffsetFactor);
                DrawOptimizedSymbol(tags[0], true, symbolY, BullishColor);

                // M3: Draw spike marker if enabled
                if (ShowSpikeMarkers && isSpike)
                {
                    double spikeY = symbolY - (cachedBarHeight * 0.1);
                    Draw.Diamond(this, tags[4], false, 0, spikeY, SpikeColor);
                }

                if (ShowLabel)
                {
                    double labelY = symbolY - (cachedBarHeight * VolumeOffsetFactor);
                    double ratio = cachedReferenceVolume > 0 ? cachedCurrentVolume / cachedReferenceVolume : 0.0;
                    string text = EnableVolumeNormalization ? 
                        $"Vol: {(cachedCurrentVolume / cachedReferenceVolume * 100):F1}%" :
                        $"Vol: {cachedCurrentVolume:N0}  (x{ratio:F2})";
                    Draw.Text(this, tags[2], false, text, 0, labelY, 0, BullishColor, labelFont, TextAlignment.Center, null, null, 1);
                }
            }
            else if (isBearish)
            {
                double symbolY = cachedHigh + (cachedBarHeight * ArrowOffsetFactor);
                DrawOptimizedSymbol(tags[1], false, symbolY, BearishColor);

                // M3: Draw spike marker if enabled
                if (ShowSpikeMarkers && isSpike)
                {
                    double spikeY = symbolY + (cachedBarHeight * 0.1);
                    Draw.Diamond(this, tags[5], false, 0, spikeY, SpikeColor);
                }

                if (ShowLabel)
                {
                    double labelY = symbolY + (cachedBarHeight * VolumeOffsetFactor);
                    double ratio = cachedReferenceVolume > 0 ? cachedCurrentVolume / cachedReferenceVolume : 0.0;
                    string text = EnableVolumeNormalization ? 
                        $"Vol: {(cachedCurrentVolume / cachedReferenceVolume * 100):F1}%" :
                        $"Vol: {cachedCurrentVolume:N0}  (x{ratio:F2})";
                    Draw.Text(this, tags[3], false, text, 0, labelY, 0, BearishColor, labelFont, TextAlignment.Center, null, null, 1);
                }
            }
            else if (ShowSpikeMarkers && isSpike)
            {
                // Spike-only marker (no signal)
                double spikeY = (cachedHigh + cachedLow) / 2;
                Draw.Diamond(this, tags[4], false, 0, spikeY, SpikeColor);
            }
        }

        private string[] GetOrCreateDrawingTags(int barIndex)
        {
            if (!activeDrawings.TryGetValue(barIndex, out string[] tags))
            {
                tags = new string[]
                {
                    $"{BULL_SIGNAL}_{barIndex}",
                    $"{BEAR_SIGNAL}_{barIndex}",
                    $"{BULL_LABEL}_{barIndex}",
                    $"{BEAR_LABEL}_{barIndex}",
                    $"{SPIKE_BULL}_{barIndex}",
                    $"{SPIKE_BEAR}_{barIndex}"
                };
                activeDrawings[barIndex] = tags;
            }
            return tags;
        }

        private void CleanupBarDrawings(int barIndex)
        {
            if (activeDrawings.TryGetValue(barIndex, out string[] tags))
            {
                foreach (string tag in tags)
                {
                    RemoveDrawObject(tag);
                }
                activeDrawings.Remove(barIndex);
            }
        }

        private void DrawOptimizedSymbol(string tag, bool isBullish, double yPosition, Brush color)
        {
            switch (SymbolType)
            {
                case 0: // Arrow
                    if (isBullish)
                        Draw.ArrowUp(this, tag, false, 0, yPosition, color);
                    else
                        Draw.ArrowDown(this, tag, false, 0, yPosition, color);
                    break;
                case 1: // Diamond
                    Draw.Diamond(this, tag, false, 0, yPosition, color);
                    break;
                case 2: // Square
                    Draw.Square(this, tag, false, 0, yPosition, color);
                    break;
                case 3: // Triangle
                    if (isBullish)
                        Draw.TriangleUp(this, tag, false, 0, yPosition, color);
                    else
                        Draw.TriangleDown(this, tag, false, 0, yPosition, color);
                    break;
                case 4: // Line
                    Draw.Line(this, tag, false, 0, yPosition, 1, yPosition, color, DashStyleHelper.Solid, SymbolSize);
                    break;
            }
        }

        private void RecordPerformanceMetrics(long memBefore)
        {
            perfTimer.Stop();
            totalExecutionTime += perfTimer.ElapsedTicks;
            executionCount++;

            long memAfter = GC.GetTotalMemory(false);
            long currentMem = memAfter - startMemory;
            if (currentMem > peakMemory) peakMemory = currentMem;

            if (executionCount % 1000 == 0)
            {
                Print($"Processed {executionCount} bars | Avg time: {(totalExecutionTime / executionCount / 10)} microseconds");
            }
        }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private TTW.TTWVolumeFlowOptimized[] cacheTTWVolumeFlowOptimized;
        public TTW.TTWVolumeFlowOptimized TTWVolumeFlowOptimized(double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showStopLine, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
        {
            return TTWVolumeFlowOptimized(Input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showStopLine, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
        }

        public TTW.TTWVolumeFlowOptimized TTWVolumeFlowOptimized(ISeries<double> input, double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showStopLine, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
        {
            if (cacheTTWVolumeFlowOptimized != null)
                for (int idx = 0; idx < cacheTTWVolumeFlowOptimized.Length; idx++)
                    if (cacheTTWVolumeFlowOptimized[idx] != null && cacheTTWVolumeFlowOptimized[idx].VolumeMultiplier == volumeMultiplier && cacheTTWVolumeFlowOptimized[idx].VolumePeriod == volumePeriod && cacheTTWVolumeFlowOptimized[idx].AtrPeriod == atrPeriod && cacheTTWVolumeFlowOptimized[idx].AtrMultiplier == atrMultiplier && cacheTTWVolumeFlowOptimized[idx].ArrowOffsetFactor == arrowOffsetFactor && cacheTTWVolumeFlowOptimized[idx].VolumeOffsetFactor == volumeOffsetFactor && cacheTTWVolumeFlowOptimized[idx].ShowLabel == showLabel && cacheTTWVolumeFlowOptimized[idx].SymbolType == symbolType && cacheTTWVolumeFlowOptimized[idx].SymbolSize == symbolSize && cacheTTWVolumeFlowOptimized[idx].BullishColor == bullishColor && cacheTTWVolumeFlowOptimized[idx].BearishColor == bearishColor && cacheTTWVolumeFlowOptimized[idx].LabelFontFamily == labelFontFamily && cacheTTWVolumeFlowOptimized[idx].LabelFontSize == labelFontSize && cacheTTWVolumeFlowOptimized[idx].LabelFontBold == labelFontBold && cacheTTWVolumeFlowOptimized[idx].EnableAtrTrailingFilter == enableAtrTrailingFilter && cacheTTWVolumeFlowOptimized[idx].AtrTrailingPeriod == atrTrailingPeriod && cacheTTWVolumeFlowOptimized[idx].AtrTrailingMultiplier == atrTrailingMultiplier && cacheTTWVolumeFlowOptimized[idx].ShowStopLine == showStopLine && cacheTTWVolumeFlowOptimized[idx].AtrLongStopColor == atrLongStopColor && cacheTTWVolumeFlowOptimized[idx].AtrShortStopColor == atrShortStopColor && cacheTTWVolumeFlowOptimized[idx].ProcessSecondarySeries == processSecondarySeries && cacheTTWVolumeFlowOptimized[idx].IgnoreFirstBarsOfSession == ignoreFirstBarsOfSession && cacheTTWVolumeFlowOptimized[idx].EnableAlerts == enableAlerts && cacheTTWVolumeFlowOptimized[idx].AlertSound == alertSound && cacheTTWVolumeFlowOptimized[idx].EqualsInput(input))
                        return cacheTTWVolumeFlowOptimized[idx];
            return CacheIndicator<TTW.TTWVolumeFlowOptimized>(new TTW.TTWVolumeFlowOptimized() { VolumeMultiplier = volumeMultiplier, VolumePeriod = volumePeriod, AtrPeriod = atrPeriod, AtrMultiplier = atrMultiplier, ArrowOffsetFactor = arrowOffsetFactor, VolumeOffsetFactor = volumeOffsetFactor, ShowLabel = showLabel, SymbolType = symbolType, SymbolSize = symbolSize, BullishColor = bullishColor, BearishColor = bearishColor, LabelFontFamily = labelFontFamily, LabelFontSize = labelFontSize, LabelFontBold = labelFontBold, EnableAtrTrailingFilter = enableAtrTrailingFilter, AtrTrailingPeriod = atrTrailingPeriod, AtrTrailingMultiplier = atrTrailingMultiplier, ShowStopLine = showStopLine, AtrLongStopColor = atrLongStopColor, AtrShortStopColor = atrShortStopColor, ProcessSecondarySeries = processSecondarySeries, IgnoreFirstBarsOfSession = ignoreFirstBarsOfSession, EnableAlerts = enableAlerts, AlertSound = alertSound }, input, ref cacheTTWVolumeFlowOptimized);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.TTW.TTWVolumeFlowOptimized TTWVolumeFlowOptimized(double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showStopLine, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
        {
            return indicator.TTWVolumeFlowOptimized(Input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showStopLine, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
        }

        public Indicators.TTW.TTWVolumeFlowOptimized TTWVolumeFlowOptimized(ISeries<double> input, double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showStopLine, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
        {
            return indicator.TTWVolumeFlowOptimized(input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showStopLine, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.TTW.TTWVolumeFlowOptimized TTWVolumeFlowOptimized(double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showStopLine, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
        {
            return indicator.TTWVolumeFlowOptimized(Input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showStopLine, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
        }

        public Indicators.TTW.TTWVolumeFlowOptimized TTWVolumeFlowOptimized(ISeries<double> input, double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showStopLine, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
        {
            return indicator.TTWVolumeFlowOptimized(input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showStopLine, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
        }
    }
}

#endregion
