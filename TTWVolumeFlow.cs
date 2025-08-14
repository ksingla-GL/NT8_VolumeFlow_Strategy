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
using System.Windows;             // <-- Wichtig für TextAlignment
using System.Windows.Media;
using System.Text;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.TTW
{
    /// <summary>
    /// TTWVolumeFlow - erkennt signifikante Volumenspikes und markiert potentielle Stops/Umkehrzonen.
    /// Optional: ATR Trailing Stop Filter, Alerts, Session-Filter, Multi-Series-Schutz, ATR-Stop-Plots.
    /// </summary>
    [Description("Erkennt signifikante Volumenspikes und markiert potentielle Stops/Umkehrzonen. Optional mit ATR Trailing Stop Filter.")]
    public class TTWVolumeFlow : Indicator
    {
        // Internals
        private SMA volumeSMA;
        private ATR atr;
        private ATR atrTrailing;

        private NinjaTrader.Gui.Tools.SimpleFont labelFont;
        private StringBuilder sb;

        // ATR Trailing Stop series
        private Series<double> preliminaryTrend;
        private Series<double> trend;
        private Series<double> currentStopLong;
        private Series<double> currentStopShort;

        // Alert-State (–1 bear, 0 none, 1 bull)
        private Series<int> signalState;

        // Session-Bar-Zähler
        private int sessionBarCount;

        #region === Properties ===

        // --- Hauptparameter ---
        [NinjaScriptProperty]
        [Range(1.0, double.MaxValue)]
        [Display(Name = "Volume Multiplier", Description = "Multiplikator für Volumenspike-Erkennung", Order = 1, GroupName = "Parameters")]
        public double VolumeMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Volume Period", Description = "Periode für durchschnittliches Volumen (SMA)", Order = 2, GroupName = "Parameters")]
        public int VolumePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Description = "Periode für ATR", Order = 3, GroupName = "Parameters")]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Multiplier", Description = "Preisbestätigung per ATR-Abstand", Order = 4, GroupName = "Parameters")]
        public double AtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Arrow Offset Factor", Description = "Abstand des Symbols relativ zur Barhöhe (0.1–5.0)", Order = 5, GroupName = "Spacing")]
        public double ArrowOffsetFactor { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Volume Offset Factor", Description = "Vertikaler Offset des Volumen-Labels relativ zur Barhöhe (0.1–5.0)", Order = 6, GroupName = "Spacing")]
        public double VolumeOffsetFactor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Volume Label", Description = "Volumen-Label anzeigen", Order = 7, GroupName = "Display")]
        public bool ShowLabel { get; set; }

        [NinjaScriptProperty]
        [Range(0, 4)]
        [Display(Name = "Symbol Type", Description = "0=Arrow, 1=Diamond, 2=Square, 3=Triangle, 4=Line", Order = 8, GroupName = "Symbol")]
        public int SymbolType { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Symbol Size", Description = "Symbolgröße (nur Line hat Breite)", Order = 9, GroupName = "Symbol")]
        public int SymbolSize { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bullish Color", Description = "Farbe für bullische Stops", Order = 10, GroupName = "Visual")]
        public Brush BullishColor { get; set; }
        [Browsable(false)]
        public string BullishColorSerializable { get { return Serialize.BrushToString(BullishColor); } set { BullishColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bearish Color", Description = "Farbe für bärische Stops", Order = 11, GroupName = "Visual")]
        public Brush BearishColor { get; set; }
        [Browsable(false)]
        public string BearishColorSerializable { get { return Serialize.BrushToString(BearishColor); } set { BearishColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Display(Name = "Label Font Family", Description = "Schriftart der Labels", Order = 12, GroupName = "Label Appearance")]
        public string LabelFontFamily { get; set; }

        [NinjaScriptProperty]
        [Range(8, 24)]
        [Display(Name = "Label Font Size", Description = "Schriftgröße", Order = 13, GroupName = "Label Appearance")]
        public int LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Label Font Bold", Description = "Fettschrift", Order = 14, GroupName = "Label Appearance")]
        public bool LabelFontBold { get; set; }

        // --- ATR Trailing Stop ---
        [NinjaScriptProperty]
        [Display(Name = "Enable ATR Trailing Stop Filter", Description = "Volumensignale per ATR-Trailing filtern", Order = 1, GroupName = "ATR Trailing Stop")]
        public bool EnableAtrTrailingFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Trailing Period", Description = "Periode für ATR Trailing", Order = 2, GroupName = "ATR Trailing Stop")]
        public int AtrTrailingPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Trailing Multiplier", Description = "Multiplikator für ATR Trailing", Order = 3, GroupName = "ATR Trailing Stop")]
        public double AtrTrailingMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show ATR Stops", Description = "Zeigt die berechneten ATR-Stops als Linien an", Order = 4, GroupName = "ATR Trailing Stop")]
        public bool ShowAtrStops { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "ATR Long Stop Color", Description = "Farbe Long-Stop-Linie", Order = 5, GroupName = "ATR Trailing Stop")]
        public Brush AtrLongStopColor { get; set; }
        [Browsable(false)]
        public string AtrLongStopColorSerializable { get { return Serialize.BrushToString(AtrLongStopColor); } set { AtrLongStopColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "ATR Short Stop Color", Description = "Farbe Short-Stop-Linie", Order = 6, GroupName = "ATR Trailing Stop")]
        public Brush AtrShortStopColor { get; set; }
        [Browsable(false)]
        public string AtrShortStopColorSerializable { get { return Serialize.BrushToString(AtrShortStopColor); } set { AtrShortStopColor = Serialize.StringToBrush(value); } }

        // --- Advanced / Filters ---
        [NinjaScriptProperty]
        [Display(Name = "Process Secondary Series", Description = "Auch Zusatz-Datenserien verarbeiten", Order = 1, GroupName = "Advanced")]
        public bool ProcessSecondarySeries { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Ignore first bars of session", Description = "Erste X Bars je Session ignorieren", Order = 2, GroupName = "Advanced")]
        public int IgnoreFirstBarsOfSession { get; set; }

        // --- Alerts ---
        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Description = "Alert bei neuem Signal (1× je Bar)", Order = 1, GroupName = "Alerts")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert Sound", Description = "Sounddatei (z. B. Alert1.wav)", Order = 2, GroupName = "Alerts")]
        public string AlertSound { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                        = "TTWVolumeFlow";
                Description                 = "Erkennt signifikante Volumenspikes; optional mit ATR Trailing Stop Filter.";
                Calculate                   = Calculate.OnPriceChange;
                IsOverlay                   = true;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = true;
                DrawHorizontalGridLines     = true;
                DrawVerticalGridLines       = true;
                PaintPriceMarkers           = true;
                ScaleJustification          = ScaleJustification.Right;
                IsSuspendedWhileInactive    = true;

                // Defaults
                VolumeMultiplier            = 1.5;
                VolumePeriod                = 20;
                AtrPeriod                   = 14;
                AtrMultiplier               = 0.75;
                ArrowOffsetFactor           = 0.2;
                VolumeOffsetFactor          = 0.3;
                ShowLabel                   = true;
                SymbolType                  = 0;
                SymbolSize                  = 3;
                BullishColor                = Brushes.Green;
                BearishColor                = Brushes.Red;

                LabelFontFamily             = "Arial";
                LabelFontSize               = 12;
                LabelFontBold               = true;

                EnableAtrTrailingFilter     = false;
                AtrTrailingPeriod           = 10;
                AtrTrailingMultiplier       = 3.5;
                ShowAtrStops                = false;
                AtrLongStopColor            = Brushes.DodgerBlue;
                AtrShortStopColor           = Brushes.OrangeRed;

                ProcessSecondarySeries      = false;
                IgnoreFirstBarsOfSession    = 0;

                EnableAlerts                = false;
                AlertSound                  = "Alert1.wav";

                sb                          = new StringBuilder(64);

                // Plots
                AddPlot(new Stroke(BullishColor, 2), PlotStyle.Dot, "BullishVolumeFlow");
                AddPlot(new Stroke(BearishColor, 2), PlotStyle.Dot, "BearishVolumeFlow");
                AddPlot(Brushes.Transparent, "AtrLongStop");
                AddPlot(Brushes.Transparent, "AtrShortStop");
            }
            else if (State == State.Configure)
            {
                volumeSMA   = SMA(Volume, VolumePeriod);
                atr         = ATR(AtrPeriod);
                atrTrailing = (AtrTrailingPeriod != AtrPeriod) ? ATR(AtrTrailingPeriod) : atr;
            }
            else if (State == State.DataLoaded)
            {
                labelFont        = new NinjaTrader.Gui.Tools.SimpleFont(LabelFontFamily, LabelFontSize) { Bold = LabelFontBold };

                preliminaryTrend = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                trend            = new Series<double>(this, MaximumBarsLookBack.Infinite);
                currentStopLong  = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                currentStopShort = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);

                signalState      = new Series<int>(this, MaximumBarsLookBack.TwoHundredFiftySix);

                // Farben für ATR-Stop-Plots setzen
                Plots[2].Brush   = AtrLongStopColor;
                Plots[3].Brush   = AtrShortStopColor;
            }
            else if (State == State.Terminated)
            {
                sb                = null;
                labelFont         = null;
                preliminaryTrend  = null;
                trend             = null;
                currentStopLong   = null;
                currentStopShort  = null;
                signalState       = null;
            }
        }

        protected override void OnBarUpdate()
        {
            // Multi-Series: nur primäre Serie verarbeiten, wenn nicht erlaubt
            if (!ProcessSecondarySeries && BarsInProgress != 0)
                return;

            // Session-Zählung
            if (Bars.IsFirstBarOfSession && IsFirstTickOfBar)
                sessionBarCount = 0;
            if (IsFirstTickOfBar)
                sessionBarCount++;

            // Mindestbalken
            int requiredBars = Math.Max(VolumePeriod, Math.Max(AtrPeriod, AtrTrailingPeriod));
            if (CurrentBar < requiredBars || sessionBarCount <= IgnoreFirstBarsOfSession)
            {
                Values[0][0] = double.NaN;
                Values[1][0] = double.NaN;
                Values[2][0] = double.NaN;
                Values[3][0] = double.NaN;
                RemoveCurrentBarDraws(); // hängende Draws entfernen
                return;
            }

            // ATR Trailing Stop aktualisieren (nur 1× je Bar)
            if (EnableAtrTrailingFilter)
                CalculateAtrTrailingStop();

            // Cache/Guards
            double avgVolume = volumeSMA[0];
            if (avgVolume <= 0 || double.IsNaN(avgVolume) || double.IsInfinity(avgVolume))
            {
                Values[0][0] = double.NaN;
                Values[1][0] = double.NaN;
                Values[2][0] = double.NaN;
                Values[3][0] = double.NaN;
                RemoveCurrentBarDraws();
                return;
            }

            double currentVolume = Volume[0];
            double atrValue      = atr[0];

            bool isVolumeSpike   = currentVolume >= avgVolume * VolumeMultiplier;

            double openPrice  = Open[0];
            double closePrice = Close[0];
            double highPrice  = High[0];
            double lowPrice   = Low[0];

            bool isBullishStop = isVolumeSpike && (closePrice > openPrice + AtrMultiplier * atrValue);
            bool isBearishStop = isVolumeSpike && (closePrice < openPrice - AtrMultiplier * atrValue);

            if (EnableAtrTrailingFilter && CurrentBar > 2)
            {
                isBullishStop = isBullishStop && trend[0] > 0.5;
                isBearishStop = isBearishStop && trend[0] < -0.5;
            }

            // Plots setzen
            Values[0][0] = isBullishStop ? lowPrice  - 2 * TickSize : double.NaN;
            Values[1][0] = isBearishStop ? highPrice + 2 * TickSize : double.NaN;

            if (EnableAtrTrailingFilter && ShowAtrStops)
            {
                Values[2][0] = currentStopLong[0];
                Values[3][0] = currentStopShort[0];
            }
            else
            {
                Values[2][0] = double.NaN;
                Values[3][0] = double.NaN;
            }

            // Sichere Barhöhe (Doji/Nullrange-Fallback)
            double barHeight = highPrice - lowPrice;
            if (barHeight <= TickSize * 0.5)
                barHeight = TickSize * 2.0;

            // Tags pro Bar + BIP-Prefix
            string prefix  = $"BIP{BarsInProgress}_";
            string bullTag = $"{prefix}BullishVF_{CurrentBar}";
            string bearTag = $"{prefix}BearishVF_{CurrentBar}";
            string bullLbl = $"{prefix}BullishLabel_{CurrentBar}";
            string bearLbl = $"{prefix}BearishLabel_{CurrentBar}";

            // Draws erzeugen/entfernen
            try
            {
                if (isBullishStop)
                {
                    double symbolY = lowPrice - (barHeight * ArrowOffsetFactor);
                    DrawSymbol(bullTag, true, symbolY, BullishColor);

                    // Gegenseite wegräumen (falls zuvor vorhanden)
                    SafeRemove(bearTag);
                    SafeRemove(bearLbl);

                    if (ShowLabel)
                    {
                        double labelY  = symbolY - (barHeight * VolumeOffsetFactor);
                        double ratio   = avgVolume > 0 ? currentVolume / avgVolume : 0.0;
                        string text    = $"Vol: {currentVolume:N0}  (x{ratio:F2})";

                        Draw.Text(this, bullLbl, false, text, 0, labelY, 0, BullishColor, labelFont, TextAlignment.Center, null, null, 1);
                    }
                    else
                    {
                        SafeRemove(bullLbl);
                    }
                }
                else if (isBearishStop)
                {
                    double symbolY = highPrice + (barHeight * ArrowOffsetFactor);
                    DrawSymbol(bearTag, false, symbolY, BearishColor);

                    // Gegenseite wegräumen
                    SafeRemove(bullTag);
                    SafeRemove(bullLbl);

                    if (ShowLabel)
                    {
                        double labelY  = symbolY + (barHeight * VolumeOffsetFactor);
                        double ratio   = avgVolume > 0 ? currentVolume / avgVolume : 0.0;
                        string text    = $"Vol: {currentVolume:N0}  (x{ratio:F2})";

                        Draw.Text(this, bearLbl, false, text, 0, labelY, 0, BearishColor, labelFont, TextAlignment.Center, null, null, 1);
                    }
                    else
                    {
                        SafeRemove(bearLbl);
                    }
                }
                else
                {
                    // Kein Signal -> alle Draws dieses Bars entfernen
                    SafeRemove(bullTag);
                    SafeRemove(bullLbl);
                    SafeRemove(bearTag);
                    SafeRemove(bearLbl);
                }
            }
            catch (Exception ex)
            {
                Print("Fehler beim Zeichnen: " + ex.Message);
            }

            // Alerts (1× je Bar, bei Zustandswechsel)
            int state = isBullishStop ? 1 : (isBearishStop ? -1 : 0);
            if (IsFirstTickOfBar && EnableAlerts && state != 0 && signalState[1] != state)
            {
                string alertName = state == 1 ? "TTWVolumeFlow_Bull" : "TTWVolumeFlow_Bear";
                Alert(alertName, Priority.Medium, alertName, AlertSound, 0, null, null);
            }
            signalState[0] = state;
        }

        /// <summary>
        /// ATR Trailing Stop + Trendbestimmung (Update 1× je Bar)
        /// </summary>
        private void CalculateAtrTrailingStop()
        {
            if (CurrentBar < 2)
            {
                preliminaryTrend[0] = 1.0;
                trend[0]            = 1.0;
                currentStopLong[0]  = Close[0];
                currentStopShort[0] = Close[0];
                return;
            }

            double trailingAmount = AtrTrailingMultiplier * atrTrailing[0];

            if (IsFirstTickOfBar)
            {
                preliminaryTrend[0] = preliminaryTrend[1];

                if (preliminaryTrend[0] > 0.5) // Uptrend
                {
                    currentStopLong[0]  = Math.Max(currentStopLong[1], Close[1] - trailingAmount);
                    currentStopShort[0] = Close[1] + trailingAmount;

                    if (Low[0] < currentStopLong[0])
                        preliminaryTrend[0] = -1.0; // Trendwechsel
                }
                else // Downtrend
                {
                    currentStopShort[0] = Math.Min(currentStopShort[1], Close[1] + trailingAmount);
                    currentStopLong[0]  = Close[1] - trailingAmount;

                    if (High[0] > currentStopShort[0])
                        preliminaryTrend[0] = 1.0; // Trendwechsel
                }

                trend[0] = preliminaryTrend[0];
            }
        }

        private void DrawSymbol(string tag, bool isBullish, double yPosition, Brush color)
        {
            try
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

                    case 4: // Line (einzige mit Width)
                        Draw.Line(this, tag, false, 0, yPosition, 1, yPosition, color, DashStyleHelper.Solid, SymbolSize);
                        break;
                }
            }
            catch (Exception ex)
            {
                Print("Fehler beim Zeichnen des Symbols: " + ex.Message);
            }
        }

        private void SafeRemove(string tag)
        {
            try { RemoveDrawObject(tag); } catch { /* ignorieren */ }
        }

        private void RemoveCurrentBarDraws()
        {
            string prefix  = $"BIP{BarsInProgress}_";
            SafeRemove($"{prefix}BullishVF_{CurrentBar}");
            SafeRemove($"{prefix}BearishVF_{CurrentBar}");
            SafeRemove($"{prefix}BullishLabel_{CurrentBar}");
            SafeRemove($"{prefix}BearishLabel_{CurrentBar}");
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TTW.TTWVolumeFlow[] cacheTTWVolumeFlow;
		public TTW.TTWVolumeFlow TTWVolumeFlow(double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showAtrStops, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
		{
			return TTWVolumeFlow(Input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showAtrStops, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
		}

		public TTW.TTWVolumeFlow TTWVolumeFlow(ISeries<double> input, double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showAtrStops, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
		{
			if (cacheTTWVolumeFlow != null)
				for (int idx = 0; idx < cacheTTWVolumeFlow.Length; idx++)
					if (cacheTTWVolumeFlow[idx] != null && cacheTTWVolumeFlow[idx].VolumeMultiplier == volumeMultiplier && cacheTTWVolumeFlow[idx].VolumePeriod == volumePeriod && cacheTTWVolumeFlow[idx].AtrPeriod == atrPeriod && cacheTTWVolumeFlow[idx].AtrMultiplier == atrMultiplier && cacheTTWVolumeFlow[idx].ArrowOffsetFactor == arrowOffsetFactor && cacheTTWVolumeFlow[idx].VolumeOffsetFactor == volumeOffsetFactor && cacheTTWVolumeFlow[idx].ShowLabel == showLabel && cacheTTWVolumeFlow[idx].SymbolType == symbolType && cacheTTWVolumeFlow[idx].SymbolSize == symbolSize && cacheTTWVolumeFlow[idx].BullishColor == bullishColor && cacheTTWVolumeFlow[idx].BearishColor == bearishColor && cacheTTWVolumeFlow[idx].LabelFontFamily == labelFontFamily && cacheTTWVolumeFlow[idx].LabelFontSize == labelFontSize && cacheTTWVolumeFlow[idx].LabelFontBold == labelFontBold && cacheTTWVolumeFlow[idx].EnableAtrTrailingFilter == enableAtrTrailingFilter && cacheTTWVolumeFlow[idx].AtrTrailingPeriod == atrTrailingPeriod && cacheTTWVolumeFlow[idx].AtrTrailingMultiplier == atrTrailingMultiplier && cacheTTWVolumeFlow[idx].ShowAtrStops == showAtrStops && cacheTTWVolumeFlow[idx].AtrLongStopColor == atrLongStopColor && cacheTTWVolumeFlow[idx].AtrShortStopColor == atrShortStopColor && cacheTTWVolumeFlow[idx].ProcessSecondarySeries == processSecondarySeries && cacheTTWVolumeFlow[idx].IgnoreFirstBarsOfSession == ignoreFirstBarsOfSession && cacheTTWVolumeFlow[idx].EnableAlerts == enableAlerts && cacheTTWVolumeFlow[idx].AlertSound == alertSound && cacheTTWVolumeFlow[idx].EqualsInput(input))
						return cacheTTWVolumeFlow[idx];
			return CacheIndicator<TTW.TTWVolumeFlow>(new TTW.TTWVolumeFlow(){ VolumeMultiplier = volumeMultiplier, VolumePeriod = volumePeriod, AtrPeriod = atrPeriod, AtrMultiplier = atrMultiplier, ArrowOffsetFactor = arrowOffsetFactor, VolumeOffsetFactor = volumeOffsetFactor, ShowLabel = showLabel, SymbolType = symbolType, SymbolSize = symbolSize, BullishColor = bullishColor, BearishColor = bearishColor, LabelFontFamily = labelFontFamily, LabelFontSize = labelFontSize, LabelFontBold = labelFontBold, EnableAtrTrailingFilter = enableAtrTrailingFilter, AtrTrailingPeriod = atrTrailingPeriod, AtrTrailingMultiplier = atrTrailingMultiplier, ShowAtrStops = showAtrStops, AtrLongStopColor = atrLongStopColor, AtrShortStopColor = atrShortStopColor, ProcessSecondarySeries = processSecondarySeries, IgnoreFirstBarsOfSession = ignoreFirstBarsOfSession, EnableAlerts = enableAlerts, AlertSound = alertSound }, input, ref cacheTTWVolumeFlow);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TTW.TTWVolumeFlow TTWVolumeFlow(double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showAtrStops, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
		{
			return indicator.TTWVolumeFlow(Input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showAtrStops, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
		}

		public Indicators.TTW.TTWVolumeFlow TTWVolumeFlow(ISeries<double> input , double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showAtrStops, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
		{
			return indicator.TTWVolumeFlow(input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showAtrStops, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TTW.TTWVolumeFlow TTWVolumeFlow(double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showAtrStops, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
		{
			return indicator.TTWVolumeFlow(Input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showAtrStops, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
		}

		public Indicators.TTW.TTWVolumeFlow TTWVolumeFlow(ISeries<double> input , double volumeMultiplier, int volumePeriod, int atrPeriod, double atrMultiplier, double arrowOffsetFactor, double volumeOffsetFactor, bool showLabel, int symbolType, int symbolSize, Brush bullishColor, Brush bearishColor, string labelFontFamily, int labelFontSize, bool labelFontBold, bool enableAtrTrailingFilter, int atrTrailingPeriod, double atrTrailingMultiplier, bool showAtrStops, Brush atrLongStopColor, Brush atrShortStopColor, bool processSecondarySeries, int ignoreFirstBarsOfSession, bool enableAlerts, string alertSound)
		{
			return indicator.TTWVolumeFlow(input, volumeMultiplier, volumePeriod, atrPeriod, atrMultiplier, arrowOffsetFactor, volumeOffsetFactor, showLabel, symbolType, symbolSize, bullishColor, bearishColor, labelFontFamily, labelFontSize, labelFontBold, enableAtrTrailingFilter, atrTrailingPeriod, atrTrailingMultiplier, showAtrStops, atrLongStopColor, atrShortStopColor, processSecondarySeries, ignoreFirstBarsOfSession, enableAlerts, alertSound);
		}
	}
}

#endregion
