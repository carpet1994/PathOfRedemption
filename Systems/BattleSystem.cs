// =============================================================================
//  La Via della Redenzione — Systems/BattleSystem.cs
//  Package : com.refa.valdrath
//  Prompt  : 11 — Logica core del combattimento a turni (CTB / FFX-like)
//
//  Visuale: side view FF1/FF3 — gruppo a sinistra, nemici a destra.
//           Sprite con animazioni (OnAttackHitFrame sincronizzato da SpriteSheet).
//
//  Architettura:
//    BattleSystem è puro C# — zero dipendenze da MAUI.
//    La BattleScreen ascolta gli eventi esposti e aggiorna la UI.
//    Il SigilSystem di Lyra è un hook separato (Prompt 12).
//
//  Formule danno (Prompt 40):
//    Fisico: (ATK_user * scaling - DEF_target * 0.5) * elemMul * statusMul * ±5%
//    Magico: (MAG_user * scaling - RES_target * 0.5) * elemMul * statusMul * ±5%
//    Critico: ×1.5 (chance = LUK/200, cap 40%)
//    Morale Kael: -15% ATK quando Morale < 30 (via Character.GetBattleStats)
//
//  CTB Timeline:
//    Ordine turni per SPD decrescente. RebuildTurnQueue() ad ogni round.
//    GetTimelinePreview(7) per la UI stile FFX.
//
//  CORREZIONI BUG (rispetto alla versione precedente):
//
//  BUG 1 — StatusEffect.TurnsLeft / OnTurnStart delegate non esistono.
//    Character.StatusEffect espone Duration (int) e OnTurnStart(Character) come
//    metodo, non come delegate. TickStatuses() riscritta per usare l'API corretta:
//    chiama effect.OnTurnStart(owner) che ritorna int danno e decrementa Duration
//    internamente. ConsumeStun() usa RemoveStatus(Stordito) su Character.
//
//  BUG 2 — Character non ha IsKael, ApplyMoraleDelta, GetMoraleAtkMultiplier,
//    né proprietà .ATK/.MAG/.DEF/.RES/.SPD/.LUK dirette.
//    - IsKael       → c.CharacterId == "KAEL"
//    - ApplyMoraleDelta(n) → c.ModifyMorale(n)
//    - GetMoraleAtkMultiplier() → inline: IsMoraleLow ? 0.85f : 1.0f
//    - .ATK/.MAG/.DEF/.RES/.SPD/.LUK → c.GetBattleStats() per valori finali
//      oppure c.BaseSPD per la sola velocità nell'ordinamento CTB.
//
//  BUG 3 — BattleTarget.CurrentHP setter e BattleSystem scrivevano direttamente
//    su Character.CurrentHP / CurrentSP che sono private set.
//    - Danni a personaggi → character.TakeDamage(amount)
//    - Cure a personaggi  → character.RestoreHP(amount)
//    - Consumo SP         → character.ConsumeSP(cost)
//    - Recupero SP        → character.RestoreSP(1)
//    BattleTarget.CurrentHP setter rimosso per gli alleati; usa i metodi pubblici.
//
//  BUG 4 — StatusEffectType.Paura non esiste in GameEnums.cs.
//    Rimosso dal check IsConditionMet; rimangono Depresso e Dubbio.
//
//  BUG 5 — Character.ActiveStatusEffects non esiste (privato tramite _statusEffects).
//    Character espone IReadOnlyList<StatusEffect> StatusEffects.
//    - Aggiunta stato  → character.ApplyStatus(effect)
//    - Tick stati      → character.ProcessTurnStartStatuses() (già in Character)
//    - HasStatus       → character.HasStatus(type)
//    Per i nemici (EnemyInstance) ActiveStatusEffects rimane pubblico perché
//    EnemyInstance è definita in BattleSystem e non ha le stesse restrizioni.
//
//  BUG 6 — EffectiveSpeed usava t.Ally!.SPD che non esiste (si chiama BaseSPD).
//    Corretto in BaseSPD, con modificatori stati applicati inline.
// =============================================================================

using LaViaDellaRedenzione.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LaViaDellaRedenzione.Systems
{
    // =========================================================================
    //  ENEMY INSTANCE — nemico vivo in campo
    // =========================================================================

    /// <summary>
    /// Istanza runtime di un nemico in battaglia.
    /// Tiene HP correnti e stati separati dal template Enemy (immutabile).
    /// </summary>
    public sealed class EnemyInstance
    {
        public Enemy Template { get; }
        public int CurrentHP { get; set; }
        public List<StatusEffect> ActiveStatusEffects { get; } = new();

        /// <summary>Indice del nemico nella lista (per la side view FF).</summary>
        public int SlotIndex { get; init; }

        public EnemyInstance(Enemy template, int slotIndex = 0)
        {
            Template  = template;
            CurrentHP = template.MaxHP;
            SlotIndex = slotIndex;
        }

        public bool IsAlive => CurrentHP > 0;

        public float GetResistance(ElementType e) => Template.GetResistance(e);

        /// <summary>SPD effettiva dopo stati alterazione.</summary>
        public int EffectiveSPD
        {
            get
            {
                float spd = Template.SPD;
                if (HasStatus(StatusEffectType.Rallentato))  spd *= 0.70f;
                if (HasStatus(StatusEffectType.Velocizzato)) spd *= 1.25f;
                return Math.Max(1, (int)spd);
            }
        }

        public bool HasStatus(StatusEffectType t)
            => ActiveStatusEffects.Any(s => s.Type == t && s.IsActive);

        public float HpPercent
            => Template.MaxHP <= 0 ? 0f : (float)CurrentHP / Template.MaxHP;

        public override string ToString() => $"{Template.Name} [{CurrentHP}/{Template.MaxHP}]";
    }

    // =========================================================================
    //  BATTLE TARGET — bersaglio unificato (personaggio o nemico)
    // =========================================================================

    /// <summary>
    /// Wrapper che astrae personaggi e nemici per la risoluzione degli effetti.
    ///
    /// NOTA IMPORTANTE sull'accesso a HP:
    ///   Per i nemici (Foe != null) CurrentHP è read/write direttamente.
    ///   Per gli alleati (Ally != null) leggere CurrentHP è possibile, ma
    ///   scriverlo NON lo è (Character.CurrentHP ha private set).
    ///   Tutti i cambiamenti di HP/SP sugli alleati devono usare i metodi
    ///   pubblici del Character: TakeDamage(), RestoreHP(), ConsumeSP(), RestoreSP().
    /// </summary>
    public sealed class BattleTarget
    {
        public Character?     Ally { get; }
        public EnemyInstance? Foe  { get; }

        public bool IsAlly  => Ally != null;
        public bool IsEnemy => Foe  != null;

        public static BattleTarget FromAlly (Character c)     => new(c, null);
        public static BattleTarget FromEnemy(EnemyInstance e) => new(null, e);

        private BattleTarget(Character? ally, EnemyInstance? foe)
        {
            Ally = ally;
            Foe  = foe;
        }

        public string DisplayName
            => Ally?.DisplayName ?? Foe?.Template.Name ?? "?";

        public bool IsAlive
            => IsAlly ? !Ally!.IsKO : Foe!.IsAlive;

        /// <summary>
        /// Lettura HP corrente — valida per alleati e nemici.
        /// Per modificare gli HP di un alleato usare Ally.TakeDamage() o Ally.RestoreHP().
        /// </summary>
        public int CurrentHP
            => IsAlly ? Ally!.CurrentHP : Foe!.CurrentHP;

        public int MaxHP
            => IsAlly ? Ally!.MaxHP : Foe!.Template.MaxHP;

        public float HpPercent
            => MaxHP <= 0 ? 0f : (float)CurrentHP / MaxHP;

        public float GetResistance(ElementType e)
            => IsAlly
                ? Ally!.ElementalResistances.GetValueOrDefault(e, 1f)
                : Foe!.GetResistance(e);

        public override string ToString() => DisplayName;
    }

    // =========================================================================
    //  EVENT ARGS
    // =========================================================================

    public sealed class BattleEndedEventArgs : EventArgs
    {
        public BattleState Outcome    { get; init; }
        public int         ExpGained  { get; init; }
        public int         GoldGained { get; init; }
        public bool        Fled       { get; init; }
        public Dictionary<string, int> DroppedCards { get; init; } = new();
    }

    public sealed class DamageResolvedEventArgs : EventArgs
    {
        public BattleTarget Target      { get; init; } = null!;
        public int          Amount      { get; init; }
        public bool         WasCritical { get; init; }
        public ElementType  Element     { get; init; }
        public bool         WasHealing  { get; init; }
    }

    public sealed class TurnChangedEventArgs : EventArgs
    {
        public BattleTarget? CurrentActor { get; init; }
        public int           RoundIndex   { get; init; }
    }

    public sealed class MoraleChangedEventArgs : EventArgs
    {
        public int    OldValue { get; init; }
        public int    NewValue { get; init; }
        public string Cause    { get; init; } = string.Empty;
    }

    // =========================================================================
    //  BATTLE SYSTEM
    // =========================================================================

    /// <summary>
    /// Sistema di combattimento a turni CTB (Conditional Turn Battle).
    /// Ordine basato su SPD, timeline visuale a 7 turni, carte, difesa, fuga.
    /// </summary>
    public sealed class BattleSystem
    {
        // ------------------------------------------------------------------
        //  Costanti
        // ------------------------------------------------------------------

        public const int TimelinePreviewCount = 7;
        private const float DAMAGE_VARIANCE   = 0.05f;

        // ------------------------------------------------------------------
        //  Stato interno
        // ------------------------------------------------------------------

        private readonly Random _rng = new();

        private readonly List<Character>     _party   = new();
        private readonly List<EnemyInstance> _enemies = new();
        private readonly List<BattleTarget>  _turnQueue = new();

        private int  _roundIndex;
        private int  _turnIndexInRound;
        private bool _battleOver;

        /// <summary>
        /// Personaggi in modalità Difendi — DEF×2 fino al prossimo turno.
        /// Resettato all'inizio del turno del personaggio.
        /// </summary>
        private readonly HashSet<string> _defending
            = new(StringComparer.OrdinalIgnoreCase);

        // ------------------------------------------------------------------
        //  Stato pubblico
        // ------------------------------------------------------------------

        public BattleState    State       { get; private set; } = BattleState.PlayerTurn;
        public BattleTarget?  ActiveActor { get; private set; }
        public int            RoundIndex  => _roundIndex;

        public IReadOnlyList<Character>     Party   => _party;
        public IReadOnlyList<EnemyInstance> Enemies => _enemies;

        // ------------------------------------------------------------------
        //  EVENTI
        // ------------------------------------------------------------------

        public event EventHandler<BattleState>?             StateChanged;
        public event EventHandler<TurnChangedEventArgs>?    TurnChanged;
        public event EventHandler<DamageResolvedEventArgs>? DamageResolved;
        public event EventHandler<string>?                  BattleLog;
        public event EventHandler<BattleEndedEventArgs>?    BattleEnded;
        public event EventHandler<MoraleChangedEventArgs>?  MoraleChanged;

        /// <summary>
        /// Sparato ogni volta che una carta viene giocata con successo.
        /// SigilSystem si iscrive per aggiornare i sigilli di Lyra.
        /// </summary>
        public event Action<Character, CardModel>? OnCardPlayed;

        // ------------------------------------------------------------------
        //  HOOK SIGILLI LYRA
        // ------------------------------------------------------------------

        /// <summary>
        /// Hook opzionale per il SigilSystem.
        /// Restituisce il bonus elemental di Lyra per l'elemento dato (es. 1.25f = +25%).
        /// </summary>
        public Func<Character, ElementType, float>? GetSigilElementalBonus { get; set; }

        // ------------------------------------------------------------------
        //  INIZIALIZZAZIONE
        // ------------------------------------------------------------------

        public void StartBattle(
            IEnumerable<Character> party,
            IEnumerable<Enemy>     enemyTemplates)
        {
            _party.Clear();
            _enemies.Clear();
            _turnQueue.Clear();
            _defending.Clear();
            _battleOver       = false;
            _roundIndex       = 0;
            _turnIndexInRound = 0;

            foreach (var c in party)
                _party.Add(c);

            int slot = 0;
            foreach (var e in enemyTemplates)
                _enemies.Add(new EnemyInstance(e, slot++));

            RebuildTurnQueue();
            ChangeState(BattleState.PlayerTurn);
            BeginNextActorTurn();
        }

        // ------------------------------------------------------------------
        //  CTB TIMELINE
        // ------------------------------------------------------------------

        private void RebuildTurnQueue()
        {
            _turnQueue.Clear();

            var actors = new List<BattleTarget>();

            foreach (var c in _party.Where(x => !x.IsKO))
                actors.Add(BattleTarget.FromAlly(c));
            foreach (var e in _enemies.Where(x => x.IsAlive))
                actors.Add(BattleTarget.FromEnemy(e));

            foreach (var a in actors.OrderByDescending(EffectiveSpeed))
                _turnQueue.Add(a);
        }

        // BUG 6 FIX: usava t.Ally!.SPD (non esiste) — corretto in BaseSPD
        private int EffectiveSpeed(BattleTarget t)
        {
            if (t.IsAlly)
            {
                float s = t.Ally!.BaseSPD;
                if (t.Ally.HasStatus(StatusEffectType.Rallentato))  s *= 0.70f;
                if (t.Ally.HasStatus(StatusEffectType.Velocizzato)) s *= 1.25f;
                return Math.Max(1, (int)s);
            }
            return t.Foe!.EffectiveSPD;
        }

        /// <summary>
        /// Restituisce i prossimi N turni previsti per la timeline UI (stile FFX).
        /// </summary>
        public IReadOnlyList<BattleTarget> GetTimelinePreview(
            int count = TimelinePreviewCount)
        {
            var list = new List<BattleTarget>();
            if (_turnQueue.Count == 0 || _battleOver) return list;

            int n   = Math.Min(count, TimelinePreviewCount);
            int idx = _turnIndexInRound;

            for (int i = 0; i < n * 3 && list.Count < n; i++)
            {
                if (idx >= _turnQueue.Count) idx = 0;
                var t = _turnQueue[idx++];
                if (t.IsAlive) list.Add(t);
            }

            return list;
        }

        // ------------------------------------------------------------------
        //  GESTIONE TURNI
        // ------------------------------------------------------------------

        private void BeginNextActorTurn()
        {
            if (CheckBattleEnd()) return;

            if (_turnIndexInRound >= _turnQueue.Count)
            {
                _roundIndex++;
                _turnIndexInRound = 0;
                OnRoundStart();
                RebuildTurnQueue();
            }

            while (_turnIndexInRound < _turnQueue.Count)
            {
                var actor = _turnQueue[_turnIndexInRound];

                if (!actor.IsAlive)
                {
                    _turnIndexInRound++;
                    continue;
                }

                // Stordito: salta il turno
                if (IsStunned(actor))
                {
                    Log($"{actor.DisplayName} è stordito e salta il turno.");
                    ConsumeStun(actor);
                    AdvanceTurn();
                    continue;
                }

                // Rimuovi difesa attiva all'inizio del proprio turno
                if (actor.IsAlly)
                    _defending.Remove(actor.Ally!.CharacterId);

                // BUG 2 FIX: IsKael → CharacterId == "KAEL"
                if (actor.IsAlly && actor.Ally!.CharacterId == "KAEL")
                {
                    if (actor.Ally.RollMoraleRefusal())
                    {
                        Log("Il peso del passato trattiene Kael — ordine ignorato.");
                        AdvanceTurn();
                        BeginNextActorTurn();
                        return;
                    }
                }

                ActiveActor = actor;
                TurnChanged?.Invoke(this, new TurnChangedEventArgs
                {
                    CurrentActor = actor,
                    RoundIndex   = _roundIndex
                });

                if (actor.IsEnemy)
                {
                    ChangeState(BattleState.EnemyTurn);
                    ExecuteEnemyTurn(actor.Foe!);
                }
                else
                {
                    ChangeState(BattleState.PlayerTurn);
                }

                return;
            }

            BeginNextActorTurn();
        }

        private void OnRoundStart()
        {
            // BUG 1 + BUG 5 FIX: usa ProcessTurnStartStatuses() di Character
            // invece di accedere direttamente a ActiveStatusEffects
            foreach (var c in _party)
            {
                int poisonDmg = c.ProcessTurnStartStatuses();
                if (poisonDmg > 0)
                {
                    int actual = c.TakeDamage(poisonDmg);
                    DamageResolved?.Invoke(this, new DamageResolvedEventArgs
                    {
                        Target      = BattleTarget.FromAlly(c),
                        Amount      = actual,
                        WasCritical = false,
                        Element     = ElementType.Neutro,
                        WasHealing  = false
                    });
                    Log($"{c.DisplayName} subisce {actual} danni da veleno.");
                    CheckBattleEnd();
                }
            }

            // Per i nemici manteniamo la gestione diretta (EnemyInstance non
            // ha il pattern Character)
            foreach (var e in _enemies)
                TickEnemyStatuses(e.ActiveStatusEffects);
        }

        private static void TickEnemyStatuses(List<StatusEffect> list)
        {
            // BUG 1 FIX: usa Duration invece di TurnsLeft
            for (int i = list.Count - 1; i >= 0; i--)
            {
                // Duration è privato — usiamo IsActive come sentinella.
                // StatusEffect.OnTurnStart() decrementa Duration internamente.
                // Per i nemici non c'è un Character owner, passiamo null.
                // OnTurnStart gestisce null owner senza crashare per Avvelenato
                // (ritorna 0 danno se owner è null).
                list[i].OnTurnStart(null!);
                if (!list[i].IsActive)
                    list.RemoveAt(i);
            }
        }

        private void AdvanceTurn()
        {
            _turnIndexInRound++;
            if (_turnIndexInRound >= _turnQueue.Count)
            {
                _roundIndex++;
                _turnIndexInRound = 0;
                OnRoundStart();
                RebuildTurnQueue();
            }
        }

        private static bool IsStunned(BattleTarget t)
            => t.IsAlly
                ? t.Ally!.HasStatus(StatusEffectType.Stordito)
                : t.Foe!.HasStatus(StatusEffectType.Stordito);

        // BUG 1 + BUG 5 FIX: usa RemoveStatus() su Character invece di
        // scrivere direttamente su ActiveStatusEffects
        private static void ConsumeStun(BattleTarget t)
        {
            if (t.IsAlly)
            {
                t.Ally!.RemoveStatus(StatusEffectType.Stordito);
            }
            else
            {
                var list = t.Foe!.ActiveStatusEffects;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Type != StatusEffectType.Stordito) continue;
                    list.RemoveAt(i);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------
        //  AZIONI GIOCATORE
        // ------------------------------------------------------------------

        /// <summary>
        /// Gioca una carta. Chiamato dalla BattleScreen quando il giocatore
        /// seleziona una carta e un bersaglio.
        /// </summary>
        public bool TryPlayCard(
            Character                  user,
            CardModel                  card,
            IReadOnlyList<BattleTarget> explicitTargets)
        {
            if (_battleOver
                || ActiveActor?.Ally != user
                || State != BattleState.PlayerTurn)
                return false;

            // BUG 3 FIX: ConsumeSP ritorna false se SP insufficienti
            if (!user.ConsumeSP(card.SpCost))
            {
                Log($"{user.DisplayName}: SP insufficienti ({user.CurrentSP}/{card.SpCost}).");
                return false;
            }

            ChangeState(BattleState.AnimatingAction);
            OnCardPlayed?.Invoke(user, card);

            foreach (var fx in card.Effects)
            {
                var targets = ResolveTargets(user, fx, explicitTargets);
                foreach (var t in targets)
                    ResolveEffect(user, fx, card, t);
            }

            if (!CheckBattleEnd())
                ChangeState(BattleState.PlayerTurn);

            return true;
        }

        /// <summary>
        /// API granulare — risolve un singolo effetto su una lista di bersagli.
        /// Usata da SigilSystem per effetti speciali.
        /// </summary>
        public void UseCardEffect(
            Character                  user,
            CardEffect                 effect,
            CardModel                  sourceCard,
            IReadOnlyList<BattleTarget> targets)
        {
            foreach (var t in targets)
                ResolveEffect(user, effect, sourceCard, t);
        }

        /// <summary>
        /// Il giocatore sceglie Difendi: DEF×2 per questo turno, recupera 1 SP.
        /// </summary>
        public void Defend(Character user)
        {
            if (ActiveActor?.Ally != user || State != BattleState.PlayerTurn) return;

            _defending.Add(user.CharacterId);
            // BUG 3 FIX: usa RestoreSP() invece di assegnare CurrentSP direttamente
            user.RestoreSP(1);
            Log($"{user.DisplayName} si difende e recupera 1 SP.");
            ChangeState(BattleState.AnimatingAction);
        }

        /// <summary>
        /// Usa un oggetto dall'inventario.
        /// </summary>
        public bool TryUseItem(string itemId, Character user, BattleTarget? target)
        {
            Log($"Oggetto {itemId} usato da {user.DisplayName} (InventorySystem non ancora collegato).");
            ChangeState(BattleState.AnimatingAction);
            return true;
        }

        /// <summary>
        /// Tenta la fuga. 50% base. Riduce il Morale di Kael di 10 (sempre).
        /// </summary>
        public bool TryFlee(Character initiator)
        {
            if (State != BattleState.PlayerTurn || ActiveActor?.Ally != initiator)
                return false;

            // BUG 2 FIX: ApplyMoraleDelta → ModifyMorale
            if (initiator.CharacterId == "KAEL")
            {
                int oldMorale = initiator.Morale;
                initiator.ModifyMorale(-10, "Tentativo di fuga");
                MoraleChanged?.Invoke(this, new MoraleChangedEventArgs
                {
                    OldValue = oldMorale,
                    NewValue = initiator.Morale,
                    Cause    = "Tentativo di fuga"
                });
            }

            bool success = _rng.NextDouble() < 0.5;

            if (success)
            {
                Log($"{initiator.DisplayName} è fuggito dalla battaglia.");
                ChangeState(BattleState.Fleeing);
                EndBattle(BattleState.Fleeing, fled: true);
                return true;
            }

            Log("Fuga fallita.");
            ChangeState(BattleState.AnimatingAction);
            return false;
        }

        /// <summary>
        /// Chiamato dalla BattleScreen al termine dell'animazione del turno giocatore.
        /// </summary>
        public void EndPlayerAnimationPhase()
        {
            if (_battleOver) return;
            if (State == BattleState.AnimatingAction && ActiveActor?.IsAlly == true)
            {
                AdvanceTurn();
                BeginNextActorTurn();
            }
        }

        // ------------------------------------------------------------------
        //  RISOLUZIONE EFFETTI
        // ------------------------------------------------------------------

        private List<BattleTarget> ResolveTargets(
            Character                  user,
            CardEffect                 fx,
            IReadOnlyList<BattleTarget> explicitTargets)
        {
            var list = new List<BattleTarget>();

            switch (fx.Target)
            {
                case TargetType.Self:
                    list.Add(BattleTarget.FromAlly(user));
                    break;

                case TargetType.SingleEnemy:
                    if (explicitTargets.Count > 0 && explicitTargets[0].IsEnemy)
                        list.Add(explicitTargets[0]);
                    else
                    {
                        var first = _enemies.FirstOrDefault(e => e.IsAlive);
                        if (first != null) list.Add(BattleTarget.FromEnemy(first));
                    }
                    break;

                case TargetType.AllEnemies:
                    foreach (var e in _enemies.Where(x => x.IsAlive))
                        list.Add(BattleTarget.FromEnemy(e));
                    break;

                case TargetType.SingleAlly:
                    if (explicitTargets.Count > 0 && explicitTargets[0].IsAlly)
                        list.Add(explicitTargets[0]);
                    else
                        list.Add(BattleTarget.FromAlly(user));
                    break;

                case TargetType.AllAllies:
                    foreach (var c in _party.Where(x => !x.IsKO))
                        list.Add(BattleTarget.FromAlly(c));
                    break;

                case TargetType.Random:
                    int hits  = Math.Max(1, fx.HitCount);
                    var alive = _enemies.Where(e => e.IsAlive).ToList();
                    for (int h = 0; h < hits && alive.Count > 0; h++)
                        list.Add(BattleTarget.FromEnemy(alive[_rng.Next(alive.Count)]));
                    break;
            }

            return list;
        }

        private void ResolveEffect(
            Character    user,
            CardEffect   fx,
            CardModel    card,
            BattleTarget target)
        {
            // BUG 2 FIX: GetBattleStats() per i valori finali incluso Morale
            var stats   = user.GetBattleStats();
            var ctx     = StatContextExtensions.FromCharacter(user);
            float value = fx.EvaluateValue(ctx);
            var element = card.ElementType;

            // Bonus sigilli di Lyra (hook opzionale)
            float sigilBonus = 1f;
            if (GetSigilElementalBonus != null && user.Class == CharacterClass.Custode)
                sigilBonus = GetSigilElementalBonus(user, element);

            switch (fx.EffectType)
            {
                case EffectType.Danno:
                    for (int h = 0; h < Math.Max(1, fx.HitCount); h++)
                        ApplyDamage(user, stats, target, value * sigilBonus, element,
                            physical: IsPhysicalDominant(fx, card));
                    break;

                case EffectType.Cura:
                    ApplyHeal(target, (int)(value * sigilBonus));
                    break;

                case EffectType.Buff:
                case EffectType.Debuff:
                case EffectType.Stato:
                    if (fx.StatusEffect.HasValue
                        && _rng.NextDouble() <= fx.StatusChance)
                        ApplyStatus(target, fx.StatusEffect.Value,
                            fx.StatusDuration, fx.StatusIntensity, card.Id);
                    break;

                case EffectType.Scudo:
                    Log($"Scudo attivato da {card.Name} su {target.DisplayName}.");
                    break;

                case EffectType.DrawCard:
                case EffectType.Evoca:
                    Log($"Effetto {fx.EffectType} da {card.Name} (futura implementazione).");
                    break;
            }

            CheckBattleEnd();
        }

        private static bool IsPhysicalDominant(CardEffect fx, CardModel card)
        {
            if (!string.IsNullOrEmpty(fx.ScalingFormula))
            {
                if (fx.ScalingFormula.Contains("MAG", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (fx.ScalingFormula.Contains("ATK", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return card.ElementType is ElementType.Neutro or ElementType.Terra;
        }

        // ------------------------------------------------------------------
        //  FORMULA DANNO
        // ------------------------------------------------------------------

        // BUG 2 FIX: riceve BattleStats precalcolate (include già Morale e stati)
        // invece di accedere a .ATK/.MAG direttamente su Character.
        private void ApplyDamage(
            Character    attacker,
            BattleStats  attackerStats,
            BattleTarget target,
            float        raw,
            ElementType  element,
            bool         physical)
        {
            if (!target.IsAlive) return;

            float baseAtk = physical ? attackerStats.ATK : attackerStats.MAG;

            float scaled = raw > 0 ? raw : baseAtk * 1.1f;

            float defStat = physical ? GetDef(target) : GetRes(target);
            float defMul  = 0.5f;

            if (target.IsAlly && _defending.Contains(target.Ally!.CharacterId))
                defMul = 1.0f;

            float baseDmg = Math.Max(1f, scaled - defStat * defMul);

            float elemMul   = Math.Max(0.01f, target.GetResistance(element));
            float statusMul = GetStatusDamageMultiplier(attacker);
            float final     = baseDmg * elemMul * statusMul;

            // Critico: LUK/200, cap 40%
            float critChance = Math.Min(0.40f, attackerStats.LUK / 200f);
            bool  crit       = _rng.NextDouble() < critChance;
            if (crit) final *= 1.5f;

            // Rumore ±5%
            float variance = 1f + (float)(_rng.NextDouble() * DAMAGE_VARIANCE * 2 - DAMAGE_VARIANCE);
            final *= variance;

            int amount = Math.Max(1, (int)final);

            // BUG 3 FIX: usa TakeDamage() per alleati, assegnazione diretta per nemici
            if (target.IsAlly)
            {
                target.Ally!.TakeDamage(amount);
            }
            else
            {
                target.Foe!.CurrentHP = Math.Max(0, target.Foe.CurrentHP - amount);
            }

            if (!target.IsAlive)
                HandleDeath(target);

            DamageResolved?.Invoke(this, new DamageResolvedEventArgs
            {
                Target      = target,
                Amount      = amount,
                WasCritical = crit,
                Element     = element,
                WasHealing  = false
            });

            Log($"{attacker.DisplayName} → {target.DisplayName}: " +
                $"{amount}{(crit ? " CRIT!" : "")} [{element}]");
        }

        private void ApplyHeal(BattleTarget target, int amount)
        {
            if (!target.IsAlive) return;

            int healed;

            // BUG 3 FIX: usa RestoreHP() per alleati
            if (target.IsAlly)
            {
                healed = target.Ally!.RestoreHP(Math.Max(1, amount));
            }
            else
            {
                healed = Math.Min(target.MaxHP - target.CurrentHP, Math.Max(1, amount));
                target.Foe!.CurrentHP += healed;
            }

            DamageResolved?.Invoke(this, new DamageResolvedEventArgs
            {
                Target     = target,
                Amount     = healed,
                WasHealing = true,
                Element    = ElementType.Luce
            });

            Log($"Cura su {target.DisplayName}: +{healed} HP.");
        }

        private void ApplyStatus(
            BattleTarget     target,
            StatusEffectType type,
            int              duration,
            float            intensity,
            string           sourceCardId)
        {
            // BUG 4 FIX: rimosso StatusEffectType.Paura (non esiste nell'enum)
            bool isPsychological = type is
                StatusEffectType.Depresso or
                StatusEffectType.Dubbio;

            if (target.IsAlly && target.Ally!.HasStatus(StatusEffectType.Ancorato)
                && isPsychological)
            {
                Log($"{target.DisplayName} è ancorato — stato {type} resistito.");
                return;
            }

            var se = new StatusEffect(type, Math.Max(1, duration), intensity, sourceCardId);

            // BUG 5 FIX: usa ApplyStatus() su Character invece di .Add() su lista privata
            if (target.IsAlly)
                target.Ally!.ApplyStatus(se);
            else
                target.Foe!.ActiveStatusEffects.Add(se);

            Log($"{target.DisplayName}: applicato stato {type} ({duration} turni).");
        }

        // ------------------------------------------------------------------
        //  MOLTIPLICATORI STATISTICI
        // ------------------------------------------------------------------

        private static float GetStatusDamageMultiplier(Character attacker)
        {
            float mul = 1f;
            if (attacker.HasStatus(StatusEffectType.Potenziato)) mul *= 1.20f;
            if (attacker.HasStatus(StatusEffectType.Depresso))   mul *= 0.85f;
            if (attacker.HasStatus(StatusEffectType.Dubbio))     mul *= 0.80f;
            return mul;
        }

        private static int GetDef(BattleTarget t)
            => t.IsAlly ? t.Ally!.GetBattleStats().DEF : t.Foe!.Template.DEF;

        private static int GetRes(BattleTarget t)
            => t.IsAlly ? t.Ally!.GetBattleStats().RES : t.Foe!.Template.RES;

        // ------------------------------------------------------------------
        //  AI NEMICI
        // ------------------------------------------------------------------

        private void ExecuteEnemyTurn(EnemyInstance foe)
        {
            float hpPct       = foe.HpPercent;
            bool  phase2      = hpPct < 0.50f && foe.Template.IsBoss;
            bool  phase3      = hpPct < 0.25f && foe.Template.IsBoss;
            bool  playerLowHP = _party.Any(c => !c.IsKO &&
                                (float)c.CurrentHP / c.MaxHP < 0.30f);

            var action = foe.Template.PickAction(hpPct, phase2, phase3, playerLowHP, _rng);

            if (action == null)
            {
                Log($"{foe.Template.Name} non ha azioni disponibili — passa.");
                AdvanceTurn();
                BeginNextActorTurn();
                return;
            }

            Log($"{foe.Template.Name} usa {action.DisplayName}.");

            var target = PickEnemyTarget(action);
            if (target == null)
            {
                AdvanceTurn();
                BeginNextActorTurn();
                return;
            }

            bool magical = action.EffectTags.Any(t =>
                t.Contains("magic", StringComparison.OrdinalIgnoreCase));

            // Danno psicologico (attacca Morale di Kael)
            if (action.EffectTags.Contains("morale_damage", StringComparison.OrdinalIgnoreCase)
                || action.EffectTags.Contains("morale_hit", StringComparison.OrdinalIgnoreCase))
            {
                // BUG 2 FIX: IsKael → CharacterId == "KAEL"; ApplyMoraleDelta → ModifyMorale
                var kael = _party.FirstOrDefault(c => c.CharacterId == "KAEL" && !c.IsKO);
                if (kael != null)
                {
                    int oldMorale = kael.Morale;
                    kael.ModifyMorale(-15, action.DisplayName);
                    MoraleChanged?.Invoke(this, new MoraleChangedEventArgs
                    {
                        OldValue = oldMorale,
                        NewValue = kael.Morale,
                        Cause    = action.DisplayName
                    });
                    Log($"{foe.Template.Name} attacca il Morale di Kael: {oldMorale} → {kael.Morale}.");
                }
            }

            float raw = (magical ? foe.Template.MAG : foe.Template.ATK)
                      * action.BasePower;

            // Nemici non hanno BattleStats — creiamo un proxy minimo
            ApplyEnemyDamage(foe, target, raw, magical, action.Element);

            if (action.EffectTags.Contains("aoe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var c in _party.Where(x => !x.IsKO))
                {
                    var t2 = BattleTarget.FromAlly(c);
                    if (t2.Ally != target.Ally)
                        ApplyEnemyDamage(foe, t2, raw * 0.7f, magical, action.Element);
                }
            }

            ChangeState(BattleState.AnimatingAction);
            CheckBattleEnd();

            if (!_battleOver)
            {
                AdvanceTurn();
                BeginNextActorTurn();
            }
        }

        private BattleTarget? PickEnemyTarget(EnemyAction action)
        {
            var alive = _party.Where(c => !c.IsKO).ToList();
            if (alive.Count == 0) return null;

            if (action.EffectTags.Contains("psychic", StringComparison.OrdinalIgnoreCase))
            {
                // BUG 2 FIX: IsKael → CharacterId == "KAEL"
                var kael = alive.FirstOrDefault(c => c.CharacterId == "KAEL");
                if (kael != null) return BattleTarget.FromAlly(kael);
            }

            var weakest = alive.OrderBy(c => (float)c.CurrentHP / c.MaxHP).First();
            return BattleTarget.FromAlly(weakest);
        }

        private void ApplyEnemyDamage(
            EnemyInstance foe,
            BattleTarget  target,
            float         raw,
            bool          magical,
            ElementType   element)
        {
            if (!target.IsAlive) return;

            float defStat = magical ? GetRes(target) : GetDef(target);
            float defMul  = 0.5f;

            if (target.IsAlly && _defending.Contains(target.Ally!.CharacterId))
                defMul = 1.0f;

            float baseDmg  = Math.Max(1f, raw - defStat * defMul);
            float elemMul  = Math.Max(0.01f, target.GetResistance(element));
            float variance = 1f + (float)(_rng.NextDouble() * DAMAGE_VARIANCE * 2 - DAMAGE_VARIANCE);
            float final    = baseDmg * elemMul * variance;

            int amount = Math.Max(1, (int)final);

            // BUG 3 FIX: TakeDamage() per alleati
            if (target.IsAlly)
                target.Ally!.TakeDamage(amount);
            else
                target.Foe!.CurrentHP = Math.Max(0, target.Foe.CurrentHP - amount);

            if (!target.IsAlive) HandleDeath(target);

            DamageResolved?.Invoke(this, new DamageResolvedEventArgs
            {
                Target      = target,
                Amount      = amount,
                WasCritical = false,
                Element     = element,
                WasHealing  = false
            });

            Log($"{foe.Template.Name} colpisce {target.DisplayName} per {amount}.");
        }

        // ------------------------------------------------------------------
        //  MORTE E FINE BATTAGLIA
        // ------------------------------------------------------------------

        private void HandleDeath(BattleTarget target)
        {
            if (target.IsAlly)
            {
                Log($"{target.Ally!.DisplayName} è caduto.");

                // BUG 2 FIX: IsKael → CharacterId == "KAEL"; ApplyMoraleDelta → ModifyMorale
                var kael = _party.FirstOrDefault(c => c.CharacterId == "KAEL" && !c.IsKO);
                if (kael != null && target.Ally.CharacterId != "KAEL")
                {
                    int old = kael.Morale;
                    kael.ModifyMorale(-8, $"{target.Ally.DisplayName} è caduto");
                    MoraleChanged?.Invoke(this, new MoraleChangedEventArgs
                    {
                        OldValue = old,
                        NewValue = kael.Morale,
                        Cause    = $"{target.Ally.DisplayName} è caduto"
                    });
                }
            }
            else
            {
                Log($"{target.Foe!.Template.Name} è stato sconfitto.");
            }

            RebuildQueueAfterDeath();
        }

        private void RebuildQueueAfterDeath()
        {
            if (_battleOver) return;
            RebuildTurnQueue();
            if (_turnIndexInRound >= _turnQueue.Count)
                _turnIndexInRound = 0;
        }

        private bool CheckBattleEnd()
        {
            if (_battleOver) return true;

            if (!_enemies.Any(e => e.IsAlive))
            {
                EndBattle(BattleState.Victory, fled: false);
                return true;
            }

            if (!_party.Any(c => !c.IsKO))
            {
                EndBattle(BattleState.Defeat, fled: false);
                return true;
            }

            return false;
        }

        private void EndBattle(BattleState outcome, bool fled)
        {
            _battleOver = true;
            ActiveActor = null;

            int exp = 0, gold = 0;
            var dropped = new Dictionary<string, int>();

            if (outcome == BattleState.Victory)
            {
                exp  = _enemies.Sum(e => e.Template.ExpReward);
                gold = _enemies.Sum(e => e.Template.GoldReward);

                // BUG 2 FIX: IsKael → CharacterId == "KAEL"; ApplyMoraleDelta → ModifyMorale
                var kael = _party.FirstOrDefault(c => c.CharacterId == "KAEL");
                if (kael != null)
                {
                    int old = kael.Morale;
                    kael.ModifyMorale(+5, "Vittoria");
                    if (kael.Morale != old)
                        MoraleChanged?.Invoke(this, new MoraleChangedEventArgs
                        {
                            OldValue = old,
                            NewValue = kael.Morale,
                            Cause    = "Vittoria"
                        });
                }

                foreach (var foe in _enemies)
                {
                    foreach (var loot in foe.Template.LootTable)
                    {
                        if (loot.Kind == LootEntryKind.Card
                            && _rng.NextDouble() <= loot.DropChance
                            && !string.IsNullOrEmpty(loot.RefId))
                        {
                            int count = _rng.Next(loot.MinCount, loot.MaxCount + 1);
                            if (dropped.ContainsKey(loot.RefId))
                                dropped[loot.RefId] += count;
                            else
                                dropped[loot.RefId]  = count;
                        }
                    }
                }
            }

            ChangeState(outcome);
            BattleEnded?.Invoke(this, new BattleEndedEventArgs
            {
                Outcome      = outcome,
                ExpGained    = exp,
                GoldGained   = gold,
                Fled         = fled,
                DroppedCards = dropped
            });
        }

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        private void ChangeState(BattleState newState)
        {
            State = newState;
            StateChanged?.Invoke(this, newState);
        }

        private void Log(string msg) => BattleLog?.Invoke(this, msg);

        public bool IsPartyInDanger
            => _party.Any(c => !c.IsKO && (float)c.CurrentHP / c.MaxHP < 0.30f);

        public int EnemiesAlive => _enemies.Count(e => e.IsAlive);
        public int PartyAlive   => _party.Count(c => !c.IsKO);
    }
}
