// =============================================================================
//  La Via della Redenzione — Core/Enemy.cs
//  Package : com.refa.valdrath
//
//  Modello nemici, AI semplice (azioni pesate + condizioni testuali),
//  loot table, resistenze elementali, dati animazione.
//  Caricamento da Assets/Data/enemies.json (MauiAsset → Data/enemies.json).
// =============================================================================

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LaViaDellaRedenzione.Core
{
    /// <summary>Tipo di voce nella tabella drop.</summary>
    public enum LootEntryKind
    {
        Card = 0,
        Item = 1,
        Gold = 2
    }

    /// <summary>
    /// Singola voce di loot. Per <see cref="LootEntryKind.Gold"/>,
    /// <c>RefId</c> può essere vuoto: usa <see cref="MinCount"/>/<see cref="MaxCount"/>
    /// come intervallo di oro.
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

        /// <summary>Probabilità indipendente 0..1 di questa voce se il drop è attivato.</summary>
        [JsonProperty("dropChance")]
        public float DropChance { get; set; } = 1f;
    }

    /// <summary>
    /// Azione AI: il BattleSystem valuta <see cref="Condition"/> (stringa
    /// descrittiva, es. "HP% < 50") e sorteggia tra le azioni con stesso peso.
    /// </summary>
    [Serializable]
    public sealed class EnemyAction
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonProperty("weight")]
        public float Weight { get; set; } = 1f;

        /// <summary>Es. "always", "hp_percent < 50", "ally_low_hp".</summary>
        [JsonProperty("condition")]
        public string Condition { get; set; } = "always";

        /// <summary>Tag per risoluzione effetti (es. "physical", "dark", "aoe_blind").</summary>
        [JsonProperty("effectTags")]
        public List<string> EffectTags { get; set; } = new();
    }

    /// <summary>
    /// Definizione dati di un nemico (statistiche, AI, loot, sprite).
    /// </summary>
    [Serializable]
    public sealed class Enemy
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("level")]
        public int Level { get; set; } = 1;

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

        [JsonProperty("expReward")]
        public int ExpReward { get; set; }

        [JsonProperty("goldReward")]
        public int GoldReward { get; set; }

        [JsonProperty("actions")]
        public List<EnemyAction> Actions { get; set; } = new();

        [JsonProperty("lootTable")]
        public List<LootEntry> LootTable { get; set; } = new();

        [JsonProperty("elementalResistance")]
        public Dictionary<ElementType, float> ElementalResistance { get; set; } = new();

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

        [JsonProperty("flavorText")]
        public string FlavorText { get; set; } = string.Empty;

        [JsonProperty("isBoss")]
        public bool IsBoss { get; set; }

        /// <summary>Per nemici che appaiono in branco (Cane dell'Oscurità).</summary>
        [JsonProperty("spawnMin")]
        public int SpawnMin { get; set; } = 1;

        [JsonProperty("spawnMax")]
        public int SpawnMax { get; set; } = 1;

        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new();

        public float GetResistance(ElementType element)
            => ElementalResistance.TryGetValue(element, out var m) ? m : 1f;
    }

    /// <summary>
    /// Singleton che carica e indicizza <c>Data/enemies.json</c>.
    /// </summary>
    public sealed class EnemyDatabase
    {
        private static EnemyDatabase? _instance;
        public static EnemyDatabase Instance => _instance ??= new EnemyDatabase();

        private readonly Dictionary<string, Enemy> _byId = new(StringComparer.OrdinalIgnoreCase);

        public bool IsLoaded { get; private set; }

        private const string DataPath = "Data/enemies.json";

        private EnemyDatabase() { }

        public async Task LoadAllAsync()
        {
            if (IsLoaded) return;

            _byId.Clear();

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(DataPath);
                using var reader = new StreamReader(stream);
                string json = await reader.ReadToEndAsync();

                var list = JsonConvert.DeserializeObject<List<Enemy>>(json);
                if (list == null)
                {
                    IsLoaded = true;
                    return;
                }

                foreach (var enemy in list)
                {
                    if (string.IsNullOrWhiteSpace(enemy.Id))
                        continue;

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
                System.Diagnostics.Debug.WriteLine($"[EnemyDatabase] Caricamento fallito: {ex.Message}");
            }

            IsLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[EnemyDatabase] Caricati {_byId.Count} nemici.");
        }

        public Enemy? GetById(string id)
            => _byId.TryGetValue(id, out var e) ? e : null;

        public IReadOnlyCollection<Enemy> GetAll() => _byId.Values;

        public IReadOnlyList<Enemy> GetByTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return Array.Empty<Enemy>();

            return _byId.Values
                .Where(e => e.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
