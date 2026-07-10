// DANTHER (Codigo Fantasma) - sistema oficial. Base TITANVSB + breakeven+ con offset.
// Logica TITANVSB intacta: ruptura del rango + quiebre de swing + EMAs 18/40 +
// filtro VWAPx (contexto y 2a desv) + reversion de banda.
// SALIDA: SL en swing, TP por RR, breakeven+ con offset (cierra en positivo).
// 1 TRADE POR DIA (la gestion de reintento esta desactivada/oculta).
// Requiere unicamente el indicador VWAPx compilado.

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum DantherDireccion { AmbasDirecciones, SoloCompras, SoloVentas }

    public class DANTHER : Strategy
    {
        private EMA ema, emaLarga;
        private Swing swing;
        private VWAP vwapx;
        private double vwap;

        private int startSec, endSec, finSec, tradesToday, sessionFirstBar;

        private double rangoHigh, rangoLow;
        private bool rangoEnConstruccion, rangoCerrado, rangoRoto;
        private int rangoRotoBar = -1;
        private DateTime rangoStartTime;

        private int lastSwingLowBarAbs = -1;
        private double lastSwingLowPrice = double.NaN;
        private bool lastSwingLowAfterStart = false, lastSwingLowUsed = true;

        private int lastSwingHighBarAbs = -1;
        private double lastSwingHighPrice = double.NaN;
        private bool lastSwingHighAfterStart = false, lastSwingHighUsed = true;

        private int lastCrossUpBar = -1, lastCrossDownBar = -1;

        private bool tocoBandaInferior, tocoBandaSuperior;

        private bool enPosicionTrack, beHecho;
        private double entryPriceTrack, slDistActivo;
        private int qtyTrack;
        private double mfeMax, maeMax;

        // Gestion del dia
        private bool diaCerrado;
        private int reintentosUsados;
        
        // ── Licencia ──────────────────────────────────────────
        private bool isLicensed = false;

        // --- 1) Rango y sesion ---
        [NinjaScriptProperty]
        [Display(Name = "Hora inicio del rango (HH:mm)", GroupName = "1) Rango y sesion", Order = 1)]
        public string HoraInicioRango { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Hora fin del rango (HH:mm)", GroupName = "1) Rango y sesion", Order = 2)]
        public string HoraFinRango { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Hora fin operativa (HH:mm)", GroupName = "1) Rango y sesion", Order = 3)]
        public string HoraFinOperativa { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Cerrar posicion al fin de la ventana", GroupName = "1) Rango y sesion", Order = 4)]
        public bool CerrarAlFinVentana { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Dibujar el rango", GroupName = "1) Rango y sesion", Order = 5)]
        public bool MostrarRango { get; set; }

        // --- 2) Riesgo ---
        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Riesgo por trade (moneda cuenta)", GroupName = "2) Riesgo", Order = 1)]
        public double RiesgoPorTrade { get; set; }
        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Max contratos por trade", GroupName = "2) Riesgo", Order = 2)]
        public int MaxContratos { get; set; }
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Max trades por dia", GroupName = "2) Riesgo", Order = 3)]
        public int MaxTradesDia { get; set; }

        // --- 3) Senal ---
        [NinjaScriptProperty]
        [Display(Name = "Direccion permitida", GroupName = "3) Senal", Order = 0)]
        public DantherDireccion DireccionPermitida { get; set; }
        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Periodo EMA corta", GroupName = "3) Senal", Order = 1)]
        public int EmaPeriodo { get; set; }
        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Periodo EMA larga", GroupName = "3) Senal", Order = 2)]
        public int EmaPeriodoLargo { get; set; }
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Fuerza del Swing", GroupName = "3) Senal", Order = 3)]
        public int SwingStrength { get; set; }
        [NinjaScriptProperty]
        [Range(5, 1000)]
        [Display(Name = "Lookback de Swing (barras)", GroupName = "3) Senal", Order = 4)]
        public int SwingLookback { get; set; }
        [NinjaScriptProperty]
        [Range(1, 4)]
        [Display(Name = "Banda de desviacion VWAPx (1-4)", GroupName = "3) Senal", Order = 5)]
        public int NumeroDesviacion { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Reversion SOLO compra en 2a desv inferior", GroupName = "3) Senal", Order = 6)]
        public bool UsarReversionBandaCompra { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Reiniciar swing con cada cruce de EMAs", GroupName = "3) Senal", Order = 7)]
        public bool ReiniciarSwingConCruceEma { get; set; }


        // --- 4) Salida ---
        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "Multiplicador del SL (x swing)", GroupName = "4) Salida", Order = 1)]
        public double SLMultiplicador { get; set; }
        [NinjaScriptProperty]
        [Range(0.1, 50.0)]
        [Display(Name = "Multiplicador del TP (x swing base)", GroupName = "4) Salida", Order = 2)]
        public double TPMultiplicador { get; set; }
        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Buffer extra del SL (ticks)", GroupName = "4) Salida", Order = 3)]
        public int BufferStopTicks { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Usar breakeven", GroupName = "4) Salida", Order = 4)]
        public bool UsarBreakeven { get; set; }
        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "RR para mover a breakeven (recorrido)", GroupName = "4) Salida", Order = 5)]
        public double RR_Breakeven { get; set; }
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Offset del breakeven en ticks (cierra positivo)", GroupName = "4) Salida", Order = 6)]
        public int BeOffsetTicks { get; set; }

        // --- (ocultos) Gestion del dia: desactivada, no visible en inputs ---
        [Browsable(false)] public bool ReintentarTrasSL { get; set; }
        [Browsable(false)] public int MaxReintentos { get; set; }
        [Browsable(false)] public bool CerrarDiaTrasTP { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DANTHER";
                Description = "TITANVSB original + gestion del dia: 2do trade solo tras SL, cierra el dia tras TP.";

                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                BarsRequiredToTrade = 30;

                HoraInicioRango = "08:30";
                HoraFinRango = "08:45";
                HoraFinOperativa = "11:00";
                CerrarAlFinVentana = true;
                MostrarRango = true;

                RiesgoPorTrade = 700;
                MaxContratos = 5;
                MaxTradesDia = 1;

                DireccionPermitida = DantherDireccion.AmbasDirecciones;
                EmaPeriodo = 18;
                EmaPeriodoLargo = 40;
                SwingStrength = 3;
                SwingLookback = 120;
                NumeroDesviacion = 2;
                UsarReversionBandaCompra = true;
                ReiniciarSwingConCruceEma = true;


                SLMultiplicador = 1.5;
                TPMultiplicador = 2.0;
                BufferStopTicks = 0;
                UsarBreakeven = true;
                RR_Breakeven = 0.7;
                BeOffsetTicks = 5;

                ReintentarTrasSL = false;
                MaxReintentos = 1;
                CerrarDiaTrasTP = false;
            }
            else if (State == State.Configure)
            {
                startSec = ParseHHmmss(HoraInicioRango, 83000);
                endSec = ParseHHmmss(HoraFinRango, 84500);
                finSec = ParseHHmmss(HoraFinOperativa, 110000);
                
                // ── Verificación de Licencia en la nube ──
                try 
                {
                    using (System.Net.WebClient client = new System.Net.WebClient())
                    {
                        client.Headers.Add("Cache-Control", "no-cache");
                        string url = "https://raw.githubusercontent.com/andersonmanuelsuarez-del/CODIGOFANTASMA/main/licencias.txt";
                        string validLicenses = client.DownloadString(url);
                        
                        string myMachineId = NinjaTrader.Core.Globals.MachineId;
                        
                        if (validLicenses.Contains("DANTHER-" + myMachineId))
                        {
                            isLicensed = true;
                            Print($"[Danther] Licencia validada exitosamente para el Machine ID: {myMachineId}");
                        }
                        else 
                        {
                            isLicensed = false;
                            Print("==================================================");
                            Print(" ERROR DE LICENCIA - SISTEMA DANTHER");
                            Print(" Tu Machine ID no está autorizado para usar este sistema.");
                            Print(" Tu Machine ID es: " + myMachineId);
                            Print(" Por favor envía este ID al administrador en Discord para activarlo.");
                            Print("==================================================");
                        }
                    }
                }
                catch (Exception ex)
                {
                    isLicensed = false;
                    Print("[Danther] Error verificando la licencia: " + ex.Message);
                }
            }
            else if (State == State.DataLoaded)
            {
                ema = EMA(Close, EmaPeriodo);
                emaLarga = EMA(Close, EmaPeriodoLargo);
                swing = Swing(SwingStrength);
                vwapx = VWAP();
            }
        }

        protected override void OnBarUpdate()
        {
            if (!isLicensed) return;
            if (CurrentBar < 1) return;

            if (Bars.IsFirstBarOfSession)
            {
                tradesToday = 0;
                sessionFirstBar = CurrentBar;
                diaCerrado = false;
                reintentosUsados = 0;
                rangoHigh = double.MinValue; rangoLow = double.MaxValue;
                rangoEnConstruccion = false; rangoCerrado = false;
                rangoRoto = false; rangoRotoBar = -1;
                lastSwingLowBarAbs = -1; lastSwingLowPrice = double.NaN;
                lastSwingLowAfterStart = false; lastSwingLowUsed = true;
                lastSwingHighBarAbs = -1; lastSwingHighPrice = double.NaN;
                lastSwingHighAfterStart = false; lastSwingHighUsed = true;
                tocoBandaInferior = false; tocoBandaSuperior = false;
                beHecho = false;
            }

            vwap = vwapx.PlotVWAP[0];
            int now = ToTime(Time[0]);

            if (vwap > 0)
            {
                bool enVentana = now >= startSec && now <= finSec;
                double bSup = BandaSup(); double bInf = BandaInf();
                if (enVentana && Low[0] <= bInf) tocoBandaInferior = true;
                if (enVentana && High[0] >= bSup) tocoBandaSuperior = true;
                if (Close[0] >= vwap) tocoBandaInferior = false;
                if (Close[0] <= vwap) tocoBandaSuperior = false;
            }

            if (now >= startSec && now < endSec)
            {
                if (!rangoEnConstruccion)
                { rangoEnConstruccion = true; rangoStartTime = Time[0]; rangoHigh = High[0]; rangoLow = Low[0]; }
                else
                { rangoHigh = Math.Max(rangoHigh, High[0]); rangoLow = Math.Min(rangoLow, Low[0]); }
            }
            if (!rangoCerrado && now >= endSec && rangoEnConstruccion && rangoHigh > rangoLow)
            {
                rangoCerrado = true;
                Print($"[TT] {Time[0]} Rango: alto={rangoHigh:F2} bajo={rangoLow:F2}");
            }
            if (MostrarRango && rangoCerrado)
                Draw.Rectangle(this, "OR_" + rangoStartTime.ToString("yyyyMMdd"), false,
                    rangoStartTime, rangoLow, Time[0], rangoHigh, Brushes.SteelBlue, Brushes.SteelBlue, 8);

            if (CurrentBar < BarsRequiredToTrade || CurrentBar < SwingStrength + 2) return;

            ActualizarSwings();

            if (ema[0] > emaLarga[0] && ema[1] <= emaLarga[1]) lastCrossUpBar = CurrentBar;
            if (ema[0] < emaLarga[0] && ema[1] >= emaLarga[1]) lastCrossDownBar = CurrentBar;

            bool enPos = Position.MarketPosition != MarketPosition.Flat;
            if (enPos && !enPosicionTrack)
            {
                enPosicionTrack = true;
                entryPriceTrack = Position.AveragePrice;
                qtyTrack = Position.Quantity;
                mfeMax = 0; maeMax = 0;
            }
            if (enPos)
            {
                double fav, adv;
                if (Position.MarketPosition == MarketPosition.Long)
                { fav = High[0] - entryPriceTrack; adv = entryPriceTrack - Low[0]; }
                else
                { fav = entryPriceTrack - Low[0]; adv = High[0] - entryPriceTrack; }
                if (fav > mfeMax) mfeMax = fav;
                if (adv > maeMax) maeMax = adv;
            }
            else if (enPosicionTrack)
            {
                enPosicionTrack = false;
                double pv = Instrument.MasterInstrument.PointValue;
                Print(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[TT][TRADE] {0} MFE={1:F0}t (${2:F0}) MAE={3:F0}t (${4:F0}) qty={5} | Rmfe={6:F2} Rmae={7:F2}",
                    Time[0], mfeMax / TickSize, mfeMax * pv * qtyTrack,
                    maeMax / TickSize, maeMax * pv * qtyTrack, qtyTrack,
                    slDistActivo > 0 ? mfeMax / slDistActivo : 0.0,
                    slDistActivo > 0 ? maeMax / slDistActivo : 0.0));
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (UsarBreakeven && !beHecho && slDistActivo > 0)
                {
                    if (Position.MarketPosition == MarketPosition.Long
                        && High[0] >= entryPriceTrack + RR_Breakeven * slDistActivo)
                    {
                        double bePrice = entryPriceTrack + BeOffsetTicks * TickSize;
                        SetStopLoss("TT_Long", CalculationMode.Price, bePrice, false);
                        beHecho = true;
                        Print($"[TT] {Time[0]} Breakeven+ (largo) a {RR_Breakeven}R, stop en {bePrice:F2} (entrada {entryPriceTrack:F2} + {BeOffsetTicks}t).");
                    }
                    else if (Position.MarketPosition == MarketPosition.Short
                        && Low[0] <= entryPriceTrack - RR_Breakeven * slDistActivo)
                    {
                        double bePrice = entryPriceTrack - BeOffsetTicks * TickSize;
                        SetStopLoss("TT_Short", CalculationMode.Price, bePrice, false);
                        beHecho = true;
                        Print($"[TT] {Time[0]} Breakeven+ (corto) a {RR_Breakeven}R, stop en {bePrice:F2} (entrada {entryPriceTrack:F2} - {BeOffsetTicks}t).");
                    }
                }
                if (CerrarAlFinVentana && now > finSec)
                {
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong("Fin ventana", "TT_Long");
                    else ExitShort("Fin ventana", "TT_Short");
                }
                return;
            }

            if (!rangoCerrado) return;
            if (now < endSec) return;
            if (now > finSec) return;

            // Gestion del dia
            if (diaCerrado) return;
            int capDia = ReintentarTrasSL ? (1 + MaxReintentos) : MaxTradesDia;
            if (tradesToday >= capDia) return;

            if (!rangoRoto)
            {
                if (Close[0] > rangoHigh || Close[0] < rangoLow)
                { rangoRoto = true; rangoRotoBar = CurrentBar; }
            }
            if (!rangoRoto) return;
            if (vwap <= 0) return;

            double bandaSup = BandaSup();
            double bandaInf = BandaInf();

            bool permiteCorto = DireccionPermitida != DantherDireccion.SoloCompras;
            bool permiteLargo = DireccionPermitida != DantherDireccion.SoloVentas;

            bool cruceOkLargo = !ReiniciarSwingConCruceEma
                || (lastCrossUpBar >= 0 && lastCrossUpBar > lastCrossDownBar && lastSwingHighBarAbs >= lastCrossUpBar);
            bool cruceOkCorto = !ReiniciarSwingConCruceEma
                || (lastCrossDownBar >= 0 && lastCrossDownBar > lastCrossUpBar && lastSwingLowBarAbs >= lastCrossDownBar);


            // ---- VENTA ----
            bool ventaContexto = Close[0] < vwap && ema[0] < vwap && ema[0] < emaLarga[0];
            bool ventaQuiebre = !double.IsNaN(lastSwingLowPrice) && lastSwingLowAfterStart && !lastSwingLowUsed
                && lastSwingLowBarAbs >= rangoRotoBar && cruceOkCorto
                && Close[0] < lastSwingLowPrice && Close[1] >= lastSwingLowPrice;

            if (permiteCorto && ventaQuiebre && ventaContexto)
            {
                if (!double.IsNaN(bandaInf) && Close[0] <= bandaInf) { lastSwingLowUsed = true; }
                else EntrarCorto();
                return;
            }

            // ---- COMPRA ----
            bool compraContexto = Close[0] > vwap && ema[0] > vwap && ema[0] > emaLarga[0];
            bool compraReversion = UsarReversionBandaCompra && tocoBandaInferior && Close[0] < vwap && ema[0] > emaLarga[0];
            bool compraQuiebre = !double.IsNaN(lastSwingHighPrice) && lastSwingHighAfterStart && !lastSwingHighUsed
                && lastSwingHighBarAbs >= rangoRotoBar && cruceOkLargo
                && Close[0] > lastSwingHighPrice && Close[1] <= lastSwingHighPrice;

            if (permiteLargo && compraQuiebre && (compraContexto || compraReversion))
            {
                if (!compraReversion && !double.IsNaN(bandaSup) && Close[0] >= bandaSup) { lastSwingHighUsed = true; }
                else { if (compraReversion) tocoBandaInferior = false; EntrarLargo(); }
                return;
            }
        }

        private void EntrarLargo()
        {
            double swingLow = BuscarMinimoEstructura(Close[0]);
            if (double.IsNaN(swingLow)) { lastSwingHighUsed = true; return; }
            double baseDist = Close[0] - swingLow;
            if (baseDist <= TickSize) { lastSwingHighUsed = true; return; }
            double slDist = baseDist * SLMultiplicador + BufferStopTicks * TickSize;
            double slPrice = Close[0] - slDist;
            double tpPrice = Close[0] + baseDist * TPMultiplicador;
            int qty = CalcularContratos(slDist);
            if (qty < 1) { lastSwingHighUsed = true; return; }
            SetStopLoss("TT_Long", CalculationMode.Price, slPrice, false);
            SetProfitTarget("TT_Long", CalculationMode.Price, tpPrice);
            EnterLong(qty, "TT_Long");
            slDistActivo = slDist; beHecho = false;
            tradesToday++; lastSwingHighUsed = true;
            Print($"[TT] {Time[0]} LONG #{tradesToday} qty={qty} entry={Close[0]:F2} SL={slPrice:F2} TP={tpPrice:F2}");
        }

        private void EntrarCorto()
        {
            double swingHigh = BuscarMaximoEstructura(Close[0]);
            if (double.IsNaN(swingHigh)) { lastSwingLowUsed = true; return; }
            double baseDist = swingHigh - Close[0];
            if (baseDist <= TickSize) { lastSwingLowUsed = true; return; }
            double slDist = baseDist * SLMultiplicador + BufferStopTicks * TickSize;
            double slPrice = Close[0] + slDist;
            double tpPrice = Close[0] - baseDist * TPMultiplicador;
            int qty = CalcularContratos(slDist);
            if (qty < 1) { lastSwingLowUsed = true; return; }
            SetStopLoss("TT_Short", CalculationMode.Price, slPrice, false);
            SetProfitTarget("TT_Short", CalculationMode.Price, tpPrice);
            EnterShort(qty, "TT_Short");
            slDistActivo = slDist; beHecho = false;
            tradesToday++; lastSwingLowUsed = true;
            Print($"[TT] {Time[0]} SHORT #{tradesToday} qty={qty} entry={Close[0]:F2} SL={slPrice:F2} TP={tpPrice:F2}");
        }

        private void ActualizarSwings()
        {
            int slBarsAgo = swing.SwingLowBar(0, 1, SwingLookback);
            if (slBarsAgo > 0 && slBarsAgo <= CurrentBar)
            {
                int slAbs = CurrentBar - slBarsAgo;
                if (slAbs != lastSwingLowBarAbs)
                {
                    lastSwingLowBarAbs = slAbs; lastSwingLowPrice = Low[slBarsAgo];
                    lastSwingLowAfterStart = slAbs >= sessionFirstBar && ToTime(Time[slBarsAgo]) >= startSec;
                    lastSwingLowUsed = false;
                }
            }
            int shBarsAgo = swing.SwingHighBar(0, 1, SwingLookback);
            if (shBarsAgo > 0 && shBarsAgo <= CurrentBar)
            {
                int shAbs = CurrentBar - shBarsAgo;
                if (shAbs != lastSwingHighBarAbs)
                {
                    lastSwingHighBarAbs = shAbs; lastSwingHighPrice = High[shBarsAgo];
                    lastSwingHighAfterStart = shAbs >= sessionFirstBar && ToTime(Time[shBarsAgo]) >= startSec;
                    lastSwingHighUsed = false;
                }
            }
        }

        private double BuscarMaximoEstructura(double precio)
        {
            for (int inst = 1; inst <= 8; inst++)
            {
                int ba = swing.SwingHighBar(0, inst, SwingLookback);
                if (ba < 0) break;
                if (High[ba] > precio) return High[ba];
            }
            return double.NaN;
        }

        private double BuscarMinimoEstructura(double precio)
        {
            for (int inst = 1; inst <= 8; inst++)
            {
                int ba = swing.SwingLowBar(0, inst, SwingLookback);
                if (ba < 0) break;
                if (Low[ba] < precio) return Low[ba];
            }
            return double.NaN;
        }

        private int CalcularContratos(double stopDist)
        {
            double pointValue = Instrument.MasterInstrument.PointValue;
            double riskPerContract = stopDist * pointValue;
            if (riskPerContract <= 0) return 0;
            int qty = (int)Math.Floor(RiesgoPorTrade / riskPerContract);
            return Math.Max(0, Math.Min(qty, MaxContratos));
        }

        private double BandaSup()
        {
            if (vwapx == null) return double.NaN;
            switch (NumeroDesviacion)
            {
                case 1: return vwapx.PlotVWAP1U[0];
                case 3: return vwapx.PlotVWAP3U[0];
                case 4: return vwapx.PlotVWAP4U[0];
                default: return vwapx.PlotVWAP2U[0];
            }
        }

        private double BandaInf()
        {
            if (vwapx == null) return double.NaN;
            switch (NumeroDesviacion)
            {
                case 1: return vwapx.PlotVWAP1L[0];
                case 3: return vwapx.PlotVWAP3L[0];
                case 4: return vwapx.PlotVWAP4L[0];
                default: return vwapx.PlotVWAP2L[0];
            }
        }

        private int ParseHHmmss(string txt, int fallback)
        {
            try
            {
                TimeSpan ts = TimeSpan.Parse(txt.Trim());
                return ts.Hours * 10000 + ts.Minutes * 100 + ts.Seconds;
            }
            catch { Print($"[TT] Hora invalida '{txt}', usando {fallback}."); return fallback; }
        }

        // Detecta resultado del trade por el nombre de la orden de salida
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            if (execution.Order == null || execution.Order.OrderState != OrderState.Filled) return;
            if (Position.MarketPosition != MarketPosition.Flat) return;   // solo al cerrar el trade completo
            string n = execution.Order.Name;

            if (n.Contains("Profit"))
            {
                if (CerrarDiaTrasTP) { diaCerrado = true; Print($"[TT] {time} TP -> dia cerrado."); }
            }
            else if (n.Contains("Stop"))
            {
                if (beHecho)
                { diaCerrado = true; Print($"[TT] {time} Salida en breakeven+ (pequena ganancia) -> dia cerrado."); }
                else if (ReintentarTrasSL && reintentosUsados < MaxReintentos)
                { reintentosUsados++; Print($"[TT] {time} SL -> reintento {reintentosUsados}/{MaxReintentos}."); }
                else { diaCerrado = true; Print($"[TT] {time} SL sin mas reintentos -> dia cerrado."); }
            }
            else if (n.Contains("ventana"))
            {
                diaCerrado = true;
            }
        }
    }
}
