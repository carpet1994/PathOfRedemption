// =============================================================================
//  La Via della Redenzione — Platforms/Android/TouchInputHandler.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Handler di input touch per Android. Intercetta i gesture
//                del VirtualDPad e dei VirtualActionButtons e li traduce
//                in InputAction tramite InputSystem.SetPressed().
//
//  Architettura:
//    TouchInputHandler non conosce la logica di gioco — si limita a
//    mappare coordinate touch → InputAction → InputSystem.
//    BattleSystem, SideScrollRenderer e WorldMapSystem leggono poi
//    InputSystem senza sapere che l'input viene dal touch.
//
//  Integrazione MAUI:
//    Viene istanziato da GameManager su Android e registra i propri
//    listener sui PanGestureRecognizer e TapGestureRecognizer
//    esposti da VirtualDPad e VirtualActionButtons.
//
//  Thread safety:
//    I callback MAUI arrivano sul MainThread.
//    InputSystem.SetPressed() è thread-safe per lettura concorrente
//    dal GameLoop thread.
// =============================================================================

using LaViaDellaRedenzione.Core;
using LaViaDellaRedenzione.Systems;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;

namespace LaViaDellaRedenzione.Platforms.Android
{
    /// <summary>
    /// Gestisce l'input touch su Android mappando i gesti del D-Pad virtuale
    /// e dei pulsanti azione all'InputSystem unificato.
    ///
    /// LIFECYCLE:
    ///   GameManager chiama Attach() quando la schermata di gioco è pronta.
    ///   GameManager chiama Detach() prima di distruggere la schermata.
    ///   In OnAppSleep() il GameLoop chiama Flush() per azzerare lo stato.
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

        /// <summary>
        /// Centro del D-Pad in coordinate assolute dello schermo.
        /// Aggiornato da VirtualDPad quando il layout cambia.
        /// </summary>
        private float _dpadCenterX;
        private float _dpadCenterY;

        /// <summary>Raggio del D-Pad in pixel — zona sensibile.</summary>
        private float _dpadRadius = 80f;

        /// <summary>
        /// Soglia minima di distanza dal centro per attivare una direzione.
        /// Evita input accidentali da tap precisi al centro.
        /// </summary>
        private const float DPAD_DEAD_ZONE = 12f;

        /// <summary>Angolo in gradi per la zona diagonale (±30° per asse).</summary>
        private const float DIAGONAL_ANGLE = 30f;

        // ------------------------------------------------------------------
        //  Stato touch pulsanti azione
        // ------------------------------------------------------------------

        /// <summary>
        /// Mappa ID touch point → InputAction premuta.
        /// Supporta multi-touch: più pulsanti contemporaneamente.
        /// </summary>
        private readonly Dictionary<long, InputAction> _activeTouches = new();

        // ------------------------------------------------------------------
        //  INIZIALIZZAZIONE
        // ------------------------------------------------------------------

        public TouchInputHandler() { }

        /// <summary>
        /// Aggiorna il centro e il raggio del D-Pad.
        /// Chiamato da VirtualDPad.OnSizeAllocated() quando il layout
        /// viene calcolato o ridimensionato.
        /// </summary>
        public void SetDPadBounds(float centerX, float centerY, float radius)
        {
            _dpadCenterX = centerX;
            _dpadCenterY = centerY;
            _dpadRadius  = radius;
        }

        // ------------------------------------------------------------------
        //  D-PAD INPUT
        // ------------------------------------------------------------------

        /// <summary>
        /// Chiamato da VirtualDPad quando riceve un PanGestureRecognizer
        /// o un TapGestureRecognizer. Traduce la posizione touch in
        /// direzioni logiche e le invia all'InputSystem.
        /// </summary>
        /// <param name="touchX">X del touch in coordinate schermo.</param>
        /// <param name="touchY">Y del touch in coordinate schermo.</param>
        /// <param name="isDown">True = dito appoggiato, False = dito sollevato.</param>
        public void OnDPadTouch(float touchX, float touchY, bool isDown)
        {
            if (!isDown)
            {
                // Rilascia tutte le direzioni
                ReleaseAllDirections();
                _input.SetNavigationAxis(0f, 0f, InputDevice.Touch);
                return;
            }

            float dx = touchX - _dpadCenterX;
            float dy = touchY - _dpadCenterY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // Dead zone: dentro la zona morta non registra nulla
            if (distance < DPAD_DEAD_ZONE)
            {
                ReleaseAllDirections();
                _input.SetNavigationAxis(0f, 0f, InputDevice.Touch);
                return;
            }

            // Normalizza per l'asse analogico
            float nx = dx / distance;
            float ny = dy / distance;
            _input.SetNavigationAxis(nx, ny, InputDevice.Touch);

            // Calcola angolo in gradi (0° = destra, 90° = giù)
            float angle = MathF.Atan2(dy, dx) * (180f / MathF.PI);
            if (angle < 0) angle += 360f;

            // Mappa angolo → direzioni cardinali e diagonali
            // Zona destra:  -30° ... +30°   (330°..30°)
            // Zona giù:      60° ... 120°
            // Zona sinistra: 150° ... 210°
            // Zona su:       240° ... 300°

            bool right = IsInArc(angle, 0f,   DIAGONAL_ANGLE);
            bool down  = IsInArc(angle, 90f,  DIAGONAL_ANGLE);
            bool left  = IsInArc(angle, 180f, DIAGONAL_ANGLE);
            bool up    = IsInArc(angle, 270f, DIAGONAL_ANGLE);

            // Diagonali: due direzioni attive contemporaneamente
            bool downRight = IsInArc(angle, 45f,  DIAGONAL_ANGLE);
            bool downLeft  = IsInArc(angle, 135f, DIAGONAL_ANGLE);
            bool upLeft    = IsInArc(angle, 225f, DIAGONAL_ANGLE);
            bool upRight   = IsInArc(angle, 315f, DIAGONAL_ANGLE);

            _input.SetPressed(InputAction.NavigateRight,
                right || downRight || upRight, InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateLeft,
                left  || downLeft  || upLeft,  InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateDown,
                down  || downRight || downLeft, InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateUp,
                up    || upRight   || upLeft,   InputDevice.Touch);
        }

        /// <summary>
        /// Rilascia tutte le direzioni di navigazione.
        /// </summary>
        private void ReleaseAllDirections()
        {
            _input.SetPressed(InputAction.NavigateRight, false, InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateLeft,  false, InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateDown,  false, InputDevice.Touch);
            _input.SetPressed(InputAction.NavigateUp,    false, InputDevice.Touch);
        }

        /// <summary>
        /// Ritorna true se l'angolo è entro ±range gradi dall'angolo target.
        /// Gestisce il wrap-around a 360°.
        /// </summary>
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
        /// Chiamato da VirtualActionButtons quando un pulsante viene premuto.
        /// Supporta multi-touch tramite touchId univoco per ogni dito.
        /// </summary>
        /// <param name="touchId">ID univoco del punto di contatto.</param>
        /// <param name="action">Azione logica del pulsante premuto.</param>
        /// <param name="isDown">True = premuto, False = rilasciato.</param>
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
                    // Rilascia solo se nessun altro touch sta tenendo la stessa azione
                    bool stillActive = false;
                    foreach (var a in _activeTouches.Values)
                        if (a == prevAction) { stillActive = true; break; }

                    if (!stillActive)
                        _input.SetPressed(prevAction, false, InputDevice.Touch);
                }
            }
        }

        /// <summary>
        /// Chiamato quando il pulsante Pausa/Cancel viene toccato.
        /// </summary>
        public void OnCancelButtonTouch(bool isDown)
            => _input.SetPressed(InputAction.Cancel, isDown, InputDevice.Touch);

        /// <summary>
        /// Chiamato quando il pulsante Interagisci viene toccato
        /// (hotspot nel side-scroll).
        /// </summary>
        public void OnInteractButtonTouch(bool isDown)
            => _input.SetPressed(InputAction.Interact, isDown, InputDevice.Touch);

        /// <summary>
        /// Chiamato da un tap su qualsiasi area dello schermo
        /// (per skippare cutscene e dialoghi).
        /// </summary>
        public void OnScreenTap()
            => _input.SetPressed(InputAction.Confirm, true, InputDevice.Touch);

        // ------------------------------------------------------------------
        //  SCROLL TOUCH (swipe verticale per liste)
        // ------------------------------------------------------------------

        private float _scrollStartY;
        private const float SCROLL_THRESHOLD = 30f;

        /// <summary>
        /// Inizio di uno swipe verticale (per scorrere galleria carte, bestiario).
        /// </summary>
        public void OnScrollStart(float y) => _scrollStartY = y;

        /// <summary>
        /// Aggiornamento posizione durante lo swipe.
        /// </summary>
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

        /// <summary>Fine dello swipe.</summary>
        public void OnScrollEnd()
        {
            _input.SetPressed(InputAction.ScrollUp,   false, InputDevice.Touch);
            _input.SetPressed(InputAction.ScrollDown, false, InputDevice.Touch);
        }

        // ------------------------------------------------------------------
        //  PULIZIA
        // ------------------------------------------------------------------

        /// <summary>
        /// Azzeramento completo dello stato touch.
        /// Chiamato in OnAppSleep() e al cambio di schermata.
        /// Evita direzioni "bloccate" quando l'app torna in foreground.
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
