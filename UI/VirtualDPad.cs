// =============================================================================
//  La Via della Redenzione — UI/VirtualDPad.cs
//  Package : com.refa.valdrath
//
//  Descrizione : D-Pad virtuale touch per Android. ContentView MAUI che
//                disegna un cerchio semi-trasparente con croce direzionale
//                e gestisce i gesti touch (tap e pan).
//
//  Posizionamento:
//    Lato sinistro dello schermo, fascia inferiore.
//    Occupa il 40% della larghezza sinistra e il 30% dell'altezza bottom.
//    Visibile SOLO su Android (DeviceInfo.Platform check in GameManager).
//
//  Design:
//    - Cerchio di sfondo semi-trasparente (opacity 0.5)
//    - Croce con 4 frecce direzionali
//    - Freccia attiva si illumina al touch
//    - Supporta tap discreti e hold/swipe
//    - Si ridimensiona in base alla densità DPI del device
//
//  Rendering:
//    Usa MAUI GraphicsView con IDrawable per disegnare il D-Pad
//    con Canvas 2D. Nessuna immagine esterna richiesta.
//
//  CORREZIONE BUG:
//    Il file originale importava LaViaDellaRedenzione.Platforms.Android
//    e riceveva TouchInputHandler direttamente nel costruttore. Essendo
//    questo file in /UI/ (compilato su tutte le piattaforme), la build
//    Windows falliva perché TouchInputHandler esiste solo nella build Android.
//    La dipendenza è stata invertita tramite delegate statici (stesso pattern
//    usato per InputHintBar.GamepadHintResolver):
//      VirtualDPad.DPadTouchHandler  — iniettato da TouchInputHandler su Android
//      VirtualDPad.ScreenTapHandler  — iniettato da TouchInputHandler su Android
//    Su Windows i delegate restano null e non vengono mai invocati perché
//    VirtualDPad.IsVisible è false (PlatformAdaptiveLayout).
// =============================================================================

using LaViaDellaRedenzione.Core;
using LaViaDellaRedenzione.Systems;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;

namespace LaViaDellaRedenzione.UI
{
    // -------------------------------------------------------------------------
    //  DRAWABLE — disegna il D-Pad su GraphicsView
    // -------------------------------------------------------------------------

    /// <summary>
    /// IDrawable per il D-Pad virtuale. Disegna cerchio, croce e frecce
    /// con MAUI Canvas 2D. Aggiornato ogni frame tramite Invalidate().
    /// </summary>
    internal sealed class DPadDrawable : IDrawable
    {
        // Stato corrente delle direzioni attive (per highlight)
        public bool ActiveUp    { get; set; }
        public bool ActiveDown  { get; set; }
        public bool ActiveLeft  { get; set; }
        public bool ActiveRight { get; set; }

        // Colori
        private static readonly Color BgColor    = Color.FromRgba(255, 255, 255, 80);
        private static readonly Color ArrowColor  = Color.FromRgba(255, 255, 255, 160);
        private static readonly Color ArrowActive = Color.FromRgba(255, 255, 255, 255);
        private static readonly Color RimColor    = Color.FromRgba(255, 255, 255, 50);

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float cx = dirtyRect.Width  * 0.5f;
            float cy = dirtyRect.Height * 0.5f;
            float r  = MathF.Min(cx, cy) * 0.92f;
            float arrowSize = r * 0.32f;
            float arrowDist = r * 0.55f;

            // ── Cerchio di sfondo ────────────────────────────────────────────
            canvas.FillColor   = BgColor;
            canvas.FillCircle(cx, cy, r);

            canvas.StrokeColor = RimColor;
            canvas.StrokeSize  = 1.5f;
            canvas.DrawCircle(cx, cy, r);

            // ── Cerchio centrale piccolo ─────────────────────────────────────
            canvas.FillColor = Color.FromRgba(255, 255, 255, 40);
            canvas.FillCircle(cx, cy, r * 0.18f);

            // ── Frecce direzionali ───────────────────────────────────────────
            DrawArrow(canvas, cx, cy - arrowDist, 0f,   arrowSize, ActiveUp);    // Su
            DrawArrow(canvas, cx, cy + arrowDist, 180f, arrowSize, ActiveDown);  // Giù
            DrawArrow(canvas, cx - arrowDist, cy, 90f,  arrowSize, ActiveLeft);  // Sinistra
            DrawArrow(canvas, cx + arrowDist, cy, 270f, arrowSize, ActiveRight); // Destra

            // ── Linee della croce ────────────────────────────────────────────
            canvas.StrokeColor = Color.FromRgba(255, 255, 255, 30);
            canvas.StrokeSize  = 1f;
            canvas.DrawLine(cx - r * 0.85f, cy, cx + r * 0.85f, cy);
            canvas.DrawLine(cx, cy - r * 0.85f, cx, cy + r * 0.85f);
        }

        /// <summary>
        /// Disegna una freccia triangolare centrata in (x, y).
        /// rotation: 0 = punta su, 180 = punta giù, 90 = sinistra, 270 = destra.
        /// </summary>
        private static void DrawArrow(
            ICanvas canvas,
            float x, float y,
            float rotation,
            float size,
            bool active)
        {
            canvas.SaveState();
            canvas.Translate(x, y);
            canvas.Rotate(rotation);

            var path = new PathF();
            path.MoveTo(0,           -size * 0.5f);
            path.LineTo(-size * 0.45f, size * 0.4f);
            path.LineTo( size * 0.45f, size * 0.4f);
            path.Close();

            canvas.FillColor = active ? ArrowActive : ArrowColor;
            canvas.FillPath(path);

            canvas.RestoreState();
        }
    }

    // -------------------------------------------------------------------------
    //  VIRTUAL D-PAD
    // -------------------------------------------------------------------------

    /// <summary>
    /// ContentView MAUI che ospita il GraphicsView del D-Pad e gestisce
    /// i gesture recognizer touch.
    ///
    /// POSIZIONAMENTO (impostato da GameManager nel layout della BattleScreen
    /// e della SideScrollScreen):
    ///   AbsoluteLayout.LayoutBounds = (0, 0.7, 0.4, 0.3)
    ///   AbsoluteLayout.LayoutFlags  = All (proporzionale)
    ///
    /// VISIBILITÀ:
    ///   IsVisible viene impostato a false su Windows da PlatformAdaptiveLayout.
    /// </summary>
    public sealed class VirtualDPad : ContentView
    {
        // ------------------------------------------------------------------
        //  Delegate iniettati da TouchInputHandler (solo su Android)
        //
        //  TouchInputHandler li imposta nel proprio costruttore:
        //    VirtualDPad.DPadTouchHandler  = OnDPadTouch;
        //    VirtualDPad.ScreenTapHandler  = OnScreenTap;
        //    VirtualDPad.SetBoundsCallback = SetDPadBounds;
        //  Su Windows restano null — non vengono mai chiamati perché
        //  VirtualDPad è nascosto (IsVisible = false).
        // ------------------------------------------------------------------

        /// <summary>
        /// Chiamato quando il dito si muove sul D-Pad.
        /// Parametri: touchX, touchY (coordinate assolute schermo), isDown.
        /// </summary>
        public static Action<float, float, bool>? DPadTouchHandler { get; set; }

        /// <summary>
        /// Chiamato per un tap singolo fuori dal pan (skip cutscene, conferma).
        /// </summary>
        public static Action? ScreenTapHandler { get; set; }

        /// <summary>
        /// Chiamato quando il layout cambia per aggiornare i bounds nell'handler.
        /// Parametri: centerX, centerY, radius (tutti in coordinate assolute schermo).
        /// </summary>
        public static Action<float, float, float>? SetBoundsCallback { get; set; }

        // ------------------------------------------------------------------
        //  Drawable e view
        // ------------------------------------------------------------------

        private readonly DPadDrawable _drawable = new();
        private readonly GraphicsView _graphicsView;

        // ------------------------------------------------------------------
        //  Stato touch corrente
        // ------------------------------------------------------------------

        private bool _isTouching = false;

        // ------------------------------------------------------------------
        //  Costruttore
        // ------------------------------------------------------------------

        public VirtualDPad()
        {
            _graphicsView = new GraphicsView
            {
                Drawable          = _drawable,
                BackgroundColor   = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions   = LayoutOptions.Fill
            };

            Content = _graphicsView;

            MinimumWidthRequest  = 130;
            MinimumHeightRequest = 130;
            BackgroundColor      = Colors.Transparent;

            AttachGestureRecognizers();
        }

        // ------------------------------------------------------------------
        //  GESTURE RECOGNIZERS
        // ------------------------------------------------------------------

        private void AttachGestureRecognizers()
        {
            // ── Pan (hold e swipe) ────────────────────────────────────────────
            var pan = new PanGestureRecognizer();

            pan.PanUpdated += (s, e) =>
            {
                switch (e.StatusType)
                {
                    case GestureStatus.Started:
                        _isTouching = true;
                        UpdateDPadTouch(GetTouchPosition(e), true);
                        break;

                    case GestureStatus.Running:
                        if (_isTouching)
                            UpdateDPadTouch(GetTouchPosition(e), true);
                        break;

                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        _isTouching = false;
                        ResetArrows();
                        DPadTouchHandler?.Invoke(0, 0, false);
                        break;
                }
            };

            GestureRecognizers.Add(pan);

            // ── Tap discreto ─────────────────────────────────────────────────
            var tap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };

            tap.Tapped += (s, e) =>
            {
                if (!_isTouching)
                    ScreenTapHandler?.Invoke();
            };

            GestureRecognizers.Add(tap);
        }

        // ------------------------------------------------------------------
        //  AGGIORNAMENTO TOUCH
        // ------------------------------------------------------------------

        private void UpdateDPadTouch(Point touchPos, bool isDown)
        {
            float centerX = (float)(X + Width  * 0.5);
            float centerY = (float)(Y + Height * 0.5);
            float radius  = (float)(MathF.Min(Width, Height) * 0.5 * 0.92);

            SetBoundsCallback?.Invoke(centerX, centerY, radius);

            DPadTouchHandler?.Invoke(
                (float)touchPos.X + (float)X,
                (float)touchPos.Y + (float)Y,
                isDown);

            UpdateArrowVisuals();
        }

        private static Point GetTouchPosition(PanUpdatedEventArgs e)
            => new Point(e.TotalX, e.TotalY);

        private void UpdateArrowVisuals()
        {
            var input = InputSystem.Instance;
            bool changed =
                _drawable.ActiveUp    != input.IsPressed(InputAction.NavigateUp)    ||
                _drawable.ActiveDown  != input.IsPressed(InputAction.NavigateDown)  ||
                _drawable.ActiveLeft  != input.IsPressed(InputAction.NavigateLeft)  ||
                _drawable.ActiveRight != input.IsPressed(InputAction.NavigateRight);

            _drawable.ActiveUp    = input.IsPressed(InputAction.NavigateUp);
            _drawable.ActiveDown  = input.IsPressed(InputAction.NavigateDown);
            _drawable.ActiveLeft  = input.IsPressed(InputAction.NavigateLeft);
            _drawable.ActiveRight = input.IsPressed(InputAction.NavigateRight);

            if (changed)
                _graphicsView.Invalidate();
        }

        private void ResetArrows()
        {
            _drawable.ActiveUp    = false;
            _drawable.ActiveDown  = false;
            _drawable.ActiveLeft  = false;
            _drawable.ActiveRight = false;
            _graphicsView.Invalidate();
        }

        // ------------------------------------------------------------------
        //  LAYOUT
        // ------------------------------------------------------------------

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            if (width > 0 && height > 0)
            {
                float centerX = (float)(X + width  * 0.5);
                float centerY = (float)(Y + height * 0.5);
                float radius  = (float)(MathF.Min(width, height) * 0.5 * 0.92);
                SetBoundsCallback?.Invoke(centerX, centerY, radius);
            }
        }
    }
}
