// =============================================================================
//  La Via della Redenzione — Core/GameStateManager.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Macchina a stati globale del gioco con stack push/pop
//                per overlay (es. Pausa sopra Battaglia, Dialogo sopra
//                WorldMap, ecc.).
//
//  Architettura renderer supportata:
//    - GameState.WorldMap  → WorldMapRenderer  (isometrico fisso, FF8-style)
//    - GameState.Game      → SideScrollRenderer (2D laterale, parallax)
//    - GameState.Battle    → BattleScreen       (frontale, turni)
//
//  Thread-safety: tutti i metodi devono essere chiamati dal thread principale.
//  Su MAUI usare MainThread.BeginInvokeOnMainThread() se chiamati da thread
//  secondari (es. caricamento asincrono asset).
// =============================================================================

using System;
using System.Collections.Generic;

namespace LaViaDellaRedenzione.Core
{
    // -------------------------------------------------------------------------
    //  ARGOMENTI CAMBIO STATO
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dati opzionali passati durante una transizione di stato.
    /// Permette di comunicare contesto senza accoppiamento diretto tra sistemi.
    /// </summary>
    public sealed class StateTransitionArgs : EventArgs
    {
        /// <summary>Stato di destinazione.</summary>
        public GameState NewState { get; }

        /// <summary>Stato da cui si proviene (None se primo avvio).</summary>
        public GameState PreviousState { get; }

        /// <summary>
        /// Payload opzionale: ID locazione per WorldMap/Game,
        /// ID battaglia per Battle, ecc.
        /// </summary>
        public string? ContextId { get; }

        /// <summary>
        /// Se true, la transizione è un push di overlay (la scena sottostante
        /// rimane in memoria e non viene distrutta).
        /// </summary>
        public bool IsOverlay { get; }

        public StateTransitionArgs(
            GameState newState,
            GameState previousState,
            string?   contextId = null,
            bool      isOverlay = false)
        {
            NewState      = newState;
            PreviousState = previousState;
            ContextId     = contextId;
            IsOverlay     = isOverlay;
        }
    }

    // -------------------------------------------------------------------------
    //  INTERFACCIA STATO
    // -------------------------------------------------------------------------

    /// <summary>
    /// Contratto che ogni schermata/sistema deve implementare per integrarsi
    /// con il GameStateManager.
    /// </summary>
    public interface IGameScreen
    {
        /// <summary>Chiamato una volta sola quando lo stato viene creato.</summary>
        void OnEnter(StateTransitionArgs args);

        /// <summary>
        /// Chiamato ogni frame dal GameLoop.
        /// deltaTime in secondi (tipicamente ~0.0167 a 60 FPS).
        /// </summary>
        void OnUpdate(float deltaTime);

        /// <summary>
        /// Chiamato ogni frame per il rendering dopo OnUpdate.
        /// </summary>
        void OnRender(float deltaTime);

        /// <summary>
        /// Chiamato quando un overlay viene pushato sopra questo stato.
        /// Lo stato sottostante deve sospendere input e animazioni.
        /// </summary>
        void OnPause();

        /// <summary>
        /// Chiamato quando l'overlay sopra viene poppato e questo stato
        /// torna in primo piano.
        /// </summary>
        void OnResume();

        /// <summary>
        /// Chiamato quando lo stato viene distrutto (pop definitivo dallo stack).
        /// Liberare risorse, annullare subscription a EventBus.
        /// </summary>
        void OnExit();
    }

    // -------------------------------------------------------------------------
    //  GAME STATE MANAGER
    // -------------------------------------------------------------------------

    /// <summary>
    /// Singleton che gestisce lo stack degli stati di gioco.
    ///
    /// PUSH / POP OVERLAY:
    ///   Push → aggiunge uno stato sopra lo stack; lo stato sottostante
    ///          riceve OnPause() ma NON OnExit().
    ///   Pop  → rimuove lo stato in cima; lo stato sottostante riceve OnResume().
    ///
    /// REPLACE (transizione normale):
    ///   Lo stato corrente riceve OnExit(), il nuovo riceve OnEnter().
    ///   Lo stack viene svuotato fino al punto di replace.
    ///
    /// RENDERER ROUTING:
    ///   Gli ascoltatori di OnStateChanged devono ispezionare
    ///   args.NewState per attivare il renderer corretto:
    ///     WorldMap → WorldMapRenderer (isometrico)
    ///     Game     → SideScrollRenderer
    ///     Battle   → BattleScreen
    /// </summary>
    public sealed class GameStateManager
    {
        // ------------------------------------------------------------------
        //  Singleton
        // ------------------------------------------------------------------

        private static GameStateManager? _instance;

        public static GameStateManager Instance
            => _instance ??= new GameStateManager();

        private GameStateManager() { }

        // ------------------------------------------------------------------
        //  Stack stati
        // ------------------------------------------------------------------

        /// <summary>
        /// Stack degli stati attivi. Il top è lo stato corrente.
        /// Ogni entry è una coppia (GameState enum, IGameScreen implementazione).
        /// </summary>
        private readonly Stack<(GameState State, IGameScreen Screen)> _stack = new();

        /// <summary>Factory opzionale: iniettata dall'esterno per costruire gli IGameScreen.</summary>
        private Func<GameState, string?, IGameScreen>? _screenFactory;

        // ------------------------------------------------------------------
        //  Stato corrente (lettura pubblica)
        // ------------------------------------------------------------------

        /// <summary>
        /// Stato in cima allo stack (quello attivo).
        /// Ritorna GameState.Splash se lo stack è vuoto (primo avvio).
        /// </summary>
        public GameState CurrentState
            => _stack.Count > 0 ? _stack.Peek().State : GameState.Splash;

        /// <summary>
        /// Profondità corrente dello stack.
        /// 1 = nessun overlay, 2+ = almeno un overlay attivo.
        /// </summary>
        public int StackDepth => _stack.Count;

        /// <summary>True se almeno un overlay è attivo sopra la scena base.</summary>
        public bool HasOverlay => _stack.Count > 1;

        // ------------------------------------------------------------------
        //  Renderer routing helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// True se il renderer isometrico della world map deve essere attivo.
        /// </summary>
        public bool IsWorldMapActive => CurrentState == GameState.WorldMap;

        /// <summary>
        /// True se il renderer side-scroll delle micro aree deve essere attivo.
        /// </summary>
        public bool IsSideScrollActive => CurrentState == GameState.Game;

        /// <summary>
        /// True se la BattleScreen deve essere attiva.
        /// </summary>
        public bool IsBattleActive => CurrentState == GameState.Battle;

        // ------------------------------------------------------------------
        //  Evento cambio stato
        // ------------------------------------------------------------------

        /// <summary>
        /// Sparato ogni volta che lo stato in cima allo stack cambia
        /// (push, pop o replace). Ascoltato da SceneManager, AudioSystem,
        /// InputSystem per aggiornare renderer e comportamenti.
        /// </summary>
        public event EventHandler<StateTransitionArgs>? OnStateChanged;

        // ------------------------------------------------------------------
        //  Inizializzazione
        // ------------------------------------------------------------------

        /// <summary>
        /// Inietta la factory che crea le istanze di IGameScreen.
        /// Chiamare prima di qualsiasi transizione.
        /// Esempio:
        ///   GameStateManager.Instance.Initialize((state, ctx) => state switch {
        ///       GameState.WorldMap => new WorldMapScreen(),
        ///       GameState.Game     => new SideScrollScreen(ctx),
        ///       GameState.Battle   => new BattleScreen(ctx),
        ///       _                  => new NullScreen()
        ///   });
        /// </summary>
        public void Initialize(Func<GameState, string?, IGameScreen> screenFactory)
        {
            _screenFactory = screenFactory
                ?? throw new ArgumentNullException(nameof(screenFactory));
        }

        // ------------------------------------------------------------------
        //  REPLACE — transizione normale (svuota stack fino alla base)
        // ------------------------------------------------------------------

        /// <summary>
        /// Sostituisce lo stato corrente con <paramref name="newState"/>.
        /// Tutti gli stati nello stack ricevono OnExit() in ordine LIFO.
        /// Lo stack viene poi ripopolato con il solo nuovo stato.
        /// </summary>
        /// <param name="newState">Stato di destinazione.</param>
        /// <param name="contextId">
        /// ID opzionale passato alla nuova schermata (es. ID locazione,
        /// ID battaglia). Usato da SideScrollRenderer per caricare la
        /// mappa corretta, o da BattleSystem per inizializzare i nemici.
        /// </param>
        public void TransitionTo(GameState newState, string? contextId = null)
        {
            var previousState = CurrentState;

            // Esci da tutti gli stati nello stack
            ExitAllScreens();

            // Crea e registra il nuovo stato
            var screen = BuildScreen(newState, contextId);
            _stack.Push((newState, screen));

            var args = new StateTransitionArgs(
                newState, previousState, contextId, isOverlay: false);

            screen.OnEnter(args);
            OnStateChanged?.Invoke(this, args);
        }

        // ------------------------------------------------------------------
        //  PUSH — overlay (la scena sottostante resta in memoria)
        // ------------------------------------------------------------------

        /// <summary>
        /// Pusha <paramref name="overlayState"/> sopra lo stato corrente.
        /// Lo stato sottostante riceve OnPause() ma NON viene distrutto.
        ///
        /// Usare per: Pausa, Dialogo, DeckBuilder, Opzioni.
        /// NON usare per transizioni di scena principali (usare TransitionTo).
        /// </summary>
        public void PushOverlay(GameState overlayState, string? contextId = null)
        {
            // Metti in pausa lo stato corrente
            if (_stack.Count > 0)
                _stack.Peek().Screen.OnPause();

            var previousState = CurrentState;
            var screen = BuildScreen(overlayState, contextId);
            _stack.Push((overlayState, screen));

            var args = new StateTransitionArgs(
                overlayState, previousState, contextId, isOverlay: true);

            screen.OnEnter(args);
            OnStateChanged?.Invoke(this, args);
        }

        // ------------------------------------------------------------------
        //  POP — rimuove l'overlay corrente
        // ------------------------------------------------------------------

        /// <summary>
        /// Rimuove lo stato in cima allo stack (deve essere un overlay).
        /// Lo stato sottostante riceve OnResume().
        /// Se lo stack ha un solo elemento, il pop è ignorato con un warning.
        /// </summary>
        public void PopOverlay()
        {
            if (_stack.Count <= 1)
            {
                // Non si può poppare l'unico stato base — log warning
                System.Diagnostics.Debug.WriteLine(
                    "[GameStateManager] PopOverlay ignorato: stack ha un solo stato.");
                return;
            }

            var (poppedState, poppedScreen) = _stack.Pop();
            poppedScreen.OnExit();

            // Riprendi lo stato sottostante
            var (resumedState, resumedScreen) = _stack.Peek();
            resumedScreen.OnResume();

            var args = new StateTransitionArgs(
                resumedState, poppedState, contextId: null, isOverlay: false);

            OnStateChanged?.Invoke(this, args);
        }

        // ------------------------------------------------------------------
        //  UPDATE / RENDER — chiamati dal GameLoop
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiorna solo lo stato in cima allo stack.
        /// Gli stati sottostanti sono in pausa e non ricevono update.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (_stack.Count > 0)
                _stack.Peek().Screen.OnUpdate(deltaTime);
        }

        /// <summary>
        /// Renderizza solo lo stato in cima allo stack.
        /// </summary>
        public void Render(float deltaTime)
        {
            if (_stack.Count > 0)
                _stack.Peek().Screen.OnRender(deltaTime);
        }

        // ------------------------------------------------------------------
        //  QUERY RAPIDE
        // ------------------------------------------------------------------

        /// <summary>
        /// Ritorna true se <paramref name="state"/> è presente nello stack
        /// (anche se non è in cima).
        /// </summary>
        public bool IsInStack(GameState state)
        {
            foreach (var entry in _stack)
                if (entry.State == state) return true;
            return false;
        }

        /// <summary>
        /// Ritorna il secondo stato dallo stack (quello sotto l'overlay corrente).
        /// Utile per sapere da dove è stato aperto un overlay.
        /// Ritorna null se lo stack ha meno di 2 elementi.
        /// </summary>
        public GameState? GetStateBelow()
        {
            if (_stack.Count < 2) return null;

            // Stack non espone accesso per indice: iteriamo i primi due
            var enumerator = _stack.GetEnumerator();
            enumerator.MoveNext(); // top (overlay)
            enumerator.MoveNext(); // quello sotto
            return enumerator.Current.State;
        }

        // ------------------------------------------------------------------
        //  UTILITY INTERNE
        // ------------------------------------------------------------------

        /// <summary>
        /// Chiama OnExit() su tutti gli stati nello stack in ordine LIFO
        /// e svuota lo stack.
        /// </summary>
        private void ExitAllScreens()
        {
            while (_stack.Count > 0)
            {
                var (_, screen) = _stack.Pop();
                screen.OnExit();
            }
        }

        /// <summary>
        /// Costruisce un IGameScreen tramite la factory iniettata.
        /// Lancia InvalidOperationException se la factory non è stata inizializzata.
        /// </summary>
        private IGameScreen BuildScreen(GameState state, string? contextId)
        {
            if (_screenFactory == null)
                throw new InvalidOperationException(
                    "[GameStateManager] Initialize() non è stato chiamato " +
                    "prima di una transizione di stato.");

            return _screenFactory(state, contextId);
        }

        // ------------------------------------------------------------------
        //  HELPER DI TRANSIZIONE SEMANTICI
        //  Metodi ad alto livello che incapsulano le transizioni più comuni.
        //  Evitano di disseminare stringhe magiche nel codice chiamante.
        // ------------------------------------------------------------------

        /// <summary>
        /// Vai alla World Map isometrica.
        /// </summary>
        public void GoToWorldMap()
            => TransitionTo(GameState.WorldMap);

        /// <summary>
        /// Entra in una micro area (side-scroll).
        /// </summary>
        /// <param name="locationId">ID del LocationNode (es. "LOC_MARSHEN_LOCANDA").</param>
        public void EnterLocation(string locationId)
            => TransitionTo(GameState.Game, locationId);

        /// <summary>
        /// Avvia una battaglia.
        /// </summary>
        /// <param name="encounterId">ID dell'encounter (es. "ENC_OMBRA_VUOTA_01").</param>
        public void StartBattle(string encounterId)
            => TransitionTo(GameState.Battle, encounterId);

        /// <summary>
        /// Apre il menu di pausa come overlay.
        /// Funziona sia dalla WorldMap che dal side-scroll che dalla battaglia.
        /// </summary>
        public void OpenPause()
            => PushOverlay(GameState.Pause);

        /// <summary>
        /// Chiude il menu di pausa e riprende lo stato sottostante.
        /// </summary>
        public void ClosePause()
            => PopOverlay();

        /// <summary>
        /// Apre un dialogo di trama come overlay.
        /// </summary>
        /// <param name="eventId">ID dello StoryEvent (es. "01_INCONTRO_LYRA").</param>
        public void OpenDialog(string eventId)
            => PushOverlay(GameState.Dialog, eventId);

        /// <summary>
        /// Chiude il dialogo corrente.
        /// </summary>
        public void CloseDialog()
            => PopOverlay();

        /// <summary>
        /// Torna al menu principale, svuotando l'intero stack.
        /// </summary>
        public void GoToMainMenu()
            => TransitionTo(GameState.MainMenu);
    }
}
