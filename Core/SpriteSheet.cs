// =============================================================================
//  La Via della Redenzione — Core/SpriteSheet.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Gestione sprite sheet con animazioni row-based, flip
//                orizzontale e supporto animazioni d'attacco sequenziali
//                per la battle screen side view FF-style.
//
//  Struttura sprite sheet attesa:
//    Ogni riga = una animazione (idle, walk, attack, cast, hurt, ko, victory,
//                                defend).
//    Ogni colonna = un frame dell'animazione.
//    Tutti i frame hanno la stessa dimensione (FrameWidth x FrameHeight).
//
//    Esempio layout per Kael (8 colonne, 8 righe):
//      Riga 0: idle     (4 frame)
//      Riga 1: walk     (6 frame)
//      Riga 2: attack   (6 frame) ← swing orizzontale con la lama
//      Riga 3: cast     (5 frame)
//      Riga 4: hurt     (3 frame)
//      Riga 5: ko       (4 frame)
//      Riga 6: victory  (4 frame)
//      Riga 7: defend   (2 frame)
//
//  Animazioni d'attacco (battle side view):
//    Le animazioni Attack e Cast sono non-loopate e sparano l'evento
//    OnAttackHitFrame quando raggiungono il frame di impatto definito
//    in AnimationDefinition.HitFrame. La BattleScreen ascolta questo
//    evento per triggerare VFX e danno nel momento visivamente corretto.
//
//  Flip orizzontale:
//    I nemici vengono disegnati con FlipHorizontal = true per farli
//    "guardare" verso destra (verso il gruppo) nella side view FF-style.
//    Il flip è applicato dal renderer — SpriteSheet si limita a esporre
//    il flag, non modifica i pixel.
//
//  Coordinate UV:
//    SpriteSheet.GetFrameUV() restituisce il rettangolo (x, y, w, h)
//    in pixel assoluti nello sheet. Il renderer MAUI usa questi valori
//    per disegnare la porzione corretta della bitmap.
// =============================================================================

using System;
using System.Collections.Generic;

namespace LaViaDellaRedenzione.Core
{
    // -------------------------------------------------------------------------
    //  NOMI ANIMAZIONE — costanti stringa per evitare magic strings
    // -------------------------------------------------------------------------

    /// <summary>
    /// Nomi standard delle animazioni. Usare queste costanti invece di
    /// stringhe letterali per evitare typo silenziosi.
    /// </summary>
    public static class AnimationName
    {
        public const string Idle    = "idle";
        public const string Walk    = "walk";
        public const string Attack  = "attack";
        public const string Cast    = "cast";
        public const string Hurt    = "hurt";
        public const string KO      = "ko";
        public const string Victory = "victory";
        public const string Defend  = "defend";
    }

    // -------------------------------------------------------------------------
    //  DEFINIZIONE ANIMAZIONE
    // -------------------------------------------------------------------------

    /// <summary>
    /// Descrive una singola animazione all'interno dello sprite sheet.
    /// Caricata dal sprite_manifest.json tramite SpriteAnimationSystem.
    /// </summary>
    public sealed class AnimationDefinition
    {
        /// <summary>Nome dell'animazione (vedi AnimationName).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Indice di riga nello sprite sheet (0-based).</summary>
        public int Row { get; set; } = 0;

        /// <summary>Numero totale di frame dell'animazione.</summary>
        public int FrameCount { get; set; } = 1;

        /// <summary>Frame al secondo dell'animazione.</summary>
        public float FPS { get; set; } = 12f;

        /// <summary>True se l'animazione deve ripetersi in loop.</summary>
        public bool Loop { get; set; } = true;

        /// <summary>
        /// Frame di impatto per animazioni di attacco (0-based).
        /// Quando il frame corrente raggiunge questo indice, viene sparato
        /// OnAttackHitFrame — la BattleScreen applica danno e VFX.
        /// -1 = nessun hit frame (per animazioni non offensive).
        /// </summary>
        public int HitFrame { get; set; } = -1;

        /// <summary>
        /// Frame dal quale iniziare (utile per sheet con frame vuoti in fondo).
        /// Default 0.
        /// </summary>
        public int StartFrame { get; set; } = 0;
    }

    // -------------------------------------------------------------------------
    //  RETTANGOLO FRAME UV
    // -------------------------------------------------------------------------

    /// <summary>
    /// Coordinate pixel del frame corrente nello sprite sheet.
    /// Passate al renderer per il clipping della bitmap.
    /// </summary>
    public readonly struct FrameRect
    {
        /// <summary>X in pixel nello sheet (angolo superiore sinistro).</summary>
        public int X { get; }

        /// <summary>Y in pixel nello sheet (angolo superiore sinistro).</summary>
        public int Y { get; }

        /// <summary>Larghezza del frame in pixel.</summary>
        public int Width { get; }

        /// <summary>Altezza del frame in pixel.</summary>
        public int Height { get; }

        public FrameRect(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public override string ToString()
            => $"[{X},{Y} {Width}x{Height}]";
    }

    // -------------------------------------------------------------------------
    //  SPRITE SHEET
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rappresenta un singolo sprite sheet e gestisce lo stato
    /// dell'animazione corrente per un'entità (personaggio o nemico).
    ///
    /// OGNI ENTITÀ ha la propria istanza di SpriteSheet — non è un singleton.
    /// Kael, Lyra, Voran, Sera e ogni nemico hanno ciascuno il proprio.
    ///
    /// THREAD SAFETY:
    ///   Update() e GetCurrentFrameRect() devono essere chiamati
    ///   dallo stesso thread (thread del GameLoop).
    ///   Il renderer legge i valori risultanti nel frame successivo — questo
    ///   è accettabile dato che MAUI ridisegna su MainThread separato.
    /// </summary>
    public sealed class SpriteSheet
    {
        // ------------------------------------------------------------------
        //  Configurazione sheet
        // ------------------------------------------------------------------

        /// <summary>Larghezza di ogni frame in pixel.</summary>
        public int FrameWidth  { get; private set; }

        /// <summary>Altezza di ogni frame in pixel.</summary>
        public int FrameHeight { get; private set; }

        /// <summary>Numero di colonne nello sheet.</summary>
        public int Columns { get; private set; }

        /// <summary>
        /// Path dell'asset bitmap (relativo ad /Assets/Sprites/).
        /// Usato dall'AssetManager per caricare la texture.
        /// </summary>
        public string AssetPath { get; private set; } = string.Empty;

        /// <summary>
        /// Se true, il renderer disegna il frame specchiato orizzontalmente.
        /// Usato per i nemici nella side view FF-style (guardano verso destra).
        /// </summary>
        public bool FlipHorizontal { get; set; } = false;

        // ------------------------------------------------------------------
        //  Animazioni registrate
        // ------------------------------------------------------------------

        private readonly Dictionary<string, AnimationDefinition> _animations = new();

        // ------------------------------------------------------------------
        //  Stato animazione corrente
        // ------------------------------------------------------------------

        private AnimationDefinition? _current;
        private float  _frameTimer    = 0f;
        private int    _frameIndex    = 0;
        private bool   _isPlaying     = true;
        private bool   _hitFired      = false;   // HitFrame già sparato questo ciclo?

        // ------------------------------------------------------------------
        //  EVENTI
        // ------------------------------------------------------------------

        /// <summary>
        /// Sparato quando l'animazione non-loopata è completata.
        /// La BattleScreen ascolta questo evento per tornare a idle
        /// dopo attack/hurt/ko.
        /// </summary>
        public event Action<SpriteSheet>? OnAnimationComplete;

        /// <summary>
        /// Sparato quando il frame corrente raggiunge HitFrame.
        /// La BattleScreen applica il danno e triggera i VFX
        /// (damage number, hit flash, screen shake) in questo momento.
        /// Garantisce che l'effetto visivo sia sincronizzato con l'animazione.
        /// </summary>
        public event Action<SpriteSheet>? OnAttackHitFrame;

        // ------------------------------------------------------------------
        //  COSTRUTTORE
        // ------------------------------------------------------------------

        /// <param name="assetPath">Path relativo dello sprite sheet.</param>
        /// <param name="frameWidth">Larghezza singolo frame in pixel.</param>
        /// <param name="frameHeight">Altezza singolo frame in pixel.</param>
        /// <param name="columns">Numero di colonne nello sheet.</param>
        public SpriteSheet(
            string assetPath,
            int    frameWidth,
            int    frameHeight,
            int    columns)
        {
            AssetPath   = assetPath   ?? throw new ArgumentNullException(nameof(assetPath));
            FrameWidth  = frameWidth;
            FrameHeight = frameHeight;
            Columns     = columns;
        }

        // ------------------------------------------------------------------
        //  REGISTRAZIONE ANIMAZIONI
        // ------------------------------------------------------------------

        /// <summary>
        /// Registra una definizione di animazione.
        /// Da chiamare dopo la costruzione, prima di Play().
        /// </summary>
        public void RegisterAnimation(AnimationDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            _animations[definition.Name] = definition;
        }

        /// <summary>
        /// Registra più animazioni in una sola chiamata.
        /// </summary>
        public void RegisterAnimations(IEnumerable<AnimationDefinition> definitions)
        {
            foreach (var def in definitions)
                RegisterAnimation(def);
        }

        // ------------------------------------------------------------------
        //  CONTROLLO RIPRODUZIONE
        // ------------------------------------------------------------------

        /// <summary>
        /// Avvia la riproduzione di un'animazione.
        /// Se l'animazione richiesta è già in riproduzione, non fa nulla
        /// (evita restart continui nell'update loop).
        /// </summary>
        /// <param name="animationName">Nome animazione (vedi AnimationName).</param>
        /// <param name="forceRestart">
        /// Se true, riavvia l'animazione anche se è già quella corrente.
        /// Usare per hurt e attack che devono sempre ripartire dal frame 0.
        /// </param>
        public void Play(string animationName, bool forceRestart = false)
        {
            if (!_animations.TryGetValue(animationName, out var def))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SpriteSheet] Animazione '{animationName}' non trovata in {AssetPath}");
                return;
            }

            // Già in riproduzione e non forzato — nessun restart
            if (_current?.Name == animationName && !forceRestart) return;

            _current    = def;
            _frameIndex = def.StartFrame;
            _frameTimer = 0f;
            _isPlaying  = true;
            _hitFired   = false;
        }

        /// <summary>
        /// Mette in pausa l'animazione (utile durante menu/dialogo).
        /// </summary>
        public void Pause() => _isPlaying = false;

        /// <summary>
        /// Riprende l'animazione dopo una pausa.
        /// </summary>
        public void Resume() => _isPlaying = true;

        /// <summary>
        /// Ferma l'animazione e torna al frame 0.
        /// </summary>
        public void Stop()
        {
            _isPlaying  = false;
            _frameIndex = 0;
            _frameTimer = 0f;
        }

        /// <summary>
        /// Forza un frame specifico senza riproduzione.
        /// Usato per posa statica (es. KO fermo sull'ultimo frame).
        /// </summary>
        public void SetFrame(int frameIndex)
        {
            if (_current == null) return;
            _frameIndex = Math.Clamp(frameIndex, 0, _current.FrameCount - 1);
            _isPlaying  = false;
        }

        // ------------------------------------------------------------------
        //  PROPRIETÀ DI STATO
        // ------------------------------------------------------------------

        /// <summary>Nome dell'animazione corrente. Null se nessuna è stata avviata.</summary>
        public string? CurrentAnimationName => _current?.Name;

        /// <summary>True se l'animazione è in riproduzione.</summary>
        public bool IsPlaying => _isPlaying;

        /// <summary>Indice del frame corrente (0-based).</summary>
        public int CurrentFrameIndex => _frameIndex;

        /// <summary>True se l'animazione corrente è loopata.</summary>
        public bool IsLooping => _current?.Loop ?? false;

        // ------------------------------------------------------------------
        //  UPDATE — chiamato ogni frame dal SpriteAnimationSystem
        // ------------------------------------------------------------------

        /// <summary>
        /// Avanza il timer e aggiorna il frame corrente.
        /// Spara gli eventi OnAttackHitFrame e OnAnimationComplete
        /// nei momenti corretti.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!_isPlaying || _current == null) return;

            float frameDuration = 1f / _current.FPS;
            _frameTimer += deltaTime;

            while (_frameTimer >= frameDuration)
            {
                _frameTimer -= frameDuration;
                AdvanceFrame();
            }
        }

        // ------------------------------------------------------------------
        //  LETTURA FRAME CORRENTE
        // ------------------------------------------------------------------

        /// <summary>
        /// Restituisce le coordinate pixel del frame corrente nello sheet.
        /// Chiamato dal renderer ogni frame per disegnare la porzione corretta.
        /// </summary>
        public FrameRect GetCurrentFrameRect()
        {
            if (_current == null)
                return new FrameRect(0, 0, FrameWidth, FrameHeight);

            int col = _frameIndex % Columns;
            int row = _current.Row;

            return new FrameRect(
                col * FrameWidth,
                row * FrameHeight,
                FrameWidth,
                FrameHeight);
        }

        // ------------------------------------------------------------------
        //  LOGICA AVANZAMENTO FRAME
        // ------------------------------------------------------------------

        private void AdvanceFrame()
        {
            if (_current == null) return;

            int lastFrame = _current.StartFrame + _current.FrameCount - 1;

            // Controlla HitFrame prima di avanzare
            CheckHitFrame();

            _frameIndex++;

            if (_frameIndex > lastFrame)
            {
                if (_current.Loop)
                {
                    // Riparti dall'inizio
                    _frameIndex = _current.StartFrame;
                    _hitFired   = false;
                }
                else
                {
                    // Animazione terminata — fermati sull'ultimo frame
                    _frameIndex = lastFrame;
                    _isPlaying  = false;
                    OnAnimationComplete?.Invoke(this);
                }
            }
        }

        /// <summary>
        /// Controlla se il frame corrente corrisponde all'HitFrame
        /// e spara l'evento se non è già stato sparato in questo ciclo.
        /// </summary>
        private void CheckHitFrame()
        {
            if (_current == null)           return;
            if (_current.HitFrame < 0)      return;  // nessun hit frame definito
            if (_hitFired)                  return;  // già sparato

            // L'hit frame è relativo all'inizio dell'animazione
            int absoluteHitFrame = _current.StartFrame + _current.HitFrame;

            if (_frameIndex == absoluteHitFrame)
            {
                _hitFired = true;
                OnAttackHitFrame?.Invoke(this);
            }
        }

        // ------------------------------------------------------------------
        //  FACTORY: PERSONAGGI PRINCIPALI
        //  Configurazioni standard pronte all'uso, allineate alla lore.
        //  Le dimensioni frame sono placeholder — verranno aggiornate
        //  con gli asset reali dal SpriteAnimationSystem via manifest JSON.
        // ------------------------------------------------------------------

        /// <summary>
        /// Crea e configura lo SpriteSheet di Kael Dawnford.
        /// Attack: swing orizzontale con la lama (6 frame, hit al frame 3).
        /// </summary>
        public static SpriteSheet CreateKael()
        {
            var sheet = new SpriteSheet(
                assetPath:   "Characters/kael_sheet.png",
                frameWidth:  64,
                frameHeight: 64,
                columns:     8);

            sheet.RegisterAnimations(new[]
            {
                new AnimationDefinition { Name = AnimationName.Idle,    Row = 0, FrameCount = 4, FPS = 6f,  Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Walk,    Row = 1, FrameCount = 6, FPS = 10f, Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Attack,  Row = 2, FrameCount = 6, FPS = 14f, Loop = false, HitFrame = 3  },
                new AnimationDefinition { Name = AnimationName.Cast,    Row = 3, FrameCount = 5, FPS = 10f, Loop = false, HitFrame = 3  },
                new AnimationDefinition { Name = AnimationName.Hurt,    Row = 4, FrameCount = 3, FPS = 12f, Loop = false, HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.KO,      Row = 5, FrameCount = 4, FPS = 8f,  Loop = false, HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Victory, Row = 6, FrameCount = 4, FPS = 6f,  Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Defend,  Row = 7, FrameCount = 2, FPS = 6f,  Loop = false, HitFrame = -1 },
            });

            sheet.Play(AnimationName.Idle);
            return sheet;
        }

        /// <summary>
        /// Crea e configura lo SpriteSheet di Lyra Ashveil.
        /// Cast: rune sul dorso della mano che si illuminano (5 frame, hit al frame 3).
        /// Attack: attacco fisico veloce di supporto (4 frame, hit al frame 2).
        /// </summary>
        public static SpriteSheet CreateLyra()
        {
            var sheet = new SpriteSheet(
                assetPath:   "Characters/lyra_sheet.png",
                frameWidth:  64,
                frameHeight: 64,
                columns:     8);

            sheet.RegisterAnimations(new[]
            {
                new AnimationDefinition { Name = AnimationName.Idle,    Row = 0, FrameCount = 4, FPS = 6f,  Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Walk,    Row = 1, FrameCount = 6, FPS = 10f, Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Attack,  Row = 2, FrameCount = 4, FPS = 14f, Loop = false, HitFrame = 2  },
                new AnimationDefinition { Name = AnimationName.Cast,    Row = 3, FrameCount = 5, FPS = 10f, Loop = false, HitFrame = 3  },
                new AnimationDefinition { Name = AnimationName.Hurt,    Row = 4, FrameCount = 3, FPS = 12f, Loop = false, HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.KO,      Row = 5, FrameCount = 4, FPS = 8f,  Loop = false, HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Victory, Row = 6, FrameCount = 4, FPS = 6f,  Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Defend,  Row = 7, FrameCount = 2, FPS = 6f,  Loop = false, HitFrame = -1 },
            });

            sheet.Play(AnimationName.Idle);
            return sheet;
        }

        /// <summary>
        /// Crea e configura lo SpriteSheet di Voran il Silente.
        /// Cast: lento e pesante, richiama anni di potere trattenuto (6 frame, hit al frame 4).
        /// </summary>
        public static SpriteSheet CreateVoran()
        {
            var sheet = new SpriteSheet(
                assetPath:   "Characters/voran_sheet.png",
                frameWidth:  64,
                frameHeight: 64,
                columns:     8);

            sheet.RegisterAnimations(new[]
            {
                new AnimationDefinition { Name = AnimationName.Idle,    Row = 0, FrameCount = 4, FPS = 5f,  Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Walk,    Row = 1, FrameCount = 6, FPS = 8f,  Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Attack,  Row = 2, FrameCount = 4, FPS = 10f, Loop = false, HitFrame = 2  },
                new AnimationDefinition { Name = AnimationName.Cast,    Row = 3, FrameCount = 6, FPS = 8f,  Loop = false, HitFrame = 4  },
                new AnimationDefinition { Name = AnimationName.Hurt,    Row = 4, FrameCount = 3, FPS = 10f, Loop = false, HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.KO,      Row = 5, FrameCount = 4, FPS = 6f,  Loop = false, HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Victory, Row = 6, FrameCount = 4, FPS = 5f,  Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Defend,  Row = 7, FrameCount = 2, FPS = 5f,  Loop = false, HitFrame = -1 },
            });

            sheet.Play(AnimationName.Idle);
            return sheet;
        }

        /// <summary>
        /// Crea e configura lo SpriteSheet di Sera.
        /// Attack: rapido e basso, mani callose da bambina di campagna (4 frame, hit al frame 1).
        /// </summary>
        public static SpriteSheet CreateSera()
        {
            var sheet = new SpriteSheet(
                assetPath:   "Characters/sera_sheet.png",
                frameWidth:  48,   // Sera è più piccola — frame ridotti
                frameHeight: 48,
                columns:     8);

            sheet.RegisterAnimations(new[]
            {
                new AnimationDefinition { Name = AnimationName.Idle,    Row = 0, FrameCount = 4, FPS = 6f,  Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Walk,    Row = 1, FrameCount = 6, FPS = 12f, Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Attack,  Row = 2, FrameCount = 4, FPS = 16f, Loop = false, HitFrame = 1  },
                new AnimationDefinition { Name = AnimationName.Cast,    Row = 3, FrameCount = 3, FPS = 10f, Loop = false, HitFrame = 2  },
                new AnimationDefinition { Name = AnimationName.Hurt,    Row = 4, FrameCount = 3, FPS = 12f, Loop = false, HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.KO,      Row = 5, FrameCount = 4, FPS = 8f,  Loop = false, HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Victory, Row = 6, FrameCount = 4, FPS = 8f,  Loop = true,  HitFrame = -1 },
                new AnimationDefinition { Name = AnimationName.Defend,  Row = 7, FrameCount = 2, FPS = 6f,  Loop = false, HitFrame = -1 },
            });

            sheet.Play(AnimationName.Idle);
            return sheet;
        }
    }
}
