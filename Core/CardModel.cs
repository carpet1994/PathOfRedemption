// =============================================================================
//  La Via della Redenzione — Core/CardModel.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Modello dati puro del sistema di carte.
//                Nessuna dipendenza da UI, piattaforma o MAUI.
//
//  CORREZIONE BUG 2:
//    CardDatabase è stato spostato in Systems/CardDatabase.cs.
//    Questo file ora contiene solo CardEffect, StatContext,
//    FormulaEvaluator e CardModel — tutti tipi puramente dati,
//    senza dipendenze da FileSystem o altri sistemi MAUI.
//    Il `using LaViaDellaRedenzione.Systems` è stato rimosso.
//
//  Struttura:
//    CardEffect       → singolo effetto applicato da una carta
//    StatContext      → contesto statistiche per valutazione formula
//    FormulaEvaluator → valutatore espressioni semplice (no reflection)
//    CardModel        → carta completa con tutti i suoi dati
//
//  Formula scaling effetti:
//    Stringa valutabile come espressione matematica semplice.
//    Variabili disponibili: ATK, MAG, DEF, RES, SPD, LUK, HP, SP, LVL
//    Esempi: "ATK * 1.5 + 20"  →  danno fisico forte
//            "MAG * 2.0"       →  danno magico puro
//            "50"              →  valore fisso
//            "HP * 0.3"        →  cura proporzionale agli HP max
// =============================================================================

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LaViaDellaRedenzione.Core
{
    // =========================================================================
    //  CARD EFFECT — singolo effetto di una carta
    // =========================================================================

    /// <summary>
    /// Singolo effetto prodotto da una carta quando viene giocata.
    /// Una carta può avere più effetti (es. danno + stato alterazione).
    /// </summary>
    [Serializable]
    public sealed class CardEffect
    {
        /// <summary>Tipo di effetto (danno, cura, buff, stato, ecc.).</summary>
        [JsonProperty("effectType")]
        public EffectType EffectType { get; set; } = EffectType.Danno;

        /// <summary>A chi si applica l'effetto.</summary>
        [JsonProperty("target")]
        public TargetType Target { get; set; } = TargetType.SingleEnemy;

        /// <summary>
        /// Valore base dell'effetto (usato se ScalingFormula è vuota).
        /// </summary>
        [JsonProperty("baseValue")]
        public float BaseValue { get; set; } = 0f;

        /// <summary>
        /// Formula di scaling in stringa.
        /// Variabili: ATK, MAG, DEF, RES, SPD, LUK, HP, SP, LVL
        /// Vuota = usa BaseValue fisso.
        /// </summary>
        [JsonProperty("scalingFormula")]
        public string ScalingFormula { get; set; } = string.Empty;

        /// <summary>
        /// Stato alterazione applicato (solo se EffectType == Stato).
        /// </summary>
        [JsonProperty("statusEffect")]
        public StatusEffectType? StatusEffect { get; set; }

        /// <summary>Durata dello stato in turni (0 = permanente fino alla fine battaglia).</summary>
        [JsonProperty("statusDuration")]
        public int StatusDuration { get; set; } = 2;

        /// <summary>Intensità dello stato (moltiplicatore, es. 0.5 = -50% ATK per Debuff).</summary>
        [JsonProperty("statusIntensity")]
        public float StatusIntensity { get; set; } = 1.0f;

        /// <summary>
        /// Probabilità di applicare lo stato (0.0..1.0).
        /// 1.0 = sempre, 0.5 = 50% di chance.
        /// </summary>
        [JsonProperty("statusChance")]
        public float StatusChance { get; set; } = 1.0f;

        /// <summary>
        /// Numero di colpi (per effetti multi-hit come "Parole Contate" di Voran).
        /// Default 1.
        /// </summary>
        [JsonProperty("hitCount")]
        public int HitCount { get; set; } = 1;

        // ------------------------------------------------------------------
        //  VALUTAZIONE FORMULA
        // ------------------------------------------------------------------

        /// <summary>
        /// Calcola il valore finale dell'effetto dato un contesto di statistiche.
        /// Se ScalingFormula è vuota, ritorna BaseValue.
        /// </summary>
        public float EvaluateValue(StatContext ctx)
        {
            if (string.IsNullOrWhiteSpace(ScalingFormula))
                return BaseValue;

            try
            {
                return FormulaEvaluator.Evaluate(ScalingFormula, ctx);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CardEffect] Errore valutazione formula: {ScalingFormula}");
                return BaseValue;
            }
        }
    }

    // =========================================================================
    //  STAT CONTEXT — contesto statistiche per valutazione formula
    // =========================================================================

    /// <summary>
    /// Contesto di statistiche passato al FormulaEvaluator.
    /// Popolato dal BattleSystem con i valori reali del personaggio.
    /// </summary>
    public sealed class StatContext
    {
        public float ATK { get; set; }
        public float MAG { get; set; }
        public float DEF { get; set; }
        public float RES { get; set; }
        public float SPD { get; set; }
        public float LUK { get; set; }
        public float HP  { get; set; }  // HP massimi
        public float SP  { get; set; }  // SP massimi
        public float LVL { get; set; }

        /// <summary>
        /// Costruisce un StatContext da valori numerici espliciti.
        /// </summary>
        public static StatContext FromValues(
            float atk, float mag, float def, float res,
            float spd, float luk, float hp, float sp, float lvl) => new()
        {
            ATK = atk,
            MAG = mag,
            DEF = def,
            RES = res,
            SPD = spd,
            LUK = luk,
            HP  = hp,
            SP  = sp,
            LVL = lvl
        };

        // NOTA: StatContextExtensions.FromCharacter(Character character) è
        // definito in Character.cs per evitare dipendenze circolari.
    }

    // =========================================================================
    //  FORMULA EVALUATOR — valutatore espressioni semplice
    // =========================================================================

    /// <summary>
    /// Valutatore di espressioni matematiche semplici con variabili.
    /// Supporta: +, -, *, /, parentesi, variabili (ATK, MAG, ecc.).
    /// Implementato senza dipendenze esterne (no Roslyn, no DataTable).
    /// </summary>
    public static class FormulaEvaluator
    {
        public static float Evaluate(string formula, StatContext ctx)
        {
            string expr = formula
                .Replace("ATK", ctx.ATK.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("MAG", ctx.MAG.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("DEF", ctx.DEF.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("RES", ctx.RES.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("SPD", ctx.SPD.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("LUK", ctx.LUK.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("HP",  ctx.HP.ToString("F2",  System.Globalization.CultureInfo.InvariantCulture))
                .Replace("SP",  ctx.SP.ToString("F2",  System.Globalization.CultureInfo.InvariantCulture))
                .Replace("LVL", ctx.LVL.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));

            var table  = new System.Data.DataTable();
            var result = table.Compute(expr, null);
            return Convert.ToSingle(result);
        }
    }

    // =========================================================================
    //  CARD MODEL — carta completa
    // =========================================================================

    /// <summary>
    /// Modello dati completo di una carta.
    /// Caricato dal JSON e immutabile a runtime (tutte le modifiche
    /// avvengono su copie o tramite il sistema di fusione).
    /// </summary>
    [Serializable]
    public sealed class CardModel
    {
        // ------------------------------------------------------------------
        //  Identificazione
        // ------------------------------------------------------------------

        /// <summary>ID univoco (es. "CARD_KAEL_SLASH_001").</summary>
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>Nome visualizzato della carta.</summary>
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Descrizione breve mostrata nella galleria.</summary>
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>Testo narrativo dalla lore di Valdrath (in corsivo nella UI).</summary>
        [JsonProperty("flavorText")]
        public string FlavorText { get; set; } = string.Empty;

        // ------------------------------------------------------------------
        //  Classificazione
        // ------------------------------------------------------------------

        [JsonProperty("cardType")]
        public CardType CardType { get; set; } = CardType.Abilita;

        [JsonProperty("cardRarity")]
        public CardRarity CardRarity { get; set; } = CardRarity.Comune;

        [JsonProperty("elementType")]
        public ElementType ElementType { get; set; } = ElementType.Neutro;

        /// <summary>
        /// Sotto-tipo per carte Equipaggiamento (Arma, Armatura, Accessorio).
        /// Null per carte non-equipaggiamento.
        /// </summary>
        [JsonProperty("equipmentSubType")]
        public EquipmentSubType? EquipmentSubType { get; set; }

        // ------------------------------------------------------------------
        //  Costo
        // ------------------------------------------------------------------

        /// <summary>Costo SP per usare la carta in battaglia (0-6).</summary>
        [JsonProperty("spCost")]
        public int SpCost { get; set; } = 1;

        // ------------------------------------------------------------------
        //  Effetti
        // ------------------------------------------------------------------

        /// <summary>Lista degli effetti prodotti dalla carta.</summary>
        [JsonProperty("effects")]
        public List<CardEffect> Effects { get; set; } = new();

        // ------------------------------------------------------------------
        //  Requisiti
        // ------------------------------------------------------------------

        /// <summary>
        /// Classi che possono equipaggiare questa carta.
        /// Null o lista vuota = tutti i personaggi.
        /// </summary>
        [JsonProperty("allowedClasses")]
        public List<CharacterClass>? AllowedClasses { get; set; }

        /// <summary>
        /// Livello minimo del personaggio per sbloccare la carta.
        /// 0 = disponibile dal livello 1.
        /// </summary>
        [JsonProperty("unlockLevel")]
        public int UnlockLevel { get; set; } = 0;

        // ------------------------------------------------------------------
        //  Sinergie e tag
        // ------------------------------------------------------------------

        /// <summary>
        /// Tag per combo e sinergie.
        /// Esempi: ["spada", "luce", "kael"], ["sigillo", "custode"]
        /// </summary>
        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new();

        // ------------------------------------------------------------------
        //  Asset
        // ------------------------------------------------------------------

        /// <summary>Path relativo all'artwork della carta (/Assets/Sprites/Cards/).</summary>
        [JsonProperty("artworkPath")]
        public string ArtworkPath { get; set; } = string.Empty;

        // ------------------------------------------------------------------
        //  Statistiche equipaggiamento (per carte Equipaggiamento)
        // ------------------------------------------------------------------

        [JsonProperty("statATK")]
        public int StatATK { get; set; } = 0;

        [JsonProperty("statMAG")]
        public int StatMAG { get; set; } = 0;

        [JsonProperty("statDEF")]
        public int StatDEF { get; set; } = 0;

        [JsonProperty("statRES")]
        public int StatRES { get; set; } = 0;

        [JsonProperty("statSPD")]
        public int StatSPD { get; set; } = 0;

        [JsonProperty("statSP")]
        public int StatSP  { get; set; } = 0;

        [JsonProperty("statHP")]
        public int StatHP  { get; set; } = 0;

        // ------------------------------------------------------------------
        //  Fusione
        // ------------------------------------------------------------------

        /// <summary>
        /// ID della carta risultante dalla fusione (versione "+").
        /// Null = questa carta non ha una versione fusa.
        /// </summary>
        [JsonProperty("fusionResultId")]
        public string? FusionResultId { get; set; }

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        /// <summary>
        /// True se la carta può essere equipaggiata dal personaggio specificato.
        /// </summary>
        public bool CanBeUsedBy(CharacterClass characterClass)
        {
            if (AllowedClasses == null || AllowedClasses.Count == 0)
                return true;
            return AllowedClasses.Contains(characterClass);
        }

        /// <summary>True se la carta ha il tag specificato.</summary>
        public bool HasTag(string tag)
            => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True se la carta è disponibile per il personaggio al livello dato.
        /// </summary>
        public bool IsUnlockedAt(int level)
            => level >= UnlockLevel;

        public override string ToString()
            => $"[{CardRarity}] {Name} ({CardType}, {ElementType}, {SpCost}SP)";
    }
}
