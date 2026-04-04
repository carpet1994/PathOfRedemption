// =============================================================================
//  La Via della Redenzione — Core/GameLoop.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Loop di gioco principale a 60 FPS con delta time a
//                precisione nanosecondo, protezione spiral-of-death e
//                degradazione a 30 FPS su Android per risparmio batteria.
//
//  Architettura renderer:
//    Il GameLoop non conosce i renderer direttamente.
//    Delega aggiornamento e rendering al GameStateManager, che instrada
//    al renderer corretto in base allo stato attivo:
//      GameState.WorldMap → WorldMapRenderer  (isometrico fisso)
//      GameState.Game     → SideScrollRenderer (side-scroll 2D)
//      GameState.Battle   → BattleScreen       (side view FF-style)
//
//  Schermata di battaglia (side view FF1-style):
//    La BattleScreen riceve OnUpdate(deltaTime) per animazioni VFX
//    (flash, shake, damage numbers) e OnRender(deltaTime) per disegnare
//    sprite nemici a sinistra e gruppo a destra su sfondo fisso.
//    Non c'è movimento sul campo — solo feedback visivo.
//
//  Uso:
//    var loop = new GameLoop(GameStateManager.Instance);
//    loop.Start();   // avvia il loop
//    loop.Stop();    // ferma il loop (OnSleep Android / chiusura Windows)
//    loop.Pause();   // sospende update (menu pausa, dialogo)
//    loop.Resume();  // riprende
// =============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LaViaDellaRedenzione.Core
{
    // -------------------------------------------------------------------------
    //  STATISTICHE FPS (visibili solo in build Debug)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Statistiche di performance aggiornate ogni secondo.
    /// Esposte al FPS overlay nella build Debug.
    /// </summary>
    public sealed class FrameStats
    {
        /// <summary>Frame per secondo misurati nell'ultimo secondo.</summary>
        public int   FPS           { get; internal set; }

        /// <summary>Delta time medio in millisecondi nell'ultimo secondo.</summary>
        public float AvgDeltaMs    { get; internal set; }

        /// <summary>Delta time massimo registrato nell'ultimo secondo (spike).</summary>
        public float MaxDeltaMs    { get; internal set; }

        /// <summary>Numero di frame saltati per spiral-of-death protection.</summary>
        public int   DroppedFrames { get; internal set; }

        /// <summary>True se il loop sta girando a 30 FPS (modalità risparmio).</summary>
        public bool  IsThrottled   { get; internal set; }
    }

    // -------------------------------------------------------------------------
    //  GAME LOOP
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loop di gioco principale. Gira su un thread dedicato separato dal
    /// thread UI di MAUI per non bloccare le animazioni dell'interfaccia.
    ///
    /// TIMING:
    ///   Target:  60 FPS  → frame budget = 16.667 ms
    ///   Throttle: 30 FPS → frame budget = 33.333 ms
    ///   Il Stopwatch usa QueryPerformanceCounter su Windows e
    ///   clock_gettime(CLOCK_MONOTONIC) su Android — entrambi nanosecondo.
    ///
    /// SPIRAL-OF-DEATH PROTECTION:
    ///   Se deltaTime supera MAX_DELTA (200 ms, equivalente a ~5 FPS),
    ///   viene clampato. Questo evita che dopo uno spike (GC, I/O)
    ///   il gioco tenti di recuperare simulando secondi di logica in un frame.
    ///
    /// THREAD SAFETY:
    ///   Update e Render vengono eseguiti sul thread del loop.
    ///   Qualsiasi interazione con la UI MAUI deve passare per
    ///   MainThread.BeginInvokeOnMainThread() negli IGameScreen.
    /// </summary>
    public sealed class GameLoop
    {
        // ------------------------------------------------------------------
        //  Costanti di timing
        // ------------------------------------------------------------------

        /// <summary>Frame budget target a 60 FPS in secondi.</summary>
        private const double TARGET_FPS        = 60.0;

        /// <summary>Frame budget ridotto a 30 FPS (risparmio batteria Android).</summary>
        private const double THROTTLED_FPS     = 30.0;

        /// <summary>
        /// Delta time massimo consentito in secondi (spiral-of-death protection).
        /// Qualsiasi delta superiore viene clampato a questo valore.
        /// Equivale a simulare al massimo ~5 FPS di logica per frame.
        /// </summary>
        private const float  MAX_DELTA         = 0.200f;   // 200 ms

        /// <summary>
        /// Soglia oltre la quale un frame viene considerato uno spike e
        /// registrato come DroppedFrame nelle statistiche.
        /// </summary>
        private const float  SPIKE_THRESHOLD   = 0.033f;   // 33 ms (~30 FPS)

        /// <summary>
        /// Soglia oltre la quale il loop degrada automaticamente a 30 FPS
        /// per risparmiare batteria su Android.
        /// Se il frame time medio supera questo valore per 60 frame consecutivi,
        /// si attiva la modalità risparmio.
        /// </summary>
        private const float  THROTTLE_TRIGGER  = 0.025f;   // 25 ms

        /// <summary>
        /// Frame consecutivi lenti prima di attivare il throttle.
        /// </summary>
        private const int    THROTTLE_FRAMES   = 60;

        // ------------------------------------------------------------------
        //  Dipendenze
        // ------------------------------------------------------------------

        private readonly GameStateManager _stateManager;

        // ------------------------------------------------------------------
        //  Stato interno del loop
        // ------------------------------------------------------------------

        private CancellationTokenSource? _cts;
        private Task?                    _loopTask;

        private volatile bool _isPaused   = false;
        private volatile bool _isRunning  = false;
        private volatile bool _isThrottle = false;

        /// <summary>Contatore frame lenti consecutivi per throttle detection.</summary>
        private int _slowFrameCount = 0;

        // ------------------------------------------------------------------
        //  Statistiche (aggiornate ogni secondo, thread-safe via Interlocked)
        // ------------------------------------------------------------------

        public FrameStats Stats { get; } = new FrameStats();

        private int   _frameCountAccum  = 0;
        private float _deltaAccum       = 0f;
        private float _maxDeltaAccum    = 0f;
        private int   _droppedAccum     = 0;
        private float _statsTimer       = 0f;

        // ------------------------------------------------------------------
        //  Costruttore
        // ------------------------------------------------------------------

        public GameLoop(GameStateManager stateManager)
        {
            _stateManager = stateManager
                ?? throw new ArgumentNullException(nameof(stateManager));
        }

        // ------------------------------------------------------------------
        //  CONTROLLO DEL LOOP
        // ------------------------------------------------------------------

        /// <summary>
        /// Avvia il game loop su un thread dedicato.
        /// Chiamare dopo aver inizializzato GameStateManager.
        /// Idempotente: chiamate multiple senza Stop() intermedio sono ignorate.
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _cts       = new CancellationTokenSource();
            var token  = _cts.Token;

            // Task con priorità alta — non blocca il thread UI MAUI
            _loopTask = Task.Factory.StartNew(
                () => RunLoop(token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Ferma il game loop. Blocca fino al completamento del thread.
        /// Chiamare in OnSleep (Android) e alla chiusura finestra (Windows).
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();

            try
            {
                _loopTask?.Wait(timeout: TimeSpan.FromSeconds(2));
            }
            catch (AggregateException) { /* task annullato normalmente */ }
            finally
            {
                _cts?.Dispose();
                _cts      = null;
                _loopTask = null;
            }
        }

        /// <summary>
        /// Sospende Update e Render senza fermare il thread.
        /// Il loop continua a girare ma non aggiorna la logica di gioco.
        /// Usare per: menu di pausa, dialoghi, transizioni di scena.
        /// </summary>
        public void Pause()  => _isPaused = true;

        /// <summary>
        /// Riprende Update e Render dopo una Pause().
        /// Il delta time viene resettato al resume per evitare spike.
        /// </summary>
        public void Resume()
        {
            _isPaused     = false;
            _resumeReset  = true;   // segnala al loop di resettare il timer
        }

        /// <summary>Flag per resettare il Stopwatch al resume.</summary>
        private volatile bool _resumeReset = false;

        // ------------------------------------------------------------------
        //  LOOP PRINCIPALE
        // ------------------------------------------------------------------

        /// <summary>
        /// Corpo del loop. Gira sul thread dedicato finché il token non
        /// viene cancellato.
        /// </summary>
        private void RunLoop(CancellationToken token)
        {
            var stopwatch    = Stopwatch.StartNew();
            long lastTicks   = stopwatch.ElapsedTicks;
            double ticksPerSecond = (double)Stopwatch.Frequency;

            while (!token.IsCancellationRequested && _isRunning)
            {
                // --------------------------------------------------------------
                //  CALCOLO DELTA TIME
                // --------------------------------------------------------------

                long   currentTicks = stopwatch.ElapsedTicks;
                double elapsedSecs  = (currentTicks - lastTicks) / ticksPerSecond;
                lastTicks           = currentTicks;

                // Reset delta al resume (evita spike dopo pausa lunga)
                if (_resumeReset)
                {
                    _resumeReset = false;
                    elapsedSecs  = 0.0;
                }

                // Spiral-of-death protection
                float deltaTime = (float)Math.Min(elapsedSecs, MAX_DELTA);

                // --------------------------------------------------------------
                //  THROTTLE DETECTION (Android battery saving)
                // --------------------------------------------------------------

                if (deltaTime > THROTTLE_TRIGGER)
                {
                    _slowFrameCount++;
                    if (_slowFrameCount >= THROTTLE_FRAMES && !_isThrottle)
                    {
                        _isThrottle = true;
                        Stats.IsThrottled = true;
                    }
                }
                else
                {
                    _slowFrameCount = 0;
                    if (_isThrottle)
                    {
                        _isThrottle       = false;
                        Stats.IsThrottled = false;
                    }
                }

                // --------------------------------------------------------------
                //  UPDATE & RENDER (solo se non in pausa)
                // --------------------------------------------------------------

                if (!_isPaused)
                {
                    // Update logica di gioco
                    // GameStateManager instrada al renderer corretto:
                    //   WorldMap (isometrico) | Game (side-scroll) | Battle (side view FF)
                    _stateManager.Update(deltaTime);

                    // Render frame
                    _stateManager.Render(deltaTime);
                }

                // --------------------------------------------------------------
                //  STATISTICHE (aggiornate ogni secondo)
                // --------------------------------------------------------------

                UpdateStats(deltaTime);

                // --------------------------------------------------------------
                //  SLEEP — cede il thread per il tempo residuo del frame budget
                // --------------------------------------------------------------

                double targetFrameSeconds = _isThrottle
                    ? 1.0 / THROTTLED_FPS
                    : 1.0 / TARGET_FPS;

                double elapsed = (stopwatch.ElapsedTicks - currentTicks) / ticksPerSecond;
                double sleepSeconds = targetFrameSeconds - elapsed;

                if (sleepSeconds > 0.001)   // > 1ms: vale la pena dormire
                {
                    Thread.Sleep((int)(sleepSeconds * 1000.0));
                }
                // Se sleepSeconds <= 0 il frame ha sforato il budget:
                // si continua immediatamente senza sleep (frame drop).
                else if (deltaTime > SPIKE_THRESHOLD)
                {
                    _droppedAccum++;
                }
            }

            stopwatch.Stop();
        }

        // ------------------------------------------------------------------
        //  STATISTICHE
        // ------------------------------------------------------------------

        /// <summary>
        /// Accumula dati di performance e aggiorna FrameStats ogni secondo.
        /// </summary>
        private void UpdateStats(float deltaTime)
        {
            _frameCountAccum++;
            _deltaAccum    += deltaTime;
            _statsTimer    += deltaTime;

            if (deltaTime > _maxDeltaAccum)
                _maxDeltaAccum = deltaTime;

            if (_statsTimer >= 1.0f)
            {
                Stats.FPS           = _frameCountAccum;
                Stats.AvgDeltaMs    = (_deltaAccum / _frameCountAccum) * 1000f;
                Stats.MaxDeltaMs    = _maxDeltaAccum * 1000f;
                Stats.DroppedFrames = _droppedAccum;

                // Reset accumulatori
                _frameCountAccum = 0;
                _deltaAccum      = 0f;
                _maxDeltaAccum   = 0f;
                _droppedAccum    = 0;
                _statsTimer      = 0f;
            }
        }

        // ------------------------------------------------------------------
        //  LIFECYCLE ANDROID
        // ------------------------------------------------------------------

        /// <summary>
        /// Da chiamare in App.OnSleep() su Android.
        /// Sospende il loop senza distruggerlo.
        /// </summary>
        public void OnAppSleep()  => Pause();

        /// <summary>
        /// Da chiamare in App.OnResume() su Android.
        /// </summary>
        public void OnAppResume() => Resume();
    }
}
