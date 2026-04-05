// =============================================================================
//  La Via della Redenzione — Systems/DeckSystem.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Sistema di gestione mazzo per ogni personaggio.
//                Gestisce slot, vincoli, sinergie e raccomandazioni.
//
//  Struttura mazzo (8 slot totali per personaggio):
//    Slot 0   : Arma        (CardType.Equipaggiamento, EquipmentSubType.Arma)
//    Slot 1   : Armatura    (CardType.Equipaggiamento, EquipmentSubType.Armatura)
//    Slot 2-3 : Accessori   (CardType.Equipaggiamento, EquipmentSubType.Accessorio)
//    Slot 4-7 : Abilità     (CardType.Abilita | Passiva | Reazione)
//
//  Sinergie:
//    Due o più carte in mazzo con tag condivisi attivano SynergyBonus passivi.
//    Calcolati da SynergyCalculator all'inizio di ogni battaglia.
//    Esempio: "spada" + "spada" → +5% danno fisico per tutto il gruppo.
//
//  Uso:
//    var deck = new CharacterDeck("KAEL");
//    deckSystem.SwapCard(deck, slotIndex: 0, newCard);
//    var errors = deckSystem.ValidateDeck(deck);
//    var synergies = deckSystem.GetSynergies(deck);
// =============================================================================

using LaViaDellaRedenzione.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LaViaDellaRedenzione.Systems
{
    // =========================================================================
    //  SLOT TYPE — tipo di slot nel mazzo
    // =========================================================================

    public enum DeckSlotType
    {
        Arma       = 0,
        Armatura   = 1,
        Accessorio = 2,
        Abilita    = 3   // include Passiva e Reazione
    }

    // =========================================================================
    //  DECK SLOT — singolo slot del mazzo
    // =========================================================================

    /// <summary>
    /// Singolo slot nel mazzo di un personaggio.
    /// </summary>
    public sealed class DeckSlot
    {
        public int          Index    { get; }
        public DeckSlotType SlotType { get; }

        /// <summary>Carta equipaggiata in questo slot. Null = slot vuoto.</summary>
        public CardModel?   Card     { get; private set; }

        public bool IsEmpty => Card == null;

        public DeckSlot(int index, DeckSlotType slotType)
        {
            Index    = index;
            SlotType = slotType;
        }

        internal void SetCard(CardModel? card) => Card = card;
    }

    // =========================================================================
    //  CHARACTER DECK — mazzo di un personaggio
    // =========================================================================

    /// <summary>
    /// Mazzo di 8 carte di un personaggio giocabile.
    /// </summary>
    public sealed class CharacterDeck
    {
        // ------------------------------------------------------------------
        //  Dati
        // ------------------------------------------------------------------

        public string CharacterId    { get; }
        public CharacterClass Class  { get; }

        /// <summary>
        /// 8 slot del mazzo in ordine fisso:
        /// [0]=Arma, [1]=Armatura, [2-3]=Accessori, [4-7]=Abilità
        /// </summary>
        public IReadOnlyList<DeckSlot> Slots => _slots;
        private readonly DeckSlot[] _slots;

        // ------------------------------------------------------------------
        //  Costruttore
        // ------------------------------------------------------------------

        public CharacterDeck(string characterId, CharacterClass characterClass)
        {
            CharacterId = characterId;
            Class       = characterClass;

            _slots = new DeckSlot[8];
            _slots[0] = new DeckSlot(0, DeckSlotType.Arma);
            _slots[1] = new DeckSlot(1, DeckSlotType.Armatura);
            _slots[2] = new DeckSlot(2, DeckSlotType.Accessorio);
            _slots[3] = new DeckSlot(3, DeckSlotType.Accessorio);
            _slots[4] = new DeckSlot(4, DeckSlotType.Abilita);
            _slots[5] = new DeckSlot(5, DeckSlotType.Abilita);
            _slots[6] = new DeckSlot(6, DeckSlotType.Abilita);
            _slots[7] = new DeckSlot(7, DeckSlotType.Abilita);
        }

        // ------------------------------------------------------------------
        //  Accesso slot
        // ------------------------------------------------------------------

        public DeckSlot GetSlot(int index) => _slots[index];

        /// <summary>Tutte le carte equipaggiate (slot non vuoti).</summary>
        public IEnumerable<CardModel> GetEquippedCards()
            => _slots.Where(s => !s.IsEmpty).Select(s => s.Card!);

        /// <summary>Tutti i tag presenti nel mazzo corrente.</summary>
        public IEnumerable<string> GetAllTags()
            => GetEquippedCards().SelectMany(c => c.Tags).Distinct();

        /// <summary>Statistiche totali sommate da tutte le carte equipaggiate.</summary>
        public DeckStats GetTotalStats()
        {
            var stats = new DeckStats();
            foreach (var card in GetEquippedCards())
            {
                stats.ATK += card.StatATK;
                stats.MAG += card.StatMAG;
                stats.DEF += card.StatDEF;
                stats.RES += card.StatRES;
                stats.SPD += card.StatSPD;
                stats.SP  += card.StatSP;
            }
            return stats;
        }

        /// <summary>Salva il mazzo come lista di ID carta (per SaveData).</summary>
        public List<string> ToIdList()
            => _slots.Select(s => s.Card?.Id ?? string.Empty).ToList();

        /// <summary>Carica il mazzo da una lista di ID (da SaveData).</summary>
        public void LoadFromIdList(List<string> ids, CardDatabase db)
        {
            for (int i = 0; i < Math.Min(ids.Count, _slots.Length); i++)
            {
                var card = string.IsNullOrEmpty(ids[i])
                    ? null
                    : db.GetById(ids[i]);
                _slots[i].SetCard(card);
            }
        }
    }

    // =========================================================================
    //  DECK STATS — statistiche aggregate del mazzo
    // =========================================================================

    public sealed class DeckStats
    {
        public int ATK { get; set; }
        public int MAG { get; set; }
        public int DEF { get; set; }
        public int RES { get; set; }
        public int SPD { get; set; }
        public int SP  { get; set; }
    }

    // =========================================================================
    //  SYNERGY BONUS — bonus attivato da tag condivisi
    // =========================================================================

    /// <summary>
    /// Bonus passivo attivato quando due o più carte nel mazzo
    /// condividono lo stesso tag.
    /// </summary>
    public sealed class SynergyBonus
    {
        /// <summary>Tag che ha attivato la sinergia.</summary>
        public string Tag { get; init; } = string.Empty;

        /// <summary>Numero di carte che condividono il tag.</summary>
        public int CardCount { get; init; }

        /// <summary>Descrizione leggibile della sinergia.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>Bonus ATK percentuale (0.05 = +5%).</summary>
        public float BonusATKPercent { get; init; }

        /// <summary>Bonus MAG percentuale.</summary>
        public float BonusMAGPercent { get; init; }

        /// <summary>Bonus DEF percentuale.</summary>
        public float BonusDEFPercent { get; init; }

        /// <summary>Bonus SP fisso per turno.</summary>
        public int BonusSPRegen { get; init; }

        /// <summary>Bonus percentuale danno dello stesso elemento.</summary>
        public float BonusElementalPercent { get; init; }

        /// <summary>Elemento potenziato dalla sinergia (se applicabile).</summary>
        public ElementType? BoostedElement { get; init; }
    }

    // =========================================================================
    //  SYNERGY CALCULATOR
    // =========================================================================

    /// <summary>
    /// Calcola le sinergie attive nel mazzo corrente.
    /// Chiamato da BattleSystem all'inizio di ogni battaglia.
    /// </summary>
    public static class SynergyCalculator
    {
        /// <summary>
        /// Definizioni di sinergia per tag.
        /// Ogni entry descrive il bonus che si attiva quando N carte
        /// condividono quel tag.
        /// </summary>
        private static readonly Dictionary<string, Func<int, SynergyBonus?>> SynergyRules
            = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Sinergie di Kael ─────────────────────────────────────────
            ["spada"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "spada", CardCount = count,
                Description       = "Lame affilate: +8% danno fisico",
                BonusATKPercent   = 0.08f
            } : null,

            ["greypass"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "greypass", CardCount = count,
                Description          = "Ombra di Greypass: +10% danno Ombra",
                BonusElementalPercent = 0.10f,
                BoostedElement       = ElementType.Ombra
            } : null,

            ["militare"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "militare", CardCount = count,
                Description     = "Disciplina imperiale: +5% ATK e DEF",
                BonusATKPercent = 0.05f,
                BonusDEFPercent = 0.05f
            } : null,

            // ── Sinergie di Lyra ─────────────────────────────────────────
            ["sigillo"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "sigillo", CardCount = count,
                Description          = $"Sigilli attivi ({count}): +{count * 5}% danno Luce",
                BonusElementalPercent = count * 0.05f,
                BoostedElement       = ElementType.Luce
            } : null,

            ["custode"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "custode", CardCount = count,
                Description     = "Arte dei Custodi: +10% MAG",
                BonusMAGPercent = 0.10f
            } : null,

            ["analisi"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "analisi", CardCount = count,
                Description     = "Metodicità: +1 SP per turno",
                BonusSPRegen    = 1
            } : null,

            // ── Sinergie di Voran ─────────────────────────────────────────
            ["silenzio"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "silenzio", CardCount = count,
                Description  = "Il silenzio parla: +1 SP per turno",
                BonusSPRegen = 1
            } : null,

            ["monastero"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "monastero", CardCount = count,
                Description     = "Decenni di pratica: +12% RES",
                BonusDEFPercent = 0.08f,
                BonusMAGPercent = 0.04f
            } : null,

            ["redenzione"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "redenzione", CardCount = count,
                Description     = "Verso la redenzione: +15% MAG",
                BonusMAGPercent = 0.15f
            } : null,

            // ── Sinergie di Sera ─────────────────────────────────────────
            ["campagna"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "campagna", CardCount = count,
                Description     = "Vita di campagna: +10% ATK fisico",
                BonusATKPercent = 0.10f
            } : null,

            ["fiducia"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "fiducia", CardCount = count,
                Description  = "Fiducia incondizionata: +2 SP per turno",
                BonusSPRegen = 2
            } : null,

            // ── Sinergie trasversali ─────────────────────────────────────
            ["cura"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "cura", CardCount = count,
                Description     = "Spirito di cura: +6% MAG curativo",
                BonusMAGPercent = 0.06f
            } : null,

            ["luce"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "luce", CardCount = count,
                Description          = $"Luce dei Sigilli: +{count * 4}% danno Luce",
                BonusElementalPercent = count * 0.04f,
                BoostedElement       = ElementType.Luce
            } : null,

            ["ombra"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "ombra", CardCount = count,
                Description          = $"Oscurità trattenuta: +{count * 4}% danno Ombra",
                BonusElementalPercent = count * 0.04f,
                BoostedElement       = ElementType.Ombra
            } : null,

            ["supporto"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "supporto", CardCount = count,
                Description  = "Sinergia di gruppo: +1 SP per turno",
                BonusSPRegen = 1
            } : null,

            ["reazione"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "reazione", CardCount = count,
                Description     = "Riflessi affinati: +8% evasione",
                BonusDEFPercent = 0.08f
            } : null,

            ["edric"] = count => count >= 2 ? new SynergyBonus
            {
                Tag = "edric", CardCount = count,
                Description     = "Memoria di Edric: +12% ATK quando un alleato è ferito",
                BonusATKPercent = 0.12f
            } : null
        };

        /// <summary>
        /// Calcola tutte le sinergie attive nel mazzo dato.
        /// </summary>
        public static List<SynergyBonus> Calculate(CharacterDeck deck)
        {
            var result = new List<SynergyBonus>();

            // Conta le occorrenze di ogni tag nel mazzo
            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in deck.GetEquippedCards())
            {
                foreach (var tag in card.Tags)
                {
                    tagCounts[tag] = tagCounts.TryGetValue(tag, out int c) ? c + 1 : 1;
                }
            }

            // Applica le regole di sinergia
            foreach (var (tag, count) in tagCounts)
            {
                if (!SynergyRules.TryGetValue(tag, out var rule)) continue;
                var bonus = rule(count);
                if (bonus != null) result.Add(bonus);
            }

            return result;
        }
    }

    // =========================================================================
    //  DECK SYSTEM
    // =========================================================================

    /// <summary>
    /// Singleton. Gestisce validazione, swap e raccomandazioni dei mazzi.
    /// </summary>
    public sealed class DeckSystem
    {
        // ------------------------------------------------------------------
        //  Singleton
        // ------------------------------------------------------------------

        private static DeckSystem? _instance;
        public static DeckSystem Instance => _instance ??= new DeckSystem();
        private DeckSystem() { }

        // ------------------------------------------------------------------
        //  VALIDATE DECK
        // ------------------------------------------------------------------

        /// <summary>
        /// Valida il mazzo di un personaggio.
        /// Ritorna lista di errori (vuota = mazzo valido).
        /// </summary>
        public List<string> ValidateDeck(CharacterDeck deck)
        {
            var errors = new List<string>();
            var db     = CardDatabase.Instance;

            for (int i = 0; i < deck.Slots.Count; i++)
            {
                var slot = deck.Slots[i];
                if (slot.IsEmpty) continue;

                var card = slot.Card!;

                // ── Verifica che il tipo di carta corrisponda allo slot ──────
                bool slotOk = slot.SlotType switch
                {
                    DeckSlotType.Arma      => card.CardType == CardType.Equipaggiamento
                                           && card.EquipmentSubType == EquipmentSubType.Arma,
                    DeckSlotType.Armatura  => card.CardType == CardType.Equipaggiamento
                                           && card.EquipmentSubType == EquipmentSubType.Armatura,
                    DeckSlotType.Accessorio=> card.CardType == CardType.Equipaggiamento
                                           && card.EquipmentSubType == EquipmentSubType.Accessorio,
                    DeckSlotType.Abilita   => card.CardType == CardType.Abilita
                                          || card.CardType == CardType.Passiva
                                          || card.CardType == CardType.Reazione,
                    _ => false
                };

                if (!slotOk)
                    errors.Add($"Slot {i}: '{card.Name}' non è compatibile con lo slot {slot.SlotType}.");

                // ── Verifica restrizione classe ──────────────────────────────
                if (!card.CanBeUsedBy(deck.Class))
                    errors.Add($"Slot {i}: '{card.Name}' non può essere usata da {deck.Class}.");

                // ── Verifica duplicati (stessa carta in più slot) ────────────
                for (int j = i + 1; j < deck.Slots.Count; j++)
                {
                    if (!deck.Slots[j].IsEmpty && deck.Slots[j].Card!.Id == card.Id)
                        errors.Add($"Duplicato: '{card.Name}' è presente in più slot.");
                }
            }

            return errors;
        }

        // ------------------------------------------------------------------
        //  GET SYNERGIES
        // ------------------------------------------------------------------

        /// <summary>
        /// Calcola le sinergie attive nel mazzo.
        /// </summary>
        public List<SynergyBonus> GetSynergies(CharacterDeck deck)
            => SynergyCalculator.Calculate(deck);

        // ------------------------------------------------------------------
        //  SWAP CARD
        // ------------------------------------------------------------------

        /// <summary>
        /// Tenta di equipaggiare una carta in uno slot specifico.
        /// Ritorna true se riuscito, false se la carta non è compatibile.
        /// </summary>
        public bool SwapCard(CharacterDeck deck, int slotIndex, CardModel? newCard)
        {
            if (slotIndex < 0 || slotIndex >= deck.Slots.Count)
                return false;

            var slot = deck.Slots[slotIndex];

            // Slot vuoto: rimuove la carta corrente
            if (newCard == null)
            {
                slot.SetCard(null);
                return true;
            }

            // Verifica compatibilità tipo → slot
            bool compatible = slot.SlotType switch
            {
                DeckSlotType.Arma       => newCard.CardType == CardType.Equipaggiamento
                                        && newCard.EquipmentSubType == EquipmentSubType.Arma,
                DeckSlotType.Armatura   => newCard.CardType == CardType.Equipaggiamento
                                        && newCard.EquipmentSubType == EquipmentSubType.Armatura,
                DeckSlotType.Accessorio => newCard.CardType == CardType.Equipaggiamento
                                        && newCard.EquipmentSubType == EquipmentSubType.Accessorio,
                DeckSlotType.Abilita    => newCard.CardType == CardType.Abilita
                                       || newCard.CardType == CardType.Passiva
                                       || newCard.CardType == CardType.Reazione,
                _ => false
            };

            if (!compatible) return false;

            // Verifica restrizione classe
            if (!newCard.CanBeUsedBy(deck.Class)) return false;

            slot.SetCard(newCard);
            return true;
        }

        // ------------------------------------------------------------------
        //  GET RECOMMENDED DECK
        // ------------------------------------------------------------------

        /// <summary>
        /// Suggerisce il mazzo ottimale per un personaggio dato il suo
        /// inventario carte e il livello attuale.
        /// Strategia: massimizza le statistiche totali privilegiando
        /// le sinergie di tag più forti.
        /// </summary>
        public CharacterDeck GetRecommendedDeck(
            CharacterClass       characterClass,
            string               characterId,
            int                  level,
            Dictionary<string, int> cardInventory)
        {
            var db   = CardDatabase.Instance;
            var deck = new CharacterDeck(characterId, characterClass);

            // Carte disponibili per questo personaggio a questo livello
            var available = db.GetCardsForCharacter(characterClass, level)
                .Where(c => cardInventory.ContainsKey(c.Id) && cardInventory[c.Id] > 0)
                .ToList();

            // ── Slot Arma (0) ─────────────────────────────────────────────
            var bestArma = available
                .Where(c => c.CardType == CardType.Equipaggiamento
                         && c.EquipmentSubType == EquipmentSubType.Arma)
                .OrderByDescending(c => c.StatATK + c.StatMAG)
                .FirstOrDefault();
            if (bestArma != null) deck.GetSlot(0).SetCard(bestArma);

            // ── Slot Armatura (1) ─────────────────────────────────────────
            var bestArmatura = available
                .Where(c => c.CardType == CardType.Equipaggiamento
                         && c.EquipmentSubType == EquipmentSubType.Armatura)
                .OrderByDescending(c => c.StatDEF + c.StatRES)
                .FirstOrDefault();
            if (bestArmatura != null) deck.GetSlot(1).SetCard(bestArmatura);

            // ── Slot Accessori (2-3) ──────────────────────────────────────
            var accessories = available
                .Where(c => c.CardType == CardType.Equipaggiamento
                         && c.EquipmentSubType == EquipmentSubType.Accessorio)
                .OrderByDescending(c => c.StatSP + c.StatSPD + c.StatRES)
                .Take(2)
                .ToList();

            for (int i = 0; i < accessories.Count; i++)
                deck.GetSlot(2 + i).SetCard(accessories[i]);

            // ── Slot Abilità (4-7) ────────────────────────────────────────
            // Priorità: Passiva > Reazione > Abilita con SpCost ottimale
            var abilities = available
                .Where(c => c.CardType == CardType.Abilita
                         || c.CardType == CardType.Passiva
                         || c.CardType == CardType.Reazione)
                .OrderByDescending(c =>
                    (c.CardType == CardType.Passiva   ? 100 : 0) +
                    (c.CardType == CardType.Reazione  ? 50  : 0) +
                    (int)c.CardRarity * 10 +
                    c.Effects.Count * 5)
                .Take(4)
                .ToList();

            for (int i = 0; i < abilities.Count; i++)
                deck.GetSlot(4 + i).SetCard(abilities[i]);

            return deck;
        }
    }
}
