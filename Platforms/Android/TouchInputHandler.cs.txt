// =============================================================================
//  La Via della Redenzione — Platforms/Android/TouchInputHandler.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Handler di input touch per Android. Traduce i gesti del
//                VirtualDPad e dei VirtualActionButtons in InputAction
//                tramite InputSystem.SetPressed().
//
//  Architettura:
//    TouchInputHandler non conosce la logica di gioco — mappa coordinate
//    touch → InputAction → InputSystem.
//    BattleSystem, SideScrollRenderer e WorldMapSystem leggono InputSystem
//    senza sapere che l'input viene dal touch.
//
//  CORREZIONE BUG (accoppiato a VirtualDPad e VirtualActionButtons):
//    In precedenza VirtualDPad e VirtualActionButtons ricevevano
//    TouchInputHandler direttamente nel costruttore, creando una dipendenza
//    su Platforms.Android da codice in /UI/ (cross-platform).
//    Ora il flusso è invertito: TouchInputHandler inietta i propri metodi
//    come delegate statici nelle classi UI nel proprio costruttore.
//    VirtualDPad e VirtualActionButtons chiamano i delegate senza sapere
//    chi li ha forniti.
//
//  Thread safety:
//    I callback MAUI arrivano sul MainThread.
//    InputSystem.SetPressed() è thread-safe per lettura concorrente
//    dal GameLoop thread.
// =============================================================================

using LaViaDellaRedenzione.Core;
using LaViaDellaRedenzione.Systems;
using LaViaDellaRedenzione.UI;
using System;
using System.Collections.Generic;

namespace LaViaDellaRedenzione.Platforms.Android
{
    /// <summary>
    /// Gestisce l'input touch su Android mappando i gesti del D-Pad virtuale
    /// e dei pulsanti azione all'InputSystem unificato.
    ///
    /// LIFECYCLE:
    ///   Istanziato da GameManager su Android all'avvio.
    ///   Il costruttore inietta i delegate nelle classi UI.
    ///   Flush() va chiamato in OnAppSleep() e ad ogni cambio di schermata.
    /// </summary>
    public sealed class TouchInputHandler
    {
        // ------------------------------------------------------------------
        //  Riferimento all'InputSystem singleton
        // ------------------------------------------------------------------

        private readonly InputSystem _input = InputSystem.Instance;

        // ------------------------------------------------------------------
        //  Stato touch D-Pad
        // ------------------------------------------------------------------

        private float _dpadCenterX;
        private float _dpadCenterY;
        private float _dpadRadius = 80f;

        private const float DPAD_DEAD_ZONE  = 12f;
        private const float DIAGONAL_ANGLE  = 30f;

        // ------------------------------------------------------------------
        //  Stato touch pulsanti azione
        // ------------------------------------------------------------------

        private readonly Dictionary<long, InputAction> _activeTouches = new();

        // ------------------------------------------------------------------
        //  COSTRUTTORE — inietta i delegate nelle classi UI cross-platform
        // ------------------------------------------------------------------

        public TouchInputHandler()
        {
            // VirtualDPad: inietta i callback senza che VirtualDPad
            // debba importare Platforms.Android
            VirtualDPad.DPadTouchHandler  = OnDPadTouch;
            VirtualDPad.ScreenTapHandler  = OnScreenTap;
            VirtualDPad.SetBoundsCallback = SetDPadBounds;

            // VirtualActionButtons: inietta i callback per i pulsanti
            VirtualActionButtons.ActionButtonTouchHandler = OnActionButtonTouch;
            VirtualActionButtons.CancelButtonTouchHandler = OnCancelButtonTouch;
        }

        // ------------------------------------------------------------------
        //  D-PAD INPUT
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiorna il centro e il raggio del D-Pad.
        /// Chiamato da VirtualDPad.OnSizeAllocated() via SetBoundsCallback.
        /// </summary>
        public void SetDPadBounds(float centerX, float centerY, float radius)
        {
            _dpadCenterX = centerX;
            _dpadCenterY = centerY;
            _dpadRadius  = radius;
        }

        /// <summary>
        /// Chiamato da VirtualDPad via DPadTouchHandler.
        /// Traduce la posizione touch in direzioni logiche e asse analogico.
        /// </summary>
        public void OnDPadTouch(float touchX, float touchY, bool isDown)
        {
            if (!isDown)
            {
                ReleaseAllDirections();
                _input.SetNavigationAxis(0f, 0f, InputDevice.Touch);
                return;
            }

            float dx       = touchX - _dpadCenterX;
            float dy       = touchY - _dpadCenterY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            if (distance < DPAD_DEAD_ZONE)
            {
                ReleaseAllDirections();
                _input.SetNavigationAxis(0f, 0f, InputDevice.Touch);
                return;
            }

            // Asse analogico normalizzato
            _input.SetNavigationAxis(dx / distance, dy / distance, InputDevice.Touch);

            // Angolo in gradi (0° = destra, 90° = giù)
            float angle = MathF.Atan2(dy, dx) * (180f / MathF.PI);
            if (angle < 0) angle += 360f;

            bool right     = IsInArc(angle, 0f,   DIAGONAL_ANGLE);
            bool down      = IsInArc(angle, 90f,  DIAGONAL_ANGLE);
            bool left      = IsInArc(angle, 180f, DIAGONAL_ANGLE);
            bool up        = IsInArc(angle, 270f, DIAGONAL_ANGLE);
            bool downRight = IsInArc(angle, 45f,  DIAGONAL_ANGLE);
            bool downLeft  = IsInArc(angle, 135f, DIAGONAL_ANGLE);
            bool upLeft    = IsInArc(angle, 225f, DIAGONAL_ANGLE);
            bool upRight   = IsInArc(angle, 315f, DIAGONAL_ANGLE);

            _input.SetPressed(InputAction.NavigateRight,
                right || downRight || upRight,  InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateLeft,
                left  || downLeft  || upLeft,   InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateDown,
                down  || downRight || downLeft, InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateUp,
                up    || upRight   || upLeft,   InputDevice.Touch);
        }

        private void ReleaseAllDirections()
        {
            _input.SetPressed(InputAction.NavigateRight, false, InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateLeft,  false, InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateDown,  false, InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateUp,    false, InputDevice.Touch);
        }

        private static bool IsInArc(float angle, float target, float range)
        {
            float diff = MathF.Abs(angle - target) % 360f;
            if (diff > 180f) diff = 360f - diff;
            return diff <= range;
        }

        // ------------------------------------------------------------------
        //  PULSANTI AZIONE INPUT
        // ------------------------------------------------------------------

        /// <summary>
        /// Chiamato da VirtualActionButtons via ActionButtonTouchHandler.
        /// Supporta multi-touch tramite touchId univoco per ogni dito.
        /// </summary>
        public void OnActionButtonTouch(long touchId, InputAction action, bool isDown)
        {
            if (isDown)
            {
                _activeTouches[touchId] = action;
                _input.SetPressed(action, true, InputDevice.Touch);
            }
            else
            {
                if (_activeTouches.TryGetValue(touchId, out var prevAction))
                {
                    _activeTouches.Remove(touchId);

                    // Rilascia solo se nessun altro touch tiene la stessa azione
                    bool stillActive = false;
                    foreach (var a in _activeTouches.Values)
                        if (a == prevAction) { stillActive = true; break; }

                    if (!stillActive)
                        _input.SetPressed(prevAction, false, InputDevice.Touch);
                }
            }
        }

        /// <summary>
        /// Chiamato da VirtualPauseButton via CancelButtonTouchHandler.
        /// </summary>
        public void OnCancelButtonTouch(bool isDown)
            => _input.SetPressed(InputAction.Cancel, isDown, InputDevice.Touch);

        /// <summary>
        /// Chiamato da hotspot nel side-scroll.
        /// </summary>
        public void OnInteractButtonTouch(bool isDown)
            => _input.SetPressed(InputAction.Interact, isDown, InputDevice.Touch);

        /// <summary>
        /// Tap generico su schermo (skip cutscene, conferma dialogo).
        /// Chiamato da VirtualDPad via ScreenTapHandler.
        /// </summary>
        public void OnScreenTap()
            => _input.SetPressed(InputAction.Confirm, true, InputDevice.Touch);

        // ------------------------------------------------------------------
        //  SCROLL TOUCH (swipe verticale per liste)
        // ------------------------------------------------------------------

        private float _scrollStartY;
        private const float SCROLL_THRESHOLD = 30f;

        public void OnScrollStart(float y)  => _scrollStartY = y;

        public void OnScrollUpdate(float y)
        {
            float delta = y - _scrollStartY;

            if (delta < -SCROLL_THRESHOLD)
            {
                _input.SetPressed(InputAction.ScrollUp,   true,  InputDevice.Touch);
                _input.SetPressed(InputAction.ScrollDown, false, InputDevice.Touch);
            }
            else if (delta > SCROLL_THRESHOLD)
            {
                _input.SetPressed(InputAction.ScrollDown, true,  InputDevice.Touch);
                _input.SetPressed(InputAction.ScrollUp,   false, InputDevice.Touch);
            }
            else
            {
                _input.SetPressed(InputAction.ScrollUp,   false, InputDevice.Touch);
                _input.SetPressed(InputAction.ScrollDown, false, InputDevice.Touch);
            }
        }

        public void OnScrollEnd()
        {
            _input.SetPressed(InputAction.ScrollUp,   false, InputDevice.Touch);
            _input.SetPressed(InputAction.ScrollDown, false, InputDevice.Touch);
        }

        // ------------------------------------------------------------------
        //  FLUSH
        // ------------------------------------------------------------------

        /// <summary>
        /// Azzeramento completo dello stato touch.
        /// Chiamare in OnAppSleep() e ad ogni cambio di schermata.
        /// </summary>
        public void Flush()
        {
            ReleaseAllDirections();
            _activeTouches.Clear();
            _input.SetPressed(InputAction.ActionA, false, InputDevice.Touch);
            _input.SetPressed(InputAction.ActionB, false, InputDevice.Touch);
            _input.SetPressed(InputAction.ActionC, false, InputDevice.Touch);
            _input.SetPressed(InputAction.ActionD, false, InputDevice.Touch);
            _input.SetPressed(InputAction.Cancel,  false, InputDevice.Touch);
            _input.SetPressed(InputAction.Confirm, false, InputDevice.Touch);
            _input.SetPressed(InputAction.Interact,false, InputDevice.Touch);
            _input.SetNavigationAxis(0f, 0f, InputDevice.Touch);
        }
    }
}
