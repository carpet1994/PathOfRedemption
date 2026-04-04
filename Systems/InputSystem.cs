// =============================================================================
//  La Via della Redenzione — Core/InputActions.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Definizione di tutte le azioni logiche di input del gioco.
//                Completamente agnostica alla piattaforma — nessun riferimento
//                a tasti fisici, touch o gamepad.
//
//  Mapping concreto:
//    Android  → TouchInputHandler    (/Platforms/Android/)
//    Windows  → KeyboardInputHandler + MouseInputHandler + GamepadInputHandler
//                                    (/Platforms/Windows/)
//
//  Contesti d'uso per schermata:
//    WorldMap (isometrico)  : Navigate, Confirm, Cancel, OpenMenu
//    SideScroll (micro area): Navigate, Confirm, Cancel, Interact, OpenMenu
//    Battle (side view FF)  : ActionA..D, Confirm, Cancel, OpenMenu,
//                             ScrollUp, ScrollDown
//    Dialogo / UI           : Confirm, Cancel, AnyInput, ScrollUp, ScrollDown
// =============================================================================

namespace LaViaDellaRedenzione.Core
{
    /// <summary>
    /// Azioni logiche di input riconosciute dal gioco.
    /// Ogni handler di piattaforma mappa i propri input fisici su questi valori.
    /// </summary>
    public enum InputAction
    {
        // ------------------------------------------------------------------
        //  NAVIGAZIONE
        // ------------------------------------------------------------------

        /// <summary>
        /// Movimento su/giù/sinistra/destra.
        /// WorldMap: sposta il gruppo tra i nodi.
        /// SideScroll: sposta il personaggio.
        /// Battle: naviga tra carte e bersagli.
        /// Menu: sposta il cursore tra le voci.
        /// </summary>
        NavigateUp    = 0,
        NavigateDown  = 1,
        NavigateLeft  = 2,
        NavigateRight = 3,

        // ------------------------------------------------------------------
        //  CONFERMA / ANNULLA
        // ------------------------------------------------------------------

        /// <summary>Conferma selezione corrente.</summary>
        Confirm = 10,

        /// <summary>Torna indietro / annulla / apre pausa se non c'è nulla da annullare.</summary>
        Cancel  = 11,

        // ------------------------------------------------------------------
        //  AZIONI DI BATTAGLIA (side view FF-style)
        //  Mappate sui 4 pulsanti virtuali Android (rombo) e sui tasti ZXAS Windows.
        //  In battaglia:
        //    ActionA = Usa Carta selezionata
        //    ActionB = Difendi (recupera 1 SP, raddoppia DEF per il turno)
        //    ActionC = Apri inventario Oggetti
        //    ActionD = Tenta la Fuga
        // ------------------------------------------------------------------

        /// <summary>Pulsante azione A — Usa Carta. Android: cerchio blu (alto rombo).</summary>
        ActionA = 20,

        /// <summary>Pulsante azione B — Difendi. Android: cerchio verde (destra rombo).</summary>
        ActionB = 21,

        /// <summary>Pulsante azione C — Oggetti. Android: cerchio giallo (basso rombo).</summary>
        ActionC = 22,

        /// <summary>Pulsante azione D — Fuggi. Android: cerchio rosso (sinistra rombo).</summary>
        ActionD = 23,

        // ------------------------------------------------------------------
        //  MENU E SCROLL
        // ------------------------------------------------------------------

        /// <summary>Apre il menu di pausa da qualsiasi schermata.</summary>
        OpenMenu   = 30,

        /// <summary>Scorre liste verso l'alto (galleria carte, bestiario, log dialogo).</summary>
        ScrollUp   = 31,

        /// <summary>Scorre liste verso il basso.</summary>
        ScrollDown = 32,

        /// <summary>
        /// Interagisce con un hotspot nel side-scroll (NPC, oggetto, porta).
        /// Distinto da Confirm per permettere binding separati su Windows.
        /// </summary>
        Interact   = 33,

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        /// <summary>
        /// Qualsiasi input — usato per skippare cutscene e splash screen.
        /// True se almeno un'altra azione è IsJustPressed in questo frame.
        /// </summary>
        AnyInput   = 99
    }
}


// =============================================================================
//  La Via della Redenzione — Systems/InputSystem.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Sistema di input unificato. Astrae touch, tastiera, mouse
//                e gamepad in un'unica interfaccia che tutti gli altri sistemi
//                (BattleSystem, WorldMapSystem, SideScrollSystem, ecc.) usano
//                senza sapere da dove arriva l'input.
//
//  Aggiornamento:
//    InputSystem.Update() deve essere chiamato UNA VOLTA per frame
//    all'inizio di GameLoop, prima di GameStateManager.Update().
//    Questo garantisce che IsJustPressed/IsJustReleased siano coerenti
//    per tutta la durata del frame.
//
//  Handler di piattaforma:
//    Gli handler concreti (TouchInputHandler, KeyboardInputHandler, ecc.)
//    chiamano InputSystem.SetPressed(action, true/false) quando rilevano
//    un input. InputSystem si occupa di calcolare JustPressed e JustReleased.
// =============================================================================

using System;
using System.Collections.Generic;
using LaViaDellaRedenzione.Core;

namespace LaViaDellaRedenzione.Systems
{
    /// <summary>
    /// Vettore 2D semplice per l'asse analogico di navigazione.
    /// X: -1.0 (sinistra) → +1.0 (destra)
    /// Y: -1.0 (su)        → +1.0 (giù)
    /// Usato per stick analogico gamepad e swipe touch.
    /// </summary>
    public readonly struct InputVector2
    {
        public float X { get; }
        public float Y { get; }

        public InputVector2(float x, float y) { X = x; Y = y; }

        public static readonly InputVector2 Zero = new(0f, 0f);

        public override string ToString() => $"({X:F2}, {Y:F2})";
    }

    // -------------------------------------------------------------------------
    //  INPUT SYSTEM
    // -------------------------------------------------------------------------

    /// <summary>
    /// Singleton. Punto unico di accesso allo stato di input per frame.
    ///
    /// UTILIZZO TIPICO negli IGameScreen:
    ///   if (InputSystem.Instance.IsJustPressed(InputAction.Confirm))
    ///       ConfirmSelection();
    ///
    ///   if (InputSystem.Instance.IsPressed(InputAction.NavigateLeft))
    ///       MoveLeft(deltaTime);
    ///
    ///   var axis = InputSystem.Instance.GetNavigationAxis();
    ///   player.Velocity = new Vector2(axis.X * speed, 0);
    /// </summary>
    public sealed class InputSystem
    {
        // ------------------------------------------------------------------
        //  Singleton
        // ------------------------------------------------------------------

        private static InputSystem? _instance;
        public static InputSystem Instance => _instance ??= new InputSystem();
        private InputSystem() { }

        // ------------------------------------------------------------------
        //  Stato corrente e precedente
        // ------------------------------------------------------------------

        /// <summary>Stato delle azioni nel frame corrente.</summary>
        private readonly HashSet<InputAction> _current  = new();

        /// <summary>Stato delle azioni nel frame precedente.</summary>
        private readonly HashSet<InputAction> _previous = new();

        /// <summary>
        /// Asse analogico di navigazione — aggiornato dagli handler.
        /// Per input digitali (tastiera/D-pad) viene impostato a ±1.0.
        /// Per stick analogico gamepad contiene il valore raw (-1.0..+1.0).
        /// </summary>
        private InputVector2 _navigationAxis = InputVector2.Zero;

        // ------------------------------------------------------------------
        //  Dispositivo corrente
        // ------------------------------------------------------------------

        /// <summary>
        /// Ultimo dispositivo che ha prodotto un input.
        /// Aggiornato automaticamente da SetPressed().
        /// Controlla quali hint (icone) vengono mostrati nella InputHintBar.
        /// </summary>
        public InputDevice CurrentDevice { get; private set; } = InputDevice.Touch;

        // ------------------------------------------------------------------
        //  EVENTI
        // ------------------------------------------------------------------

        /// <summary>
        /// Sparato quando CurrentDevice cambia.
        /// La InputHintBar si iscrive per aggiornare le icone.
        /// </summary>
        public event Action<InputDevice>? OnDeviceChanged;

        // ------------------------------------------------------------------
        //  API CHIAMATA DAGLI HANDLER DI PIATTAFORMA
        // ------------------------------------------------------------------

        /// <summary>
        /// Registra lo stato premuto/rilasciato di un'azione.
        /// Chiamato da TouchInputHandler, KeyboardInputHandler, ecc.
        /// </summary>
        /// <param name="action">Azione logica.</param>
        /// <param name="pressed">True = premuto, False = rilasciato.</param>
        /// <param name="device">Dispositivo che ha generato l'input.</param>
        public void SetPressed(InputAction action, bool pressed, InputDevice device)
        {
            // Aggiorna dispositivo corrente
            if (device != CurrentDevice)
            {
                CurrentDevice = device;
                OnDeviceChanged?.Invoke(device);
            }

            if (pressed)
                _current.Add(action);
            else
                _current.Remove(action);
        }

        /// <summary>
        /// Imposta l'asse analogico di navigazione.
        /// Chiamato da GamepadInputHandler (stick) e TouchInputHandler (swipe).
        /// </summary>
        public void SetNavigationAxis(float x, float y, InputDevice device)
        {
            _navigationAxis = new InputVector2(x, y);

            if (device != CurrentDevice)
            {
                CurrentDevice = device;
                OnDeviceChanged?.Invoke(device);
            }

            // Sincronizza le azioni digitali di navigazione con l'asse
            // per garantire che IsPressed(NavigateLeft) funzioni anche con lo stick
            float threshold = 0.5f;

            SetPressedInternal(InputAction.NavigateLeft,  x < -threshold);
            SetPressedInternal(InputAction.NavigateRight, x >  threshold);
            SetPressedInternal(InputAction.NavigateUp,    y < -threshold);
            SetPressedInternal(InputAction.NavigateDown,  y >  threshold);
        }

        // ------------------------------------------------------------------
        //  UPDATE — chiamato una volta per frame dal GameLoop
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiorna lo stato precedente copiando quello corrente.
        /// Deve essere chiamato ALL'INIZIO di ogni frame, prima di Update().
        /// </summary>
        public void Update()
        {
            _previous.Clear();
            foreach (var action in _current)
                _previous.Add(action);
        }

        // ------------------------------------------------------------------
        //  API PUBBLICA DI QUERY — usata dagli IGameScreen
        // ------------------------------------------------------------------

        /// <summary>
        /// True se l'azione è premuta in questo frame (anche se lo era già).
        /// Usare per azioni continue (movimento, hold).
        /// </summary>
        public bool IsPressed(InputAction action)
        {
            if (action == InputAction.AnyInput)
                return _current.Count > 0;

            return _current.Contains(action);
        }

        /// <summary>
        /// True SOLO nel primo frame in cui l'azione viene premuta.
        /// Usare per azioni one-shot (conferma, attacca, apri menu).
        /// </summary>
        public bool IsJustPressed(InputAction action)
        {
            if (action == InputAction.AnyInput)
                return HasAnyJustPressed();

            return _current.Contains(action) && !_previous.Contains(action);
        }

        /// <summary>
        /// True SOLO nel primo frame in cui l'azione viene rilasciata.
        /// Usare per feedback al rilascio (es. animazione pulsante).
        /// </summary>
        public bool IsJustReleased(InputAction action)
        {
            if (action == InputAction.AnyInput)
                return HasAnyJustReleased();

            return !_current.Contains(action) && _previous.Contains(action);
        }

        /// <summary>
        /// Asse analogico di navigazione normalizzato (-1.0..+1.0).
        /// Per input digitali vale ±1.0 o 0.0.
        /// Usare per movimento fluido nel side-scroll e nella world map.
        /// </summary>
        public InputVector2 GetNavigationAxis() => _navigationAxis;

        /// <summary>
        /// Restituisce true se almeno una direzione di navigazione è premuta.
        /// Utile per animare il personaggio in walk vs idle nel side-scroll.
        /// </summary>
        public bool IsNavigating()
            => IsPressed(InputAction.NavigateLeft)
            || IsPressed(InputAction.NavigateRight)
            || IsPressed(InputAction.NavigateUp)
            || IsPressed(InputAction.NavigateDown);

        // ------------------------------------------------------------------
        //  UTILITY INTERNE
        // ------------------------------------------------------------------

        private void SetPressedInternal(InputAction action, bool pressed)
        {
            if (pressed) _current.Add(action);
            else         _current.Remove(action);
        }

        private bool HasAnyJustPressed()
        {
            foreach (var action in _current)
                if (!_previous.Contains(action)) return true;
            return false;
        }

        private bool HasAnyJustReleased()
        {
            foreach (var action in _previous)
                if (!_current.Contains(action)) return true;
            return false;
        }

        // ------------------------------------------------------------------
        //  RESET — chiamato al cambio di scena per evitare input "fantasma"
        // ------------------------------------------------------------------

        /// <summary>
        /// Svuota lo stato corrente e precedente.
        /// Da chiamare in GameStateManager quando avviene una transizione,
        /// per evitare che un Confirm tenuto premuto triggeri la scena successiva.
        /// </summary>
        public void Flush()
        {
            _current.Clear();
            _previous.Clear();
            _navigationAxis = InputVector2.Zero;
        }
    }
}
