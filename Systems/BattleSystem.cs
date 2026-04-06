// =============================================================================
//  La Via della Redenzione — Systems/BattleSystem.cs
//  Package : com.refa.valdrath
//  Prompt  : 11 — Logica core del combattimento a turni (CTB / FFX-like)
//
//  Visuale: side view FF1/FF3 — gruppo a destra, nemici a sinistra.
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
//    Morale Kael: moltiplicatore ATK da GetMoraleAtkMultiplier()
//
//  CTB Timeline:
//    Ordine turni per SPD decrescente. RebuildTurnQueue() ad ogni round.
//    GetTimelinePreview(7) per la UI stile FFX.
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

        /// <summary>Indice del nemico nella lista (per la side view FF — posizione a sinistra).</summary>
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
            => ActiveStatusEffects.Any(s => s.Type == t && s.TurnsLeft > 0);

        /// <summary>
        /// Percentuale HP corrente (0..1). Usata da PickAction per le condizioni AI.
        /// </summary>
        public float HpPercent
            => Template.MaxHP <= 0 ? 0f : (float)CurrentHP / Template.MaxHP;

        public override string ToString() => $"{Template.Name} [{CurrentHP}/{Template.MaxHP}]";
    }

    // =========================================================================
    //  BATTLE TARGET — bersaglio unificato (personaggio o nemico)
    // =========================================================================

    /// <summary>
    /// Wrapper che astrae personaggi e nemici per la risoluzione degli effetti.
    /// Il BattleSystem lavora sempre su BattleTarget — non sa se sta colpendo
    /// un Character o un EnemyInstance.
    /// </summary>
    public sealed class BattleTarget
    {
        public Character?    Ally { get; }
        public EnemyInstance? Foe  { get; }

        public bool IsAlly  => Ally != null;
        public bool IsEnemy => Foe  != null;

        public static BattleTarget FromAlly (Character c)    => new(c, null);
        public static BattleTarget FromEnemy(EnemyInstance e) => new(null, e);

        private BattleTarget(Character? ally, EnemyInstance? foe)
        {
            Ally = ally;
            Foe  = foe;
        }

        public string DisplayName
            => Ally?.DisplayName ?? Foe?.Template.Name ?? "?";

        public bool IsAlive
            => IsAlly ? Ally!.CurrentHP > 0 : Foe!.IsAlive;

        public int CurrentHP
        {
            get => IsAlly ? Ally!.CurrentHP : Foe!.CurrentHP;
            set
            {
                if (IsAlly) Ally!.CurrentHP = Math.Max(0, value);
                else        Foe!.CurrentHP  = Math.Max(0, value);
            }
        }

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
        /// <summary>Carte droppate dai nemici sconfitti (ID carta → quantità).</summary>
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
    ///
    /// INTEGRAZIONE BATTLE SCREEN (side view FF-style):
    ///   1. BattleScreen.StartBattle(party, enemies)
    ///   2. BattleScreen ascolta DamageResolved → mostra VFX + damage number
    ///   3. BattleScreen ascolta TurnChanged → aggiorna highlight personaggio attivo
    ///   4. SpriteSheet.OnAttackHitFrame → BattleSystem.ApplyPendingDamage()
    ///
    /// INTEGRAZIONE SIGILLI LYRA (Prompt 12):
    ///   SigilSystem si iscrive a OnCardPlayed e aggiorna i sigilli.
    ///   BattleSystem interroga SigilSystem per i bonus passivi.
    /// </summary>
    public sealed class BattleSystem
    {
        // ------------------------------------------------------------------
        //  Costanti
        // ------------------------------------------------------------------

        public const int TimelinePreviewCount = 7;

        /// <summary>
        /// Rumore casuale sul danno finale (±5%).
        /// Mantiene le battaglie imprevedibili senza stravolgere il bilanciamento.
        /// </summary>
        private const float DAMAGE_VARIANCE = 0.05f;

        // ------------------------------------------------------------------
        //  Stato interno
        // ------------------------------------------------------------------

        private readonly Random _rng = new();

        private readonly List<Character>     _party   = new();
        private readonly List<EnemyInstance> _enemies = new();

        /// <summary>Ordine CTB del round corrente (SPD decrescente).</summary>
        private readonly List<BattleTarget> _turnQueue = new();

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
        //  EVENTI — ascoltati da BattleScreen e SigilSystem
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
        //  HOOK SIGILLI LYRA (Prompt 12)
        // ------------------------------------------------------------------

        /// <summary>
        /// Hook opzionale per il SigilSystem.
        /// Se impostato, GetSigilElementalBonus() viene chiamato durante
        /// il calcolo del danno per Lyra.
        /// </summary>
        public Func<Character, ElementType, float>? GetSigilElementalBonus { get; set; }

        // ------------------------------------------------------------------
        //  INIZIALIZZAZIONE
        // ------------------------------------------------------------------

        /// <summary>
        /// Avvia una nuova battaglia.
        /// </summary>
        public void StartBattle(
            IEnumerable<Character> party,
            IEnumerable<Enemy>     enemyTemplates)
        {
            _party.Clear();
            _enemies.Clear();
            _turnQueue.Clear();
            _defending.Clear();
            _battleOver          = false;
            _roundIndex          = 0;
            _turnIndexInRound    = 0;

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

            foreach (var c in _party.Where(x => x.CurrentHP > 0))
                actors.Add(BattleTarget.FromAlly(c));
            foreach (var e in _enemies.Where(x => x.IsAlive))
                actors.Add(BattleTarget.FromEnemy(e));

            foreach (var a in actors.OrderByDescending(EffectiveSpeed))
                _turnQueue.Add(a);
        }

        private int EffectiveSpeed(BattleTarget t)
        {
            if (t.IsAlly)
            {
                float s = t.Ally!.SPD;
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

                // Refusal Morale (solo Kael, solo turno giocatore)
                if (actor.IsAlly && actor.Ally!.IsKael)
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
                    // Attende l'input del giocatore — la BattleScreen chiama
                    // TryPlayCard / Defend / TryFlee quando il giocatore sceglie.
                }

                return;
            }

            // Fine coda: nuovo round
            BeginNextActorTurn();
        }

        private void OnRoundStart()
        {
            // Tick stati a inizio round
            foreach (var c in _party)
                TickStatuses(c.ActiveStatusEffects, c);
            foreach (var e in _enemies)
                TickEnemyStatuses(e.ActiveStatusEffects);
        }

        private static void TickStatuses(List<StatusEffect> list, Character owner)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var s = list[i];
                if (s.TurnsLeft > 0)
                {
                    s.OnTurnStart?.Invoke(owner);
                    s.TurnsLeft--;
                }
                if (s.TurnsLeft <= 0)
                    list.RemoveAt(i);
            }
        }

        private static void TickEnemyStatuses(List<StatusEffect> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                list[i].TurnsLeft--;
                if (list[i].TurnsLeft <= 0)
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

        private static void ConsumeStun(BattleTarget t)
        {
            var list = t.IsAlly
                ? t.Ally!.ActiveStatusEffects
                : t.Foe!.ActiveStatusEffects;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Type != StatusEffectType.Stordito) continue;
                list.RemoveAt(i);
                return;
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

            if (user.CurrentSP < card.SpCost)
            {
                Log($"{user.DisplayName}: SP insufficienti ({user.CurrentSP}/{card.SpCost}).");
                return false;
            }

            user.CurrentSP -= card.SpCost;
            ChangeState(BattleState.AnimatingAction);

            // Notifica SigilSystem prima di risolvere gli effetti
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
        /// Usata da SigilSystem per effetti speciali ("Custode dei Cinque").
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
            user.CurrentSP = Math.Min(user.MaxSP, user.CurrentSP + 1);
            Log($"{user.DisplayName} si difende e recupera 1 SP.");

            ChangeState(BattleState.AnimatingAction);
        }

        /// <summary>
        /// Usa un oggetto dall'inventario.
        /// La logica vera arriverà con InventorySystem (Prompt 30).
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

            // Penalità Morale indipendente dall'esito
            if (initiator.IsKael)
            {
                int oldMorale = initiator.Morale;
                initiator.ApplyMoraleDelta(-10);
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
        /// Avanza al prossimo attore nella timeline.
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
                    foreach (var c in _party.Where(x => x.CurrentHP > 0))
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
            Character   user,
            CardEffect  fx,
            CardModel   card,
            BattleTarget target)
        {
            // Contesto statistiche — include Morale di Kael
            var ctx = StatContextExtensions.FromCharacter(user);
            if (user.IsKael)
                ctx.ATK *= user.GetMoraleAtkMultiplier();

            float value   = fx.EvaluateValue(ctx);
            var   element = card.ElementType;

            // Bonus sigilli di Lyra (hook opzionale)
            float sigilBonus = 1f;
            if (GetSigilElementalBonus != null && user.Class == CharacterClass.Custode)
                sigilBonus = GetSigilElementalBonus(user, element);

            switch (fx.EffectType)
            {
                case EffectType.Danno:
                    for (int h = 0; h < Math.Max(1, fx.HitCount); h++)
                        ApplyDamage(user, target, value * sigilBonus, element,
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
                    // Scudo: implementazione completa nel Prompt 12 con SigilSystem
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
        //  FORMULA DANNO (Prompt 40)
        // ------------------------------------------------------------------

        private void ApplyDamage(
            Character   attacker,
            BattleTarget target,
            float       raw,
            ElementType element,
            bool        physical)
        {
            if (!target.IsAlive) return;

            // Fallback se raw è zero (nessuna formula definita)
            float scaled = raw > 0
                ? raw
                : (physical ? attacker.ATK : attacker.MAG) * 1.1f;

            // Difesa efficace
            float defStat = physical ? GetDef(target) : GetRes(target);
            float defMul  = 0.5f;

            // Difesa raddoppiata se in modalità Difendi
            if (target.IsAlly && _defending.Contains(target.Ally!.CharacterId))
                defMul = 1.0f;

            float baseDmg = Math.Max(1f, scaled - defStat * defMul);

            // Resistenza elementale
            float elemMul = Math.Max(0.01f, target.GetResistance(element));

            // Moltiplicatore stati (Potenziato, Depresso)
            float statusMul = GetStatusDamageMultiplier(attacker);

            // Formula finale
            float final = baseDmg * elemMul * statusMul;

            // Critico: LUK/200, cap 40%
            float critChance = Math.Min(0.40f, attacker.LUK / 200f);
            bool  crit       = _rng.NextDouble() < critChance;
            if (crit) final *= 1.5f;

            // Rumore ±5%
            float variance = 1f + (float)(_rng.NextDouble() * DAMAGE_VARIANCE * 2 - DAMAGE_VARIANCE);
            final *= variance;

            int amount = Math.Max(1, (int)final);
            target.CurrentHP -= amount;

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

            int healed = Math.Min(target.MaxHP - target.CurrentHP, Math.Max(1, amount));
            target.CurrentHP += healed;

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
            BattleTarget    target,
            StatusEffectType type,
            int              duration,
            float            intensity,
            string           sourceCardId)
        {
            // Personaggi con Ancorato sono immuni agli stati psicologici
            if (target.IsAlly && target.Ally!.HasStatus(StatusEffectType.Ancorato))
            {
                bool isPsychological = type is
                    StatusEffectType.Depresso or
                    StatusEffectType.Dubbio   or
                    StatusEffectType.Paura;

                if (isPsychological)
                {
                    Log($"{target.DisplayName} è ancorato — stato {type} resistito.");
                    return;
                }
            }

            var se = new StatusEffect(type, Math.Max(1, duration), intensity, sourceCardId);

            if (target.IsAlly) target.Ally!.ActiveStatusEffects.Add(se);
            else                target.Foe!.ActiveStatusEffects.Add(se);

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
            => t.IsAlly ? t.Ally!.DEF : t.Foe!.Template.DEF;

        private static int GetRes(BattleTarget t)
            => t.IsAlly ? t.Ally!.RES : t.Foe!.Template.RES;

        // ------------------------------------------------------------------
        //  AI NEMICI — targeting intelligente
        // ------------------------------------------------------------------

        private void ExecuteEnemyTurn(EnemyInstance foe)
        {
            float hpPct      = foe.HpPercent;
            bool  phase2     = hpPct < 0.50f && foe.Template.IsBoss;
            bool  phase3     = hpPct < 0.25f && foe.Template.IsBoss;
            bool  playerLowHP = _party.Any(c => c.CurrentHP > 0 &&
                                (float)c.CurrentHP / c.MaxHP < 0.30f);

            // Usa PickAction da Enemy.cs (logica pesata + condizioni)
            var action = foe.Template.PickAction(hpPct, phase2, phase3, playerLowHP, _rng);

            if (action == null)
            {
                Log($"{foe.Template.Name} non ha azioni disponibili — passa.");
                AdvanceTurn();
                BeginNextActorTurn();
                return;
            }

            Log($"{foe.Template.Name} usa {action.DisplayName}.");

            // Target: personaggio con HP più bassa (non sempre il primo — AI più credibile)
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
                var kael = _party.FirstOrDefault(c => c.IsKael && c.CurrentHP > 0);
                if (kael != null)
                {
                    int oldMorale = kael.Morale;
                    kael.ApplyMoraleDelta(-15);
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

            ApplyEnemyDamage(foe, target, raw, magical, action.Element);

            // AoE: colpisce tutti i personaggi vivi
            if (action.EffectTags.Contains("aoe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var c in _party.Where(x => x.CurrentHP > 0))
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

        /// <summary>
        /// Sceglie il bersaglio giocatore per un attacco nemico.
        /// Default: personaggio con HP% più bassa (AI credibile).
        /// </summary>
        private BattleTarget? PickEnemyTarget(EnemyAction action)
        {
            var alive = _party.Where(c => c.CurrentHP > 0).ToList();
            if (alive.Count == 0) return null;

            // Attacchi psicologici preferiscono Kael
            if (action.EffectTags.Contains("psychic", StringComparison.OrdinalIgnoreCase))
            {
                var kael = alive.FirstOrDefault(c => c.IsKael);
                if (kael != null) return BattleTarget.FromAlly(kael);
            }

            // Default: personaggio con HP più bassa (pressione su chi è in difficoltà)
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

            float baseDmg = Math.Max(1f, raw - defStat * defMul);
            float elemMul = Math.Max(0.01f, target.GetResistance(element));
            float variance = 1f + (float)(_rng.NextDouble() * DAMAGE_VARIANCE * 2 - DAMAGE_VARIANCE);
            float final   = baseDmg * elemMul * variance;

            int amount = Math.Max(1, (int)final);
            target.CurrentHP -= amount;

            if (!target.IsAlive) HandleDeath(target);

            DamageResolved?.Invoke(this, new DamageResolvedEventArgs
            {
                Target     = target,
                Amount     = amount,
                WasCritical = false,
                Element    = element,
                WasHealing = false
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

                // Penalità Morale per Kael quando un alleato cade
                var kael = _party.FirstOrDefault(c => c.IsKael && c.CurrentHP > 0);
                if (kael != null && !target.Ally.IsKael)
                {
                    int old = kael.Morale;
                    kael.ApplyMoraleDelta(-8);
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
            int before = _turnIndexInRound;
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

            if (!_party.Any(c => c.CurrentHP > 0))
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

                // Morale Kael: +5 per vittoria
                var kael = _party.FirstOrDefault(c => c.IsKael);
                if (kael != null)
                {
                    int old = kael.Morale;
                    kael.ApplyMoraleDelta(+5);
                    if (kael.Morale != old)
                        MoraleChanged?.Invoke(this, new MoraleChangedEventArgs
                        {
                            OldValue = old,
                            NewValue = kael.Morale,
                            Cause    = "Vittoria"
                        });
                }

                // Loot drop (logica base — CardAcquisitionSystem la estenderà)
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

        // -----------------------------------------------------------------------
        //  PROPRIETÀ HELPER — per BattleScreen
        // -----------------------------------------------------------------------

        /// <summary>
        /// True se almeno un personaggio giocabile è a HP bassa (&lt;30%).
        /// Usato dalla BattleScreen per mostrare avvisi visivi.
        /// </summary>
        public bool IsPartyInDanger
            => _party.Any(c => c.CurrentHP > 0 && (float)c.CurrentHP / c.MaxHP < 0.30f);

        /// <summary>Numero di nemici ancora in vita.</summary>
        public int EnemiesAlive => _enemies.Count(e => e.IsAlive);

        /// <summary>Numero di personaggi giocabili ancora in piedi.</summary>
        public int PartyAlive => _party.Count(c => c.CurrentHP > 0);
    }
}
