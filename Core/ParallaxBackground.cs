// =============================================================================
//  La Via della Redenzione — Core/ParallaxBackground.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Sistema di sfondo parallax multi-layer per il side-scroll
//                delle micro aree. Supporta:
//
//    - Layer procedurali (cielo, nuvole, silhouette montagne) generati
//      senza asset esterni — funzionano come placeholder dark-fantasy
//    - Layer bitmap tileable caricati dall'AssetManager
//    - Velocità di scroll indipendente per layer (depth 0.0..1.0)
//    - Tiling orizzontale infinito
//    - Variazione per zona (Marshen, Drevath, Montagne Ceneri, ecc.)
//    - Integrazione con Camera2D.GetParallaxOffsetX()
//
//  Architettura layer (dal più lontano al più vicino):
//    Layer 0 — Cielo (depth 0.00) : gradiente procedurale, non scorre mai
//    Layer 1 — Nuvole (depth 0.05): silhouette procedurali, scorrono lente
//    Layer 2 — Montagne lontane   (depth 0.15): silhouette scure
//    Layer 3 — Colline medie      (depth 0.30): tono più scuro
//    Layer 4 — Alberi/strutture   (depth 0.55): elementi riconoscibili
//    Layer 5 — Primo piano        (depth 0.80): erba, rocce, dettagli
//
//  Uso nel SideScrollRenderer:
//    _background.SetZone(ZoneType.Marshen);
//    _background.Update(deltaTime, camera);
//    _background.Render(renderer, viewportWidth, viewportHeight);
//
//  Uso nella BattleScreen (side view FF-style):
//    SetZone() imposta lo sfondo della zona dove avviene lo scontro.
//    La camera è fissa (CameraMode.Battle) quindi il parallax non scrolla —
//    lo sfondo è statico ma atmosfericamente coerente con la locazione.
// =============================================================================

using System;
using System.Collections.Generic;
using LaViaDellaRedenzione.Systems;

namespace LaViaDellaRedenzione.Core
{
    // -------------------------------------------------------------------------
    //  TIPO ZONA — determina palette e silhouette procedurali
    // -------------------------------------------------------------------------

    /// <summary>
    /// Zone geografiche di Valdrath, ciascuna con la propria atmosfera visiva.
    /// Usato da ParallaxBackground per selezionare palette e forme procedurali.
    /// </summary>
    public enum ZoneType
    {
        /// <summary>
        /// Brughiera occidentale — grigio-blu, nebbia bassa persistente.
        /// Cielo: grigio piombo. Montagne: nere, piatte. Nuvole: basse e pesanti.
        /// </summary>
        Marshen      = 0,

        /// <summary>
        /// Foresta di Ashgrove — verde-grigio soffocante, luce obliqua.
        /// Cielo appena visibile tra le chiome. Silhouette alberi fitti.
        /// </summary>
        Ashgrove     = 1,

        /// <summary>
        /// Pianure di Drevath (consumate) — grigio-verde morto, steli fermi.
        /// Cielo opaco senza profondità. Silhouette grano cristallizzato.
        /// </summary>
        Drevath      = 2,

        /// <summary>
        /// Foresta delle Sette Dita — arenaria, creste emergenti.
        /// Cielo più chiaro ai margini, creste di roccia come dita.
        /// </summary>
        SetteDita    = 3,

        /// <summary>
        /// Monastero di Pietra Grigia — pietra scura, cielo stellato notturno.
        /// Luce calda interna visibile dalle finestre strette.
        /// </summary>
        Monastero    = 4,

        /// <summary>
        /// Vallata del rituale — pareti di roccia grigia ai lati, cielo stretto.
        /// Atmosfera claustrofobica, luce quasi assente.
        /// </summary>
        Vallata      = 5,

        /// <summary>
        /// Montagne Ceneri — dolomia grigio-scura, brina mattutina, vento.
        /// Cielo quasi bianco all'alba. Silhouette rocce verticali.
        /// </summary>
        MontagniCeneri = 6,

        /// <summary>
        /// Bocca del Silenzio / Santuario — conca naturale protetta.
        /// Cinque luci dei Sigilli come fonti luminose ambientali.
        /// Cielo stellato denso, pietra antica.
        /// </summary>
        Santuario    = 7,

        /// <summary>
        /// Foresta Tranquilla (post-game) — colori che tornano, luce reale.
        /// Il verde riappare, gli uccelli tornano.
        /// </summary>
        ForestaTranquilla = 8
    }

    // -------------------------------------------------------------------------
    //  PALETTE ZONA
    // -------------------------------------------------------------------------

    /// <summary>
    /// Colori ARGB dei layer procedurali per una zona specifica.
    /// </summary>
    public sealed class ZonePalette
    {
        /// <summary>Colore superiore del gradiente cielo.</summary>
        public uint SkyTop    { get; init; }

        /// <summary>Colore inferiore del gradiente cielo (orizzonte).</summary>
        public uint SkyBottom { get; init; }

        /// <summary>Colore delle silhouette montagne lontane.</summary>
        public uint MountainFar { get; init; }

        /// <summary>Colore delle colline medie.</summary>
        public uint HillMid { get; init; }

        /// <summary>Colore degli alberi/strutture.</summary>
        public uint TreeNear { get; init; }

        /// <summary>Colore del primo piano.</summary>
        public uint Foreground { get; init; }

        /// <summary>Colore delle nuvole/nebbia.</summary>
        public uint Cloud { get; init; }

        // ------------------------------------------------------------------
        //  Palette predefinite per zona
        // ------------------------------------------------------------------

        public static readonly ZonePalette Marshen = new()
        {
            SkyTop      = 0xFF2D3748,  // grigio piombo scuro
            SkyBottom   = 0xFF4A5568,  // grigio piombo medio
            Cloud       = 0xFF718096,  // grigio nebbia
            MountainFar = 0xFF1A202C,  // nero quasi totale
            HillMid     = 0xFF2D3748,  // grigio scurissimo
            TreeNear    = 0xFF1A202C,  // silhouette nere
            Foreground  = 0xFF171923   // primo piano quasi nero
        };

        public static readonly ZonePalette Ashgrove = new()
        {
            SkyTop      = 0xFF2F3B2F,  // verde-grigio soffocante
            SkyBottom   = 0xFF3D4A3D,  // verde muschio scuro
            Cloud       = 0xFF4A5E4A,
            MountainFar = 0xFF1E2B1E,
            HillMid     = 0xFF253225,
            TreeNear    = 0xFF1A2A1A,  // alberi fitti scurissimi
            Foreground  = 0xFF111A11
        };

        public static readonly ZonePalette Drevath = new()
        {
            SkyTop      = 0xFF3D4A3A,  // grigio-verde morto
            SkyBottom   = 0xFF4A5545,  // opaco, senza profondità
            Cloud       = 0xFF5A6355,  // nuvole grigio-verdi ferme
            MountainFar = 0xFF2A3228,
            HillMid     = 0xFF323B30,
            TreeNear    = 0xFF252E23,  // steli cristallizzati
            Foreground  = 0xFF1A221A
        };

        public static readonly ZonePalette SetteDita = new()
        {
            SkyTop      = 0xFF3A3520,  // ocra scuro
            SkyBottom   = 0xFF5A5030,  // arancio bruciato spento
            Cloud       = 0xFF7A6A40,
            MountainFar = 0xFF2A2218,
            HillMid     = 0xFF3A3020,
            TreeNear    = 0xFF221A10,
            Foreground  = 0xFF180E08
        };

        public static readonly ZonePalette Monastero = new()
        {
            SkyTop      = 0xFF0A0A1A,  // notte profonda
            SkyBottom   = 0xFF12122A,  // blu notte
            Cloud       = 0xFF1A1A35,  // quasi invisibili
            MountainFar = 0xFF0E0E1E,
            HillMid     = 0xFF0A0A18,
            TreeNear    = 0xFF080810,
            Foreground  = 0xFF050508
        };

        public static readonly ZonePalette Vallata = new()
        {
            SkyTop      = 0xFF1A1A2A,  // cielo stretto, quasi buio
            SkyBottom   = 0xFF252535,
            Cloud       = 0xFF303040,
            MountainFar = 0xFF0D0D1A,  // pareti di roccia ai lati
            HillMid     = 0xFF0A0A15,
            TreeNear    = 0xFF080810,
            Foreground  = 0xFF050508
        };

        public static readonly ZonePalette MontagniCeneri = new()
        {
            SkyTop      = 0xFFCDD5DE,  // quasi bianco all'alba
            SkyBottom   = 0xFFE8EDF2,  // bianco ghiaccio
            Cloud       = 0xFFB0BEC8,  // grigio chiaro
            MountainFar = 0xFF4A5568,  // roccia grigio-scura
            HillMid     = 0xFF374151,
            TreeNear    = 0xFF252F3D,
            Foreground  = 0xFF1A2230
        };

        public static readonly ZonePalette Santuario = new()
        {
            SkyTop      = 0xFF05050F,  // notte stellata densa
            SkyBottom   = 0xFF0A0A1E,
            Cloud       = 0xFF101028,
            MountainFar = 0xFF080818,
            HillMid     = 0xFF060612,
            TreeNear    = 0xFF04040E,  // pietra antica
            Foreground  = 0xFF020208
        };

        public static readonly ZonePalette ForestaTranquilla = new()
        {
            SkyTop      = 0xFF1E3A5F,  // blu reale, primo cielo vero
            SkyBottom   = 0xFF2D5A8E,  // speranzoso
            Cloud       = 0xFF4A7AAB,
            MountainFar = 0xFF1A3320,
            HillMid     = 0xFF244D2A,
            TreeNear    = 0xFF1A3D20,  // verde che torna
            Foreground  = 0xFF122A18
        };

        /// <summary>Restituisce la palette per una zona.</summary>
        public static ZonePalette ForZone(ZoneType zone) => zone switch
        {
            ZoneType.Marshen           => Marshen,
            ZoneType.Ashgrove          => Ashgrove,
            ZoneType.Drevath           => Drevath,
            ZoneType.SetteDita         => SetteDita,
            ZoneType.Monastero         => Monastero,
            ZoneType.Vallata           => Vallata,
            ZoneType.MontagniCeneri    => MontagniCeneri,
            ZoneType.Santuario         => Santuario,
            ZoneType.ForestaTranquilla => ForestaTranquilla,
            _                          => Marshen
        };
    }

    // -------------------------------------------------------------------------
    //  LAYER PARALLAX
    // -------------------------------------------------------------------------

    /// <summary>
    /// Singolo layer del background parallax.
    /// </summary>
    public sealed class ParallaxLayer
    {
        /// <summary>
        /// Fattore di profondità (0.0 = fermo, 1.0 = segue la camera 1:1).
        /// </summary>
        public float Depth { get; init; }

        /// <summary>True se il layer usa una bitmap tileable dall'AssetManager.</summary>
        public bool IsBitmap { get; init; }

        /// <summary>Path asset bitmap (solo se IsBitmap = true).</summary>
        public string? AssetPath { get; init; }

        /// <summary>
        /// Altezza del layer in pixel logici (come frazione del viewport).
        /// Esempio: 0.4 = occupa il 40% inferiore dello schermo.
        /// </summary>
        public float HeightFraction { get; init; } = 1.0f;

        /// <summary>Offset Y dall'alto del viewport (0.0 = top, 1.0 = bottom).</summary>
        public float VerticalOffset { get; init; } = 0.0f;

        /// <summary>Colore ARGB per layer procedurali.</summary>
        public uint Color { get; set; }

        /// <summary>
        /// Dati procedurali delle silhouette (array di altezze normalizzate 0..1).
        /// Null per layer bitmap o cielo piatto.
        /// </summary>
        public float[]? SilhouetteHeights { get; set; }

        /// <summary>Larghezza in pixel logici del tile (per tiling infinito).</summary>
        public float TileWidth { get; set; } = 1280f;

        /// <summary>Offset di scroll corrente in pixel logici.</summary>
        public float ScrollOffset { get; set; } = 0f;
    }

    // -------------------------------------------------------------------------
    //  PARALLAX BACKGROUND
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gestisce lo stack di layer parallax per una micro area (side-scroll)
    /// o per la battle screen (sfondo statico della zona).
    ///
    /// GENERAZIONE PROCEDURALE:
    ///   Le silhouette di montagne, colline e alberi vengono generate
    ///   algoritmicamente con rumore di Perlin semplificato (value noise 1D).
    ///   Ogni zona ha un seed deterministico — lo stesso seed produce sempre
    ///   la stessa silhouette per quella zona.
    ///
    /// TILING INFINITO:
    ///   I layer bitmap vengono ripetuti orizzontalmente tramite modulo.
    ///   Il renderer disegna sempre almeno 2 copie affiancate per evitare
    ///   buchi ai bordi durante lo scroll.
    /// </summary>
    public sealed class ParallaxBackground
    {
        // ------------------------------------------------------------------
        //  Stato corrente
        // ------------------------------------------------------------------

        public ZoneType   CurrentZone    { get; private set; } = ZoneType.Marshen;
        public ZonePalette CurrentPalette { get; private set; } = ZonePalette.Marshen;

        private readonly List<ParallaxLayer> _layers = new();

        // ------------------------------------------------------------------
        //  Dimensioni viewport
        // ------------------------------------------------------------------

        private float _viewportWidth  = 1280f;
        private float _viewportHeight = 720f;

        // ------------------------------------------------------------------
        //  Costanti layer
        // ------------------------------------------------------------------

        private const int SILHOUETTE_RESOLUTION = 128;  // punti per silhouette

        // ------------------------------------------------------------------
        //  INIZIALIZZAZIONE
        // ------------------------------------------------------------------

        public ParallaxBackground(float viewportWidth = 1280f, float viewportHeight = 720f)
        {
            _viewportWidth  = viewportWidth;
            _viewportHeight = viewportHeight;
            SetZone(ZoneType.Marshen);
        }

        // ------------------------------------------------------------------
        //  CAMBIO ZONA
        // ------------------------------------------------------------------

        /// <summary>
        /// Imposta la zona e rigenera tutti i layer procedurali.
        /// Da chiamare da SceneManager ad ogni transizione di locazione.
        /// </summary>
        public void SetZone(ZoneType zone)
        {
            CurrentZone    = zone;
            CurrentPalette = ZonePalette.ForZone(zone);
            _layers.Clear();
            BuildLayers(zone);
        }

        /// <summary>Aggiorna le dimensioni del viewport (resize finestra Windows).</summary>
        public void SetViewport(float width, float height)
        {
            _viewportWidth  = width;
            _viewportHeight = height;
        }

        // ------------------------------------------------------------------
        //  UPDATE
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiorna gli offset di scroll in base alla posizione della camera.
        /// In CameraMode.Battle la camera è fissa — gli offset restano a 0
        /// e lo sfondo è statico.
        /// </summary>
        public void Update(float deltaTime, Camera2D camera)
        {
            foreach (var layer in _layers)
            {
                // L'offset è calcolato direttamente dalla posizione camera
                // scalata per il fattore di profondità del layer.
                // Questo approccio è frame-rate independent per definizione.
                layer.ScrollOffset = camera.GetParallaxOffsetX(layer.Depth);
            }
        }

        // ------------------------------------------------------------------
        //  RENDER DATA — letto dal renderer
        // ------------------------------------------------------------------

        /// <summary>
        /// Restituisce la lista di layer da disegnare, in ordine dal più
        /// lontano al più vicino. Il renderer itera questa lista e disegna
        /// ciascun layer con il proprio offset e colore.
        /// </summary>
        public IReadOnlyList<ParallaxLayer> Layers => _layers;

        /// <summary>
        /// Calcola la posizione X di disegno di un layer con tiling infinito.
        /// Il renderer deve disegnare il tile anche nella posizione X + TileWidth
        /// per evitare buchi durante lo scroll.
        /// </summary>
        public float GetLayerDrawX(ParallaxLayer layer)
        {
            float offset = layer.ScrollOffset % layer.TileWidth;
            return -offset;
        }

        // ------------------------------------------------------------------
        //  COSTRUZIONE LAYER PER ZONA
        // ------------------------------------------------------------------

        private void BuildLayers(ZoneType zone)
        {
            var p    = CurrentPalette;
            int seed = (int)zone * 1337;

            // Layer 0 — Cielo (procedurale, gradiente, depth 0)
            _layers.Add(new ParallaxLayer
            {
                Depth          = 0.00f,
                IsBitmap       = false,
                Color          = p.SkyTop,
                HeightFraction = 1.0f,
                VerticalOffset = 0.0f,
                TileWidth      = _viewportWidth
            });

            // Layer 1 — Nuvole / nebbia (procedurale, depth 0.05)
            _layers.Add(new ParallaxLayer
            {
                Depth             = 0.05f,
                IsBitmap          = false,
                Color             = p.Cloud,
                HeightFraction    = 0.25f,
                VerticalOffset    = 0.05f,
                TileWidth         = _viewportWidth * 2f,
                SilhouetteHeights = GenerateCloudSilhouette(seed + 1, zone)
            });

            // Layer 2 — Montagne lontane (procedurale, depth 0.15)
            _layers.Add(new ParallaxLayer
            {
                Depth             = 0.15f,
                IsBitmap          = false,
                Color             = p.MountainFar,
                HeightFraction    = 0.45f,
                VerticalOffset    = 0.20f,
                TileWidth         = _viewportWidth * 1.5f,
                SilhouetteHeights = GenerateMountainSilhouette(seed + 2, zone)
            });

            // Layer 3 — Colline medie (procedurale, depth 0.30)
            _layers.Add(new ParallaxLayer
            {
                Depth             = 0.30f,
                IsBitmap          = false,
                Color             = p.HillMid,
                HeightFraction    = 0.35f,
                VerticalOffset    = 0.45f,
                TileWidth         = _viewportWidth * 1.2f,
                SilhouetteHeights = GenerateHillSilhouette(seed + 3, zone)
            });

            // Layer 4 — Alberi / strutture (procedurale o bitmap, depth 0.55)
            var treeLayer = new ParallaxLayer
            {
                Depth          = 0.55f,
                HeightFraction = 0.30f,
                VerticalOffset = 0.60f,
                TileWidth      = _viewportWidth
            };

            // Alcune zone usano bitmap per gli alberi se disponibili
            string? treeBitmap = GetTreeBitmapPath(zone);
            if (treeBitmap != null)
            {
                treeLayer.IsBitmap  = true;
                treeLayer.AssetPath = treeBitmap;
            }
            else
            {
                treeLayer.IsBitmap          = false;
                treeLayer.Color             = p.TreeNear;
                treeLayer.SilhouetteHeights = GenerateTreeSilhouette(seed + 4, zone);
            }
            _layers.Add(treeLayer);

            // Layer 5 — Primo piano (procedurale, depth 0.80)
            _layers.Add(new ParallaxLayer
            {
                Depth             = 0.80f,
                IsBitmap          = false,
                Color             = p.Foreground,
                HeightFraction    = 0.12f,
                VerticalOffset    = 0.88f,
                TileWidth         = _viewportWidth * 0.8f,
                SilhouetteHeights = GenerateForegroundSilhouette(seed + 5, zone)
            });
        }

        // ------------------------------------------------------------------
        //  GENERAZIONE SILHOUETTE PROCEDURALE
        //  Value noise 1D deterministico (nessuna dipendenza esterna).
        // ------------------------------------------------------------------

        /// <summary>
        /// Genera un array di altezze normalizzate (0..1) per una silhouette.
        /// Usa value noise 1D con interpolazione coseno per smoothness.
        /// </summary>
        private static float[] GenerateSilhouette(
            int   seed,
            int   resolution,
            float minHeight,
            float maxHeight,
            int   octaves    = 3,
            float roughness  = 0.5f)
        {
            var heights = new float[resolution];
            var rng     = new Random(seed);

            // Genera punti di controllo per ogni ottava
            for (int oct = 0; oct < octaves; oct++)
            {
                float amplitude = MathF.Pow(roughness, oct);
                int   frequency = 1 << oct;  // 1, 2, 4, 8...
                int   points    = frequency + 1;

                var control = new float[points];
                for (int i = 0; i < points; i++)
                    control[i] = (float)rng.NextDouble();

                // Interpola per ogni pixel della risoluzione
                for (int x = 0; x < resolution; x++)
                {
                    float t      = (float)x / resolution * frequency;
                    int   idx0   = (int)t;
                    int   idx1   = Math.Min(idx0 + 1, points - 1);
                    float frac   = t - idx0;

                    // Interpolazione coseno (più smooth del lineare)
                    float smooth = (1f - MathF.Cos(frac * MathF.PI)) * 0.5f;
                    heights[x]  += Lerp(control[idx0], control[idx1], smooth) * amplitude;
                }
            }

            // Normalizza e scala nell'intervallo [minHeight, maxHeight]
            float hMin = float.MaxValue, hMax = float.MinValue;
            foreach (var h in heights) { hMin = MathF.Min(hMin, h); hMax = MathF.Max(hMax, h); }
            float range = hMax - hMin;
            if (range < 0.001f) range = 1f;

            for (int i = 0; i < resolution; i++)
                heights[i] = minHeight + ((heights[i] - hMin) / range) * (maxHeight - minHeight);

            return heights;
        }

        private static float[] GenerateCloudSilhouette(int seed, ZoneType zone)
        {
            // Le nuvole di Drevath sono piatte e ferme (Oscurità blocca il vento)
            float maxH = zone == ZoneType.Drevath ? 0.3f : 0.6f;
            return GenerateSilhouette(seed, SILHOUETTE_RESOLUTION,
                minHeight: 0.1f, maxHeight: maxH, octaves: 4, roughness: 0.6f);
        }

        private static float[] GenerateMountainSilhouette(int seed, ZoneType zone)
        {
            // Montagne Ceneri: picchi alti e verticali
            float maxH = zone == ZoneType.MontagniCeneri ? 0.95f : 0.70f;
            float rough = zone == ZoneType.MontagniCeneri ? 0.7f : 0.5f;
            return GenerateSilhouette(seed, SILHOUETTE_RESOLUTION,
                minHeight: 0.30f, maxHeight: maxH, octaves: 5, roughness: rough);
        }

        private static float[] GenerateHillSilhouette(int seed, ZoneType zone)
        {
            return GenerateSilhouette(seed, SILHOUETTE_RESOLUTION,
                minHeight: 0.15f, maxHeight: 0.55f, octaves: 3, roughness: 0.45f);
        }

        private static float[] GenerateTreeSilhouette(int seed, ZoneType zone)
        {
            // Drevath: steli cristallizzati dritti (roughness bassa = regolare)
            float rough = zone == ZoneType.Drevath ? 0.2f : 0.65f;
            return GenerateSilhouette(seed, SILHOUETTE_RESOLUTION,
                minHeight: 0.40f, maxHeight: 0.85f, octaves: 4, roughness: rough);
        }

        private static float[] GenerateForegroundSilhouette(int seed, ZoneType zone)
        {
            return GenerateSilhouette(seed, SILHOUETTE_RESOLUTION,
                minHeight: 0.60f, maxHeight: 1.0f, octaves: 2, roughness: 0.4f);
        }

        // ------------------------------------------------------------------
        //  PATH BITMAP OPZIONALI
        // ------------------------------------------------------------------

        /// <summary>
        /// Restituisce il path del bitmap layer alberi per una zona,
        /// o null se la zona usa silhouette procedurali.
        /// Aggiornare con i path degli asset reali quando disponibili.
        /// </summary>
        private static string? GetTreeBitmapPath(ZoneType zone) => zone switch
        {
            ZoneType.Ashgrove         => "Backgrounds/ashgrove_trees.png",
            ZoneType.ForestaTranquilla => "Backgrounds/forest_trees.png",
            _                          => null   // procedurale per tutte le altre
        };

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        private static float Lerp(float a, float b, float t)
            => a + (b - a) * t;

        // ------------------------------------------------------------------
        //  GRADIENTE CIELO — dati per il renderer
        // ------------------------------------------------------------------

        /// <summary>
        /// Dati del gradiente cielo per la zona corrente.
        /// Il renderer usa questi colori per disegnare il gradiente verticale
        /// del Layer 0 (cielo).
        /// </summary>
        public (uint Top, uint Bottom) GetSkyGradient()
            => (CurrentPalette.SkyTop, CurrentPalette.SkyBottom);

        /// <summary>
        /// Colori per le stelle nel Santuario e nel Monastero.
        /// Ritorna un array di posizioni (x, y) normalizzate 0..1
        /// se la zona ha un cielo stellato, altrimenti array vuoto.
        /// </summary>
        public (float X, float Y, float Brightness)[] GetStars()
        {
            if (CurrentZone != ZoneType.Santuario &&
                CurrentZone != ZoneType.Monastero)
                return Array.Empty<(float, float, float)>();

            var rng   = new Random((int)CurrentZone * 9999);
            int count = CurrentZone == ZoneType.Santuario ? 80 : 50;
            var stars = new (float, float, float)[count];

            for (int i = 0; i < count; i++)
            {
                stars[i] = (
                    (float)rng.NextDouble(),
                    (float)rng.NextDouble() * 0.6f,  // solo metà superiore
                    0.4f + (float)rng.NextDouble() * 0.6f
                );
            }

            return stars;
        }

        /// <summary>
        /// Restituisce le posizioni (x normalizzata) delle cinque luci dei Sigilli
        /// nel Santuario. Usate dal renderer come fonti di luce ambientale.
        /// </summary>
        public float[] GetSigilLightPositions()
        {
            if (CurrentZone != ZoneType.Santuario)
                return Array.Empty<float>();

            // Cinque Sigilli distribuiti sulla parete pentagonale
            return new[] { 0.10f, 0.28f, 0.50f, 0.72f, 0.90f };
        }
    }
}
