// =============================================================================
//  La Via della Redenzione — UI/VirtualActionButtons.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Quattro pulsanti azione touch per Android, disposti a rombo
//                stile controller (alto/destra/basso/sinistra). ContentView
//                MAUI con rendering Canvas 2D e supporto multi-touch.
//
//  Layout rombo:
//    Alto   → ActionA (Usa Carta)   — blu   (#3B82F6)
//    Destra → ActionB (Difendi)     — verde (#22C55E)
//    Basso  → ActionC (Oggetti)     — giallo(#EAB308)
//    Sinistra→ActionD (Fuggi)       — rosso (#EF4444)
//
//  Pulsante separato Cancel/Pausa in alto a destra dello schermo
//  (gestito da VirtualPauseButton, classe separata in fondo al file).
//
//  Posizionamento (impostato da GameManager):
//    AbsoluteLayout.LayoutBounds = (0.6, 0.7, 0.4, 0.3)
//    AbsoluteLayout.LayoutFlags  = All (proporzionale)
//
//  Multi-touch:
//    Ogni dito ha un touchId univoco. Più pulsanti possono essere premuti
//    contemporaneamente (es. Usa Carta + Difendi in sequenza rapida).
//
//  Visibilità:
//    IsVisible = false su Windows (PlatformAdaptiveLayout).
// =============================================================================

using LaViaDellaRedenzione.Core;
using LaViaDellaRedenzione.Platforms.Android;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Collections.Generic;

namespace LaViaDellaRedenzione.UI
{
    // -------------------------------------------------------------------------
    //  DEFINIZIONE PULSANTE
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dati di un singolo pulsante nel layout a rombo.
    /// </summary>
    internal sealed class ActionButtonDef
    {
        public InputAction Action  { get; init; }
        public string      Label  { get; init; } = string.Empty;
        public Color       Color  { get; init; } = Colors.White;

        /// <summary>
        /// Posizione normalizzata (0..1) nel ContentView.
        /// Calcolata geometricamente dal rombo.
        /// </summary>
        public float NormX { get; init; }
        public float NormY { get; init; }
    }

    // -------------------------------------------------------------------------
    //  DRAWABLE — disegna i 4 pulsanti a rombo
    // -------------------------------------------------------------------------

    internal sealed class ActionButtonsDrawable : IDrawable
    {
        private readonly ActionButtonDef[] _buttons;

        /// <summary>Set delle azioni attualmente premute (per highlight).</summary>
        public readonly HashSet<InputAction> ActiveActions = new();

        /// <summary>Raggio di ogni pulsante in pixel logici.</summary>
        public float ButtonRadius { get; set; } = 28f;

        // Colori UI
        private static readonly Color LabelColor  = Color.FromRgba(255, 255, 255, 220);
        private static readonly Color PressedRim  = Colors.White;
        private static readonly Color NormalRim   = Color.FromRgba(255, 255, 255, 80);

        public ActionButtonsDrawable(ActionButtonDef[] buttons)
            => _buttons = buttons;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float w = dirtyRect.Width;
            float h = dirtyRect.Height;

            foreach (var btn in _buttons)
            {
                float cx = btn.NormX * w;
                float cy = btn.NormY * h;
                bool  active = ActiveActions.Contains(btn.Action);

                // ── Cerchio di sfondo ──────────────────────────────────────
                byte alpha = active ? (byte)220 : (byte)130;
                canvas.FillColor = Color.FromRgba(
                    (byte)(btn.Color.Red   * 255),
                    (byte)(btn.Color.Green * 255),
                    (byte)(btn.Color.Blue  * 255),
                    alpha);
                canvas.FillCircle(cx, cy, ButtonRadius);

                // ── Bordo ─────────────────────────────────────────────────
                canvas.StrokeColor = active ? PressedRim : NormalRim;
                canvas.StrokeSize  = active ? 2.5f : 1.5f;
                canvas.DrawCircle(cx, cy, ButtonRadius);

                // ── Label (lettera) ────────────────────────────────────────
                canvas.FontColor = LabelColor;
                canvas.FontSize  = ButtonRadius * 0.72f;
                canvas.DrawString(
                    btn.Label,
                    cx - ButtonRadius, cy - ButtonRadius,
                    ButtonRadius * 2, ButtonRadius * 2,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }
        }

        /// <summary>Calcola il raggio del pulsante in base alla dimensione del view.</summary>
        public void UpdateButtonRadius(float viewWidth, float viewHeight)
        {
            float minDim = MathF.Min(viewWidth, viewHeight);
            ButtonRadius = minDim * 0.20f;
            // Clamp: minimo 22px (dispositivi piccoli), massimo 40px (tablet)
            ButtonRadius = Math.Clamp(ButtonRadius, 22f, 40f);
        }
    }

    // -------------------------------------------------------------------------
    //  VIRTUAL ACTION BUTTONS
    // -------------------------------------------------------------------------

    /// <summary>
    /// ContentView con i 4 pulsanti azione a rombo per la battle screen
    /// e il side-scroll su Android.
    /// </summary>
    public sealed class VirtualActionButtons : ContentView
    {
        // ------------------------------------------------------------------
        //  Definizioni pulsanti (layout a rombo)
        // ------------------------------------------------------------------

        /// <summary>
        /// Posizioni normalizzate nel rombo:
        ///   Alto    (0.5, 0.1)
        ///   Destra  (0.9, 0.5)
        ///   Basso   (0.5, 0.9)
        ///   Sinistra(0.1, 0.5)
        /// </summary>
        private static readonly ActionButtonDef[] ButtonDefs = new[]
        {
            new ActionButtonDef
            {
                Action = InputAction.ActionA,
                Label  = "A",
                Color  = Color.FromRgb(59,  130, 246),  // blu
                NormX  = 0.5f,
                NormY  = 0.10f
            },
            new ActionButtonDef
            {
                Action = InputAction.ActionB,
                Label  = "B",
                Color  = Color.FromRgb(34,  197, 94),   // verde
                NormX  = 0.90f,
                NormY  = 0.5f
            },
            new ActionButtonDef
            {
                Action = InputAction.ActionC,
                Label  = "C",
                Color  = Color.FromRgb(234, 179, 8),    // giallo
                NormX  = 0.5f,
                NormY  = 0.90f
            },
            new ActionButtonDef
            {
                Action = InputAction.ActionD,
                Label  = "D",
                Color  = Color.FromRgb(239, 68,  68),   // rosso
                NormX  = 0.10f,
                NormY  = 0.5f
            }
        };

        // ------------------------------------------------------------------
        //  Componenti
        // ------------------------------------------------------------------

        private readonly TouchInputHandler       _handler;
        private readonly ActionButtonsDrawable   _drawable;
        private readonly GraphicsView            _graphicsView;

        // ------------------------------------------------------------------
        //  Tracking touch per multi-touch
        // ------------------------------------------------------------------

        /// <summary>Mappa touchId → pulsante premuto.</summary>
        private readonly Dictionary<long, ActionButtonDef> _activeTouches = new();

        private long _nextTouchId = 0;

        // ------------------------------------------------------------------
        //  Costruttore
        // ------------------------------------------------------------------

        public VirtualActionButtons(TouchInputHandler handler)
        {
            _handler  = handler ?? throw new ArgumentNullException(nameof(handler));
            _drawable = new ActionButtonsDrawable(ButtonDefs);

            _graphicsView = new GraphicsView
            {
                Drawable        = _drawable,
                BackgroundColor = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions   = LayoutOptions.Fill
            };

            Content         = _graphicsView;
            BackgroundColor = Colors.Transparent;

            MinimumWidthRequest  = 140;
            MinimumHeightRequest = 140;

            AttachGestureRecognizers();
        }

        // ------------------------------------------------------------------
        //  GESTURE RECOGNIZERS
        // ------------------------------------------------------------------

        private void AttachGestureRecognizers()
        {
            // ── Pan per hold e multi-touch ────────────────────────────────
            var pan = new PanGestureRecognizer();

            pan.PanUpdated += (s, e) =>
            {
                switch (e.StatusType)
                {
                    case GestureStatus.Started:
                    {
                        var btn = HitTest((float)e.TotalX, (float)e.TotalY);
                        if (btn != null)
                        {
                            long id = _nextTouchId++;
                            _activeTouches[id] = btn;
                            _drawable.ActiveActions.Add(btn.Action);
                            _handler.OnActionButtonTouch(id, btn.Action, true);
                            _graphicsView.Invalidate();
                        }
                        break;
                    }

                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                    {
                        // Rilascia tutti i touch attivi
                        foreach (var kvp in _activeTouches)
                        {
                            _drawable.ActiveActions.Remove(kvp.Value.Action);
                            _handler.OnActionButtonTouch(kvp.Key, kvp.Value.Action, false);
                        }
                        _activeTouches.Clear();
                        _graphicsView.Invalidate();
                        break;
                    }
                }
            };

            GestureRecognizers.Add(pan);

            // ── Tap discreto ─────────────────────────────────────────────
            var tap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };

            tap.Tapped += (s, e) =>
            {
                // Ottieni posizione tap
                var pos  = e.GetPosition(_graphicsView);
                if (pos == null) return;

                var btn = HitTest((float)pos.Value.X, (float)pos.Value.Y);
                if (btn == null) return;

                // Simula press + release rapido per tap
                long id = _nextTouchId++;
                _handler.OnActionButtonTouch(id, btn.Action, true);

                // Feedback visivo momentaneo
                _drawable.ActiveActions.Add(btn.Action);
                _graphicsView.Invalidate();

                // Rilascia dopo 80ms (abbastanza per IsJustPressed nel GameLoop)
                Microsoft.Maui.Controls.Application.Current?.Dispatcher.DispatchDelayed(
                    TimeSpan.FromMilliseconds(80),
                    () =>
                    {
                        _handler.OnActionButtonTouch(id, btn.Action, false);
                        _drawable.ActiveActions.Remove(btn.Action);
                        _graphicsView.Invalidate();
                    });
            };

            GestureRecognizers.Add(tap);
        }

        // ------------------------------------------------------------------
        //  HIT TEST
        // ------------------------------------------------------------------

        /// <summary>
        /// Determina quale pulsante è stato toccato in base alla posizione
        /// relativa al ContentView. Ritorna null se nessun pulsante è stato colpito.
        /// </summary>
        private ActionButtonDef? HitTest(float localX, float localY)
        {
            float w = (float)Width;
            float h = (float)Height;
            float r = _drawable.ButtonRadius;

            foreach (var btn in ButtonDefs)
            {
                float cx = btn.NormX * w;
                float cy = btn.NormY * h;
                float dx = localX - cx;
                float dy = localY - cy;

                // Raggio di hit leggermente più grande del raggio visivo
                // per migliorare l'usabilità su schermi piccoli
                if (dx * dx + dy * dy <= (r * 1.2f) * (r * 1.2f))
                    return btn;
            }

            return null;
        }

        // ------------------------------------------------------------------
        //  LAYOUT
        // ------------------------------------------------------------------

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            if (width > 0 && height > 0)
            {
                _drawable.UpdateButtonRadius((float)width, (float)height);
                _graphicsView.Invalidate();
            }
        }
    }

    // =========================================================================
    //  VIRTUAL PAUSE BUTTON — pulsante Cancel/Pausa separato
    // =========================================================================

    /// <summary>
    /// Pulsante piccolo per aprire il menu di pausa o annullare.
    /// Posizionato in alto a destra dello schermo, sempre visibile
    /// durante il gioco (side-scroll e battaglia).
    /// </summary>
    public sealed class VirtualPauseButton : ContentView
    {
        private readonly TouchInputHandler _handler;
        private bool _isPressed = false;

        // Colori
        private static readonly Color BgNormal  = Color.FromRgba(255, 255, 255, 70);
        private static readonly Color BgPressed = Color.FromRgba(255, 255, 255, 160);

        public VirtualPauseButton(TouchInputHandler handler)
        {
            _handler = handler;

            var label = new Label
            {
                Text              = "II",
                TextColor         = Color.FromRgba(255, 255, 255, 200),
                FontSize          = 14,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
                FontAttributes    = FontAttributes.Bold
            };

            var frame = new Frame
            {
                Content         = label,
                BackgroundColor = BgNormal,
                CornerRadius    = 20,
                Padding         = new Thickness(12, 6),
                HasShadow       = false,
                BorderColor     = Color.FromRgba(255, 255, 255, 60)
            };

            Content = frame;

            // Tap per pausa
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                frame.BackgroundColor = BgPressed;
                _handler.OnCancelButtonTouch(true);

                Application.Current?.Dispatcher.DispatchDelayed(
                    TimeSpan.FromMilliseconds(100),
                    () =>
                    {
                        frame.BackgroundColor = BgNormal;
                        _handler.OnCancelButtonTouch(false);
                    });
            };

            GestureRecognizers.Add(tap);
        }
    }
}
