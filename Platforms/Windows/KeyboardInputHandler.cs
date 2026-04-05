// =============================================================================
//  La Via della Redenzione — Platforms/Windows/KeyboardInputHandler.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Handler di input tastiera per Windows. Intercetta i tasti
//                fisici e li traduce in InputAction tramite InputSystem.
//
//  Mappatura:
//    WASD / Frecce    → Navigate
//    Invio / Spazio   → Confirm
//    Esc / Backspace  → Cancel
//    Z                → ActionA (Usa Carta)
//    X                → ActionB (Difendi)
//    A (tasto)        → ActionC (Oggetti)    ← non confondere con WASD
//    S (tasto)        → ActionD (Fuggi)      ← non confondere con WASD
//    Tab              → OpenMenu
//    PgSu / PgGiù     → ScrollUp / ScrollDown
//    F11              → Fullscreen toggle (gestito da WindowsIntegration)
//    Ctrl+S           → Salvataggio rapido (gestito da GameManager)
//
//  Nota su A/S vs WASD:
//    A e S sono sia tasti di movimento (WASD) che tasti azione (ActionC/D).
//    La priorità è: se il contesto è Battle → A=ActionC, S=ActionD.
//                   se il contesto è SideScroll/WorldMap → A=Left, S=Down.
//    Il contesto viene impostato da GameManager tramite SetContext().
//
//  Integrazione MAUI Windows:
//    Usa Window.HandlerChanged e Page.KeyDown/KeyUp per intercettare
//    i tasti quando la finestra ha il focus.
// =============================================================================

using LaViaDellaRedenzione.Core;
using LaViaDellaRedenzione.Systems;
using Microsoft.Maui.Controls;
using System.Collections.Generic;

namespace LaViaDellaRedenzione.Platforms.Windows
{
    /// <summary>
    /// Contesto di input — determina la mappatura di tasti ambigui (A/S).
    /// </summary>
    public enum InputContext
    {
        /// <summary>World map e side-scroll: A=sinistra, S=giù.</summary>
        Navigation,

        /// <summary>Battle screen: A=ActionC (Oggetti), S=ActionD (Fuggi).</summary>
        Battle,

        /// <summary>Menu e dialoghi: solo Confirm/Cancel/Navigate/Scroll.</summary>
        Menu
    }

    // =========================================================================
    //  KEYBOARD INPUT HANDLER
    // =========================================================================

    /// <summary>
    /// Gestisce l'input tastiera su Windows mappando i tasti fisici
    /// all'InputSystem unificato.
    /// </summary>
    public sealed class KeyboardInputHandler
    {
        // ------------------------------------------------------------------
        //  Dipendenze
        // ------------------------------------------------------------------

        private readonly InputSystem _input = InputSystem.Instance;

        // ------------------------------------------------------------------
        //  Contesto corrente
        // ------------------------------------------------------------------

        private InputContext _context = InputContext.Navigation;

        /// <summary>
        /// Imposta il contesto di input.
        /// Chiamato da GameManager ad ogni transizione di schermata.
        /// </summary>
        public void SetContext(InputContext context) => _context = context;

        // ------------------------------------------------------------------
        //  Tasti attualmente premuti (per evitare key repeat WASD)
        // ------------------------------------------------------------------

        private readonly HashSet<string> _heldKeys = new();

        // ------------------------------------------------------------------
        //  MAPPATURA TASTI → INPUT ACTION
        // ------------------------------------------------------------------

        /// <summary>
        /// Chiamato da Page.KeyDown (o Window key hook).
        /// </summary>
        public void OnKeyDown(string key)
        {
            if (_heldKeys.Contains(key)) return; // ignora key repeat OS
            _heldKeys.Add(key);
            ApplyKey(key, true);
        }

        /// <summary>
        /// Chiamato da Page.KeyUp.
        /// </summary>
        public void OnKeyUp(string key)
        {
            _heldKeys.Remove(key);
            ApplyKey(key, false);
        }

        private void ApplyKey(string key, bool pressed)
        {
            switch (key.ToUpperInvariant())
            {
                // ── Navigazione ───────────────────────────────────────────────
                case "W":
                case "ARROWUP":
                    _input.SetPressed(InputAction.NavigateUp, pressed, InputDevice.MouseKeyboard);
                    if (pressed) _input.SetNavigationAxis(
                        GetAxisX(), -1f, InputDevice.MouseKeyboard);
                    else UpdateAxisOnRelease();
                    break;

                case "ARROWDOWN":
                    _input.SetPressed(InputAction.NavigateDown, pressed, InputDevice.MouseKeyboard);
                    if (pressed) _input.SetNavigationAxis(
                        GetAxisX(), 1f, InputDevice.MouseKeyboard);
                    else UpdateAxisOnRelease();
                    break;

                case "ARROWLEFT":
                    _input.SetPressed(InputAction.NavigateLeft, pressed, InputDevice.MouseKeyboard);
                    if (pressed) _input.SetNavigationAxis(
                        -1f, GetAxisY(), InputDevice.MouseKeyboard);
                    else UpdateAxisOnRelease();
                    break;

                case "ARROWRIGHT":
                    _input.SetPressed(InputAction.NavigateRight, pressed, InputDevice.MouseKeyboard);
                    if (pressed) _input.SetNavigationAxis(
                        1f, GetAxisY(), InputDevice.MouseKeyboard);
                    else UpdateAxisOnRelease();
                    break;

                // ── WASD (solo in contesto Navigation) ──────────────────────
                case "D":
                    if (_context != InputContext.Battle)
                    {
                        _input.SetPressed(InputAction.NavigateRight, pressed, InputDevice.MouseKeyboard);
                        if (pressed) _input.SetNavigationAxis(1f, GetAxisY(), InputDevice.MouseKeyboard);
                        else UpdateAxisOnRelease();
                    }
                    break;

                case "S":
                    if (_context == InputContext.Battle)
                        _input.SetPressed(InputAction.ActionD, pressed, InputDevice.MouseKeyboard);
                    else
                    {
                        _input.SetPressed(InputAction.NavigateDown, pressed, InputDevice.MouseKeyboard);
                        if (pressed) _input.SetNavigationAxis(GetAxisX(), 1f, InputDevice.MouseKeyboard);
                        else UpdateAxisOnRelease();
                    }
                    break;

                // ── Conferma / Annulla ────────────────────────────────────────
                case "ENTER":
                case " ":
                    _input.SetPressed(InputAction.Confirm, pressed, InputDevice.MouseKeyboard);
                    break;

                case "ESCAPE":
                case "BACK":
                    _input.SetPressed(InputAction.Cancel, pressed, InputDevice.MouseKeyboard);
                    break;

                // ── Interagisci (side-scroll hotspot) ────────────────────────
                case "E":
                    _input.SetPressed(InputAction.Interact, pressed, InputDevice.MouseKeyboard);
                    break;

                // ── Azioni battaglia ──────────────────────────────────────────
                case "Z":
                    _input.SetPressed(InputAction.ActionA, pressed, InputDevice.MouseKeyboard);
                    break;

                case "X":
                    _input.SetPressed(InputAction.ActionB, pressed, InputDevice.MouseKeyboard);
                    break;

                case "A":
                    if (_context == InputContext.Battle)
                        _input.SetPressed(InputAction.ActionC, pressed, InputDevice.MouseKeyboard);
                    else
                    {
                        // WASD sinistra
                        _input.SetPressed(InputAction.NavigateLeft, pressed, InputDevice.MouseKeyboard);
                        if (pressed) _input.SetNavigationAxis(-1f, GetAxisY(), InputDevice.MouseKeyboard);
                        else UpdateAxisOnRelease();
                    }
                    break;

                // ── Menu / Scroll ─────────────────────────────────────────────
                case "TAB":
                    _input.SetPressed(InputAction.OpenMenu, pressed, InputDevice.MouseKeyboard);
                    break;

                case "PAGEUP":
                    _input.SetPressed(InputAction.ScrollUp, pressed, InputDevice.MouseKeyboard);
                    break;

                case "PAGEDOWN":
                    _input.SetPressed(InputAction.ScrollDown, pressed, InputDevice.MouseKeyboard);
                    break;
            }
        }

        // ------------------------------------------------------------------
        //  ASSE ANALOGICO — aggiornato dai tasti WASD/Frecce
        // ------------------------------------------------------------------

        private float GetAxisX()
        {
            bool left  = _heldKeys.Contains("ARROWLEFT")  || _heldKeys.Contains("A");
            bool right = _heldKeys.Contains("ARROWRIGHT") || _heldKeys.Contains("D");
            return right ? 1f : left ? -1f : 0f;
        }

        private float GetAxisY()
        {
            bool up   = _heldKeys.Contains("ARROWUP")   || _heldKeys.Contains("W");
            bool down = _heldKeys.Contains("ARROWDOWN")  || _heldKeys.Contains("S");
            return down ? 1f : up ? -1f : 0f;
        }

        private void UpdateAxisOnRelease()
            => _input.SetNavigationAxis(GetAxisX(), GetAxisY(), InputDevice.MouseKeyboard);

        // ------------------------------------------------------------------
        //  SCROLL ROTELLA MOUSE (da MouseInputHandler, proxy qui)
        // ------------------------------------------------------------------

        public void OnMouseWheelScrolled(float delta)
        {
            if (delta > 0)
            {
                _input.SetPressed(InputAction.ScrollUp,   true,  InputDevice.MouseKeyboard);
                _input.SetPressed(InputAction.ScrollDown, false, InputDevice.MouseKeyboard);
            }
            else if (delta < 0)
            {
                _input.SetPressed(InputAction.ScrollDown, true,  InputDevice.MouseKeyboard);
                _input.SetPressed(InputAction.ScrollUp,   false, InputDevice.MouseKeyboard);
            }
        }

        // ------------------------------------------------------------------
        //  FLUSH
        // ------------------------------------------------------------------

        public void Flush()
        {
            _heldKeys.Clear();
            UpdateAxisOnRelease();
        }
    }

    // =========================================================================
    //  MOUSE INPUT HANDLER
    // =========================================================================

    /// <summary>
    /// Gestisce l'input mouse su Windows.
    /// Click sinistro → Confirm sull'elemento puntato.
    /// Click destro   → Cancel.
    /// Rotella        → ScrollUp / ScrollDown (delegato a KeyboardInputHandler).
    /// Hover          → highlight visivo (gestito dalla UI MAUI, non qui).
    ///
    /// Su Windows i VirtualDPad e VirtualActionButtons NON vengono mostrati:
    /// tutta la UI è navigabile con mouse (punta e clicca) o tastiera.
    /// </summary>
    public sealed class MouseInputHandler
    {
        private readonly InputSystem         _input;
        private readonly KeyboardInputHandler _keyboard;

        public MouseInputHandler(KeyboardInputHandler keyboard)
        {
            _input   = InputSystem.Instance;
            _keyboard = keyboard;
        }

        // ------------------------------------------------------------------
        //  CLICK
        // ------------------------------------------------------------------

        /// <summary>
        /// Click sinistro premuto su un elemento interattivo.
        /// La UI MAUI chiama questo metodo quando un Button/Label
        /// con gesture recognizer viene cliccato.
        /// </summary>
        public void OnLeftClick()
            => _input.SetPressed(InputAction.Confirm, true, InputDevice.MouseKeyboard);

        /// <summary>
        /// Click sinistro rilasciato.
        /// </summary>
        public void OnLeftClickRelease()
            => _input.SetPressed(InputAction.Confirm, false, InputDevice.MouseKeyboard);

        /// <summary>
        /// Click destro — equivale a Cancel.
        /// </summary>
        public void OnRightClick()
        {
            _input.SetPressed(InputAction.Cancel, true, InputDevice.MouseKeyboard);

            // Auto-release dopo 1 frame (il click destro è sempre un impulso)
            Microsoft.Maui.Controls.Application.Current?.Dispatcher.DispatchDelayed(
                System.TimeSpan.FromMilliseconds(32),
                () => _input.SetPressed(InputAction.Cancel, false, InputDevice.MouseKeyboard));
        }

        /// <summary>
        /// Rotella mouse — delega a KeyboardInputHandler per uniformità.
        /// </summary>
        public void OnMouseWheel(float delta)
        {
            _keyboard.OnMouseWheelScrolled(delta);

            // Auto-release scroll dopo 1 frame
            Microsoft.Maui.Controls.Application.Current?.Dispatcher.DispatchDelayed(
                System.TimeSpan.FromMilliseconds(32),
                () =>
                {
                    _input.SetPressed(InputAction.ScrollUp,   false, InputDevice.MouseKeyboard);
                    _input.SetPressed(InputAction.ScrollDown, false, InputDevice.MouseKeyboard);
                });
        }

        // ------------------------------------------------------------------
        //  HOVER — hint cursore custom
        // ------------------------------------------------------------------

        /// <summary>
        /// True se il cursore è sopra un elemento interattivo.
        /// Usato da WindowsIntegration per cambiare il cursore.
        /// </summary>
        public bool IsHoveringInteractive { get; private set; }

        public void OnHoverEnter() => IsHoveringInteractive = true;
        public void OnHoverExit()  => IsHoveringInteractive = false;
    }
}
