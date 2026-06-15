// ============================================================
//  ANZA  —  Estrategia Automatizada para Futuros
// ------------------------------------------------------------
//  Sistema automatizado de detección de quiebres de estructura
//  con gestión de riesgo dinámica y stop catastrófico.
//  Diseñado para empresas de fondeo (Apex, Topstep, etc.)
//  © Código Fantasma — Andersson Suárez
// ============================================================
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
    public enum AnzaExitMode
    {
        StopFijoClasico,
        CierreConfirmado,
        CruceEmaVwap,
        Combinado
    }
    public enum AnzaCatastrophicMode
    {
        TicksMasAllaDelNivel,
        MaximoEnMoneda,
        MultiploEstructura
    }
    public class Anza : Strategy
    {
        // ── Indicadores ───────────────────────────────────────
        private EMA   ema;
        private EMA   emaLarga;
        private Swing swing;
        // ── VWAP de sesión (calculado internamente) ───────────
        private double cumPV;
        private double cumPV2;
        private double cumVol;
        private double vwap;
        private double vwapStdDev;
        // ── Estado de sesión ──────────────────────────────────
        private int  startSec;
        private int  endSec;
        private int  tradesToday;
        private int  sessionFirstBar;
        private double cumProfitStartOfSession;
        private bool metaAlcanzadaHoy;
        // ── Estructura: último swing bajo ─────────────────────
        private int    lastSwingLowBarAbs    = -1;
        private double lastSwingLowPrice     = double.NaN;
        private bool   lastSwingLowAfterStart = false;
        private bool   lastSwingLowUsed       = true;
        // ── Estructura: último swing alto ─────────────────────
        private int    lastSwingHighBarAbs    = -1;
        private double lastSwingHighPrice     = double.NaN;
        private bool   lastSwingHighAfterStart = false;
        private bool   lastSwingHighUsed       = true;
        // ── Trade activo ──────────────────────────────────────
        private double structureStop;
        private double targetRef;
        // ── Variables para medir retroceso (MAE en TP) ────────
        private double precioEntradaRef = double.NaN;
        private double stopLossInicialRef = double.NaN;
        private double peorPrecioTrade = double.NaN;
        private bool tradeActivo = false;
        private int currentPositionQty = 0;
        private double sumaRetrocesosTP = 0;
        private int conteoTradesTP = 0;

        // ════════════════════════════════════════════════════
        //  PARÁMETROS
        // ════════════════════════════════════════════════════

        // ─── 1) Sesión (OCULTO — valores fijos internos) ───
        // Estos parámetros NO se muestran al usuario.
        // El horario está bloqueado internamente.
        [Browsable(false)]
        public string HoraInicio { get; set; }

        [Browsable(false)]
        public string HoraFin { get; set; }

        [Browsable(false)]
        public bool CerrarAlFinVentana { get; set; }

        // ─── 2) Riesgo ───
        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Riesgo por trade (moneda cuenta)", GroupName = "1) Riesgo", Order = 1)]
        public double RiesgoPorTrade { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Máx contratos por trade", GroupName = "1) Riesgo", Order = 2)]
        public int MaxContratos { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Máx trades por día", GroupName = "1) Riesgo", Order = 3)]
        public int MaxTradesDia { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Meta de ganancia diaria (0=Desactivado)", GroupName = "1) Riesgo", Order = 4)]
        public double MetaGananciaDiaria { get; set; }

        // ─── 3) Señal ───
        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Período EMA corta", GroupName = "2) Señal", Order = 1)]
        public int EmaPeriodo { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Período EMA larga (filtro tendencia)", GroupName = "2) Señal", Order = 2)]
        public int EmaPeriodoLargo { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Fuerza del Swing", GroupName = "2) Señal", Order = 3)]
        public int SwingStrength { get; set; }

        [NinjaScriptProperty]
        [Range(5, 1000)]
        [Display(Name = "Lookback de Swing (barras)", GroupName = "2) Señal", Order = 4)]
        public int SwingLookback { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Riesgo:Beneficio (RR)", GroupName = "2) Señal", Order = 5)]
        public double RR { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtrar entradas en bandas extremas del VWAP", GroupName = "2) Señal", Order = 6)]
        public bool UsarFiltroVwapDesviacion { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "Múltiplo de desviación VWAP (bloqueo)", GroupName = "2) Señal", Order = 7)]
        public double MultiploDesviacionVwap { get; set; }

        // ─── 4) Gestión de salida ───
        [NinjaScriptProperty]
        [Display(Name = "Modo de salida (SL)", GroupName = "3) Gestión de salida", Order = 1)]
        public AnzaExitMode ModoSalida { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop catastrófico — modo", GroupName = "3) Gestión de salida", Order = 2)]
        public AnzaCatastrophicMode ModoStopCatastrofico { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Stop catastrófico — ticks más allá del nivel", GroupName = "3) Gestión de salida", Order = 3)]
        public int StopCatastroficoTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Stop catastrófico — pérdida máxima (moneda)", GroupName = "3) Gestión de salida", Order = 4)]
        public double StopCatastroficoMoneda { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 10.0)]
        [Display(Name = "Stop catastrófico — múltiplo de estructura", GroupName = "3) Gestión de salida", Order = 5)]
        public double MultiploStopCatastrofico { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Buffer del nivel de estructura (ticks)", GroupName = "3) Gestión de salida", Order = 6)]
        public int BufferStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 1000)]
        [Display(Name = "Corte temprano por retroceso (%)", GroupName = "3) Gestión de salida", Order = 7)]
        public double PorcentajeCorteTemprano { get; set; }

        // ─── 5) Piramidación (DESACTIVADA — oculta al usuario) ───
        [Browsable(false)]
        public bool UsarPiramidacion { get; set; }

        [Browsable(false)]
        public int MaxPiramides { get; set; }

        [Browsable(false)]
        public double FactorQtyPiramide { get; set; }

        // ════════════════════════════════════════════════════
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "Anza";
                Description = "Anza — Estrategia automatizada para futuros. © Código Fantasma";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 1;  // Sin piramidación
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                BarsRequiredToTrade          = 30;

                // Sesión (valores fijos — no editables por el usuario)
                HoraInicio               = "08:30";
                HoraFin                  = "10:30";
                CerrarAlFinVentana       = true;

                // Riesgo
                RiesgoPorTrade           = 600;
                MaxContratos             = 5;
                MaxTradesDia             = 1;
                MetaGananciaDiaria       = 550;

                // Señal
                EmaPeriodo               = 18;
                EmaPeriodoLargo          = 40;
                SwingStrength            = 5;
                SwingLookback            = 120;
                RR                       = 0.7;
                UsarFiltroVwapDesviacion = true;
                MultiploDesviacionVwap   = 2.0;

                // Gestión de salida
                ModoSalida               = AnzaExitMode.CruceEmaVwap;
                ModoStopCatastrofico     = AnzaCatastrophicMode.MaximoEnMoneda;
                StopCatastroficoTicks    = 12;
                StopCatastroficoMoneda   = 600;
                MultiploStopCatastrofico = 1.0;
                BufferStopTicks          = 2;
                PorcentajeCorteTemprano  = 125.0;

                // Piramidación DESACTIVADA por defecto
                UsarPiramidacion         = false;
                MaxPiramides             = 0;
                FactorQtyPiramide        = 1.0;
            }
            else if (State == State.Configure)
            {
                startSec = ParseHHmmss(HoraInicio, 83000);
                endSec   = ParseHHmmss(HoraFin,   103000);
            }
            else if (State == State.DataLoaded)
            {
                ema      = EMA(Close, EmaPeriodo);
                emaLarga = EMA(Close, EmaPeriodoLargo);
                swing    = Swing(SwingStrength);
            }
        }

        // ════════════════════════════════════════════════════
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            if (Bars.IsFirstBarOfSession)
            {
                cumPV  = 0;
                cumPV2 = 0;
                cumVol = 0;
                vwapStdDev = 0;
                tradesToday     = 0;
                sessionFirstBar = CurrentBar;
                cumProfitStartOfSession = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                metaAlcanzadaHoy = false;

                lastSwingLowBarAbs     = -1;
                lastSwingLowPrice      = double.NaN;
                lastSwingLowAfterStart = false;
                lastSwingLowUsed       = true;

                lastSwingHighBarAbs     = -1;
                lastSwingHighPrice      = double.NaN;
                lastSwingHighAfterStart = false;
                lastSwingHighUsed       = true;

                precioEntradaRef = double.NaN;
                stopLossInicialRef = double.NaN;
                peorPrecioTrade = double.NaN;
                tradeActivo = false;
                currentPositionQty = 0;
            }

            double precioTipico = (High[0] + Low[0] + Close[0]) / 3.0;
            double volumen      = Volume[0];
            cumPV  += precioTipico * volumen;
            cumPV2 += precioTipico * precioTipico * volumen;
            cumVol += volumen;
            vwap    = cumVol > 0 ? cumPV / cumVol : Close[0];
            if (cumVol > 0)
            {
                double variance = cumPV2 / cumVol - vwap * vwap;
                vwapStdDev = variance > 0 ? Math.Sqrt(variance) : 0;
            }

            if (CurrentBar < BarsRequiredToTrade || CurrentBar < SwingStrength + 2)
                return;

            ActualizarSwings();

            // Rastrear el peor precio alcanzado durante el trade activo
            if (Position.MarketPosition == MarketPosition.Long && tradeActivo)
            {
                peorPrecioTrade = Math.Min(peorPrecioTrade, Low[0]);
            }
            else if (Position.MarketPosition == MarketPosition.Short && tradeActivo)
            {
                peorPrecioTrade = Math.Max(peorPrecioTrade, High[0]);
            }

            // Control de Meta de Ganancia Diaria
            if (Position.MarketPosition != MarketPosition.Flat && MetaGananciaDiaria > 0)
            {
                double pnlRealizadoHoy = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - cumProfitStartOfSession;
                double pnlNoRealizado = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

                if ((pnlRealizadoHoy + pnlNoRealizado) >= MetaGananciaDiaria)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("Meta Diaria Anza", "ANZA_Long");
                    else
                        ExitShort("Meta Diaria Anza", "ANZA_Short");

                    metaAlcanzadaHoy = true;
                    Print($"[Anza] {Time[0]} META DIARIA ALCANZADA: Realizado={pnlRealizadoHoy:F2} + NoRealizado={pnlNoRealizado:F2} >= {MetaGananciaDiaria}. Cerrando posiciones.");
                    return;
                }
            }

            if (metaAlcanzadaHoy) return;

            if (Position.MarketPosition == MarketPosition.Short)
            {
                GestionarCorto();
                return;
            }
            if (Position.MarketPosition == MarketPosition.Long)
            {
                GestionarLargo();
                return;
            }

            int now      = ToTime(Time[0]);
            bool inWindow = now >= startSec && now <= endSec;
            if (!inWindow)              return;
            if (tradesToday >= MaxTradesDia) return;

            // ---- VENTA ----
            bool ventaContexto = Close[0] < vwap && ema[0] < vwap && ema[0] < emaLarga[0];
            bool ventaQuiebre  = !double.IsNaN(lastSwingLowPrice)
                              && lastSwingLowAfterStart
                              && !lastSwingLowUsed
                              && Close[0] <  lastSwingLowPrice
                              && Close[1] >= lastSwingLowPrice;
            if (ventaContexto && ventaQuiebre)
            {
                if (UsarFiltroVwapDesviacion && vwapStdDev > 0)
                {
                    double bandaInferior = vwap - MultiploDesviacionVwap * vwapStdDev;
                    if (Close[0] <= bandaInferior)
                    {
                        Print($"[Anza] {Time[0]} Corto omitido por filtro VWAP: Close {Close[0]:F2} <= banda -{MultiploDesviacionVwap}σ ({bandaInferior:F2}).");
                        lastSwingLowUsed = true;
                        return;
                    }
                }
                EntrarCorto();
                return;
            }

            // ---- COMPRA ----
            bool compraContexto = Close[0] > vwap && ema[0] > vwap && ema[0] > emaLarga[0];
            bool compraQuiebre  = !double.IsNaN(lastSwingHighPrice)
                               && lastSwingHighAfterStart
                               && !lastSwingHighUsed
                               && Close[0] >  lastSwingHighPrice
                               && Close[1] <= lastSwingHighPrice;
            if (compraContexto && compraQuiebre)
            {
                if (UsarFiltroVwapDesviacion && vwapStdDev > 0)
                {
                    double bandaSuperior = vwap + MultiploDesviacionVwap * vwapStdDev;
                    if (Close[0] >= bandaSuperior)
                    {
                        Print($"[Anza] {Time[0]} Largo omitido por filtro VWAP: Close {Close[0]:F2} >= banda +{MultiploDesviacionVwap}σ ({bandaSuperior:F2}).");
                        lastSwingHighUsed = true;
                        return;
                    }
                }
                EntrarLargo();
                return;
            }
        }

        // ════════════════════════════════════════════════════
        private void ActualizarSwings()
        {
            int slBarsAgo = swing.SwingLowBar(0, 1, SwingLookback);
            if (slBarsAgo > 0 && slBarsAgo <= CurrentBar)
            {
                int slAbs = CurrentBar - slBarsAgo;
                if (slAbs != lastSwingLowBarAbs)
                {
                    lastSwingLowBarAbs     = slAbs;
                    lastSwingLowPrice      = Low[slBarsAgo];
                    lastSwingLowAfterStart = slAbs >= sessionFirstBar
                                          && ToTime(Time[slBarsAgo]) >= startSec;
                    lastSwingLowUsed       = false;
                }
            }

            int shBarsAgo = swing.SwingHighBar(0, 1, SwingLookback);
            if (shBarsAgo > 0 && shBarsAgo <= CurrentBar)
            {
                int shAbs = CurrentBar - shBarsAgo;
                if (shAbs != lastSwingHighBarAbs)
                {
                    lastSwingHighBarAbs     = shAbs;
                    lastSwingHighPrice      = High[shBarsAgo];
                    lastSwingHighAfterStart = shAbs >= sessionFirstBar
                                           && ToTime(Time[shBarsAgo]) >= startSec;
                    lastSwingHighUsed       = false;
                }
            }
        }

        // ════════════════════════════════════════════════════
        private void EntrarCorto()
        {
            double maxEstructura = BuscarMaximoEstructura(Close[0]);
            if (double.IsNaN(maxEstructura))
            {
                Print($"[Anza] {Time[0]} Corto omitido: no hay máximo de estructura por encima del precio.");
                lastSwingLowUsed = true;
                return;
            }

            double stopLevel = maxEstructura + BufferStopTicks * TickSize;
            double stopDist  = stopLevel - Close[0];
            if (stopDist <= TickSize)
            {
                Print($"[Anza] {Time[0]} Corto omitido: distancia de stop inválida ({stopDist:F2}).");
                lastSwingLowUsed = true;
                return;
            }

            int qty = CalcularContratos(stopDist);
            if (qty < 1)
            {
                Print($"[Anza] {Time[0]} Corto omitido: el riesgo por trade no alcanza ni 1 contrato (stopDist {stopDist:F2}).");
                lastSwingLowUsed = true;
                return;
            }

            double catDist  = CalcularDistanciaCatastrofica(stopDist, qty);

            double maxDistAllowed = catDist;
            if (PorcentajeCorteTemprano > 0)
            {
                double corteDist = stopDist * (PorcentajeCorteTemprano / 100.0);
                maxDistAllowed = Math.Min(catDist, corteDist);
            }

            double finalSLLevel = Close[0] + maxDistAllowed;
            int stopTicks   = Math.Max(1, (int)Math.Round(stopDist / TickSize));
            int finalSLTicks = Math.Max(1, (int)Math.Round(maxDistAllowed / TickSize));

            structureStop      = stopLevel;
            targetRef          = Close[0] - stopDist * RR;

            SetStopLoss("ANZA_Short", CalculationMode.Ticks,
                ModoSalida == AnzaExitMode.StopFijoClasico ? stopTicks : finalSLTicks, false);
            SetProfitTarget("ANZA_Short", CalculationMode.Price, targetRef);
            EnterShort(qty, "ANZA_Short");

            tradesToday++;
            lastSwingLowUsed = true;
            Print($"[Anza] {Time[0]} SHORT  qty={qty}  entry≈{Close[0]:F2}  SLestructura={stopLevel:F2}  " +
                  $"stopDist={stopDist:F2}  target={targetRef:F2}  SLfinal={finalSLLevel:F2} (riesgo máx≈{(maxDistAllowed * qty * Instrument.MasterInstrument.PointValue):F2})  modo={ModoSalida}");
        }

        private void EntrarLargo()
        {
            double minEstructura = BuscarMinimoEstructura(Close[0]);
            if (double.IsNaN(minEstructura))
            {
                Print($"[Anza] {Time[0]} Largo omitido: no hay mínimo de estructura por debajo del precio.");
                lastSwingHighUsed = true;
                return;
            }

            double stopLevel = minEstructura - BufferStopTicks * TickSize;
            double stopDist  = Close[0] - stopLevel;
            if (stopDist <= TickSize)
            {
                Print($"[Anza] {Time[0]} Largo omitido: distancia de stop inválida ({stopDist:F2}).");
                lastSwingHighUsed = true;
                return;
            }

            int qty = CalcularContratos(stopDist);
            if (qty < 1)
            {
                Print($"[Anza] {Time[0]} Largo omitido: el riesgo por trade no alcanza ni 1 contrato (stopDist {stopDist:F2}).");
                lastSwingHighUsed = true;
                return;
            }

            double catDist  = CalcularDistanciaCatastrofica(stopDist, qty);

            double maxDistAllowed = catDist;
            if (PorcentajeCorteTemprano > 0)
            {
                double corteDist = stopDist * (PorcentajeCorteTemprano / 100.0);
                maxDistAllowed = Math.Min(catDist, corteDist);
            }

            double finalSLLevel = Close[0] - maxDistAllowed;
            int stopTicks   = Math.Max(1, (int)Math.Round(stopDist / TickSize));
            int finalSLTicks = Math.Max(1, (int)Math.Round(maxDistAllowed / TickSize));

            structureStop      = stopLevel;
            targetRef          = Close[0] + stopDist * RR;

            SetStopLoss("ANZA_Long", CalculationMode.Ticks,
                ModoSalida == AnzaExitMode.StopFijoClasico ? stopTicks : finalSLTicks, false);
            SetProfitTarget("ANZA_Long", CalculationMode.Price, targetRef);
            EnterLong(qty, "ANZA_Long");

            tradesToday++;
            lastSwingHighUsed = true;
            Print($"[Anza] {Time[0]} LONG  qty={qty}  entry≈{Close[0]:F2}  SLestructura={stopLevel:F2}  " +
                  $"stopDist={stopDist:F2}  target={targetRef:F2}  SLfinal={finalSLLevel:F2} (riesgo máx≈{(maxDistAllowed * qty * Instrument.MasterInstrument.PointValue):F2})  modo={ModoSalida}");
        }

        // ════════════════════════════════════════════════════
        private void GestionarCorto()
        {
            int  nowTime    = ToTime(Time[0]);
            bool pastWindow = nowTime > endSec;

            if (CerrarAlFinVentana && pastWindow)
            {
                CerrarTodoCorto();
                Print($"[Anza] {Time[0]} Cierre SHORT — fin de la ventana operativa (prioridad).");
                return;
            }

            bool   salir  = false;
            string motivo = "";

            if ((ModoSalida == AnzaExitMode.CierreConfirmado || ModoSalida == AnzaExitMode.Combinado)
                && Close[0] > structureStop)
            {
                salir = true; motivo = "cierre por encima del máximo de estructura";
            }
            if (!salir
                && (ModoSalida == AnzaExitMode.CruceEmaVwap || ModoSalida == AnzaExitMode.Combinado)
                && ema[0] > vwap)
            {
                salir = true; motivo = "EMA cruzó el VWAP en sentido contrario";
            }

            if (salir)
            {
                CerrarTodoCorto();
                Print($"[Anza] {Time[0]} Cierre SHORT — motivo: {motivo}.");
            }
        }

        private void GestionarLargo()
        {
            int  nowTime    = ToTime(Time[0]);
            bool pastWindow = nowTime > endSec;

            if (CerrarAlFinVentana && pastWindow)
            {
                CerrarTodoLargo();
                Print($"[Anza] {Time[0]} Cierre LONG — fin de la ventana operativa (prioridad).");
                return;
            }

            bool   salir  = false;
            string motivo = "";

            if ((ModoSalida == AnzaExitMode.CierreConfirmado || ModoSalida == AnzaExitMode.Combinado)
                && Close[0] < structureStop)
            {
                salir = true; motivo = "cierre por debajo del mínimo de estructura";
            }
            if (!salir
                && (ModoSalida == AnzaExitMode.CruceEmaVwap || ModoSalida == AnzaExitMode.Combinado)
                && ema[0] < vwap)
            {
                salir = true; motivo = "EMA cruzó el VWAP en sentido contrario";
            }

            if (salir)
            {
                CerrarTodoLargo();
                Print($"[Anza] {Time[0]} Cierre LONG — motivo: {motivo}.");
            }
        }

        // ════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════
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
            double pointValue      = Instrument.MasterInstrument.PointValue;
            double riskPerContract = stopDist * pointValue;
            if (riskPerContract <= 0) return 0;
            int qty = (int)Math.Floor(RiesgoPorTrade / riskPerContract);
            return Math.Max(0, Math.Min(qty, MaxContratos));
        }

        private double CalcularDistanciaCatastrofica(double stopDist, int qty)
        {
            double dist;
            switch (ModoStopCatastrofico)
            {
                case AnzaCatastrophicMode.TicksMasAllaDelNivel:
                    dist = stopDist + StopCatastroficoTicks * TickSize;
                    break;
                case AnzaCatastrophicMode.MaximoEnMoneda:
                    double pv = Instrument.MasterInstrument.PointValue;
                    dist = (qty > 0 && pv > 0)
                         ? StopCatastroficoMoneda / (qty * pv)
                         : stopDist * 2.0;
                    break;
                default: // MultiploEstructura
                    dist = stopDist * MultiploStopCatastrofico;
                    break;
            }
            double minDist = stopDist + TickSize;
            if (dist < minDist)
            {
                Print($"[Anza] {Time[0]} Aviso: stop catastrófico ({dist:F2}) más ajustado que el nivel de " +
                      $"estructura ({stopDist:F2}); se ajusta a {minDist:F2}. Sube 'ticks/moneda' del catastrófico.");
                dist = minDist;
            }
            return dist;
        }

        private void CerrarTodoLargo()
        {
            string label = Position.AveragePrice < Close[0] ? "TP Anza" : "SL Anza";
            ExitLong(label, "ANZA_Long");
        }

        private void CerrarTodoCorto()
        {
            string label = Position.AveragePrice > Close[0] ? "TP Anza" : "SL Anza";
            ExitShort(label, "ANZA_Short");
        }

        private int ParseHHmmss(string txt, int fallback)
        {
            try
            {
                TimeSpan ts = TimeSpan.Parse(txt.Trim());
                return ts.Hours * 10000 + ts.Minutes * 100 + ts.Seconds;
            }
            catch
            {
                Print($"[Anza] Hora inválida '{txt}', usando {fallback} (HHmmss).");
                return fallback;
            }
        }

        // ════════════════════════════════════════════════════
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                Print($"[Anza] Fill: {execution.Order.Name} | {marketPosition} | precio {price:F2} | qty {quantity}");

                bool isEntry = execution.Order.Name.Contains("ANZA_");
                if (isEntry)
                {
                    if (!tradeActivo)
                    {
                        tradeActivo = true;
                        precioEntradaRef = price;
                        peorPrecioTrade = price;
                        stopLossInicialRef = structureStop;
                        currentPositionQty = quantity;
                    }
                    else
                    {
                        currentPositionQty += quantity;
                    }
                }
                else if (tradeActivo)
                {
                    currentPositionQty -= quantity;
                    if (currentPositionQty <= 0)
                    {
                        tradeActivo = false;
                        currentPositionQty = 0;

                        double distanciaStop = Math.Abs(precioEntradaRef - stopLossInicialRef);

                        bool eraLong = stopLossInicialRef < precioEntradaRef;
                        if (eraLong)
                            peorPrecioTrade = Math.Min(peorPrecioTrade, price);
                        else
                            peorPrecioTrade = Math.Max(peorPrecioTrade, price);

                        double retrocesoMaximo = Math.Abs(precioEntradaRef - peorPrecioTrade);
                        double porcentajeRetroceso = (distanciaStop > 0) ? (retrocesoMaximo / distanciaStop) * 100.0 : 0;

                        bool esTP = eraLong ? price >= precioEntradaRef : price <= precioEntradaRef;

                        if (esTP)
                        {
                            sumaRetrocesosTP += porcentajeRetroceso;
                            conteoTradesTP++;
                            double promedioTP = sumaRetrocesosTP / conteoTradesTP;
                            Print($"[Anza-STATS] TRADE TP cerrado. Retroceso máximo: {porcentajeRetroceso:F1}% del SL. Promedio acumulado: {promedioTP:F1}%.");
                        }
                        else
                        {
                            Print($"[Anza-STATS] TRADE SL cerrado. Retroceso: {porcentajeRetroceso:F1}% de la estructura.");
                        }

                        precioEntradaRef = double.NaN;
                        stopLossInicialRef = double.NaN;
                        peorPrecioTrade = double.NaN;
                    }
                }
            }
        }
    }
}
