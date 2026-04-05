// =============================================================================
//  La Via della Redenzione — Core/Character.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Modello completo del personaggio giocabile.
//                Gestisce statistiche base, progressione per livello,
//                stati di alterazione e sistema Morale (esclusivo di Kael).
//
//  Quattro personaggi:
//    Kael  (Guerriero)   — ATK/DEF forti, MAG debole, Morale attivo
//    Lyra  (Custode)     — MAG/RES forti, SP max alto
//    Voran (Mago)        — SP max altissimo, HP basso
//    Sera  (Esploratore) — SPD alta, HP bassa
//
//  Integrazione con altri sistemi:
//    - CardModel   : StatContext.FromCharacter() definito qui come extension
//    - DeckSystem  : CharacterDeck usa CharacterClass da questo file
//    - SaveSystem  : ApplySaveData() / ToSaveData() per persistenza
//    - BattleSystem: statistiche finali via GetBattleStats()
//
//  Formula EXP per livello:
//    EXP_per_livello = (int)(100 * Math.Pow(level, 1.8))
//    Es.: Lv1→2: 100 EXP | Lv9→10: 4.327 EXP | Lv29→30: 43.952 EXP
//
//  Nota Morale:
//    Il sistema Morale è modellato in PlayerEntity per il rendering.
//    Character mantiene il valore canonico usato dal BattleSystem e
//    dal SaveSystem. I due rimangono sincronizzati tramite SyncMorale().
// =============================================================================

using LaViaDellaRedenzione.Systems;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LaViaDellaRedenzione.Core
{
    // =========================================================================
    //  GROWTH PROFILE — curva di crescita per livello
    // =========================================================================

    /// <summary>
    /// Moltiplicatori di crescita statistica per livello.
    /// Ogni personaggio ha un profilo diverso che ne definisce il ruolo.
    /// Stat finale = StatBase + (StatGrowth * (Level - 1)).
    /// </summary>
    public sealed class GrowthProfile
    {
        public float HP  { get; init; } = 8f;
        public float SP  { get; init; } = 3f;
        public float ATK { get; init; } = 2f;
        public float MAG { get; init; } = 1f;
        public float DEF { get; init; } = 2f;
        public float RES { get; init; } = 1f;
        public float SPD { get; init; } = 1f;
        public float LUK { get; init; } = 1f;

        // ------------------------------------------------------------------
        //  Profili predefiniti per i quattro personaggi
        // ------------------------------------------------------------------

        /// <summary>
        /// Kael Dawnford — Guerriero.
        /// ATK e DEF dominanti, MAG quasi assente.
        /// HP solidi, SPD media.
        /// </summary>
        public static GrowthProfile Kael => new()
        {
            HP  = 12f,
            SP  = 2f,
            ATK = 4f,
            MAG = 1f,
            DEF = 3f,
            RES = 1.5f,
            SPD = 1.5f,
            LUK = 1f
        };

        /// <summary>
        /// Lyra Ashveil — Custode.
        /// MAG e RES elevate, ATK fisica bassa.
        /// SP max alto per sostenere i Sigilli.
        /// </summary>
        public static GrowthProfile Lyra => new()
        {
            HP  = 8f,
            SP  = 5f,
            ATK = 1.5f,
            MAG = 4f,
            DEF = 1.5f,
            RES = 3.5f,
            SPD = 2f,
            LUK = 1.5f
        };

        /// <summary>
        /// Voran il Silente — Mago.
        /// SP max altissimo, MAG forte ma HP molto bassa.
        /// SPD lenta — Voran agisce poco ma ogni azione conta.
        /// </summary>
        public static GrowthProfile Voran => new()
        {
            HP  = 6f,
            SP  = 7f,
            ATK = 1f,
            MAG = 3.5f,
            DEF = 1f,
            RES = 3f,
            SPD = 1f,
            LUK = 1.5f
        };

        /// <summary>
        /// Sera — Esploratore.
        /// SPD altissima, sempre prima nell'ordine di turno.
        /// HP e statistiche offensive basse — compensa con reazioni e supporto.
        /// </summary>
        public static GrowthProfile Sera => new()
        {
            HP  = 5f,
            SP  = 3f,
            ATK = 2f,
            MAG = 1.5f,
            DEF = 1f,
            RES = 1.5f,
            SPD = 5f,
            LUK = 3f
        };
    }

    // =========================================================================
    //  STATUS EFFECT — stato di alterazione attivo
    // =========================================================================

    /// <summary>
    /// Istanza di uno stato di alterazione applicato a un personaggio.
    /// Creata dal BattleSystem e aggiornata a ogni turno tramite OnTurnStart().
    /// </summary>
    public sealed class StatusEffect
    {
        // ------------------------------------------------------------------
        //  Dati
        // ------------------------------------------------------------------

        /// <summary>Tipo di stato (enum GameEnums).</summary>
        public StatusEffectType Type { get; }

        /// <summary>Turni rimanenti. 0 = scaduto.</summary>
        public int Duration { get; private set; }

        /// <summary>
        /// Intensità dello stato (moltiplicatore effetto).
        /// Es. 0.8 = -20% ATK per Debuff, 1.5 = +50% ATK per Potenziato.
        /// </summary>
        public float Intensity { get; }

        /// <summary>ID della carta che ha applicato lo stato (per log e UI).</summary>
        public string SourceCardId { get; }

        // ------------------------------------------------------------------
        //  Costruttore
        // ------------------------------------------------------------------

        public StatusEffect(
            StatusEffectType type,
            int              duration,
            float            intensity    = 1.0f,
            string           sourceCardId = "")
        {
            Type         = type;
            Duration     = duration;
            Intensity    = intensity;
            SourceCardId = sourceCardId;
        }

        // ------------------------------------------------------------------
        //  Utility
        // ------------------------------------------------------------------

        /// <summary>True se lo stato è ancora attivo.</summary>
        public bool IsActive => Duration > 0;

        /// <summary>True se lo stato è negativo (malus).</summary>
        public bool IsNegative => Type switch
        {
            StatusEffectType.Avvelenato => true,
            StatusEffectType.Accecato   => true,
            StatusEffectType.Rallentato => true,
            StatusEffectType.Stordito   => true,
            StatusEffectType.Depresso   => true,
            StatusEffectType.Dubbio     => true,
            _                           => false
        };

        /// <summary>True se lo stato è psicologico (rimosso da Ancorato).</summary>
        public bool IsPsychological => Type switch
        {
            StatusEffectType.Depresso => true,
            StatusEffectType.Dubbio   => true,
            StatusEffectType.Accecato => true,
            _                         => false
        };

        /// <summary>
        /// Chiamato all'inizio di ogni turno del personaggio.
        /// Applica effetti passivi (es. veleno) e decrementa la durata.
        /// Ritorna i danni da applicare (0 se nessuno).
        /// </summary>
        public int OnTurnStart(Character owner)
        {
            if (!IsActive) return 0;

            int damage = 0;

            switch (Type)
            {
                case StatusEffectType.Avvelenato:
                    // Danno = 5% degli HP massimi, arrotondato
                    damage = (int)Math.Round(owner.MaxHP * 0.05f * Intensity);
                    break;

                case StatusEffectType.Ispirato:
                    // Rigenera 1 SP per turno
                    owner.RestoreSP(1);
                    break;
            }

            Duration--;
            return damage;
        }

        public override string ToString()
            => $"{Type} ({Duration} turni, x{Intensity:F1})";
    }

    // =========================================================================
    //  CHARACTER — personaggio giocabile
    // =========================================================================

    /// <summary>
    /// Modello completo di un personaggio giocabile.
    /// Contiene statistiche base, statistiche correnti (HP/SP),
    /// stati di alterazione, progressione EXP/livello e sistema Morale.
    ///
    /// STATISTICHE BASE vs BATTAGLIA:
    ///   Le statistiche base crescono con il livello e vengono modificate
    ///   dai bonus dell'equipaggiamento (DeckStats). Il BattleSystem legge
    ///   GetBattleStats() che combina entrambe le fonti.
    ///
    /// THREAD SAFETY:
    ///   Tutti i metodi devono essere chiamati dal thread principale (GameLoop).
    /// </summary>
    public sealed class Character
    {
        // ------------------------------------------------------------------
        //  Identificazione
        // ------------------------------------------------------------------

        /// <summary>ID univoco ("KAEL", "LYRA", "VORAN", "SERA").</summary>
        public string CharacterId { get; }

        /// <summary>Nome visualizzato nell'UI.</summary>
        public string DisplayName { get; }

        /// <summary>Classe del personaggio (per vincoli carte).</summary>
        public CharacterClass Class { get; }

        /// <summary>Profilo di crescita per livello.</summary>
        public GrowthProfile Growth { get; }

        // ------------------------------------------------------------------
        //  Progressione
        // ------------------------------------------------------------------

        private int _level = 1;

        /// <summary>Livello corrente (1-30).</summary>
        public int Level
        {
            get => _level;
            private set => _level = Math.Clamp(value, 1, MAX_LEVEL);
        }

        /// <summary>EXP accumulata nel livello corrente.</summary>
        public int Experience { get; private set; } = 0;

        public const int MAX_LEVEL = 30;

        /// <summary>
        /// EXP necessaria per salire al livello successivo.
        /// Formula: (int)(100 * Math.Pow(level, 1.8))
        /// </summary>
        public int ExperienceToNextLevel
            => Level >= MAX_LEVEL ? int.MaxValue
             : (int)(100 * Math.Pow(Level, 1.8));

        // ------------------------------------------------------------------
        //  Statistiche base (calcolate da livello + growth)
        // ------------------------------------------------------------------

        /// <summary>HP massimi calcolati dal livello.</summary>
        public int MaxHP  => BaseStatAt(Level, _baseHP,  Growth.HP);

        /// <summary>SP massimi calcolati dal livello.</summary>
        public int MaxSP  => BaseStatAt(Level, _baseSP,  Growth.SP);

        /// <summary>Attacco fisico base.</summary>
        public int BaseATK => BaseStatAt(Level, _baseATK, Growth.ATK);

        /// <summary>Attacco magico base.</summary>
        public int BaseMAG => BaseStatAt(Level, _baseMAG, Growth.MAG);

        /// <summary>Difesa fisica base.</summary>
        public int BaseDEF => BaseStatAt(Level, _baseDEF, Growth.DEF);

        /// <summary>Resistenza magica base.</summary>
        public int BaseRES => BaseStatAt(Level, _baseRES, Growth.RES);

        /// <summary>Velocità base (determina ordine CTB).</summary>
        public int BaseSPD => BaseStatAt(Level, _baseSPD, Growth.SPD);

        /// <summary>Fortuna base (critico, evasione, drop rate).</summary>
        public int BaseLUK => BaseStatAt(Level, _baseLUK, Growth.LUK);

        // Valori base al livello 1 — differenti per ogni personaggio
        private readonly int _baseHP;
        private readonly int _baseSP;
        private readonly int _baseATK;
        private readonly int _baseMAG;
        private readonly int _baseDEF;
        private readonly int _baseRES;
        private readonly int _baseSPD;
        private readonly int _baseLUK;

        // ------------------------------------------------------------------
        //  Statistiche correnti
        // ------------------------------------------------------------------

        private int _currentHP;
        private int _currentSP;

        /// <summary>HP correnti (0 = KO).</summary>
        public int CurrentHP
        {
            get => _currentHP;
            private set => _currentHP = Math.Clamp(value, 0, MaxHP);
        }

        /// <summary>SP correnti.</summary>
        public int CurrentSP
        {
            get => _currentSP;
            private set => _currentSP = Math.Clamp(value, 0, MaxSP);
        }

        public bool IsKO       => CurrentHP <= 0;
        public bool IsFullHP   => CurrentHP >= MaxHP;
        public bool IsFullSP   => CurrentSP >= MaxSP;

        // ------------------------------------------------------------------
        //  Resistenze elementali
        // ------------------------------------------------------------------

        /// <summary>
        /// Moltiplicatori resistenza per elemento.
        /// 0.0 = immune | 1.0 = danno normale | 2.0 = vulnerabile.
        /// </summary>
        public Dictionary<ElementType, float> ElementalResistances { get; }
            = new()
            {
                [ElementType.Neutro]   = 1.0f,
                [ElementType.Luce]     = 1.0f,
                [ElementType.Ombra]    = 1.0f,
                [ElementType.Fuoco]    = 1.0f,
                [ElementType.Ghiaccio] = 1.0f,
                [ElementType.Terra]    = 1.0f,
                [ElementType.Vento]    = 1.0f,
            };

        // ------------------------------------------------------------------
        //  Stati di alterazione
        // ------------------------------------------------------------------

        private readonly List<StatusEffect> _statusEffects = new();

        /// <summary>Lista degli stati attivi (sola lettura).</summary>
        public IReadOnlyList<StatusEffect> StatusEffects => _statusEffects;

        // ------------------------------------------------------------------
        //  SISTEMA MORALE (esclusivo di Kael)
        // ------------------------------------------------------------------

        private int _morale = 100;

        /// <summary>True solo per Kael — gli altri personaggi ignorano il Morale.</summary>
        public bool HasMorale { get; }

        /// <summary>
        /// Morale di Kael (0-100). Ignorato per tutti gli altri personaggi.
        /// Influenza ATK (-15% sotto 30), ordini (-25% chance rifiuto sotto 10).
        /// </summary>
        public int Morale
        {
            get => _morale;
            private set => _morale = Math.Clamp(value, 0, 100);
        }

        public bool IsMoraleLow      => HasMorale && Morale < 30;
        public bool IsMoraleCritical => HasMorale && Morale < 10;
        public bool IsMoralePerfect  => HasMorale && Morale == 100;

        // ------------------------------------------------------------------
        //  EVENTI
        // ------------------------------------------------------------------

        /// <summary>Sparato quando HP cambia (danno, cura, level up).</summary>
        public event Action<Character, int, int>? OnHPChanged;
        // parametri: (character, oldHP, newHP)

        /// <summary>Sparato quando SP cambia.</summary>
        public event Action<Character, int, int>? OnSPChanged;
        // parametri: (character, oldSP, newSP)

        /// <summary>Sparato al level up. Parametri: (vecchio livello, nuovo livello).</summary>
        public event Action<Character, int, int>? OnLevelUp;

        /// <summary>Sparato quando il Morale cambia. Parametri: (vecchio, nuovo, causa).</summary>
        public event Action<Character, int, int, string>? OnMoraleChanged;

        /// <summary>Sparato quando un StatusEffect viene aggiunto o rimosso.</summary>
        public event Action<Character, StatusEffect, bool>? OnStatusEffectChanged;
        // parametri: (character, effect, wasAdded)  — wasAdded=false → rimosso

        // ------------------------------------------------------------------
        //  COSTRUTTORE PRIVATO + FACTORY
        // ------------------------------------------------------------------

        private Character(
            string         characterId,
            string         displayName,
            CharacterClass characterClass,
            GrowthProfile  growth,
            int baseHP,  int baseSP,
            int baseATK, int baseMAG,
            int baseDEF, int baseRES,
            int baseSPD, int baseLUK,
            bool hasMorale = false)
        {
            CharacterId = characterId;
            DisplayName = displayName;
            Class       = characterClass;
            Growth      = growth;
            HasMorale   = hasMorale;

            _baseHP  = baseHP;
            _baseSP  = baseSP;
            _baseATK = baseATK;
            _baseMAG = baseMAG;
            _baseDEF = baseDEF;
            _baseRES = baseRES;
            _baseSPD = baseSPD;
            _baseLUK = baseLUK;

            // Inizializza HP e SP ai massimi
            _currentHP = MaxHP;
            _currentSP = MaxSP;
        }

        // ------------------------------------------------------------------
        //  FACTORY — personaggi principali
        // ------------------------------------------------------------------

        /// <summary>
        /// Crea Kael Dawnford al livello 1.
        /// Ex-capitano: ATK e DEF solide, Morale attivo.
        /// </summary>
        public static Character CreateKael() => new(
            characterId:    "KAEL",
            displayName:    "Kael Dawnford",
            characterClass: CharacterClass.Guerriero,
            growth:         GrowthProfile.Kael,
            baseHP:  95,  baseSP:  20,
            baseATK: 18,  baseMAG: 6,
            baseDEF: 14,  baseRES: 8,
            baseSPD: 10,  baseLUK: 8,
            hasMorale: true);

        /// <summary>
        /// Crea Lyra Ashveil al livello 1.
        /// Custode dei Sigilli: MAG e RES elevate, SP abbondante.
        /// </summary>
        public static Character CreateLyra() => new(
            characterId:    "LYRA",
            displayName:    "Lyra Ashveil",
            characterClass: CharacterClass.Custode,
            growth:         GrowthProfile.Lyra,
            baseHP:  70,  baseSP:  35,
            baseATK: 8,   baseMAG: 20,
            baseDEF: 8,   baseRES: 16,
            baseSPD: 12,  baseLUK: 10);

        /// <summary>
        /// Crea Voran il Silente al livello 1.
        /// Mago anziano: SP massimo altissimo, MAG forte, HP fragile.
        /// </summary>
        public static Character CreateVoran() => new(
            characterId:    "VORAN",
            displayName:    "Voran il Silente",
            characterClass: CharacterClass.Mago,
            growth:         GrowthProfile.Voran,
            baseHP:  55,  baseSP:  50,
            baseATK: 5,   baseMAG: 18,
            baseDEF: 6,   baseRES: 14,
            baseSPD: 7,   baseLUK: 10);

        /// <summary>
        /// Crea Sera al livello 1.
        /// Esploratore: SPD altissima, statistiche offensive contenute.
        /// </summary>
        public static Character CreateSera() => new(
            characterId:    "SERA",
            displayName:    "Sera",
            characterClass: CharacterClass.Esploratore,
            growth:         GrowthProfile.Sera,
            baseHP:  50,  baseSP:  25,
            baseATK: 10,  baseMAG: 8,
            baseDEF: 5,   baseRES: 7,
            baseSPD: 18,  baseLUK: 14);

        // ------------------------------------------------------------------
        //  STATISTICHE DI BATTAGLIA
        // ------------------------------------------------------------------

        /// <summary>
        /// Restituisce le statistiche finali usate dal BattleSystem.
        /// Combina le statistiche base con i bonus dell'equipaggiamento (DeckStats)
        /// e i modificatori degli stati attivi.
        /// </summary>
        public BattleStats GetBattleStats(DeckStats? equipment = null)
        {
            int atk = BaseATK + (equipment?.ATK ?? 0);
            int mag = BaseMAG + (equipment?.MAG ?? 0);
            int def = BaseDEF + (equipment?.DEF ?? 0);
            int res = BaseRES + (equipment?.RES ?? 0);
            int spd = BaseSPD + (equipment?.SPD ?? 0);
            int luk = BaseLUK;
            int maxHP = MaxHP  + (equipment?.HP  ?? 0);
            int maxSP = MaxSP  + (equipment?.SP  ?? 0);

            // Applica modificatori stati attivi
            foreach (var fx in _statusEffects.Where(f => f.IsActive))
            {
                switch (fx.Type)
                {
                    case StatusEffectType.Potenziato:
                        atk = (int)(atk * fx.Intensity);
                        mag = (int)(mag * fx.Intensity);
                        break;
                    case StatusEffectType.Protetto:
                        def = (int)(def * fx.Intensity);
                        res = (int)(res * fx.Intensity);
                        break;
                    case StatusEffectType.Rallentato:
                        spd = (int)(spd * (2f - fx.Intensity));  // es. x0.7 → -30%
                        break;
                    case StatusEffectType.Velocizzato:
                        spd = (int)(spd * fx.Intensity);
                        break;
                    case StatusEffectType.Dubbio:
                        atk = (int)(atk * (2f - fx.Intensity));
                        mag = (int)(mag * (2f - fx.Intensity));
                        break;
                }
            }

            // Morale di Kael: sotto 30 riduce ATK del 15%
            if (HasMorale && IsMoraleLow)
                atk = (int)(atk * 0.85f);

            return new BattleStats
            {
                ATK   = Math.Max(1, atk),
                MAG   = Math.Max(1, mag),
                DEF   = Math.Max(0, def),
                RES   = Math.Max(0, res),
                SPD   = Math.Max(1, spd),
                LUK   = Math.Max(1, luk),
                MaxHP = Math.Max(1, maxHP),
                MaxSP = Math.Max(0, maxSP)
            };
        }

        // ------------------------------------------------------------------
        //  DANNI E CURE
        // ------------------------------------------------------------------

        /// <summary>
        /// Applica danno al personaggio.
        /// Ritorna i danni effettivi applicati (dopo il clamping).
        /// </summary>
        public int TakeDamage(int amount)
        {
            if (amount <= 0) return 0;

            int oldHP = CurrentHP;
            CurrentHP -= amount;
            int actual = oldHP - CurrentHP;

            OnHPChanged?.Invoke(this, oldHP, CurrentHP);
            return actual;
        }

        /// <summary>
        /// Ripristina HP al personaggio.
        /// Ritorna gli HP effettivamente curati.
        /// </summary>
        public int RestoreHP(int amount)
        {
            if (amount <= 0) return 0;

            int oldHP = CurrentHP;
            CurrentHP += amount;
            int actual = CurrentHP - oldHP;

            OnHPChanged?.Invoke(this, oldHP, CurrentHP);
            return actual;
        }

        /// <summary>Ripristina HP agli HP massimi.</summary>
        public void FullRestoreHP()
        {
            int oldHP = CurrentHP;
            CurrentHP = MaxHP;
            if (oldHP != CurrentHP)
                OnHPChanged?.Invoke(this, oldHP, CurrentHP);
        }

        /// <summary>
        /// Consuma SP per usare una carta.
        /// Ritorna false se gli SP sono insufficienti.
        /// </summary>
        public bool ConsumeSP(int amount)
        {
            if (amount < 0) return false;
            if (CurrentSP < amount) return false;

            int oldSP = CurrentSP;
            CurrentSP -= amount;
            OnSPChanged?.Invoke(this, oldSP, CurrentSP);
            return true;
        }

        /// <summary>Ripristina SP. Ritorna gli SP effettivamente ripristinati.</summary>
        public int RestoreSP(int amount)
        {
            if (amount <= 0) return 0;

            int oldSP = CurrentSP;
            CurrentSP += amount;
            int actual = CurrentSP - oldSP;

            OnSPChanged?.Invoke(this, oldSP, CurrentSP);
            return actual;
        }

        /// <summary>Ripristina SP agli SP massimi.</summary>
        public void FullRestoreSP()
        {
            int oldSP = CurrentSP;
            CurrentSP = MaxSP;
            if (oldSP != CurrentSP)
                OnSPChanged?.Invoke(this, oldSP, CurrentSP);
        }

        // ------------------------------------------------------------------
        //  STATI DI ALTERAZIONE
        // ------------------------------------------------------------------

        /// <summary>
        /// Applica uno stato di alterazione al personaggio.
        /// Se lo stato è già presente, aggiorna la durata (prende la maggiore).
        /// Gli stati psicologici vengono bloccati se Ancorato è attivo.
        /// </summary>
        public void ApplyStatus(StatusEffect effect)
        {
            // Blocca stati psicologici se Ancorato è attivo
            if (effect.IsPsychological && HasStatus(StatusEffectType.Ancorato))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Character] {DisplayName}: stato psicologico {effect.Type} bloccato da Ancorato.");
                return;
            }

            // Se lo stato è già presente, aggiorna la durata
            var existing = _statusEffects
                .FirstOrDefault(f => f.Type == effect.Type && f.IsActive);

            if (existing != null)
                _statusEffects.Remove(existing);

            _statusEffects.Add(effect);
            OnStatusEffectChanged?.Invoke(this, effect, wasAdded: true);
        }

        /// <summary>
        /// Rimuove tutti gli stati del tipo specificato.
        /// </summary>
        public void RemoveStatus(StatusEffectType type)
        {
            var toRemove = _statusEffects.Where(f => f.Type == type).ToList();
            foreach (var fx in toRemove)
            {
                _statusEffects.Remove(fx);
                OnStatusEffectChanged?.Invoke(this, fx, wasAdded: false);
            }
        }

        /// <summary>Rimuove tutti gli stati negativi.</summary>
        public void RemoveAllNegativeStatuses()
        {
            var toRemove = _statusEffects.Where(f => f.IsNegative).ToList();
            foreach (var fx in toRemove)
            {
                _statusEffects.Remove(fx);
                OnStatusEffectChanged?.Invoke(this, fx, wasAdded: false);
            }
        }

        /// <summary>Rimuove tutti gli stati (positivi e negativi).</summary>
        public void ClearAllStatuses()
        {
            foreach (var fx in _statusEffects.ToList())
                OnStatusEffectChanged?.Invoke(this, fx, wasAdded: false);
            _statusEffects.Clear();
        }

        /// <summary>True se il personaggio ha lo stato specificato attivo.</summary>
        public bool HasStatus(StatusEffectType type)
            => _statusEffects.Any(f => f.Type == type && f.IsActive);

        /// <summary>
        /// Aggiorna tutti gli stati attivi all'inizio del turno.
        /// Ritorna il danno totale da stati passivi (es. veleno).
        /// Rimuove automaticamente gli stati scaduti.
        /// </summary>
        public int ProcessTurnStartStatuses()
        {
            int totalDamage = 0;

            foreach (var fx in _statusEffects.ToList())
            {
                if (!fx.IsActive) continue;
                totalDamage += fx.OnTurnStart(this);

                if (!fx.IsActive)
                {
                    _statusEffects.Remove(fx);
                    OnStatusEffectChanged?.Invoke(this, fx, wasAdded: false);
                }
            }

            return totalDamage;
        }

        // ------------------------------------------------------------------
        //  SISTEMA MORALE (Kael)
        // ------------------------------------------------------------------

        /// <summary>
        /// Modifica il Morale di Kael.
        /// Non fa nulla su personaggi senza sistema Morale.
        /// </summary>
        public void ModifyMorale(int delta, string cause = "")
        {
            if (!HasMorale) return;

            int oldMorale = Morale;
            Morale += delta;

            if (oldMorale != Morale)
                OnMoraleChanged?.Invoke(this, oldMorale, Morale, cause);
        }

        /// <summary>
        /// Ritorna true se il Morale di Kael è critico e l'ordine viene rifiutato.
        /// Probabilità 25% quando Morale &lt; 10. Sempre false per altri personaggi.
        /// </summary>
        public bool RollMoraleRefusal()
        {
            if (!HasMorale || Morale >= 10) return false;
            return _rng.NextDouble() < 0.25;
        }

        private static readonly Random _rng = new Random();

        // ------------------------------------------------------------------
        //  PROGRESSIONE EXP / LEVEL UP
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiunge EXP al personaggio. Gestisce level up multipli.
        /// Ritorna il numero di livelli guadagnati (0 se nessuno).
        /// </summary>
        public int GainExperience(int amount)
        {
            if (amount <= 0 || Level >= MAX_LEVEL) return 0;

            int levelsGained = 0;
            Experience += amount;

            while (Level < MAX_LEVEL && Experience >= ExperienceToNextLevel)
            {
                Experience -= ExperienceToNextLevel;
                int oldLevel = Level;
                Level++;
                levelsGained++;

                // Level up: ripristina HP e SP ai nuovi massimi
                FullRestoreHP();
                FullRestoreSP();

                OnLevelUp?.Invoke(this, oldLevel, Level);

                System.Diagnostics.Debug.WriteLine(
                    $"[Character] {DisplayName} → Lv{Level}! " +
                    $"HP:{MaxHP} SP:{MaxSP} ATK:{BaseATK} DEF:{BaseDEF}");
            }

            return levelsGained;
        }

        // ------------------------------------------------------------------
        //  PERSISTENZA — integrazione SaveSystem
        // ------------------------------------------------------------------

        /// <summary>
        /// Applica i dati di un CharacterSaveData a questo personaggio.
        /// Chiamato da GameManager al caricamento di una partita.
        /// </summary>
        public void ApplySaveData(CharacterSaveData save)
        {
            // Ripristina livello ed EXP
            _level     = Math.Clamp(save.Level, 1, MAX_LEVEL);
            Experience = save.Experience;

            // Ripristina HP e SP (-1 = massimi)
            _currentHP = save.CurrentHP < 0 ? MaxHP
                       : Math.Clamp(save.CurrentHP, 0, MaxHP);
            _currentSP = save.CurrentSP < 0 ? MaxSP
                       : Math.Clamp(save.CurrentSP, 0, MaxSP);
        }

        /// <summary>
        /// Serializza lo stato corrente in un CharacterSaveData.
        /// Chiamato da SaveSystem prima di scrivere su disco.
        /// </summary>
        public CharacterSaveData ToSaveData(List<string> activeDeckIds)
            => new()
            {
                CharacterId = CharacterId,
                Level       = Level,
                Experience  = Experience,
                CurrentHP   = CurrentHP,
                CurrentSP   = CurrentSP,
                ActiveDeck  = activeDeckIds
            };

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        /// <summary>
        /// Calcola una statistica base al livello specificato.
        /// Formula: base + (int)(growth * (level - 1))
        /// </summary>
        private static int BaseStatAt(int level, int baseValue, float growth)
            => baseValue + (int)(growth * (level - 1));

        public override string ToString()
            => $"{DisplayName} Lv{Level} HP:{CurrentHP}/{MaxHP} SP:{CurrentSP}/{MaxSP}";
    }

    // =========================================================================
    //  BATTLE STATS — statistiche finali usate dal BattleSystem
    // =========================================================================

    /// <summary>
    /// Statistiche di battaglia calcolate da Character.GetBattleStats().
    /// Include bonus equipaggiamento e modificatori stati attivi.
    /// Immutabile — ricreata ogni volta che serve per riflettere i cambiamenti.
    /// </summary>
    public sealed class BattleStats
    {
        public int ATK   { get; init; }
        public int MAG   { get; init; }
        public int DEF   { get; init; }
        public int RES   { get; init; }
        public int SPD   { get; init; }
        public int LUK   { get; init; }
        public int MaxHP { get; init; }
        public int MaxSP { get; init; }

        /// <summary>Probabilità critico base (0.0..0.4, cap 40%).</summary>
        public float CritChance => Math.Min(LUK / 200f, 0.40f);

        public override string ToString()
            => $"ATK:{ATK} MAG:{MAG} DEF:{DEF} RES:{RES} SPD:{SPD} LUK:{LUK}";
    }

    // =========================================================================
    //  STAT CONTEXT EXTENSION — FromCharacter definito qui per evitare
    //  dipendenze circolari tra CardModel.cs e Character.cs
    // =========================================================================

    /// <summary>
    /// Metodi di estensione su StatContext che richiedono Character.
    /// Definiti in questo file per evitare la dipendenza circolare
    /// Core/CardModel.cs ↔ Core/Character.cs.
    /// </summary>
    public static class StatContextExtensions
    {
        /// <summary>
        /// Crea un StatContext popolato con le statistiche di battaglia
        /// del personaggio specificato.
        /// </summary>
        public static StatContext FromCharacter(Character character, DeckStats? equipment = null)
        {
            var bs = character.GetBattleStats(equipment);
            return new StatContext
            {
                ATK = bs.ATK,
                MAG = bs.MAG,
                DEF = bs.DEF,
                RES = bs.RES,
                SPD = bs.SPD,
                LUK = bs.LUK,
                HP  = bs.MaxHP,
                SP  = bs.MaxSP,
                LVL = character.Level
            };
        }
    }
}
