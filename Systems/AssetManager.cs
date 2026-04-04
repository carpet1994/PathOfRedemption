// =============================================================================
//  La Via della Redenzione — Systems/AssetManager.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Singleton per il caricamento e la cache degli asset grafici
//                (sprite sheet, sfondi, UI). Gestisce:
//
//    - Cache in-memoria con LRU eviction (limite 50MB)
//    - Caricamento asincrono su thread secondario
//    - Placeholder bitmap procedurale quando l'asset non è disponibile
//    - Pre-caricamento della scena successiva in background
//    - Scaricamento esplicito delle locazioni non più raggiungibili
//
//  Percorsi asset:
//    Tutti i path sono relativi ad /Assets/ nella root del progetto MAUI.
//    Esempio: "Characters/kael_sheet.png" → /Assets/Characters/kael_sheet.png
//
//  Placeholder:
//    Se un asset non viene trovato (sviluppo, asset mancante), viene
//    restituita una bitmap procedurale colorata con le dimensioni richieste.
//    Colori placeholder per categoria:
//      Personaggi  → viola  (#8B5CF6)
//      Nemici      → rosso  (#EF4444)
//      Sfondi      → grigio (#374151)
//      UI          → blu    (#3B82F6)
//    In build Release, un asset mancante logga un errore invece di crashare.
//
//  Thread safety:
//    Il dizionario cache è protetto da lock.
//    I callback OnLoaded vengono invocati sul thread chiamante del Load().
//    Per aggiornare UI MAUI usare MainThread.BeginInvokeOnMainThread().
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LaViaDellaRedenzione.Systems
{
    // -------------------------------------------------------------------------
    //  CATEGORIA ASSET — per placeholder colorati e logging
    // -------------------------------------------------------------------------

    public enum AssetCategory
    {
        Character,
        Enemy,
        Background,
        UI,
        Unknown
    }

    // -------------------------------------------------------------------------
    //  ENTRY CACHE LRU
    // -------------------------------------------------------------------------

    /// <summary>
    /// Singola voce nella cache LRU.
    /// Traccia la dimensione in byte e il timestamp dell'ultimo accesso.
    /// </summary>
    internal sealed class CacheEntry
    {
        /// <summary>Dati grezzi dell'immagine (PNG/JPEG decodificato).</summary>
        public byte[] Data         { get; }

        /// <summary>Dimensione in byte dell'asset caricato.</summary>
        public long   SizeBytes    { get; }

        /// <summary>Timestamp ultimo accesso (per LRU eviction).</summary>
        public long   LastAccessTick { get; set; }

        /// <summary>Larghezza in pixel (se nota).</summary>
        public int    Width        { get; set; }

        /// <summary>Altezza in pixel (se nota).</summary>
        public int    Height       { get; set; }

        public CacheEntry(byte[] data, int width = 0, int height = 0)
        {
            Data            = data;
            SizeBytes       = data.LongLength;
            Width           = width;
            Height          = height;
            LastAccessTick  = DateTime.UtcNow.Ticks;
        }
    }

    // -------------------------------------------------------------------------
    //  ASSET MANAGER
    // -------------------------------------------------------------------------

    /// <summary>
    /// Singleton. Punto unico di accesso a tutti gli asset grafici del gioco.
    ///
    /// UTILIZZO TIPICO:
    ///   // Sincrono (da GameLoop, asset già in cache):
    ///   var data = AssetManager.Instance.Get("Characters/kael_sheet.png");
    ///
    ///   // Asincrono (pre-caricamento):
    ///   await AssetManager.Instance.LoadAsync("Backgrounds/marshen_bg.png");
    ///
    ///   // Pre-carica la prossima locazione in background:
    ///   AssetManager.Instance.PreloadLocation("LOC_FORESTA_ASHGROVE");
    ///
    ///   // Scarica asset non più usati:
    ///   AssetManager.Instance.UnloadLocation("LOC_MARSHEN");
    /// </summary>
    public sealed class AssetManager
    {
        // ------------------------------------------------------------------
        //  Singleton
        // ------------------------------------------------------------------

        private static AssetManager? _instance;
        public static AssetManager Instance => _instance ??= new AssetManager();
        private AssetManager() { }

        // ------------------------------------------------------------------
        //  Configurazione cache
        // ------------------------------------------------------------------

        /// <summary>Limite massimo cache in byte (50 MB).</summary>
        private const long MAX_CACHE_BYTES = 50L * 1024L * 1024L;

        /// <summary>Dimensione placeholder in pixel quando width/height non specificati.</summary>
        private const int PLACEHOLDER_SIZE = 64;

        // ------------------------------------------------------------------
        //  Cache LRU
        // ------------------------------------------------------------------

        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly object _cacheLock = new();
        private long _currentCacheBytes = 0L;

        // ------------------------------------------------------------------
        //  Mappa locazione → lista asset (per UnloadLocation)
        // ------------------------------------------------------------------

        private readonly Dictionary<string, List<string>> _locationAssets = new();

        // ------------------------------------------------------------------
        //  Placeholder colorati per categoria
        // ------------------------------------------------------------------

        private static readonly Dictionary<AssetCategory, byte[]> _placeholders = new();
        private static readonly object _placeholderLock = new();

        // ------------------------------------------------------------------
        //  STATISTICHE (debug)
        // ------------------------------------------------------------------

        public long   CacheUsedBytes  => _currentCacheBytes;
        public int    CachedAssets    { get { lock (_cacheLock) return _cache.Count; } }
        public int    CacheHits       { get; private set; }
        public int    CacheMisses     { get; private set; }

        // ------------------------------------------------------------------
        //  GET SINCRONO — usato dal renderer ogni frame
        // ------------------------------------------------------------------

        /// <summary>
        /// Restituisce i byte dell'asset dalla cache.
        /// Se non è in cache, carica dal disco in modo sincrono (bloccante).
        /// Per asset critici (sprite in battaglia) preferire LoadAsync() in anticipo.
        /// </summary>
        /// <param name="path">Path relativo ad /Assets/.</param>
        /// <param name="category">Categoria per il placeholder colorato.</param>
        /// <returns>Byte array dell'immagine, mai null.</returns>
        public byte[] Get(string path, AssetCategory category = AssetCategory.Unknown)
        {
            path = NormalizePath(path);

            lock (_cacheLock)
            {
                if (_cache.TryGetValue(path, out var entry))
                {
                    entry.LastAccessTick = DateTime.UtcNow.Ticks;
                    CacheHits++;
                    return entry.Data;
                }
            }

            CacheMisses++;

            // Carica sincrono dal disco
            var data = LoadFromDisk(path);
            if (data == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AssetManager] Asset non trovato: {path} — uso placeholder.");
                data = GetPlaceholder(category);
            }

            AddToCache(path, data);
            return data;
        }

        // ------------------------------------------------------------------
        //  LOAD ASINCRONO — pre-caricamento
        // ------------------------------------------------------------------

        /// <summary>
        /// Carica un asset in cache in modo asincrono senza bloccare il GameLoop.
        /// Se l'asset è già in cache, ritorna immediatamente.
        /// </summary>
        public async Task LoadAsync(
            string path,
            AssetCategory category = AssetCategory.Unknown,
            CancellationToken ct   = default)
        {
            path = NormalizePath(path);

            lock (_cacheLock)
            {
                if (_cache.ContainsKey(path)) return;
            }

            var data = await Task.Run(() => LoadFromDisk(path), ct);

            if (ct.IsCancellationRequested) return;

            if (data == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AssetManager] LoadAsync: asset non trovato: {path}");
                data = GetPlaceholder(category);
            }

            AddToCache(path, data);
        }

        /// <summary>
        /// Pre-carica una lista di asset in parallelo.
        /// Usato da LoadingScreen per caricare tutti gli asset di una scena.
        /// </summary>
        /// <param name="paths">Lista di path da caricare.</param>
        /// <param name="onProgress">
        /// Callback opzionale con progresso 0.0..1.0.
        /// Chiamato sul thread del Task — usare MainThread per UI.
        /// </param>
        public async Task LoadBatchAsync(
            IReadOnlyList<string>    paths,
            Action<float>?           onProgress = null,
            CancellationToken        ct         = default)
        {
            if (paths == null || paths.Count == 0) return;

            int completed = 0;
            int total     = paths.Count;

            // Carica in parallelo con grado di parallelismo limitato a 4
            // per non saturare l'I/O su Android
            var semaphore = new SemaphoreSlim(4);
            var tasks = new List<Task>(total);

            foreach (var path in paths)
            {
                await semaphore.WaitAsync(ct);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await LoadAsync(path, AssetCategory.Unknown, ct);
                    }
                    finally
                    {
                        semaphore.Release();
                        int done = Interlocked.Increment(ref completed);
                        onProgress?.Invoke((float)done / total);
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);
        }

        // ------------------------------------------------------------------
        //  PRE-CARICAMENTO PER LOCAZIONE
        // ------------------------------------------------------------------

        /// <summary>
        /// Pre-carica tutti gli asset associati a una locazione in background.
        /// Chiamato da WorldMapSystem quando il giocatore si avvicina a un nodo.
        /// </summary>
        /// <param name="locationId">ID del LocationNode (es. "LOC_MARSHEN").</param>
        public void PreloadLocation(string locationId)
        {
            if (!_locationAssets.TryGetValue(locationId, out var assets)) return;
            if (assets.Count == 0) return;

            // Fire-and-forget in background — non blocca il GameLoop
            _ = LoadBatchAsync(assets);
        }

        /// <summary>
        /// Registra la lista di asset che appartengono a una locazione.
        /// Chiamato da WorldMapData al caricamento del gioco.
        /// </summary>
        public void RegisterLocationAssets(string locationId, IEnumerable<string> assetPaths)
        {
            _locationAssets[locationId] = new List<string>(assetPaths);
        }

        // ------------------------------------------------------------------
        //  SCARICAMENTO ESPLICITO
        // ------------------------------------------------------------------

        /// <summary>
        /// Rimuove dalla cache tutti gli asset di una locazione non più
        /// raggiungibile, liberando memoria su Android.
        /// </summary>
        public void UnloadLocation(string locationId)
        {
            if (!_locationAssets.TryGetValue(locationId, out var assets)) return;

            lock (_cacheLock)
            {
                foreach (var path in assets)
                {
                    var normalized = NormalizePath(path);
                    if (_cache.TryGetValue(normalized, out var entry))
                    {
                        _currentCacheBytes -= entry.SizeBytes;
                        _cache.Remove(normalized);
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"[AssetManager] Scaricata locazione: {locationId}. " +
                $"Cache: {_currentCacheBytes / 1024 / 1024} MB");
        }

        /// <summary>
        /// Rimuove un singolo asset dalla cache.
        /// </summary>
        public void Unload(string path)
        {
            path = NormalizePath(path);
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(path, out var entry))
                {
                    _currentCacheBytes -= entry.SizeBytes;
                    _cache.Remove(path);
                }
            }
        }

        /// <summary>
        /// Svuota completamente la cache.
        /// Da chiamare solo al reset completo del gioco.
        /// </summary>
        public void UnloadAll()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
                _currentCacheBytes = 0;
            }
        }

        // ------------------------------------------------------------------
        //  LRU EVICTION
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiunge un asset alla cache, eseguendo eviction LRU se necessario.
        /// </summary>
        private void AddToCache(string path, byte[] data)
        {
            var entry = new CacheEntry(data);

            lock (_cacheLock)
            {
                // Eviction LRU finché c'è spazio
                while (_currentCacheBytes + entry.SizeBytes > MAX_CACHE_BYTES
                       && _cache.Count > 0)
                {
                    EvictLeastRecentlyUsed();
                }

                _cache[path]        = entry;
                _currentCacheBytes += entry.SizeBytes;
            }
        }

        /// <summary>
        /// Rimuove l'entry con il LastAccessTick più vecchio.
        /// Chiamato con _cacheLock già acquisito.
        /// </summary>
        private void EvictLeastRecentlyUsed()
        {
            string? oldest    = null;
            long    oldestTick = long.MaxValue;

            foreach (var kvp in _cache)
            {
                if (kvp.Value.LastAccessTick < oldestTick)
                {
                    oldestTick = kvp.Value.LastAccessTick;
                    oldest     = kvp.Key;
                }
            }

            if (oldest != null)
            {
                _currentCacheBytes -= _cache[oldest].SizeBytes;
                _cache.Remove(oldest);

                System.Diagnostics.Debug.WriteLine(
                    $"[AssetManager] LRU eviction: {oldest}");
            }
        }

        // ------------------------------------------------------------------
        //  CARICAMENTO DA DISCO
        // ------------------------------------------------------------------

        /// <summary>
        /// Legge un asset dal filesystem MAUI.
        /// Su Android legge dagli asset APK via FileSystem.OpenAppPackageFileAsync.
        /// Su Windows legge dal filesystem normale.
        /// Ritorna null se il file non esiste.
        /// </summary>
        private byte[]? LoadFromDisk(string path)
        {
            try
            {
                // MAUI: FileSystem.OpenAppPackageFileAsync apre file negli asset
                // del pacchetto sia su Android che su Windows
                using var stream = FileSystem.OpenAppPackageFileAsync(path)
                                             .GetAwaiter()
                                             .GetResult();

                if (stream == null) return null;

                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AssetManager] Errore caricamento {path}: {ex.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------
        //  PLACEHOLDER PROCEDURALE
        // ------------------------------------------------------------------

        /// <summary>
        /// Restituisce una bitmap placeholder colorata per categoria.
        /// Generata una sola volta e riutilizzata (lazy singleton per categoria).
        ///
        /// Il placeholder è un PNG 64x64 monocromatico generato in memoria
        /// senza dipendenze esterne — funziona anche su Android senza accesso
        /// al filesystem.
        /// </summary>
        private static byte[] GetPlaceholder(AssetCategory category)
        {
            lock (_placeholderLock)
            {
                if (_placeholders.TryGetValue(category, out var existing))
                    return existing;

                // Seleziona colore per categoria
                (byte r, byte g, byte b) color = category switch
                {
                    AssetCategory.Character  => (0x8B, 0x5C, 0xF6),  // viola
                    AssetCategory.Enemy      => (0xEF, 0x44, 0x44),  // rosso
                    AssetCategory.Background => (0x37, 0x41, 0x51),  // grigio scuro
                    AssetCategory.UI         => (0x3B, 0x82, 0xF6),  // blu
                    _                        => (0x6B, 0x72, 0x80),  // grigio neutro
                };

                var png = GenerateSolidColorPng(
                    PLACEHOLDER_SIZE, PLACEHOLDER_SIZE,
                    color.r, color.g, color.b);

                _placeholders[category] = png;
                return png;
            }
        }

        /// <summary>
        /// Genera un PNG minimale a colore solido senza librerie esterne.
        /// Implementa la struttura PNG minima: signature + IHDR + IDAT + IEND.
        /// </summary>
        private static byte[] GenerateSolidColorPng(
            int width, int height,
            byte r, byte g, byte b)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // PNG signature
            bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            // IHDR chunk
            WriteChunk(bw, "IHDR", chunk =>
            {
                WriteInt32BE(chunk, width);
                WriteInt32BE(chunk, height);
                chunk.WriteByte(8);  // bit depth
                chunk.WriteByte(2);  // color type RGB
                chunk.WriteByte(0);  // compression
                chunk.WriteByte(0);  // filter
                chunk.WriteByte(0);  // interlace
            });

            // IDAT chunk — dati immagine compressi (zlib)
            WriteChunk(bw, "IDAT", chunk =>
            {
                // Costruisce i dati raw (filter byte 0 + RGB per ogni pixel)
                var raw = new MemoryStream();
                for (int y = 0; y < height; y++)
                {
                    raw.WriteByte(0); // filter type None
                    for (int x = 0; x < width; x++)
                    {
                        raw.WriteByte(r);
                        raw.WriteByte(g);
                        raw.WriteByte(b);
                    }
                }

                // Compressione zlib deflate
                var rawBytes = raw.ToArray();
                var compressed = Deflate(rawBytes);
                chunk.Write(compressed, 0, compressed.Length);
            });

            // IEND chunk
            WriteChunk(bw, "IEND", _ => { });

            return ms.ToArray();
        }

        private static void WriteChunk(
            BinaryWriter outer,
            string type,
            Action<MemoryStream> writeData)
        {
            using var data = new MemoryStream();
            writeData(data);
            var bytes = data.ToArray();

            // Length
            WriteInt32BE(outer, bytes.Length);

            // Type + Data
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            outer.Write(typeBytes);
            outer.Write(bytes);

            // CRC32 di type + data
            var crcInput = new byte[typeBytes.Length + bytes.Length];
            Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
            Buffer.BlockCopy(bytes, 0, crcInput, typeBytes.Length, bytes.Length);
            WriteInt32BE(outer, Crc32(crcInput));
        }

        private static void WriteInt32BE(BinaryWriter bw, int value)
        {
            bw.Write((byte)(value >> 24));
            bw.Write((byte)(value >> 16));
            bw.Write((byte)(value >>  8));
            bw.Write((byte)(value >>  0));
        }

        private static void WriteInt32BE(MemoryStream ms, int value)
        {
            ms.WriteByte((byte)(value >> 24));
            ms.WriteByte((byte)(value >> 16));
            ms.WriteByte((byte)(value >>  8));
            ms.WriteByte((byte)(value >>  0));
        }

        /// <summary>Compressione zlib/deflate minimale per il placeholder PNG.</summary>
        private static byte[] Deflate(byte[] data)
        {
            using var output = new MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(
                output, System.IO.Compression.CompressionLevel.Fastest))
            {
                deflate.Write(data, 0, data.Length);
            }

            // Avvolge in zlib header (CM=8, CINFO=7, FCHECK calcolato)
            var deflated  = output.ToArray();
            var zlib      = new byte[deflated.Length + 6];
            zlib[0] = 0x78; // CMF: deflate, window=32KB
            zlib[1] = 0x9C; // FLG: default compression
            Buffer.BlockCopy(deflated, 0, zlib, 2, deflated.Length);

            // Adler-32 checksum
            uint adler = Adler32(data);
            int  tail  = deflated.Length + 2;
            zlib[tail + 0] = (byte)(adler >> 24);
            zlib[tail + 1] = (byte)(adler >> 16);
            zlib[tail + 2] = (byte)(adler >>  8);
            zlib[tail + 3] = (byte)(adler >>  0);

            return zlib;
        }

        private static uint Adler32(byte[] data)
        {
            uint s1 = 1, s2 = 0;
            foreach (byte b in data)
            {
                s1 = (s1 + b)  % 65521;
                s2 = (s2 + s1) % 65521;
            }
            return (s2 << 16) | s1;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0
                        ? (crc >> 1) ^ 0xEDB88320
                        : crc >> 1;
            }
            return ~crc;
        }

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        private static string NormalizePath(string path)
            => path.Replace('\\', '/').TrimStart('/');
    }
}
