// =============================================================================
//  La Via della Redenzione — Platforms/Windows/GamepadInputHandler.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Handler di input gamepad per Windows usando
//                Windows.Gaming.Input (disponibile su WinUI/MAUI Windows).
//                Supporta controller Xbox e DirectInput compatibili.
//
//  Mappatura Xbox → InputAction:
//    Stick sinistro / D-Pad → Navigate + asse analogico
//    A                      → Confirm
//    B                      → Cancel
//    X                      → ActionA (Usa Carta)
//    Y                      → ActionB (Difendi)
//    LB (Left Bumper)       → ActionC (Oggetti)
//    RB (Right Bumper)      → ActionD (Fuggi)
//    Start                  → OpenMenu
//    LT (Left Trigger)      → ScrollUp   (valore analogico > soglia)
//    RT (Right Trigger)     → ScrollDown (valore analogico > soglia)
//
//  Hot-plug:
//    Rileva connessione/disconnessione durante il gioco.
//    Al collegamento: CurrentDevice → Gamepad (mostra icone controller).
//    Alla disconnessione: CurrentDevice → MouseKeyboard, toast in-app.
//
//  Polling:
//    GameLoop chiama Update() ogni frame — non usa eventi asincroni
//    per evitare race condition con il thread di gioco.
//
//  Thread safety:
//    Gamepad.Gamepads è thread-safe per lettura.
//    _currentGamepad viene aggiornato solo su MainThread tramite
//    Gamepad.GamepadAdded/Removed events.
// =============================================================================

using LaViaDellaRedenzione.Core;
using LaViaDellaRedenzione.Systems;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;

#if WINDOWS
using Windows.Gaming.Input;
#endif

namespace LaViaDellaRedenzione.Platforms.Windows
{
    /// <summary>
    /// Gestisce l'input gamepad su Windows con supporto hot-plug e
    /// mappatura Xbox standard.
    /// </summary>
    public sealed class GamepadInputHandler
    {
        // ------------------------------------------------------------------
        //  Dipendenze
        // ------------------------------------------------------------------

        private readonly InputSystem _input = InputSystem.Instance;

        // ------------------------------------------------------------------
        //  Stato gamepad
        // ------------------------------------------------------------------

#if WINDOWS
        private Gamepad? _currentGamepad;
        private GamepadReading _previousReading;
#endif

        /// <summary>True se un gamepad è attualmente connesso e attivo.</summary>
        public bool IsConnected { get; private set; } = false;

        // ------------------------------------------------------------------
        //  Dead zone stick analogico
        // ------------------------------------------------------------------

        /// <summary>
        /// Dead zone per lo stick sinistro (0.0..1.0).
        /// Valori sotto questa soglia vengono ignorati per evitare drift.
        /// 0.15f è il valore standard raccomandato per controller Xbox.
        /// </summary>
        public float StickDeadZone { get; set; } = 0.15f;

        /// <summary>Soglia trigger per attivare ScrollUp/ScrollDown.</summary>
        private const float TRIGGER_THRESHOLD = 0.3f;

        // ------------------------------------------------------------------
        //  EVENTI CONNESSIONE / DISCONNESSIONE
        // ------------------------------------------------------------------

        /// <summary>
        /// Sparato quando un gamepad viene connesso.
        /// GameManager mostra il toast "Controller connesso".
        /// </summary>
        public event Action<string>? OnGamepadConnected;

        /// <summary>
        /// Sparato quando il gamepad attivo viene disconnesso.
        /// GameManager mostra il toast "Controller disconnesso".
        /// </summary>
        public event Action? OnGamepadDisconnected;

        // ------------------------------------------------------------------
        //  INIZIALIZZAZIONE
        // ------------------------------------------------------------------

        public GamepadInputHandler()
        {
            // Inietta il resolver degli hint gamepad nella InputHintBar
            // cross-platform. Viene eseguito solo su Windows perché
            // GamepadInputHandler non viene mai istanziato su Android.
            LaViaDellaRedenzione.UI.InputHintBar.GamepadHintResolver = GetHint;

#if WINDOWS
            // Registra listener per hot-plug
            Gamepad.GamepadAdded   += HandleGamepadAdded;
            Gamepad.GamepadRemoved += HandleGamepadRemoved;

            // Se c'è già un gamepad collegato all'avvio
            if (Gamepad.Gamepads.Count > 0)
            {
                _currentGamepad = Gamepad.Gamepads[0];
                IsConnected     = true;
                _previousReading = _currentGamepad.GetCurrentReading();
            }
#endif
        }

        // ------------------------------------------------------------------
        //  HOT-PLUG
        // ------------------------------------------------------------------

#if WINDOWS
        private void HandleGamepadAdded(object? sender, Gamepad gamepad)
        {
            // Usa sempre il primo gamepad disponibile
            if (_currentGamepad != null) return;

            _currentGamepad  = gamepad;
            IsConnected      = true;
            _previousReading = gamepad.GetCurrentReading();

            // Notifica sul MainThread per aggiornare la UI
            Microsoft.Maui.Controls.Application.Current?.Dispatcher.Dispatch(() =>
            {
                _input.SetPressed(InputAction.AnyInput, false, InputDevice.Gamepad);
                OnGamepadConnected?.Invoke(gamepad.ToString() ?? "Controller");
            });
        }

        private void HandleGamepadRemoved(object? sender, Gamepad gamepad)
        {
            if (_currentGamepad != gamepad) return;

            _currentGamepad = null;
            IsConnected     = false;

            // Rilascia tutti gli input del gamepad
            Flush();

            // Torna a MouseKeyboard
            _input.SetPressed(InputAction.AnyInput, false, InputDevice.MouseKeyboard);

            Microsoft.Maui.Controls.Application.Current?.Dispatcher.Dispatch(() =>
            {
                OnGamepadDisconnected?.Invoke();

                // Tenta di switchare al prossimo gamepad disponibile
                if (Gamepad.Gamepads.Count > 0)
                {
                    _currentGamepad  = Gamepad.Gamepads[0];
                    IsConnected      = true;
                    _previousReading = _currentGamepad.GetCurrentReading();
                    OnGamepadConnected?.Invoke("Controller");
                }
            });
        }
#endif

        // ------------------------------------------------------------------
        //  UPDATE — polling ogni frame dal GameLoop
        // ------------------------------------------------------------------

        /// <summary>
        /// Legge lo stato corrente del gamepad e aggiorna InputSystem.
        /// Chiamato all'inizio di ogni frame dal GameLoop, prima di
        /// GameStateManager.Update().
        /// </summary>
        public void Update()
        {
#if WINDOWS
            if (_currentGamepad == null || !IsConnected) return;

            GamepadReading reading;
            try
            {
                reading = _currentGamepad.GetCurrentReading();
            }
            catch
            {
                // Il gamepad è stato disconnesso tra un frame e l'altro
                IsConnected     = false;
                _currentGamepad = null;
                Flush();
                OnGamepadDisconnected?.Invoke();
                return;
            }

            // ── Stick sinistro (asse analogico) ───────────────────────────
            float lx = ApplyDeadZone((float)reading.LeftThumbstickX);
            float ly = ApplyDeadZone((float)reading.LeftThumbstickY);

            // Windows.Gaming.Input: Y positivo = su (invertito rispetto al gioco)
            _input.SetNavigationAxis(lx, -ly, InputDevice.Gamepad);

            // ── D-Pad ─────────────────────────────────────────────────────
            _input.SetPressed(InputAction.NavigateUp,
                IsButtonPressed(reading, GamepadButtons.DPadUp),
                InputDevice.Gamepad);
            _input.SetPressed(InputAction.NavigateDown,
                IsButtonPressed(reading, GamepadButtons.DPadDown),
                InputDevice.Gamepad);
            _input.SetPressed(InputAction.NavigateLeft,
                IsButtonPressed(reading, GamepadButtons.DPadLeft),
                InputDevice.Gamepad);
            _input.SetPressed(InputAction.NavigateRight,
                IsButtonPressed(reading, GamepadButtons.DPadRight),
                InputDevice.Gamepad);

            // ── Pulsanti face ─────────────────────────────────────────────
            _input.SetPressed(InputAction.Confirm,
                IsButtonPressed(reading, GamepadButtons.A),
                InputDevice.Gamepad);
            _input.SetPressed(InputAction.Cancel,
                IsButtonPressed(reading, GamepadButtons.B),
                InputDevice.Gamepad);
            _input.SetPressed(InputAction.ActionA,
                IsButtonPressed(reading, GamepadButtons.X),
                InputDevice.Gamepad);
            _input.SetPressed(InputAction.ActionB,
                IsButtonPressed(reading, GamepadButtons.Y),
                InputDevice.Gamepad);

            // ── Bumper ────────────────────────────────────────────────────
            _input.SetPressed(InputAction.ActionC,
                IsButtonPressed(reading, GamepadButtons.LeftShoulder),
                InputDevice.Gamepad);
            _input.SetPressed(InputAction.ActionD,
                IsButtonPressed(reading, GamepadButtons.RightShoulder),
                InputDevice.Gamepad);

            // ── Start ─────────────────────────────────────────────────────
            _input.SetPressed(InputAction.OpenMenu,
                IsButtonPressed(reading, GamepadButtons.Menu),
                InputDevice.Gamepad);

            // ── Trigger (scroll) ──────────────────────────────────────────
            _input.SetPressed(InputAction.ScrollUp,
                reading.LeftTrigger  > TRIGGER_THRESHOLD,
                InputDevice.Gamepad);
            _input.SetPressed(InputAction.ScrollDown,
                reading.RightTrigger > TRIGGER_THRESHOLD,
                InputDevice.Gamepad);

            _previousReading = reading;
#endif
        }

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

#if WINDOWS
        private static bool IsButtonPressed(GamepadReading reading, GamepadButtons button)
            => (reading.Buttons & button) == button;
#endif

        /// <summary>
        /// Applica dead zone circolare allo stick analogico.
        /// Valori sotto la soglia vengono azzerati per eliminare il drift.
        /// Sopra la soglia, il valore viene riscalato a 0..1 per linearità.
        /// </summary>
        private float ApplyDeadZone(float value)
        {
            float abs = MathF.Abs(value);
            if (abs < StickDeadZone) return 0f;

            // Riscala da [deadZone..1] a [0..1]
            float scaled = (abs - StickDeadZone) / (1f - StickDeadZone);
            return MathF.Sign(value) * Math.Clamp(scaled, 0f, 1f);
        }

        // ------------------------------------------------------------------
        //  HINT UI — icone pulsante per InputHintBar
        // ------------------------------------------------------------------

        /// <summary>
        /// Restituisce il testo dell'hint per un'azione con icone controller.
        /// Usato da InputHintBar quando CurrentDevice == Gamepad.
        /// </summary>
        public static string GetHint(InputAction action) => action switch
        {
            InputAction.Confirm       => "[A]",
            InputAction.Cancel        => "[B]",
            InputAction.ActionA       => "[X]",
            InputAction.ActionB       => "[Y]",
            InputAction.ActionC       => "[LB]",
            InputAction.ActionD       => "[RB]",
            InputAction.OpenMenu      => "[Start]",
            InputAction.ScrollUp      => "[LT]",
            InputAction.ScrollDown    => "[RT]",
            InputAction.NavigateUp    => "[↑]",
            InputAction.NavigateDown  => "[↓]",
            InputAction.NavigateLeft  => "[←]",
            InputAction.NavigateRight => "[→]",
            _                         => string.Empty
        };

        // ------------------------------------------------------------------
        //  FLUSH
        // ------------------------------------------------------------------

        /// <summary>
        /// Rilascia tutti gli input del gamepad.
        /// Chiamato alla disconnessione e al cambio di schermata.
        /// </summary>
        public void Flush()
        {
            _input.SetPressed(InputAction.NavigateUp,    false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.NavigateDown,  false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.NavigateLeft,  false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.NavigateRight, false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.Confirm,       false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.Cancel,        false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.ActionA,       false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.ActionB,       false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.ActionC,       false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.ActionD,       false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.OpenMenu,      false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.ScrollUp,      false, InputDevice.Gamepad);
            _input.SetPressed(InputAction.ScrollDown,    false, InputDevice.Gamepad);
            _input.SetNavigationAxis(0f, 0f, InputDevice.Gamepad);
        }

        // ------------------------------------------------------------------
        //  CLEANUP
        // ------------------------------------------------------------------

        /// <summary>
        /// Deregistra i listener hot-plug.
        /// Chiamato da GameManager alla chiusura dell'app.
        /// </summary>
        public void Dispose()
        {
#if WINDOWS
            Gamepad.GamepadAdded   -= HandleGamepadAdded;
            Gamepad.GamepadRemoved -= HandleGamepadRemoved;
            Flush();
#endif
        }
    }

}
