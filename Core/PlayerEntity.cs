// =============================================================================
//  La Via della Redenzione — Core/PlayerEntity.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Entità giocabile di Kael Dawnford (e wrapper generico per
//                gli altri personaggi). Gestisce:
//
//    - State machine: IDLE, WALK, ATTACK, HURT, KO, VICTORY, DEFEND
//    - Movimento orizzontale nel side-scroll (con accelerazione/decelerazione)
//    - AABB collision detection con il mondo
//    - Hitbox d'attacco attiva solo durante i frame dell'animazione attack
//    - Sistema Morale (0-100) specifico di Kael
//    - Integrazione con SpriteSheet (animazioni d'attacco con OnAttackHitFrame)
//    - Rendering placeholder (rettangolo colorato + debug overlay)
//
//  Coordinate:
//    Tutte in pixel logici del mondo (stesse di Camera2D).
//    X, Y = angolo superiore sinistro dell'AABB del personaggio.
//
//  Uso nel SideScrollRenderer:
//    entity.Update(deltaTime, inputSystem, camera);
//    entity.Render(renderer);   // placeholder o sprite reale
//
//  Uso nella BattleScreen (side view FF-style):
//    entity.PlayAnimation(AnimationName.Attack);
//    entity.Sprite.OnAttackHitFrame += OnHitFrameReached;
//    // → BattleSystem applica danno e VFX al momento corretto
// =============================================================================

using System;
using LaViaDellaRedenzione.Systems;

namespace LaViaDellaRedenzione.Core
{
    // -------------------------------------------------------------------------
    //  STATO ENTITÀ
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stati della macchina a stati del PlayerEntity.
    /// Determina quale animazione è in riproduzione e quali input sono accettati.
    /// </summary>
    public enum EntityState
    {
        Idle    = 0,
        Walk    = 1,
        Attack  = 2,
        Hurt    = 3,
        KO      = 4,
        Victory = 5,
        Defend  = 6
    }

    // -------------------------------------------------------------------------
    //  AABB — Axis-Aligned Bounding Box
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rettangolo di collisione allineato agli assi.
    /// Usato per collision detection con il mondo e con altri AABB (hitbox).
    /// </summary>
    public readonly struct AABB
    {
        public float X      { get; }
        public float Y      { get; }
        public float Width  { get; }
        public float Height { get; }

        public float Left   => X;
        public float Right  => X + Width;
        public float Top    => Y;
        public float Bottom => Y + Height;
        public float CenterX => X + Width  * 0.5f;
        public float CenterY => Y + Height * 0.5f;

        public AABB(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        /// <summary>Ritorna true se questo AABB si sovrappone con other.</summary>
        public bool Intersects(AABB other)
            => Left   < other.Right
            && Right  > other.Left
            && Top    < other.Bottom
            && Bottom > other.Top;

        /// <summary>Ritorna true se il punto (px, py) è dentro l'AABB.</summary>
        public bool Contains(float px, float py)
            => px >= Left && px <= Right
            && py >= Top  && py <= Bottom;

        public override string ToString()
            => $"AABB({X:F0},{Y:F0} {Width:F0}x{Height:F0})";
    }

    // -------------------------------------------------------------------------
    //  PLAYER ENTITY
    // -------------------------------------------------------------------------

    /// <summary>
    /// Entità giocabile principale. Ogni membro del gruppo (Kael, Lyra,
    /// Voran, Sera) è un'istanza separata con il proprio SpriteSheet e
    /// le proprie statistiche base.
    ///
    /// Nel SIDE-SCROLL: si muove, interagisce con hotspot, anima walk/idle.
    /// Nella BATTLE SCREEN: posizione fissa, anima attack/hurt/ko/victory.
    /// Nella WORLD MAP: rappresentato dallo sprite del gruppo (non usa questa classe).
    /// </summary>
    public sealed class PlayerEntity
    {
        // ------------------------------------------------------------------
        //  Identificazione
        // ------------------------------------------------------------------

        /// <summary>ID univoco del personaggio (es. "KAEL", "LYRA").</summary>
        public string CharacterId { get; }

        /// <summary>Nome visualizzato nell'UI.</summary>
        public string DisplayName { get; }

        /// <summary>Classe del personaggio (per vincoli carte).</summary>
        public CharacterClass Class { get; }

        // ------------------------------------------------------------------
        //  Posizione e dimensioni nel mondo
        // ------------------------------------------------------------------

        public float X { get; private set; }
        public float Y { get; private set; }

        /// <summary>Larghezza AABB in pixel logici.</summary>
        public float Width  { get; }

        /// <summary>Altezza AABB in pixel logici.</summary>
        public float Height { get; }

        /// <summary>AABB corrente del personaggio nel mondo.</summary>
        public AABB Bounds => new(X, Y, Width, Height);

        // ------------------------------------------------------------------
        //  Hitbox d'attacco
        // ------------------------------------------------------------------

        /// <summary>
        /// Hitbox d'attacco — attiva solo durante i frame HitFrame dell'animazione.
        /// Posizionata davanti al personaggio (destra se non flippato, sinistra se flippato).
        /// La BattleSystem la usa per determinare quali nemici vengono colpiti
        /// (rilevante per attacchi area nel side-scroll; in battaglia FF è automatico).
        /// </summary>
        public AABB AttackHitbox { get; private set; }

        /// <summary>True se la hitbox d'attacco è attiva questo frame.</summary>
        public bool IsAttackHitboxActive { get; private set; }

        // ------------------------------------------------------------------
        //  Velocità e movimento (side-scroll)
        // ------------------------------------------------------------------

        private float _velocityX = 0f;
        private float _velocityY = 0f;

        /// <summary>Velocità di movimento orizzontale in pixel/secondo.</summary>
        public float MoveSpeed { get; set; } = 180f;

        /// <summary>Accelerazione (pixel/secondo²) — per avvio fluido.</summary>
        public float Acceleration { get; set; } = 800f;

        /// <summary>Decelerazione (pixel/secondo²) — per stop fluido.</summary>
        public float Deceleration { get; set; } = 1200f;

        // ------------------------------------------------------------------
        //  Direzione e flip
        // ------------------------------------------------------------------

        /// <summary>True se il personaggio guarda verso sinistra.</summary>
        public bool FacingLeft { get; private set; } = false;

        // ------------------------------------------------------------------
        //  State machine
        // ------------------------------------------------------------------

        public EntityState State { get; private set; } = EntityState.Idle;

        // ------------------------------------------------------------------
        //  Sprite sheet e animazioni
        // ------------------------------------------------------------------

        /// <summary>
        /// Sprite sheet del personaggio.
        /// Null = usa rendering placeholder (rettangolo colorato).
        /// </summary>
        public SpriteSheet? Sprite { get; private set; }

        // ------------------------------------------------------------------
        //  SISTEMA MORALE — esclusivo di Kael
        // ------------------------------------------------------------------

        private int _morale = 100;

        /// <summary>
        /// Morale di Kael (0-100). Ignorato per gli altri personaggi.
        /// Influenza statistiche di battaglia e comportamento AI.
        /// </summary>
        public int Morale
        {
            get => _morale;
            private set => _morale = Math.Clamp(value, 0, 100);
        }

        /// <summary>True se questo personaggio ha il sistema Morale attivo.</summary>
        public bool HasMorale { get; }

        /// <summary>
        /// Sparato quando il Morale cambia.
        /// BattleSystem e UI si iscrivono per aggiornare statistiche e barra.
        /// </summary>
        public event Action<int, int, string>? OnMoraleChanged;
        // parametri: (vecchioValore, nuovoValore, causa)

        // ------------------------------------------------------------------
        //  Colore placeholder (debug / sviluppo)
        // ------------------------------------------------------------------

        /// <summary>
        /// Colore ARGB del rettangolo placeholder.
        /// Usato finché gli sprite reali non sono disponibili.
        /// </summary>
        public uint PlaceholderColor { get; }

        // ------------------------------------------------------------------
        //  COSTRUTTORE
        // ------------------------------------------------------------------

        /// <param name="characterId">ID univoco ("KAEL", "LYRA", "VORAN", "SERA").</param>
        /// <param name="displayName">Nome visualizzato.</param>
        /// <param name="characterClass">Classe personaggio.</param>
        /// <param name="width">Larghezza AABB in pixel logici.</param>
        /// <param name="height">Altezza AABB in pixel logici.</param>
        /// <param name="hasMorale">True solo per Kael.</param>
        /// <param name="placeholderColor">Colore ARGB placeholder.</param>
        public PlayerEntity(
            string         characterId,
            string         displayName,
            CharacterClass characterClass,
            float          width,
            float          height,
            bool           hasMorale      = false,
            uint           placeholderColor = 0xFF8B5CF6)
        {
            CharacterId      = characterId ?? throw new ArgumentNullException(nameof(characterId));
            DisplayName      = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Class            = characterClass;
            Width            = width;
            Height           = height;
            HasMorale        = hasMorale;
            PlaceholderColor = placeholderColor;
        }

        // ------------------------------------------------------------------
        //  FACTORY — personaggi principali
        // ------------------------------------------------------------------

        /// <summary>Crea l'entità di Kael Dawnford con sprite sheet e Morale attivo.</summary>
        public static PlayerEntity CreateKael(float startX = 0f, float startY = 0f)
        {
            var entity = new PlayerEntity(
                characterId:     "KAEL",
                displayName:     "Kael Dawnford",
                characterClass:  CharacterClass.Guerriero,
                width:           40f,
                height:          60f,
                hasMorale:       true,
                placeholderColor: 0xFF4B5563);  // grigio-blu, giacca imperiale

            entity.SetPosition(startX, startY);
            entity.AttachSprite(SpriteSheet.CreateKael());
            return entity;
        }

        /// <summary>Crea l'entità di Lyra Ashveil.</summary>
        public static PlayerEntity CreateLyra(float startX = 0f, float startY = 0f)
        {
            var entity = new PlayerEntity(
                characterId:     "LYRA",
                displayName:     "Lyra Ashveil",
                characterClass:  CharacterClass.Custode,
                width:           36f,
                height:          58f,
                hasMorale:       false,
                placeholderColor: 0xFF1D4ED8);  // blu scuro, mantello

            entity.SetPosition(startX, startY);
            entity.AttachSprite(SpriteSheet.CreateLyra());
            return entity;
        }

        /// <summary>Crea l'entità di Voran il Silente.</summary>
        public static PlayerEntity CreateVoran(float startX = 0f, float startY = 0f)
        {
            var entity = new PlayerEntity(
                characterId:     "VORAN",
                displayName:     "Voran il Silente",
                characterClass:  CharacterClass.Mago,
                width:           38f,
                height:          62f,
                hasMorale:       false,
                placeholderColor: 0xFF6B7280);  // grigio, veste monacale

            entity.SetPosition(startX, startY);
            entity.AttachSprite(SpriteSheet.CreateVoran());
            return entity;
        }

        /// <summary>Crea l'entità di Sera.</summary>
        public static PlayerEntity CreateSera(float startX = 0f, float startY = 0f)
        {
            var entity = new PlayerEntity(
                characterId:     "SERA",
                displayName:     "Sera",
                characterClass:  CharacterClass.Esploratore,
                width:           28f,   // più piccola degli adulti
                height:          44f,
                hasMorale:       false,
                placeholderColor: 0xFFF59E0B);  // ambra, capelli color grano

            entity.SetPosition(startX, startY);
            entity.AttachSprite(SpriteSheet.CreateSera());
            return entity;
        }

        // ------------------------------------------------------------------
        //  SETUP
        // ------------------------------------------------------------------

        /// <summary>Imposta la posizione iniziale nel mondo.</summary>
        public void SetPosition(float x, float y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// Collega uno sprite sheet all'entità.
        /// Registra i listener per OnAttackHitFrame e OnAnimationComplete.
        /// </summary>
        public void AttachSprite(SpriteSheet sheet)
        {
            // Deregistra listener dal vecchio sprite se presente
            if (Sprite != null)
            {
                Sprite.OnAttackHitFrame    -= HandleAttackHitFrame;
                Sprite.OnAnimationComplete -= HandleAnimationComplete;
            }

            Sprite = sheet;

            if (Sprite != null)
            {
                Sprite.OnAttackHitFrame    += HandleAttackHitFrame;
                Sprite.OnAnimationComplete += HandleAnimationComplete;
            }
        }

        // ------------------------------------------------------------------
        //  UPDATE — chiamato ogni frame dal GameLoop (side-scroll)
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiorna movimento, animazione e hitbox.
        /// Chiamato ogni frame nel side-scroll.
        /// In battaglia, l'update del movimento non avviene (posizione fissa).
        /// </summary>
        /// <param name="deltaTime">Delta time in secondi.</param>
        /// <param name="input">InputSystem per leggere i comandi del giocatore.</param>
        /// <param name="worldWidth">Larghezza del mondo per il clamping.</param>
        public void Update(float deltaTime, InputSystem input, float worldWidth = float.MaxValue)
        {
            UpdateMovement(deltaTime, input, worldWidth);
            UpdateStateMachine(input);
            UpdateHitbox();
            Sprite?.Update(deltaTime);
        }

        /// <summary>
        /// Aggiorna solo l'animazione (per la battle screen — posizione fissa).
        /// </summary>
        public void UpdateAnimationOnly(float deltaTime)
        {
            Sprite?.Update(deltaTime);
        }

        // ------------------------------------------------------------------
        //  MOVIMENTO (side-scroll)
        // ------------------------------------------------------------------

        private void UpdateMovement(float deltaTime, InputSystem input, float worldWidth)
        {
            // Non si muove se sta attaccando, è a terra o è in stato KO/Hurt
            if (State == EntityState.Attack ||
                State == EntityState.Hurt   ||
                State == EntityState.KO)
                return;

            bool movingLeft  = input.IsPressed(InputAction.NavigateLeft);
            bool movingRight = input.IsPressed(InputAction.NavigateRight);

            if (movingRight && !movingLeft)
            {
                // Accelera verso destra
                _velocityX += Acceleration * deltaTime;
                _velocityX  = MathF.Min(_velocityX, MoveSpeed);
                FacingLeft  = false;

                if (Sprite != null)
                    Sprite.FlipHorizontal = false;
            }
            else if (movingLeft && !movingRight)
            {
                // Accelera verso sinistra
                _velocityX -= Acceleration * deltaTime;
                _velocityX  = MathF.Max(_velocityX, -MoveSpeed);
                FacingLeft  = true;

                if (Sprite != null)
                    Sprite.FlipHorizontal = true;
            }
            else
            {
                // Decelera verso zero
                if (_velocityX > 0f)
                {
                    _velocityX -= Deceleration * deltaTime;
                    if (_velocityX < 0f) _velocityX = 0f;
                }
                else if (_velocityX < 0f)
                {
                    _velocityX += Deceleration * deltaTime;
                    if (_velocityX > 0f) _velocityX = 0f;
                }
            }

            // Applica velocità e clampa ai bordi del mondo
            X += _velocityX * deltaTime;
            X  = Math.Clamp(X, 0f, worldWidth - Width);
        }

        // ------------------------------------------------------------------
        //  STATE MACHINE
        // ------------------------------------------------------------------

        private void UpdateStateMachine(InputSystem input)
        {
            switch (State)
            {
                case EntityState.Idle:
                    if (MathF.Abs(_velocityX) > 1f)
                        TransitionTo(EntityState.Walk);
                    else if (input.IsJustPressed(InputAction.ActionA))
                        TransitionTo(EntityState.Attack);
                    else if (input.IsJustPressed(InputAction.ActionB))
                        TransitionTo(EntityState.Defend);
                    break;

                case EntityState.Walk:
                    if (MathF.Abs(_velocityX) < 1f)
                        TransitionTo(EntityState.Idle);
                    else if (input.IsJustPressed(InputAction.ActionA))
                        TransitionTo(EntityState.Attack);
                    break;

                case EntityState.Attack:
                    // L'uscita avviene tramite OnAnimationComplete
                    break;

                case EntityState.Defend:
                    if (!input.IsPressed(InputAction.ActionB))
                        TransitionTo(EntityState.Idle);
                    break;

                case EntityState.Hurt:
                    // L'uscita avviene tramite OnAnimationComplete
                    break;

                case EntityState.KO:
                    // Stato terminale — esce solo quando il BattleSystem
                    // riporta il personaggio in vita (cura o Pietra di Ancora)
                    break;

                case EntityState.Victory:
                    // Loop fino al cambio scena
                    break;
            }
        }

        /// <summary>Transizione esplicita a un nuovo stato (usata anche dalla BattleScreen).</summary>
        public void TransitionTo(EntityState newState)
        {
            if (State == newState && newState != EntityState.Hurt
                                  && newState != EntityState.Attack)
                return;

            State = newState;
            IsAttackHitboxActive = false;

            switch (newState)
            {
                case EntityState.Idle:
                    Sprite?.Play(AnimationName.Idle);
                    break;

                case EntityState.Walk:
                    Sprite?.Play(AnimationName.Walk);
                    break;

                case EntityState.Attack:
                    _velocityX = 0f;
                    Sprite?.Play(AnimationName.Attack, forceRestart: true);
                    break;

                case EntityState.Hurt:
                    _velocityX = 0f;
                    Sprite?.Play(AnimationName.Hurt, forceRestart: true);
                    break;

                case EntityState.KO:
                    _velocityX = 0f;
                    Sprite?.Play(AnimationName.KO, forceRestart: true);
                    break;

                case EntityState.Victory:
                    _velocityX = 0f;
                    Sprite?.Play(AnimationName.Victory);
                    break;

                case EntityState.Defend:
                    _velocityX = 0f;
                    Sprite?.Play(AnimationName.Defend);
                    break;
            }
        }

        // ------------------------------------------------------------------
        //  HITBOX D'ATTACCO
        // ------------------------------------------------------------------

        private void UpdateHitbox()
        {
            if (!IsAttackHitboxActive)
            {
                AttackHitbox = default;
                return;
            }

            // La hitbox è posizionata davanti al personaggio
            float hitboxWidth  = Width * 1.2f;
            float hitboxHeight = Height * 0.6f;
            float hitboxY      = Y + Height * 0.2f;

            float hitboxX = FacingLeft
                ? X - hitboxWidth               // sinistra
                : X + Width;                    // destra

            AttackHitbox = new AABB(hitboxX, hitboxY, hitboxWidth, hitboxHeight);
        }

        // ------------------------------------------------------------------
        //  LISTENER SPRITE SHEET
        // ------------------------------------------------------------------

        /// <summary>
        /// Chiamato dallo SpriteSheet quando raggiunge il frame di impatto.
        /// Attiva la hitbox e notifica la BattleScreen tramite evento.
        /// </summary>
        private void HandleAttackHitFrame(SpriteSheet sheet)
        {
            IsAttackHitboxActive = true;
            OnAttackHit?.Invoke(this);
        }

        /// <summary>
        /// Chiamato dallo SpriteSheet quando un'animazione non-loopata finisce.
        /// Riporta l'entità a Idle dopo attack/hurt.
        /// </summary>
        private void HandleAnimationComplete(SpriteSheet sheet)
        {
            IsAttackHitboxActive = false;

            switch (State)
            {
                case EntityState.Attack:
                case EntityState.Hurt:
                case EntityState.Defend:
                    TransitionTo(EntityState.Idle);
                    break;

                case EntityState.KO:
                    // Rimane fermo sull'ultimo frame KO
                    Sprite?.SetFrame(sheet.CurrentFrameIndex);
                    break;
            }

            OnAnimationFinished?.Invoke(this, State);
        }

        // ------------------------------------------------------------------
        //  EVENTI PUBBLICI
        // ------------------------------------------------------------------

        /// <summary>
        /// Sparato quando la hitbox d'attacco diventa attiva (frame di impatto).
        /// La BattleScreen/BattleSystem si iscrive per applicare danno e VFX.
        /// </summary>
        public event Action<PlayerEntity>? OnAttackHit;

        /// <summary>
        /// Sparato quando un'animazione non-loopata è completata.
        /// Parametri: (entità, stato al momento del completamento).
        /// </summary>
        public event Action<PlayerEntity, EntityState>? OnAnimationFinished;

        // ------------------------------------------------------------------
        //  SISTEMA MORALE (Kael)
        // ------------------------------------------------------------------

        /// <summary>
        /// Modifica il Morale di Kael.
        /// Ignorato se HasMorale è false (tutti tranne Kael).
        /// </summary>
        /// <param name="delta">Variazione (positiva = aumenta, negativa = diminuisce).</param>
        /// <param name="cause">Descrizione della causa (per log e UI).</param>
        public void ModifyMorale(int delta, string cause = "")
        {
            if (!HasMorale) return;

            int oldMorale = Morale;
            Morale += delta;

            if (oldMorale != Morale)
                OnMoraleChanged?.Invoke(oldMorale, Morale, cause);
        }

        /// <summary>
        /// Ritorna true se il Morale di Kael è abbastanza basso da
        /// causare il rifiuto di un ordine del giocatore (25% chance sotto 10).
        /// </summary>
        public bool RollMoraleRefusal()
        {
            if (!HasMorale || Morale >= 10) return false;
            return new Random().NextDouble() < 0.25;
        }

        /// <summary>
        /// Proprietà derivate di soglia Morale per BattleSystem e UI.
        /// </summary>
        public bool IsMoraleLow      => HasMorale && Morale < 30;
        public bool IsMoraleCritical => HasMorale && Morale < 10;
        public bool IsMoralePerfect  => HasMorale && Morale == 100;

        // ------------------------------------------------------------------
        //  RENDERING PLACEHOLDER (debug / sviluppo senza asset)
        // ------------------------------------------------------------------

        /// <summary>
        /// Dati di rendering per il frame corrente.
        /// Il renderer legge questa struttura — se Sprite è null usa il placeholder.
        /// </summary>
        public RenderData GetRenderData(Camera2D camera)
        {
            float screenX = camera.WorldToScreenX(X);
            float screenY = camera.WorldToScreenY(Y);

            if (Sprite == null)
            {
                // Placeholder: rettangolo colorato
                return new RenderData
                {
                    ScreenX         = screenX,
                    ScreenY         = screenY,
                    Width           = Width,
                    Height          = Height,
                    IsPlaceholder   = true,
                    PlaceholderColor = PlaceholderColor,
                    FlipHorizontal  = FacingLeft
                };
            }

            var frame = Sprite.GetCurrentFrameRect();
            return new RenderData
            {
                ScreenX          = screenX,
                ScreenY          = screenY,
                Width            = frame.Width,
                Height           = frame.Height,
                IsPlaceholder    = false,
                AssetPath        = Sprite.AssetPath,
                FrameX           = frame.X,
                FrameY           = frame.Y,
                FrameWidth       = frame.Width,
                FrameHeight      = frame.Height,
                FlipHorizontal   = Sprite.FlipHorizontal,
                PlaceholderColor = PlaceholderColor
            };
        }

        // ------------------------------------------------------------------
        //  DEBUG OVERLAY
        // ------------------------------------------------------------------

        /// <summary>
        /// Testo debug mostrato sopra il personaggio in build Debug.
        /// Contiene stato, velocità, morale e AABB.
        /// </summary>
        public string GetDebugText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{DisplayName} [{State}]");
            sb.AppendLine($"Pos: ({X:F0},{Y:F0}) Vel: {_velocityX:F0}");
            sb.AppendLine($"AABB: {Bounds}");
            if (IsAttackHitboxActive)
                sb.AppendLine($"HitBox: {AttackHitbox}");
            if (HasMorale)
                sb.AppendLine($"Morale: {Morale}/100");
            return sb.ToString();
        }
    }

    // -------------------------------------------------------------------------
    //  DATI DI RENDERING — passati al renderer ogni frame
    // -------------------------------------------------------------------------

    /// <summary>
    /// Struttura dati che il renderer usa per disegnare un'entità.
    /// Separa la logica di gioco dal rendering — il PlayerEntity
    /// non conosce la libreria grafica usata.
    /// </summary>
    public struct RenderData
    {
        /// <summary>Posizione X sullo schermo in pixel logici.</summary>
        public float ScreenX;

        /// <summary>Posizione Y sullo schermo in pixel logici.</summary>
        public float ScreenY;

        /// <summary>Larghezza in pixel logici.</summary>
        public float Width;

        /// <summary>Altezza in pixel logici.</summary>
        public float Height;

        /// <summary>True se non c'è sprite — disegnare PlaceholderColor.</summary>
        public bool IsPlaceholder;

        /// <summary>Colore ARGB del placeholder.</summary>
        public uint PlaceholderColor;

        /// <summary>Path asset sprite sheet (usato se IsPlaceholder = false).</summary>
        public string? AssetPath;

        /// <summary>Coordinata X del frame nello sheet.</summary>
        public int FrameX;

        /// <summary>Coordinata Y del frame nello sheet.</summary>
        public int FrameY;

        /// <summary>Larghezza frame nello sheet.</summary>
        public int FrameWidth;

        /// <summary>Larghezza frame nello sheet.</summary>
        public int FrameHeight;

        /// <summary>Se true, il renderer disegna specchiato orizzontalmente.</summary>
        public bool FlipHorizontal;
    }
}
