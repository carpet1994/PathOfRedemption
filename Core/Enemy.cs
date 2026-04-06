// =============================================================================
//  La Via della Redenzione — Core/Enemy.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Modello dati completo dei nemici di Valdrath.
//                Caricato da Assets/Data/enemies.json all'avvio.
//
//  Struttura:
//    LootEntry     → singola voce della tabella drop
//    EnemyAction   → azione AI con peso e condizione
//    Enemy         → definizione completa di un nemico (stat, AI, loot, sprite)
//    EnemyDatabase → singleton che carica e indicizza enemies.json
//
//  AI semplice (BattleSystem):
//    1. Filtra le EnemyAction con Condition soddisfatta (EnemyAction.IsConditionMet)
//    2. Sceglie l'azione con peso proporzionale (PickAction)
//    3. Risolve gli EffectTags tramite BattleSystem.ResolveEnemyAction()
//
//  Condizioni supportate (stringa):
//    "always"          → sempre disponibile
//    "hp_percent < 50" → HP corrente < 50% degli HP max
//    "hp_percent < 25" → fase finale / forma disperata
//    "ally_low_hp"     → un alleato nemico ha HP < 30% (per nemici a branco)
//    "player_low_hp"   → un personaggio giocabile ha HP < 30%
//    "boss_phase_2"    → attivata dal BattleSystem quando HP < 50% (boss)
//    "boss_phase_3"    → attivata quando HP < 25% (boss a 3 fasi)
//
//  Resistenze elementali:
//    0.0 = immune, 0.5 = resistente, 1.0 = normale, 2.0 = vulnerabile
//    I valori mancanti vengono completati con 1.0 al caricamento.
// =============================================================================

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LaViaDellaRedenzione.Core
{
    // =========================================================================
    //  LOOT ENTRY
    // =========================================================================

    /// <summary>Tipo di voce nella tabella drop.</summary>
    public enum LootEntryKind
    {
        Card = 0,
        Item = 1,
        Gold = 2
    }

    /// <summary>
    /// Singola voce di loot. Per <see cref="LootEntryKind.Gold"/>,
    /// <c>RefId</c> può essere vuoto: usa MinCount/MaxCount come intervallo di oro.
    /// </summary>
    [Serializable]
    public sealed class LootEntry
    {
        [JsonProperty("kind")]
        public LootEntryKind Kind { get; set; }

        /// <summary>ID carta, ID oggetto, o vuoto per oro generico.</summary>
        [JsonProperty("refId")]
        public string RefId { get; set; } = string.Empty;

        [JsonProperty("minCount")]
        public int MinCount { get; set; } = 1;

        [JsonProperty("maxCount")]
        public int MaxCount { get; set; } = 1;

        /// <summary>
        /// Probabilità indipendente 0..1 di questa voce.
        /// 1.0 = sempre droppato se il loot è attivato.
        /// </summary>
        [JsonProperty("dropChance")]
        public float DropChance { get; set; } = 1f;

        /// <summary>
        /// Rarità minima richiesta nel giocatore per ottenere questa voce.
        /// 0 = nessun requisito. Usato per drop garantiti da boss.
        /// </summary>
        [JsonProperty("minRarity")]
        public int MinRarity { get; set; } = 0;
    }

    // =========================================================================
    //  ENEMY ACTION — azione AI
    // =========================================================================

    /// <summary>
    /// Azione che un nemico può eseguire in battaglia.
    /// Il BattleSystem valuta Condition e sceglie tra le azioni disponibili
    /// con probabilità proporzionale al Weight.
    /// </summary>
    [Serializable]
    public sealed class EnemyAction
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Peso per la selezione casuale pesata.
        /// Più alto = più frequente rispetto ad altre azioni disponibili.
        /// </summary>
        [JsonProperty("weight")]
        public float Weight { get; set; } = 1f;

        /// <summary>
        /// Condizione in formato stringa valutata dal BattleSystem.
        /// Valori supportati: "always", "hp_percent &lt; 50",
        /// "hp_percent &lt; 25", "player_low_hp", "boss_phase_2", "boss_phase_3".
        /// </summary>
        [JsonProperty("condition")]
        public string Condition { get; set; } = "always";

        /// <summary>
        /// Tag per la risoluzione degli effetti da parte del BattleSystem.
        /// Esempi: "physical", "dark", "aoe_blind", "drain_sp",
        ///         "psychological" (attacca Morale di Kael).
        /// </summary>
        [JsonProperty("effectTags")]
        public List<string> EffectTags { get; set; } = new();

        /// <summary>
        /// Valore base del danno/effetto (scalato dal BattleSystem con ATK o MAG).
        /// </summary>
        [JsonProperty("basePower")]
        public float BasePower { get; set; } = 1.0f;

        /// <summary>
        /// Elemento dell'azione. Neutro = danno fisico puro.
        /// </summary>
        [JsonProperty("element")]
        public ElementType Element { get; set; } = ElementType.Neutro;

        // ------------------------------------------------------------------
        //  VALUTAZIONE CONDIZIONE
        // ------------------------------------------------------------------

        /// <summary>
        /// Valuta se la condizione di questa azione è soddisfatta.
        /// Chiamato dal BattleSystem prima di sorteggiare l'azione.
        /// </summary>
        /// <param name="hpPercent">HP corrente del nemico in % (0..1).</param>
        /// <param name="isBossPhase2">True se il boss è in fase 2 (HP &lt; 50%).</param>
        /// <param name="isBossPhase3">True se il boss è in fase 3 (HP &lt; 25%).</param>
        /// <param name="playerLowHP">True se almeno un personaggio giocabile ha HP &lt; 30%.</param>
        public bool IsConditionMet(
            float hpPercent,
            bool  isBossPhase2   = false,
            bool  isBossPhase3   = false,
            bool  playerLowHP    = false)
        {
            return Condition.ToLowerInvariant() switch
            {
                "always"          => true,
                "hp_percent < 50" => hpPercent < 0.50f,
                "hp_percent < 25" => hpPercent < 0.25f,
                "player_low_hp"   => playerLowHP,
                "boss_phase_2"    => isBossPhase2,
                "boss_phase_3"    => isBossPhase3,
                _                 => true   // condizione sconosciuta = sempre vera
            };
        }
    }

    // =========================================================================
    //  ENEMY — definizione completa di un nemico
    // =========================================================================

    /// <summary>
    /// Template dati di un nemico. Immutabile a runtime — ogni istanza
    /// in battaglia usa <c>EnemyInstance</c> (in BattleSystem.cs) che
    /// tiene HP correnti e stati separati.
    /// </summary>
    [Serializable]
    public sealed class Enemy
    {
        // ------------------------------------------------------------------
        //  Identificazione
        // ------------------------------------------------------------------

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("level")]
        public int Level { get; set; } = 1;

        // ------------------------------------------------------------------
        //  Statistiche base
        // ------------------------------------------------------------------

        [JsonProperty("maxHp")]
        public int MaxHP { get; set; }

        [JsonProperty("atk")]
        public int ATK { get; set; }

        [JsonProperty("mag")]
        public int MAG { get; set; }

        [JsonProperty("def")]
        public int DEF { get; set; }

        [JsonProperty("res")]
        public int RES { get; set; }

        [JsonProperty("spd")]
        public int SPD { get; set; }

        [JsonProperty("luk")]
        public int LUK { get; set; }

        // ------------------------------------------------------------------
        //  Ricompense
        // ------------------------------------------------------------------

        [JsonProperty("expReward")]
        public int ExpReward { get; set; }

        [JsonProperty("goldReward")]
        public int GoldReward { get; set; }

        // ------------------------------------------------------------------
        //  AI
        // ------------------------------------------------------------------

        [JsonProperty("actions")]
        public List<EnemyAction> Actions { get; set; } = new();

        // ------------------------------------------------------------------
        //  Loot
        // ------------------------------------------------------------------

        [JsonProperty("lootTable")]
        public List<LootEntry> LootTable { get; set; } = new();

        // ------------------------------------------------------------------
        //  Resistenze elementali
        // ------------------------------------------------------------------

        /// <summary>
        /// Moltiplicatore danno per elemento (0.0=immune, 1.0=normale, 2.0=vulnerabile).
        /// Chiavi mancanti completate con 1.0 al caricamento da EnemyDatabase.
        /// </summary>
        [JsonProperty("elementalResistance")]
        public Dictionary<ElementType, float> ElementalResistance { get; set; } = new();

        // ------------------------------------------------------------------
        //  Sprite e animazioni
        // ------------------------------------------------------------------

        /// <summary>Path relativo allo sprite sheet (/Assets/Sprites/Enemies/).</summary>
        [JsonProperty("spriteSheet")]
        public string SpriteSheet { get; set; } = string.Empty;

        [JsonProperty("idleFrames")]
        public int IdleFrames { get; set; } = 4;

        [JsonProperty("attackFrames")]
        public int AttackFrames { get; set; } = 6;

        [JsonProperty("hurtFrames")]
        public int HurtFrames { get; set; } = 3;

        [JsonProperty("deathFrames")]
        public int DeathFrames { get; set; } = 8;

        // ------------------------------------------------------------------
        //  Metadati
        // ------------------------------------------------------------------

        [JsonProperty("flavorText")]
        public string FlavorText { get; set; } = string.Empty;

        /// <summary>True per boss con barra HP separata e più fasi.</summary>
        [JsonProperty("isBoss")]
        public bool IsBoss { get; set; }

        /// <summary>
        /// Per nemici che appaiono in branco (es. Cane dell'Oscurità: 2-3 per incontro).
        /// </summary>
        [JsonProperty("spawnMin")]
        public int SpawnMin { get; set; } = 1;

        [JsonProperty("spawnMax")]
        public int SpawnMax { get; set; } = 1;

        /// <summary>Tag per ricerca e categorizzazione (es. "oscurita", "boss", "branco").</summary>
        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new();

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        /// <summary>
        /// Resistenza elementale per un elemento specifico.
        /// Ritorna 1.0 se l'elemento non è nella tabella.
        /// </summary>
        public float GetResistance(ElementType element)
            => ElementalResistance.TryGetValue(element, out var m) ? m : 1f;

        /// <summary>
        /// Seleziona un'azione AI con selezione casuale pesata,
        /// filtrando per condizioni soddisfatte.
        /// Ritorna null se non ci sono azioni disponibili.
        /// </summary>
        /// <param name="hpPercent">HP corrente / MaxHP (0..1).</param>
        /// <param name="isBossPhase2">Fase 2 boss attiva.</param>
        /// <param name="isBossPhase3">Fase 3 boss attiva.</param>
        /// <param name="playerLowHP">Almeno un personaggio giocabile a HP bassa.</param>
        /// <param name="rng">Random per riproducibilità nei test (null = new Random()).</param>
        public EnemyAction? PickAction(
            float   hpPercent,
            bool    isBossPhase2 = false,
            bool    isBossPhase3 = false,
            bool    playerLowHP  = false,
            Random? rng          = null)
        {
            rng ??= Random.Shared;

            // Filtra azioni disponibili
            var available = Actions
                .Where(a => a.IsConditionMet(hpPercent, isBossPhase2, isBossPhase3, playerLowHP))
                .ToList();

            if (available.Count == 0) return null;
            if (available.Count == 1) return available[0];

            // Selezione pesata
            float totalWeight = available.Sum(a => a.Weight);
            float roll        = (float)(rng.NextDouble() * totalWeight);
            float cumulative  = 0f;

            foreach (var action in available)
            {
                cumulative += action.Weight;
                if (roll <= cumulative)
                    return action;
            }

            // Fallback: ultima azione disponibile
            return available[^1];
        }

        /// <summary>
        /// True se questo nemico ha il tag specificato.
        /// </summary>
        public bool HasTag(string tag)
            => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Calcola la chance critica del nemico basata su LUK.
        /// Formula identica a quella dei personaggi giocabili.
        /// </summary>
        public float CritChance => Math.Min(LUK / 200f, 0.40f);

        public override string ToString()
            => $"[{(IsBoss ? "BOSS" : "ENE")}] {Name} Lv{Level} HP:{MaxHP}";
    }

    // =========================================================================
    //  ENEMY DATABASE — singleton, carica enemies.json
    // =========================================================================

    /// <summary>
    /// Singleton che carica e indicizza <c>Data/enemies.json</c> all'avvio.
    ///
    /// INIZIALIZZAZIONE:
    ///   await EnemyDatabase.Instance.LoadAllAsync();
    ///
    /// QUERY:
    ///   var enemy = EnemyDatabase.Instance.GetById("ENEMY_OMBRA_VUOTA");
    ///   var bosses = EnemyDatabase.Instance.GetByTag("boss");
    /// </summary>
    public sealed class EnemyDatabase
    {
        // ------------------------------------------------------------------
        //  Singleton
        // ------------------------------------------------------------------

        private static EnemyDatabase? _instance;
        public static EnemyDatabase Instance => _instance ??= new EnemyDatabase();
        private EnemyDatabase() { }

        // ------------------------------------------------------------------
        //  Storage
        // ------------------------------------------------------------------

        private readonly Dictionary<string, Enemy> _byId
            = new(StringComparer.OrdinalIgnoreCase);

        public bool IsLoaded { get; private set; }
        public int  Count    => _byId.Count;

        private const string DataPath = "Data/enemies.json";

        // ------------------------------------------------------------------
        //  CARICAMENTO
        // ------------------------------------------------------------------

        /// <summary>
        /// Carica tutti i nemici dal JSON. Da chiamare una sola volta
        /// all'avvio, insieme a CardDatabase.LoadAllAsync().
        /// </summary>
        public async Task LoadAllAsync()
        {
            if (IsLoaded) return;

            _byId.Clear();

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(DataPath);
                using var reader = new StreamReader(stream);
                string json      = await reader.ReadToEndAsync();

                var list = JsonConvert.DeserializeObject<List<Enemy>>(json);
                if (list == null)
                {
                    IsLoaded = true;
                    return;
                }

                foreach (var enemy in list)
                {
                    if (string.IsNullOrWhiteSpace(enemy.Id)) continue;

                    // Completa le resistenze elementali mancanti con 1.0
                    foreach (ElementType el in Enum.GetValues<ElementType>())
                    {
                        if (!enemy.ElementalResistance.ContainsKey(el))
                            enemy.ElementalResistance[el] = 1f;
                    }

                    _byId[enemy.Id] = enemy;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EnemyDatabase] Caricamento fallito: {ex.Message}");
            }

            IsLoaded = true;
            System.Diagnostics.Debug.WriteLine(
                $"[EnemyDatabase] Caricati {_byId.Count} nemici.");
        }

        // ------------------------------------------------------------------
        //  QUERY
        // ------------------------------------------------------------------

        /// <summary>Restituisce il nemico per ID. Null se non trovato.</summary>
        public Enemy? GetById(string id)
            => _byId.TryGetValue(id, out var e) ? e : null;

        /// <summary>Tutti i nemici caricati.</summary>
        public IReadOnlyCollection<Enemy> GetAll() => _byId.Values;

        /// <summary>Nemici filtrati per tag (es. "boss", "oscurita", "branco").</summary>
        public IReadOnlyList<Enemy> GetByTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return Array.Empty<Enemy>();
            return _byId.Values
                .Where(e => e.HasTag(tag))
                .ToList();
        }

        /// <summary>Solo i boss (IsBoss = true).</summary>
        public IReadOnlyList<Enemy> GetBosses()
            => _byId.Values.Where(e => e.IsBoss).ToList();

        /// <summary>
        /// Nemici nel range di livello specificato.
        /// Usato da EncounterSystem per scalare gli incontri casuali.
        /// </summary>
        public IReadOnlyList<Enemy> GetByLevelRange(int minLevel, int maxLevel)
            => _byId.Values
                .Where(e => e.Level >= minLevel && e.Level <= maxLevel && !e.IsBoss)
                .OrderBy(e => e.Level)
                .ToList();
    }
}
