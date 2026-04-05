// =============================================================================
//  La Via della Redenzione — Systems/SaveSystem.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Sistema di salvataggio con 3 slot, autosave, versioning
//                e migrazione dati. Serializzazione JSON via Newtonsoft.Json.
//
//  Percorsi file:
//    Android : FileSystem.AppDataDirectory/saves/slot_N.json
//    Windows : %APPDATA%\LaViaDellaRedenzione\saves\slot_N.json
//
//  Versioning:
//    Ogni SaveData ha un campo SaveVersion (int). Al caricamento, se la
//    versione è inferiore all'attuale, Migrate() aggiorna i dati senza
//    perdere la partita. Versione corrente: CURRENT_SAVE_VERSION = 1.
//
//  Icona salvataggio:
//    OnSaveStarted  → GameManager mostra icona floppy disk in alto a destra
//    OnSaveComplete → GameManager nasconde l'icona dopo 2 secondi
//    OnSaveFailed   → GameManager mostra icona di errore
//
//  Autosave:
//    Scatta ad ogni cambio di schermata (GameStateManager.OnStateChanged)
//    e dopo ogni battaglia (BattleEndedEvent). Usa sempre lo slot attivo.
//    In modalità Racconto (difficoltà Story) l'autosave è più frequente.
// =============================================================================

using LaViaDellaRedenzione.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LaViaDellaRedenzione.Systems
{
    // =========================================================================
    //  SAVE DATA — struttura dati serializzata
    // =========================================================================

    /// <summary>
    /// Dati completi di una partita salvata.
    /// Tutti i campi devono avere valori di default validi per supportare
    /// la migrazione da versioni precedenti.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        // ------------------------------------------------------------------
        //  Metadati
        // ------------------------------------------------------------------

        /// <summary>Versione del formato di salvataggio. Attuale: 1.</summary>
        public int SaveVersion { get; set; } = SaveSystem.CURRENT_SAVE_VERSION;

        /// <summary>Timestamp dell'ultimo salvataggio (UTC).</summary>
        public DateTime LastSaved { get; set; } = DateTime.UtcNow;

        /// <summary>Ore totali di gioco in secondi (per display "12h 34m").</summary>
        public float PlaytimeSeconds { get; set; } = 0f;

        /// <summary>Capitolo attuale (1-12).</summary>
        public int CurrentChapter { get; set; } = 1;

        /// <summary>ID della locazione corrente (es. "LOC_MARSHEN_LOCANDA").</summary>
        public string CurrentLocationId { get; set; } = "LOC_MARSHEN";

        /// <summary>True se questa partita è in modalità New Game Plus.</summary>
        public bool IsNewGamePlus { get; set; } = false;

        /// <summary>True se la partita principale è stata completata.</summary>
        public bool GameCompleted { get; set; } = false;

        /// <summary>Livello di difficoltà selezionato.</summary>
        public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Viandante;

        // ------------------------------------------------------------------
        //  Stato personaggi
        // ------------------------------------------------------------------

        /// <summary>Stato di ogni personaggio giocabile indicizzato per CharacterId.</summary>
        public Dictionary<string, CharacterSaveData> Characters { get; set; } = new()
        {
            ["KAEL"]  = new CharacterSaveData { CharacterId = "KAEL",  Level = 1 },
            ["LYRA"]  = new CharacterSaveData { CharacterId = "LYRA",  Level = 1 },
            ["VORAN"] = new CharacterSaveData { CharacterId = "VORAN", Level = 1 },
            ["SERA"]  = new CharacterSaveData { CharacterId = "SERA",  Level = 1 }
        };

        /// <summary>Personaggi attualmente nel gruppo attivo (lista di CharacterId).</summary>
        public List<string> ActiveParty { get; set; } = new() { "KAEL" };

        // ------------------------------------------------------------------
        //  Inventario
        // ------------------------------------------------------------------

        /// <summary>
        /// Inventario carte: mappa CardId → quantità posseduta.
        /// </summary>
        public Dictionary<string, int> CardInventory { get; set; } = new();

        /// <summary>
        /// Inventario oggetti consumabili: mappa ItemId → quantità.
        /// </summary>
        public Dictionary<string, int> ItemInventory { get; set; } = new();

        /// <summary>Oro posseduto.</summary>
        public int Gold { get; set; } = 0;

        // ------------------------------------------------------------------
        //  Flags di trama
        // ------------------------------------------------------------------

        /// <summary>
        /// Dizionario flag narrativi.
        /// Chiave: nome flag (es. "kael_told_lyra_greypass").
        /// Valore: true/false.
        /// </summary>
        public Dictionary<string, bool> StoryFlags { get; set; } = new();

        /// <summary>
        /// Variabili numeriche di stato mondo.
        /// Chiave: nome variabile (es. "oscurita_livello_drevath").
        /// Valore: intero.
        /// </summary>
        public Dictionary<string, int> WorldVariables { get; set; } = new();

        /// <summary>Lista di StoryEvent già completati.</summary>
        public List<string> CompletedEvents { get; set; } = new();

        // ------------------------------------------------------------------
        //  Achievement e statistiche
        // ------------------------------------------------------------------

        /// <summary>Achievement sbloccati (lista di achievement ID).</summary>
        public List<string> UnlockedAchievements { get; set; } = new();

        /// <summary>Contatori achievement (es. "battles_won" → 47).</summary>
        public Dictionary<string, int> AchievementCounters { get; set; } = new();

        // ------------------------------------------------------------------
        //  Bestiario
        // ------------------------------------------------------------------

        /// <summary>
        /// Nemici incontrati: mappa EnemyId → numero di incontri.
        /// </summary>
        public Dictionary<string, int> BestiaryEncounters { get; set; } = new();

        /// <summary>
        /// Nemici sconfitti: mappa EnemyId → numero di sconfitte.
        /// </summary>
        public Dictionary<string, int> BestiaryDefeats { get; set; } = new();

        // ------------------------------------------------------------------
        //  Tutorial
        // ------------------------------------------------------------------

        /// <summary>Tutorial già mostrati (lista di tutorial ID).</summary>
        public List<string> ShownTutorials { get; set; } = new();

        // ------------------------------------------------------------------
        //  Morale (Kael — valore separato per accesso rapido)
        // ------------------------------------------------------------------

        public int KaelMorale { get; set; } = 100;
    }

    // -------------------------------------------------------------------------
    //  CHARACTER SAVE DATA
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stato salvato di un singolo personaggio.
    /// </summary>
    [Serializable]
    public sealed class CharacterSaveData
    {
        public string CharacterId { get; set; } = string.Empty;
        public int    Level       { get; set; } = 1;
        public int    Experience  { get; set; } = 0;
        public int    CurrentHP   { get; set; } = -1;  // -1 = HP massimi
        public int    CurrentSP   { get; set; } = -1;  // -1 = SP massimi

        /// <summary>IDs delle carte nel mazzo attivo (8 slot).</summary>
        public List<string> ActiveDeck { get; set; } = new();

        /// <summary>Skin della carta attiva per questo personaggio.</summary>
        public string ActiveCardSkin { get; set; } = "Classica";
    }

    // -------------------------------------------------------------------------
    //  SAVE SLOT PREVIEW — anteprima per il menu di selezione slot
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dati leggeri per mostrare l'anteprima di uno slot nel menu principale.
    /// Caricati senza deserializzare l'intero SaveData.
    /// </summary>
    public sealed class SaveSlotPreview
    {
        public int      SlotIndex      { get; set; }
        public bool     IsEmpty        { get; set; }
        public string   LocationName   { get; set; } = string.Empty;
        public int      Chapter        { get; set; }
        public float    PlaytimeSeconds { get; set; }
        public DateTime LastSaved      { get; set; }
        public bool     IsNewGamePlus  { get; set; }
        public DifficultyLevel Difficulty { get; set; }

        /// <summary>Ore e minuti formattati per il display (es. "12h 34m").</summary>
        public string PlaytimeFormatted
        {
            get
            {
                int hours   = (int)(PlaytimeSeconds / 3600);
                int minutes = (int)(PlaytimeSeconds % 3600 / 60);
                return $"{hours}h {minutes:D2}m";
            }
        }

        /// <summary>Data formattata per il display.</summary>
        public string LastSavedFormatted
            => LastSaved.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    // =========================================================================
    //  SAVE SYSTEM
    // =========================================================================

    /// <summary>
    /// Singleton. Gestisce lettura, scrittura, autosave e migrazione dei dati.
    ///
    /// ICONA DI SALVATAGGIO:
    ///   OnSaveStarted  → mostra icona floppy disk (🖫) in alto a destra
    ///   OnSaveComplete → nasconde icona dopo 2 secondi
    ///   OnSaveFailed   → mostra icona errore ⚠ per 3 secondi
    /// </summary>
    public sealed class SaveSystem
    {
        // ------------------------------------------------------------------
        //  Costanti
        // ------------------------------------------------------------------

        public const int CURRENT_SAVE_VERSION = 1;
        public const int MAX_SLOTS            = 3;

        private const string SAVE_FILE_PREFIX  = "slot_";
        private const string SAVE_FILE_EXT     = ".json";
        private const string SETTINGS_FILE     = "settings.json";

        // ------------------------------------------------------------------
        //  Singleton
        // ------------------------------------------------------------------

        private static SaveSystem? _instance;
        public static SaveSystem Instance => _instance ??= new SaveSystem();
        private SaveSystem() { }

        // ------------------------------------------------------------------
        //  Stato
        // ------------------------------------------------------------------

        /// <summary>Slot attivo corrente (0-based). -1 = nessuno slot selezionato.</summary>
        public int ActiveSlot { get; private set; } = -1;

        /// <summary>SaveData attualmente caricato in memoria.</summary>
        public SaveData? Current { get; private set; }

        /// <summary>True se un salvataggio è in corso.</summary>
        public bool IsSaving { get; private set; } = false;

        // ------------------------------------------------------------------
        //  EVENTI ICONA SALVATAGGIO
        // ------------------------------------------------------------------

        /// <summary>
        /// Sparato quando inizia il salvataggio.
        /// SaveIndicatorOverlay mostra l'icona floppy disk in alto a destra.
        /// </summary>
        public event Action? OnSaveStarted;

        /// <summary>
        /// Sparato quando il salvataggio è completato con successo.
        /// SaveIndicatorOverlay avvia il timer di 2s per nascondere l'icona.
        /// </summary>
        public event Action? OnSaveComplete;

        /// <summary>
        /// Sparato se il salvataggio fallisce.
        /// SaveIndicatorOverlay mostra l'icona di errore per 3s.
        /// </summary>
        public event Action<string>? OnSaveFailed;

        /// <summary>
        /// Sparato quando un autosave viene triggerato.
        /// Parametro: slot usato.
        /// </summary>
        public event Action<int>? OnAutosaveTriggered;

        // ------------------------------------------------------------------
        //  PERCORSI FILE
        // ------------------------------------------------------------------

        private static string GetSaveDirectory()
        {
            string baseDir = FileSystem.AppDataDirectory;

#if WINDOWS
            // Windows: %APPDATA%\LaViaDellaRedenzione\saves\
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LaViaDellaRedenzione");
#endif
            return Path.Combine(baseDir, "saves");
        }

        private static string GetSlotPath(int slot)
            => Path.Combine(GetSaveDirectory(), $"{SAVE_FILE_PREFIX}{slot}{SAVE_FILE_EXT}");

        private static string GetSettingsPath()
            => Path.Combine(GetSaveDirectory(), SETTINGS_FILE);

        private static void EnsureSaveDirectoryExists()
            => Directory.CreateDirectory(GetSaveDirectory());

        // ------------------------------------------------------------------
        //  SAVE
        // ------------------------------------------------------------------

        /// <summary>
        /// Salva il SaveData corrente nello slot specificato in modo asincrono.
        /// Spara OnSaveStarted → OnSaveComplete/OnSaveFailed.
        /// </summary>
        public async Task SaveAsync(int slot, SaveData data)
        {
            if (slot < 0 || slot >= MAX_SLOTS)
                throw new ArgumentOutOfRangeException(nameof(slot));

            if (IsSaving) return; // evita salvataggi concorrenti

            IsSaving = true;
            OnSaveStarted?.Invoke();

            try
            {
                EnsureSaveDirectoryExists();

                data.SaveVersion = CURRENT_SAVE_VERSION;
                data.LastSaved   = DateTime.UtcNow;

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                string path = GetSlotPath(slot);

                // Scrivi su file temporaneo poi rinomina (atomic write)
                // Evita file corrotti se l'app crasha durante la scrittura
                string tempPath = path + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, path, overwrite: true);

                Current    = data;
                ActiveSlot = slot;

                OnSaveComplete?.Invoke();

                System.Diagnostics.Debug.WriteLine(
                    $"[SaveSystem] Slot {slot} salvato: {path}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SaveSystem] Errore salvataggio slot {slot}: {ex.Message}");
                OnSaveFailed?.Invoke(ex.Message);
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// Salva il SaveData corrente nello slot attivo.
        /// Usato dall'autosave.
        /// </summary>
        public async Task SaveCurrentAsync()
        {
            if (Current == null || ActiveSlot < 0) return;
            await SaveAsync(ActiveSlot, Current);
        }

        // ------------------------------------------------------------------
        //  AUTOSAVE
        // ------------------------------------------------------------------

        /// <summary>
        /// Esegue un autosave silenzioso.
        /// Chiamato da GameStateManager.OnStateChanged e dopo ogni battaglia.
        /// </summary>
        public async Task AutosaveAsync()
        {
            if (Current == null || ActiveSlot < 0) return;

            OnAutosaveTriggered?.Invoke(ActiveSlot);
            await SaveCurrentAsync();
        }

        // ------------------------------------------------------------------
        //  LOAD
        // ------------------------------------------------------------------

        /// <summary>
        /// Carica un SaveData dallo slot specificato.
        /// Esegue automaticamente la migrazione se la versione è vecchia.
        /// Ritorna null se lo slot è vuoto o il file è corrotto.
        /// </summary>
        public async Task<SaveData?> LoadAsync(int slot)
        {
            if (slot < 0 || slot >= MAX_SLOTS)
                throw new ArgumentOutOfRangeException(nameof(slot));

            string path = GetSlotPath(slot);

            if (!File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SaveSystem] Slot {slot} vuoto.");
                return null;
            }

            try
            {
                string json = await File.ReadAllTextAsync(path);
                var data    = JsonConvert.DeserializeObject<SaveData>(json);

                if (data == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[SaveSystem] Slot {slot}: deserializzazione fallita.");
                    return null;
                }

                // Migrazione se necessario
                if (data.SaveVersion < CURRENT_SAVE_VERSION)
                {
                    data = Migrate(data, data.SaveVersion, CURRENT_SAVE_VERSION);
                    // Salva immediatamente la versione migrata
                    await SaveAsync(slot, data);
                }

                Current    = data;
                ActiveSlot = slot;

                System.Diagnostics.Debug.WriteLine(
                    $"[SaveSystem] Slot {slot} caricato. Capitolo {data.CurrentChapter}.");

                return data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SaveSystem] Errore caricamento slot {slot}: {ex.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------
        //  DELETE
        // ------------------------------------------------------------------

        /// <summary>
        /// Elimina lo slot specificato.
        /// </summary>
        public void Delete(int slot)
        {
            if (slot < 0 || slot >= MAX_SLOTS)
                throw new ArgumentOutOfRangeException(nameof(slot));

            string path = GetSlotPath(slot);

            if (File.Exists(path))
            {
                File.Delete(path);
                System.Diagnostics.Debug.WriteLine(
                    $"[SaveSystem] Slot {slot} eliminato.");
            }

            if (ActiveSlot == slot)
            {
                ActiveSlot = -1;
                Current    = null;
            }
        }

        /// <summary>
        /// Elimina tutti i salvataggi.
        /// Chiamato da OptionsScreen → "Elimina tutti i salvataggi".
        /// </summary>
        public void DeleteAll()
        {
            for (int i = 0; i < MAX_SLOTS; i++)
                Delete(i);
        }

        // ------------------------------------------------------------------
        //  GET PREVIEWS — anteprime per il menu slot
        // ------------------------------------------------------------------

        /// <summary>
        /// Restituisce le anteprime di tutti gli slot senza caricare i dati
        /// completi. Usato da MainMenuScreen e SaveSlotSelectionView.
        /// </summary>
        public async Task<SaveSlotPreview[]> GetPreviewsAsync()
        {
            var previews = new SaveSlotPreview[MAX_SLOTS];

            for (int i = 0; i < MAX_SLOTS; i++)
            {
                string path = GetSlotPath(i);

                if (!File.Exists(path))
                {
                    previews[i] = new SaveSlotPreview
                    {
                        SlotIndex = i,
                        IsEmpty   = true
                    };
                    continue;
                }

                try
                {
                    // Deserializza solo i campi necessari per l'anteprima
                    // tramite un tipo anonimo (evita di caricare tutto)
                    string json = await File.ReadAllTextAsync(path);
                    var partial = JsonConvert.DeserializeAnonymousType(json, new
                    {
                        CurrentChapter  = 1,
                        CurrentLocationId = string.Empty,
                        PlaytimeSeconds = 0f,
                        LastSaved       = DateTime.UtcNow,
                        IsNewGamePlus   = false,
                        Difficulty      = DifficultyLevel.Viandante
                    });

                    previews[i] = new SaveSlotPreview
                    {
                        SlotIndex       = i,
                        IsEmpty         = false,
                        Chapter         = partial?.CurrentChapter  ?? 1,
                        LocationName    = GetLocationDisplayName(partial?.CurrentLocationId ?? ""),
                        PlaytimeSeconds = partial?.PlaytimeSeconds ?? 0f,
                        LastSaved       = partial?.LastSaved       ?? DateTime.UtcNow,
                        IsNewGamePlus   = partial?.IsNewGamePlus   ?? false,
                        Difficulty      = partial?.Difficulty      ?? DifficultyLevel.Viandante
                    };
                }
                catch
                {
                    previews[i] = new SaveSlotPreview
                    {
                        SlotIndex = i,
                        IsEmpty   = true
                    };
                }
            }

            return previews;
        }

        // ------------------------------------------------------------------
        //  MIGRATE — aggiornamento versione save
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiorna un SaveData da una versione vecchia alla versione corrente.
        /// Aggiunge i campi mancanti con valori di default senza perdere dati.
        /// </summary>
        public SaveData Migrate(SaveData data, int fromVersion, int toVersion)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[SaveSystem] Migrazione save v{fromVersion} → v{toVersion}");

            // Migrazione v0 → v1
            if (fromVersion < 1)
            {
                // v1: aggiunge KaelMorale (default 100 se mancante)
                if (data.KaelMorale == 0) data.KaelMorale = 100;

                // v1: aggiunge IsNewGamePlus (default false)
                // Già gestito dal default della proprietà

                // v1: assicura che tutti i personaggi abbiano CharacterSaveData
                foreach (var id in new[] { "KAEL", "LYRA", "VORAN", "SERA" })
                {
                    if (!data.Characters.ContainsKey(id))
                        data.Characters[id] = new CharacterSaveData
                        {
                            CharacterId = id,
                            Level       = 1
                        };
                }

                data.SaveVersion = 1;
            }

            // Qui in futuro: if (fromVersion < 2) { ... }

            return data;
        }

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        /// <summary>
        /// Converte un LocationId nel nome visualizzato (per le anteprime slot).
        /// </summary>
        private static string GetLocationDisplayName(string locationId) => locationId switch
        {
            "LOC_MARSHEN"            => "Marshen",
            "LOC_MARSHEN_LOCANDA"    => "Locanda del Corno Spezzato",
            "LOC_ASHGROVE"           => "Foresta di Ashgrove",
            "LOC_VERDHOLM"           => "Verdholm",
            "LOC_DREVATH"            => "Pianure di Drevath",
            "LOC_SETTE_DITA"         => "Foresta delle Sette Dita",
            "LOC_MONASTERO"          => "Monastero di Pietra Grigia",
            "LOC_VALLATA"            => "Vallata del Rituale",
            "LOC_MONTAGNE_CENERI"    => "Montagne Ceneri",
            "LOC_BOCCA_SILENZIO"     => "Bocca del Silenzio",
            "LOC_FORESTA_TRANQUILLA" => "Foresta Tranquilla",
            _                        => locationId
        };

        /// <summary>
        /// True se lo slot specificato ha un file di salvataggio.
        /// </summary>
        public bool SlotExists(int slot)
            => File.Exists(GetSlotPath(slot));

        /// <summary>
        /// Imposta il SaveData corrente senza salvarlo su disco.
        /// Usato da GameManager per inizializzare una nuova partita.
        /// </summary>
        public void SetCurrent(SaveData data, int slot)
        {
            Current    = data;
            ActiveSlot = slot;
        }
    }
}
