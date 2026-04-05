// =============================================================================
//  La Via della Redenzione — Core/Camera2D.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Camera 2D con smooth-follow esponenziale, clamping ai
//                bordi del mondo e supporto per due modalità di rendering:
//
//    MODALITÀ ISOMETRICA (WorldMap):
//      La mappa è sempre visibile per intero — la camera è fissa al centro
//      della scena. Nessun follow, nessuno scroll. Lo sprite del gruppo
//      si muove sulla mappa, non la camera.
//
//    MODALITÀ SIDE-SCROLL (micro aree):
//      La camera segue il personaggio con lerp esponenziale smorzato.
//      Clampata ai bordi della micro area (WorldBounds).
//      Supporta parallax offset per il ParallaxBackground.
//
//    MODALITÀ BATTLE (side view FF-style):
//      Camera fissa al centro della BattleScreen.
//      Screen shake applicato come offset temporaneo (VFXSystem).
//
//  Coordinate:
//    Il sistema di coordinate è in pixel logici (indipendente dalla densità
//    dello schermo). Il renderer applica lo scale DPI prima del draw.
//    Origine (0,0) = angolo in alto a sinistra del mondo.
//
//  Uso:
//    Camera2D.Instance.SetMode(CameraMode.SideScroll, worldWidth, worldHeight);
//    Camera2D.Instance.SetTarget(playerX, playerY);
//    Camera2D.Instance.Update(deltaTime);
//    float camX = Camera2D.Instance.X;   // usato dal renderer per offset draw
// =============================================================================

using System;

namespace LaViaDellaRedenzione.Core
{
    // -------------------------------------------------------------------------
    //  MODALITÀ CAMERA
    // -------------------------------------------------------------------------

    /// <summary>
    /// Modalità operative della Camera2D.
    /// Cambiata da SceneManager ad ogni transizione di schermata.
    /// </summary>
    public enum CameraMode
    {
        /// <summary>
        /// Mappa isometrica fissa (WorldMap).
        /// La camera non si muove — la mappa è sempre visibile per intero.
        /// </summary>
        Isometric  = 0,

        /// <summary>
        /// Micro area side-scroll.
        /// La camera segue il target con lerp esponenziale.
        /// </summary>
        SideScroll = 1,

        /// <summary>
        /// Schermata di battaglia side view FF-style.
        /// Camera fissa, solo screen shake come offset temporaneo.
        /// </summary>
        Battle     = 2
    }

    // -------------------------------------------------------------------------
    //  CAMERA 2D
    // -------------------------------------------------------------------------

    /// <summary>
    /// Singleton. Gestisce posizione, follow e shake della camera.
    ///
    /// LERP ESPONENZIALE:
    ///   La formula usata è:
    ///     position += (target - position) * (1 - exp(-smoothing * deltaTime))
    ///   Questo garantisce un follow frame-rate independent: la velocità di
    ///   avvicinamento al target è sempre la stessa indipendentemente dal FPS.
    ///   Con smoothing = 8.0f la camera raggiunge il 99% del target in ~0.58s.
    ///
    /// SCREEN SHAKE:
    ///   Applicato come offset additivo (ShakeX, ShakeY) dal VFXSystem.
    ///   Non modifica la posizione base della camera — si azzera da solo
    ///   decadendo esponenzialmente ogni frame.
    /// </summary>
    public sealed class Camera2D
    {
        // ------------------------------------------------------------------
        //  Singleton
        // ------------------------------------------------------------------

        private static Camera2D? _instance;
        public static Camera2D Instance => _instance ??= new Camera2D();
        private Camera2D() { }

        // ------------------------------------------------------------------
        //  Modalità corrente
        // ------------------------------------------------------------------

        public CameraMode Mode { get; private set; } = CameraMode.Isometric;

        // ------------------------------------------------------------------
        //  Posizione camera (pixel logici, angolo superiore sinistro del viewport)
        // ------------------------------------------------------------------

        /// <summary>Posizione X corrente della camera (dopo lerp e clamp).</summary>
        public float X { get; private set; } = 0f;

        /// <summary>Posizione Y corrente della camera (dopo lerp e clamp).</summary>
        public float Y { get; private set; } = 0f;

        // ------------------------------------------------------------------
        //  Screen shake offset (additivo, gestito da VFXSystem)
        // ------------------------------------------------------------------

        /// <summary>Offset X di screen shake corrente.</summary>
        public float ShakeX { get; private set; } = 0f;

        /// <summary>Offset Y di screen shake corrente.</summary>
        public float ShakeY { get; private set; } = 0f;

        /// <summary>
        /// Posizione finale da passare al renderer (posizione + shake).
        /// </summary>
        public float RenderX => X + ShakeX;

        /// <summary>
        /// Posizione finale da passare al renderer (posizione + shake).
        /// </summary>
        public float RenderY => Y + ShakeY;

        // ------------------------------------------------------------------
        //  Target di follow (posizione del personaggio / gruppo)
        // ------------------------------------------------------------------

        private float _targetX = 0f;
        private float _targetY = 0f;

        // ------------------------------------------------------------------
        //  Dimensioni viewport (schermo logico in pixel)
        // ------------------------------------------------------------------

        /// <summary>Larghezza del viewport in pixel logici.</summary>
        public float ViewportWidth  { get; private set; } = 1280f;

        /// <summary>Altezza del viewport in pixel logici.</summary>
        public float ViewportHeight { get; private set; } = 720f;

        // ------------------------------------------------------------------
        //  Bounds del mondo (side-scroll)
        // ------------------------------------------------------------------

        private float _worldWidth  = 0f;
        private float _worldHeight = 0f;

        // ------------------------------------------------------------------
        //  Parametri di smoothing
        // ------------------------------------------------------------------

        /// <summary>
        /// Coefficiente di smorzamento del lerp esponenziale.
        /// Valori consigliati: 4.0 (lento/cinematico) — 12.0 (reattivo).
        /// Default 8.0: buon compromesso per side-scroll su mobile.
        /// </summary>
        public float Smoothing { get; set; } = 8.0f;

        // ------------------------------------------------------------------
        //  Screen shake
        // ------------------------------------------------------------------

        private float _shakeMagnitude  = 0f;
        private float _shakeDecay      = 0f;
        private float _shakeTimer      = 0f;
        private float _shakeDuration   = 0f;
        private readonly Random _rng   = new();

        // ------------------------------------------------------------------
        //  INIZIALIZZAZIONE / CAMBIO MODALITÀ
        // ------------------------------------------------------------------

        /// <summary>
        /// Imposta la modalità e i parametri del mondo.
        /// Da chiamare da SceneManager ad ogni transizione di schermata.
        /// </summary>
        /// <param name="mode">Modalità operativa.</param>
        /// <param name="worldWidth">
        /// Larghezza del mondo in pixel logici (solo SideScroll).
        /// Ignorato in Isometric e Battle.
        /// </param>
        /// <param name="worldHeight">
        /// Altezza del mondo in pixel logici (solo SideScroll).
        /// </param>
        /// <param name="viewportWidth">Larghezza schermo logico.</param>
        /// <param name="viewportHeight">Altezza schermo logico.</param>
        public void SetMode(
            CameraMode mode,
            float worldWidth    = 0f,
            float worldHeight   = 0f,
            float viewportWidth  = 1280f,
            float viewportHeight = 720f)
        {
            Mode            = mode;
            _worldWidth     = worldWidth;
            _worldHeight    = worldHeight;
            ViewportWidth   = viewportWidth;
            ViewportHeight  = viewportHeight;

            // Reset posizione e shake al cambio modalità
            ShakeX = 0f;
            ShakeY = 0f;
            _shakeMagnitude = 0f;

            switch (mode)
            {
                case CameraMode.Isometric:
                case CameraMode.Battle:
                    // Camera fissa all'origine — il renderer gestisce
                    // il centramento della scena indipendentemente
                    X = 0f;
                    Y = 0f;
                    break;

                case CameraMode.SideScroll:
                    // Posiziona immediatamente la camera sul target
                    // senza lerp (snap) per evitare la "scivolata" iniziale
                    SnapToTarget();
                    break;
            }
        }

        /// <summary>
        /// Imposta le dimensioni del viewport (da chiamare al cambio
        /// orientamento o ridimensionamento finestra Windows).
        /// </summary>
        public void SetViewport(float width, float height)
        {
            ViewportWidth  = width;
            ViewportHeight = height;
        }

        // ------------------------------------------------------------------
        //  TARGET
        // ------------------------------------------------------------------

        /// <summary>
        /// Imposta il punto che la camera deve seguire.
        /// Tipicamente il centro del personaggio giocabile.
        /// In modalità Isometric e Battle viene ignorato.
        /// </summary>
        /// <param name="targetX">Centro X del target in pixel logici del mondo.</param>
        /// <param name="targetY">Centro Y del target in pixel logici del mondo.</param>
        public void SetTarget(float targetX, float targetY)
        {
            _targetX = targetX;
            _targetY = targetY;
        }

        /// <summary>
        /// Porta immediatamente la camera sul target senza lerp.
        /// Usare al primo frame di una nuova micro area per evitare
        /// la "scivolata" da (0,0) alla posizione iniziale del personaggio.
        /// </summary>
        public void SnapToTarget()
        {
            if (Mode != CameraMode.SideScroll) return;

            X = ComputeTargetCameraX(_targetX);
            Y = ComputeTargetCameraY(_targetY);
        }

        // ------------------------------------------------------------------
        //  UPDATE — chiamato ogni frame dal GameLoop
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiorna posizione (lerp) e screen shake.
        /// </summary>
        public void Update(float deltaTime)
        {
            UpdateShake(deltaTime);

            switch (Mode)
            {
                case CameraMode.SideScroll:
                    UpdateSideScroll(deltaTime);
                    break;

                case CameraMode.Isometric:
                case CameraMode.Battle:
                    // Camera fissa — nessun aggiornamento posizione
                    break;
            }
        }

        // ------------------------------------------------------------------
        //  SIDE-SCROLL: lerp esponenziale + clamp
        // ------------------------------------------------------------------

        private void UpdateSideScroll(float deltaTime)
        {
            float desiredX = ComputeTargetCameraX(_targetX);
            float desiredY = ComputeTargetCameraY(_targetY);

            // Lerp esponenziale frame-rate independent
            float factor = 1f - MathF.Exp(-Smoothing * deltaTime);

            X += (desiredX - X) * factor;
            Y += (desiredY - Y) * factor;

            // Clamp ai bordi del mondo
            X = Clamp(X, 0f, MathF.Max(0f, _worldWidth  - ViewportWidth));
            Y = Clamp(Y, 0f, MathF.Max(0f, _worldHeight - ViewportHeight));
        }

        /// <summary>
        /// Calcola la X della camera che centra il target nel viewport.
        /// </summary>
        private float ComputeTargetCameraX(float targetX)
            => targetX - ViewportWidth  * 0.5f;

        /// <summary>
        /// Calcola la Y della camera che centra il target nel viewport.
        /// </summary>
        private float ComputeTargetCameraY(float targetY)
            => targetY - ViewportHeight * 0.5f;

        // ------------------------------------------------------------------
        //  SCREEN SHAKE — usato da VFXSystem per colpi pesanti in battaglia
        // ------------------------------------------------------------------

        /// <summary>
        /// Avvia uno screen shake.
        /// In modalità Battle produce il classico tremolio FF al colpo potente.
        /// In SideScroll può essere usato per eventi ambientali.
        /// </summary>
        /// <param name="magnitude">Intensità massima in pixel logici.</param>
        /// <param name="duration">Durata totale in secondi.</param>
        /// <param name="decay">
        /// Velocità di decadimento. 1.0 = decadimento lineare,
        /// 3.0 = decadimento rapido (shake si smorza velocemente).
        /// </param>
        public void StartShake(float magnitude, float duration, float decay = 2.0f)
        {
            _shakeMagnitude = magnitude;
            _shakeDuration  = duration;
            _shakeDecay     = decay;
            _shakeTimer     = 0f;
        }

        /// <summary>
        /// Ferma immediatamente lo screen shake.
        /// </summary>
        public void StopShake()
        {
            _shakeMagnitude = 0f;
            ShakeX          = 0f;
            ShakeY          = 0f;
        }

        private void UpdateShake(float deltaTime)
        {
            if (_shakeMagnitude <= 0f) return;

            _shakeTimer += deltaTime;

            // Decadimento esponenziale dell'intensità
            float progress  = _shakeTimer / _shakeDuration;
            float currentMag = _shakeMagnitude * MathF.Exp(-_shakeDecay * progress);

            if (_shakeTimer >= _shakeDuration || currentMag < 0.1f)
            {
                StopShake();
                return;
            }

            // Offset casuale ogni frame entro la magnitudine corrente
            ShakeX = ((float)_rng.NextDouble() * 2f - 1f) * currentMag;
            ShakeY = ((float)_rng.NextDouble() * 2f - 1f) * currentMag;
        }

        // ------------------------------------------------------------------
        //  PARALLAX OFFSET — usato da ParallaxBackground nel side-scroll
        // ------------------------------------------------------------------

        /// <summary>
        /// Calcola l'offset di scroll per un layer parallax dato il suo
        /// fattore di profondità.
        /// </summary>
        /// <param name="depth">
        /// Fattore 0.0..1.0.
        /// 0.0 = layer fisso (cielo, non si muove mai).
        /// 1.0 = layer in primo piano (si muove 1:1 con la camera).
        /// Valori intermedi: 0.2 montagne lontane, 0.5 alberi medi, 0.8 rocce vicine.
        /// </param>
        /// <returns>Offset X del layer in pixel logici.</returns>
        public float GetParallaxOffsetX(float depth)
            => X * depth;

        /// <summary>
        /// Offset Y del layer parallax (per effetti di profondità verticale).
        /// </summary>
        public float GetParallaxOffsetY(float depth)
            => Y * depth;

        // ------------------------------------------------------------------
        //  WORLD → SCREEN e SCREEN → WORLD
        // ------------------------------------------------------------------

        /// <summary>
        /// Converte una coordinata X del mondo in coordinata X dello schermo.
        /// Usato dal renderer per posizionare sprite e hotspot.
        /// </summary>
        public float WorldToScreenX(float worldX) => worldX - RenderX;

        /// <summary>
        /// Converte una coordinata Y del mondo in coordinata Y dello schermo.
        /// </summary>
        public float WorldToScreenY(float worldY) => worldY - RenderY;

        /// <summary>
        /// Converte una coordinata X dello schermo in coordinata X del mondo.
        /// Usato per il rilevamento tap/click su hotspot nel side-scroll.
        /// </summary>
        public float ScreenToWorldX(float screenX) => screenX + RenderX;

        /// <summary>
        /// Converte una coordinata Y dello schermo in coordinata Y del mondo.
        /// </summary>
        public float ScreenToWorldY(float screenY) => screenY + RenderY;

        /// <summary>
        /// Ritorna true se un rettangolo del mondo è almeno parzialmente
        /// visibile nel viewport corrente. Usato per culling degli sprite
        /// fuori schermo nel side-scroll.
        /// </summary>
        public bool IsVisible(float worldX, float worldY, float width, float height)
            => worldX + width  > RenderX
            && worldX          < RenderX + ViewportWidth
            && worldY + height > RenderY
            && worldY          < RenderY + ViewportHeight;

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        private static float Clamp(float value, float min, float max)
            => value < min ? min : value > max ? max : value;
    }
}
