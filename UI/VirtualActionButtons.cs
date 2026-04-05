// =============================================================================
//  La Via della Redenzione — UI/VirtualActionButtons.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Quattro pulsanti azione touch per Android, disposti a rombo
//                stile controller (alto/destra/basso/sinistra). ContentView
//                MAUI con rendering Canvas 2D e supporto multi-touch.
//
//  Layout rombo:
//    Alto    → ActionA (Usa Carta)  — blu   (#3B82F6)
//    Destra  → ActionB (Difendi)   — verde (#22C55E)
//    Basso   → ActionC (Oggetti)   — giallo(#EAB308)
//    Sinistra→ ActionD (Fuggi)     — rosso (#EF4444)
//
//  Pulsante separato Cancel/Pausa: VirtualPauseButton (in fondo al file).
//
//  Posizionamento (impostato da GameManager):
//    AbsoluteLayout.LayoutBounds = (0.6, 0.7, 0.4, 0.3)
//    AbsoluteLayout.LayoutFlags  = All (proporzionale)
//
//  Multi-touch:
//    Ogni dito ha un touchId univoco. Più pulsanti possono essere premuti
//    contemporaneamente.
//
//  CORREZIONE BUG:
//    Come VirtualDPad.cs, il file originale importava Platforms.Android
//    e richiedeva TouchInputHandler nel costruttore, rompendo la build
//    Windows. La dipendenza è stata sostituita con delegate statici
//    iniettati da TouchInputHandler sul solo target Android:
//      VirtualActionButtons.ActionButtonTouchHandler
//      VirtualActionButtons.CancelButtonTouchHandler
// =============================================================================

using LaViaDellaRedenzione.Core;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Collections.Generic;

namespace LaViaDellaRedenzione.UI
{
    // -------------------------------------------------------------------------
    //  DEFINIZIONE PULSANTE
    // -------------------------------------------------------------------------

    internal sealed class ActionButtonDef
    {
        public InputAction Action { get; init; }
        public string      Label  { get; init; } = string.Empty;
        public Color       Color  { get; init; } = Colors.White;
        public float NormX { get; init; }
        public float NormY { get; init; }
    }

    // -------------------------------------------------------------------------
    //  DRAWABLE — disegna i 4 pulsanti a rombo
    // -------------------------------------------------------------------------

    internal sealed class ActionButtonsDrawable : IDrawable
    {
        private readonly ActionButtonDef[] _buttons;

        public readonly HashSet<InputAction> ActiveActions = new();
        public float ButtonRadius { get; set; } = 28f;

        private static readonly Color LabelColor = Color.FromRgba(255, 255, 255, 220);
        private static readonly Color PressedRim = Colors.White;
        private static readonly Color NormalRim  = Color.FromRgba(255, 255, 255, 80);

        public ActionButtonsDrawable(ActionButtonDef[] buttons)
            => _buttons = buttons;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float w = dirtyRect.Width;
            float h = dirtyRect.Height;

            foreach (var btn in _buttons)
            {
                float cx     = btn.NormX * w;
                float cy     = btn.NormY * h;
                bool  active = ActiveActions.Contains(btn.Action);

                byte alpha = active ? (byte)220 : (byte)130;
                canvas.FillColor = Color.FromRgba(
                    (byte)(btn.Color.Red   * 255),
                    (byte)(btn.Color.Green * 255),
                    (byte)(btn.Color.Blue  * 255),
                    alpha);
                canvas.FillCircle(cx, cy, ButtonRadius);

                canvas.StrokeColor = active ? PressedRim : NormalRim;
                canvas.StrokeSize  = active ? 2.5f : 1.5f;
                canvas.DrawCircle(cx, cy, ButtonRadius);

                canvas.FontColor = LabelColor;
                canvas.FontSize  = ButtonRadius * 0.72f;
                canvas.DrawString(
                    btn.Label,
                    cx - ButtonRadius, cy - ButtonRadius,
                    ButtonRadius * 2,  ButtonRadius * 2,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }
        }

        public void UpdateButtonRadius(float viewWidth, float viewHeight)
        {
            float minDim = MathF.Min(viewWidth, viewHeight);
            ButtonRadius = Math.Clamp(minDim * 0.20f, 22f, 40f);
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
        //  Delegate iniettati da TouchInputHandler (solo su Android)
        //
        //  TouchInputHandler li imposta nel proprio costruttore:
        //    VirtualActionButtons.ActionButtonTouchHandler = OnActionButtonTouch;
        //    VirtualActionButtons.CancelButtonTouchHandler = OnCancelButtonTouch;
        // ------------------------------------------------------------------

        /// <summary>
        /// Chiamato al press/release di un pulsante azione.
        /// Parametri: touchId, InputAction, isDown.
        /// </summary>
        public static Action<long, InputAction, bool>? ActionButtonTouchHandler { get; set; }

        /// <summary>
        /// Chiamato al press/release del pulsante Cancel/Pausa.
        /// Parametro: isDown.
        /// </summary>
        public static Action<bool>? CancelButtonTouchHandler { get; set; }

        // ------------------------------------------------------------------
        //  Definizioni pulsanti
        // ------------------------------------------------------------------

        private static readonly ActionButtonDef[] ButtonDefs = new[]
        {
            new ActionButtonDef
            {
                Action = InputAction.ActionA, Label = "A",
                Color  = Color.FromRgb(59,  130, 246),   // blu
                NormX  = 0.5f, NormY = 0.10f
            },
            new ActionButtonDef
            {
                Action = InputAction.ActionB, Label = "B",
                Color  = Color.FromRgb(34,  197, 94),    // verde
                NormX  = 0.90f, NormY = 0.5f
            },
            new ActionButtonDef
            {
                Action = InputAction.ActionC, Label = "C",
                Color  = Color.FromRgb(234, 179, 8),     // giallo
                NormX  = 0.5f, NormY = 0.90f
            },
            new ActionButtonDef
            {
                Action = InputAction.ActionD, Label = "D",
                Color  = Color.FromRgb(239, 68,  68),    // rosso
                NormX  = 0.10f, NormY = 0.5f
            }
        };

        // ------------------------------------------------------------------
        //  Componenti
        // ------------------------------------------------------------------

        private readonly ActionButtonsDrawable _drawable;
        private readonly GraphicsView          _graphicsView;

        private readonly Dictionary<long, ActionButtonDef> _activeTouches = new();
        private long _nextTouchId = 0;

        // ------------------------------------------------------------------
        //  Costruttore
        // ------------------------------------------------------------------

        public VirtualActionButtons()
        {
            _drawable = new ActionButtonsDrawable(ButtonDefs);

            _graphicsView = new GraphicsView
            {
                Drawable          = _drawable,
                BackgroundColor   = Colors.Transparent,
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
                            ActionButtonTouchHandler?.Invoke(id, btn.Action, true);
                            _graphicsView.Invalidate();
                        }
                        break;
                    }

                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                    {
                        foreach (var kvp in _activeTouches)
                        {
                            _drawable.ActiveActions.Remove(kvp.Value.Action);
                            ActionButtonTouchHandler?.Invoke(kvp.Key, kvp.Value.Action, false);
                        }
                        _activeTouches.Clear();
                        _graphicsView.Invalidate();
                        break;
                    }
                }
            };

            GestureRecognizers.Add(pan);

            var tap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };

            tap.Tapped += (s, e) =>
            {
                var pos = e.GetPosition(_graphicsView);
                if (pos == null) return;

                var btn = HitTest((float)pos.Value.X, (float)pos.Value.Y);
                if (btn == null) return;

                long id = _nextTouchId++;
                ActionButtonTouchHandler?.Invoke(id, btn.Action, true);
                _drawable.ActiveActions.Add(btn.Action);
                _graphicsView.Invalidate();

                Microsoft.Maui.Controls.Application.Current?.Dispatcher.DispatchDelayed(
                    TimeSpan.FromMilliseconds(80),
                    () =>
                    {
                        ActionButtonTouchHandler?.Invoke(id, btn.Action, false);
                        _drawable.ActiveActions.Remove(btn.Action);
                        _graphicsView.Invalidate();
                    });
            };

            GestureRecognizers.Add(tap);
        }

        // ------------------------------------------------------------------
        //  HIT TEST
        // ------------------------------------------------------------------

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
    //  VIRTUAL PAUSE BUTTON
    // =========================================================================

    /// <summary>
    /// Pulsante piccolo per aprire il menu di pausa o annullare.
    /// Posizionato in alto a destra dello schermo.
    /// </summary>
    public sealed class VirtualPauseButton : ContentView
    {
        private static readonly Color BgNormal  = Color.FromRgba(255, 255, 255, 70);
        private static readonly Color BgPressed = Color.FromRgba(255, 255, 255, 160);

        public VirtualPauseButton()
        {
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

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                frame.BackgroundColor = BgPressed;
                VirtualActionButtons.CancelButtonTouchHandler?.Invoke(true);

                Application.Current?.Dispatcher.DispatchDelayed(
                    TimeSpan.FromMilliseconds(100),
                    () =>
                    {
                        frame.BackgroundColor = BgNormal;
                        VirtualActionButtons.CancelButtonTouchHandler?.Invoke(false);
                    });
            };

            GestureRecognizers.Add(tap);
        }
    }
}
