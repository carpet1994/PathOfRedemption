// =============================================================================
//  La Via della Redenzione — Core/Character.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Modello runtime del personaggio giocabile — statistiche,
//                progressione (livello 1–30), stati alterazione, Morale (Kael).
//                Nessuna dipendenza da UI o piattaforma.
// =============================================================================

using System;
using System.Collections.Generic;

namespace LaViaDellaRedenzione.Core
{
    // =========================================================================
    //  GROWTH PROFILE — moltiplicatori di crescita per livello
    // =========================================================================

    /// <summary>
    /// Profilo di crescita per livello. I valori moltiplicano un incremento
    /// base comune per ottenere statistiche distintive per personaggio.
    /// </summary>
    [Serializable]
    public sealed class GrowthProfile
    {
        public float HpMult   { get; init; } = 1f;
        public float SpMult   { get; init; } = 1f;
        public float AtkMult  { get; init; } = 1f;
        public float MagMult  { get; init; } = 1f;
        public float DefMult  { get; init; } = 1f;
        public float ResMult  { get; init; } = 1f;
        public float SpdMult  { get; init; } = 1f;
        public float LukMult  { get; init; } = 1f;

        public static GrowthProfile Kael   => new()
        {
            HpMult = 1.15f, SpMult = 0.95f, AtkMult = 1.2f, MagMult = 0.75f,
            DefMult = 1.15f, ResMult = 0.9f, SpdMult = 1.0f, LukMult = 1.0f
        };

        public static GrowthProfile Lyra   => new()
        {
            HpMult = 0.95f, SpMult = 1.1f, AtkMult = 0.8f, MagMult = 1.25f,
            DefMult = 0.85f, ResMult = 1.2f, SpdMult = 1.05f, LukMult = 1.05f
        };

        public static GrowthProfile Voran  => new()
        {
            HpMult = 0.9f, SpMult = 1.35f, AtkMult = 0.85f, MagMult = 1.2f,
            DefMult = 0.9f, ResMult = 1.15f, SpdMult = 0.85f, LukMult = 1.0f
        };

        public static GrowthProfile Sera   => new()
        {
            HpMult = 0.75f, SpMult = 1.0f, AtkMult = 0.95f, MagMult = 0.8f,
            DefMult = 0.85f, ResMult = 0.95f, SpdMult = 1.25f, LukMult = 1.1f
        };
    }

    // =========================================================================
    //  STATUS EFFECT — istanza attiva su un personaggio
    // =========================================================================

    /// <summary>
    /// Stato alterazione applicato in battaglia con durata e intensità.
    /// </summary>
    [Serializable]
    public sealed class StatusEffect
    {
        public StatusEffectType Type      { get; set; }
        public int              TurnsLeft { get; set; }
        public float            Intensity { get; set; } = 1f;

        /// <summary>ID della carta che ha applicato l'effetto (se noto).</summary>
        public string SourceCardId { get; set; } = string.Empty;

        /// <summary>
        /// Invocato all'inizio del turno del personaggio affetto (opzionale).
        /// Non serializzabile: va re-iniettato dal BattleSystem dopo il load.
        /// </summary>
        [field: NonSerialized]
        public Action<Character>? OnTurnStart { get; set; }

        public StatusEffect CloneShallow()
        {
            return new StatusEffect
            {
                Type          = Type,
                TurnsLeft     = TurnsLeft,
                Intensity     = Intensity,
                SourceCardId  = SourceCardId,
                OnTurnStart   = OnTurnStart
            };
        }
    }

    // =========================================================================
    //  CHARACTER — personaggio giocabile
    // =========================================================================

    /// <summary>
    /// Personaggio giocabile con statistiche, livello, resistenze elementali
    /// e (per Kael) Morale narrativo.
    /// </summary>
    [Serializable]
    public sealed class Character
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 30;

        // ------------------------------------------------------------------
        //  Identità
        // ------------------------------------------------------------------

        public string         CharacterId { get; set; } = string.Empty;
        public string         DisplayName   { get; set; } = string.Empty;
        public CharacterClass Class         { get; set; }

        public bool IsKael => string.Equals(CharacterId, "KAEL", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------
        //  Progressione
        // ------------------------------------------------------------------

        public int Level { get; private set; } = MinLevel;

        /// <summary>XP accumulati verso il prossimo livello (non totale vita).</summary>
        public int ExpIntoCurrentLevel { get; set; }

        public GrowthProfile Growth { get; set; } = GrowthProfile.Kael;

        // ------------------------------------------------------------------
        //  Risorse e statistiche (valori effettivi dopo applicazione crescita)
        // ------------------------------------------------------------------

        public int CurrentHP { get; set; }
        public int CurrentSP { get; set; }

        public int MaxHP { get; private set; }
        public int MaxSP { get; private set; }

        public int ATK { get; private set; }
        public int MAG { get; private set; }
        public int DEF { get; private set; }
        public int RES { get; private set; }
        public int SPD { get; private set; }
        public int LUK { get; private set; }

        /// <summary>
        /// Morale di Kael (0–100). Per altri personaggi resta tipicamente 100 e non viene usato.
        /// </summary>
        public int Morale { get; set; } = 100;

        /// <summary>
        /// Resistenze elementali: 0 = immune, 1 = normale, 2 = vulnerabile.
        /// </summary>
        public Dictionary<ElementType, float> ElementalResistance { get; } = new();

        /// <summary>Stati attivi in battaglia.</summary>
        public List<StatusEffect> ActiveStatusEffects { get; } = new();

        // ------------------------------------------------------------------
        //  Eventi (UI / BattleSystem)
        // ------------------------------------------------------------------

        public event Action<Character, int, int>? LevelUp;

        /// <summary>Invocato quando HP/SP max o statistiche cambiano da level up.</summary>
        public event Action<Character>? StatsRecalculated;

        // ------------------------------------------------------------------
        //  Factory preset
        // ------------------------------------------------------------------

        public static Character Create(string id, string displayName, CharacterClass cls, GrowthProfile growth)
        {
            var c = new Character
            {
                CharacterId = id,
                DisplayName = displayName,
                Class       = cls,
                Growth      = growth
            };
            InitDefaultResistances(c);
            c.RebuildStatsFromLevel(resetHpSpToMax: true);
            return c;
        }

        public static Character CreateKael() =>
            Create("KAEL", "Kael Dawnford", CharacterClass.Guerriero, GrowthProfile.Kael);

        public static Character CreateLyra() =>
            Create("LYRA", "Lyra Ashveil", CharacterClass.Custode, GrowthProfile.Lyra);

        public static Character CreateVoran() =>
            Create("VORAN", "Voran il Silente", CharacterClass.Mago, GrowthProfile.Voran);

        public static Character CreateSera() =>
            Create("SERA", "Sera", CharacterClass.Esploratore, GrowthProfile.Sera);

        static void InitDefaultResistances(Character c)
        {
            foreach (ElementType e in Enum.GetValues(typeof(ElementType)))
                c.ElementalResistance[e] = 1f;
        }

        // ------------------------------------------------------------------
        //  EXP / livello — formula: EXP necessari per passare da L a L+1 = 100 * L^1.8
        // ------------------------------------------------------------------

        public static int GetExperienceRequiredToAdvance(int currentLevel)
        {
            if (currentLevel < MinLevel) currentLevel = MinLevel;
            if (currentLevel >= MaxLevel) return int.MaxValue;
            return Math.Max(1, (int)Math.Round(100 * Math.Pow(currentLevel, 1.8)));
        }

        /// <summary>Aggiunge XP e gestisce più level-up in cascata.</summary>
        public void AddExperience(int amount)
        {
            if (amount <= 0 || Level >= MaxLevel) return;

            ExpIntoCurrentLevel += amount;
            while (Level < MaxLevel)
            {
                int need = GetExperienceRequiredToAdvance(Level);
                if (ExpIntoCurrentLevel < need) break;

                ExpIntoCurrentLevel -= need;
                int old = Level;
                Level++;
                RebuildStatsFromLevel(resetHpSpToMax: false);
                LevelUp?.Invoke(this, old, Level);
            }

            if (Level >= MaxLevel)
            {
                ExpIntoCurrentLevel = 0;
            }
        }

        /// <summary>Forza il livello (debug / caricamento save) e ricalcola le statistiche.</summary>
        public void SetLevel(int newLevel, bool resetHpSpToMax)
        {
            newLevel = Math.Clamp(newLevel, MinLevel, MaxLevel);
            int old = Level;
            Level = newLevel;
            RebuildStatsFromLevel(resetHpSpToMax);
            if (old != Level)
                LevelUp?.Invoke(this, old, Level);
        }

        // ------------------------------------------------------------------
        //  Statistiche base da livello
        // ------------------------------------------------------------------

        /// <summary>
        /// Curve di base livello 1 (prima dell'applicazione del GrowthProfile).
        /// </summary>
        static void GetBaseStatsForLevel(int level, out int hp, out int sp, out int atk, out int mag, out int def, out int res, out int spd, out int luk)
        {
            level = Math.Clamp(level, MinLevel, MaxLevel);
            float t = level - 1;
            hp  = 80  + (int)(t * 12);
            sp  = 20  + (int)(t * 4);
            atk = 12  + (int)(t * 2);
            mag = 10  + (int)(t * 2);
            def = 10  + (int)(t * 1.5);
            res = 10  + (int)(t * 1.5);
            spd = 10  + (int)(t * 1.2);
            luk = 8   + (int)(t * 0.8);
        }

        public void RebuildStatsFromLevel(bool resetHpSpToMax)
        {
            GetBaseStatsForLevel(Level, out int bHp, out int bSp, out int bAtk, out int bMag, out int bDef, out int bRes, out int bSpd, out int bLuk);
            var g = Growth;

            MaxHP = Math.Max(1, (int)Math.Round(bHp  * g.HpMult));
            MaxSP = Math.Max(1, (int)Math.Round(bSp  * g.SpMult));
            ATK   = Math.Max(1, (int)Math.Round(bAtk * g.AtkMult));
            MAG   = Math.Max(1, (int)Math.Round(bMag * g.MagMult));
            DEF   = Math.Max(1, (int)Math.Round(bDef * g.DefMult));
            RES   = Math.Max(1, (int)Math.Round(bRes * g.ResMult));
            SPD   = Math.Max(1, (int)Math.Round(bSpd * g.SpdMult));
            LUK   = Math.Max(1, (int)Math.Round(bLuk * g.LukMult));

            if (resetHpSpToMax || CurrentHP > MaxHP)
                CurrentHP = MaxHP;
            else if (CurrentHP < 0)
                CurrentHP = 0;

            if (resetHpSpToMax || CurrentSP > MaxSP)
                CurrentSP = MaxSP;
            else if (CurrentSP < 0)
                CurrentSP = 0;

            StatsRecalculated?.Invoke(this);
        }

        // ------------------------------------------------------------------
        //  Morale (Kael)
        // ------------------------------------------------------------------

        public void SetMorale(int value)
        {
            Morale = Math.Clamp(value, 0, 100);
        }

        public void ApplyMoraleDelta(int delta)
        {
            SetMorale(Morale + delta);
        }

        /// <summary>Moltiplicatore ATK da Morale (&lt; 30 → −15%).</summary>
        public float GetMoraleAtkMultiplier()
        {
            if (!IsKael) return 1f;
            return Morale < 30 ? 0.85f : 1f;
        }

        /// <summary>
        /// A Morale &lt; 10, 25% di chance che Kael non esegua l'ordine del giocatore.
        /// </summary>
        public bool RollDisobeyPlayerOrder(Random rng)
        {
            if (!IsKael || Morale >= 10) return false;
            return rng.NextDouble() < 0.25;
        }

        public bool HasMoraleRedemptionUnlock => IsKael && Morale >= 100;

        // ------------------------------------------------------------------
        //  Stati alterazione — helper
        // ------------------------------------------------------------------

        public void AddStatus(StatusEffect effect) => ActiveStatusEffects.Add(effect);

        public bool RemoveStatus(StatusEffectType type)
        {
            for (int i = ActiveStatusEffects.Count - 1; i >= 0; i--)
            {
                if (ActiveStatusEffects[i].Type == type)
                {
                    ActiveStatusEffects.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void ClearAllStatuses() => ActiveStatusEffects.Clear();

        // ------------------------------------------------------------------
        //  Contesto formule / serializzazione leggera
        // ------------------------------------------------------------------

        public StatContext ToStatContext() => StatContext.FromCharacter(this);
    }
}
