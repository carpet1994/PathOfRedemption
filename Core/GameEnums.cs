// =============================================================================
//  La Via della Redenzione — Core/GameEnums.cs
//  Package : com.refa.valdrath
//  Descrizione : Tutte le enumerazioni fondamentali del gioco.
//                Nessuna dipendenza da UI o piattaforma.
// =============================================================================

namespace LaViaDellaRedenzione.Core
{
    // -------------------------------------------------------------------------
    //  ELEMENTI
    // -------------------------------------------------------------------------

    /// <summary>
    /// Elemento associato a una carta, un nemico o un effetto.
    /// Determina moltiplicatori di danno e sinergie di Sigillo per Lyra.
    /// </summary>
    public enum ElementType
    {
        Neutro   = 0,
        Luce     = 1,
        Ombra    = 2,
        Fuoco    = 3,
        Ghiaccio = 4,
        Terra    = 5,
        Vento    = 6
    }

    // -------------------------------------------------------------------------
    //  RARITÀ CARTE
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rarità di una carta. Determina colore bordo, probabilità di drop
    /// e requisiti di fusione.
    /// </summary>
    public enum CardRarity
    {
        Comune      = 0,   // bordo grigio
        Insolita    = 1,   // bordo verde
        Rara        = 2,   // bordo blu
        Epica       = 3,   // bordo viola
        Leggendaria = 4    // bordo oro
    }

    // -------------------------------------------------------------------------
    //  TIPO CARTA
    // -------------------------------------------------------------------------

    /// <summary>
    /// Categoria funzionale della carta all'interno del mazzo.
    /// Determina in quale slot può essere inserita (DeckSystem).
    /// </summary>
    public enum CardType
    {
        /// <summary>Abilità attiva usabile in battaglia, occupa uno slot Abilità.</summary>
        Abilita       = 0,

        /// <summary>Arma, armatura o accessorio; occupa lo slot Equipaggiamento corrispondente.</summary>
        Equipaggiamento = 1,

        /// <summary>Effetto sempre attivo, non richiede uso esplicito.</summary>
        Passiva       = 2,

        /// <summary>Si attiva automaticamente al verificarsi di una condizione nemica.</summary>
        Reazione      = 3
    }

    // -------------------------------------------------------------------------
    //  SOTTOTIPO EQUIPAGGIAMENTO
    // -------------------------------------------------------------------------

    /// <summary>
    /// Specifica il sotto-slot dell'equipaggiamento nel mazzo.
    /// Usato da DeckSystem per validare i vincoli di slot.
    /// </summary>
    public enum EquipmentSubType
    {
        Arma       = 0,
        Armatura   = 1,
        Accessorio = 2
    }

    // -------------------------------------------------------------------------
    //  CLASSE PERSONAGGIO
    // -------------------------------------------------------------------------

    /// <summary>
    /// Classe del personaggio giocabile.
    /// Alcune carte hanno restrizioni di utilizzo basate su questo valore.
    /// </summary>
    public enum CharacterClass
    {
        Guerriero   = 0,   // Kael Dawnford
        Custode     = 1,   // Lyra Ashveil
        Mago        = 2,   // Voran il Silente
        Esploratore = 3    // Sera
    }

    // -------------------------------------------------------------------------
    //  STATO BATTAGLIA
    // -------------------------------------------------------------------------

    /// <summary>
    /// State machine del BattleSystem.
    /// Ogni valore corrisponde a una fase distinta del combattimento.
    /// </summary>
    public enum BattleState
    {
        /// <summary>Il giocatore sceglie l'azione per il personaggio attivo.</summary>
        PlayerTurn       = 0,

        /// <summary>Il nemico sta eseguendo la propria AI.</summary>
        EnemyTurn        = 1,

        /// <summary>In esecuzione un'animazione (attacco, effetto, cura).</summary>
        AnimatingAction  = 2,

        /// <summary>Tutti i nemici sono stati sconfitti.</summary>
        Victory          = 3,

        /// <summary>Tutti i personaggi giocabili sono a 0 HP.</summary>
        Defeat           = 4,

        /// <summary>Il gruppo sta tentando la fuga (risoluzione in corso).</summary>
        Fleeing          = 5
    }

    // -------------------------------------------------------------------------
    //  PIATTAFORMA
    // -------------------------------------------------------------------------

    /// <summary>
    /// Piattaforma rilevata a runtime tramite DeviceInfo.Platform.
    /// Usata per attivare/disattivare VirtualControls, gamepad, ecc.
    /// </summary>
    public enum Platform
    {
        Android = 0,
        Windows = 1,
        Unknown = 99
    }

    // -------------------------------------------------------------------------
    //  DISPOSITIVO DI INPUT
    // -------------------------------------------------------------------------

    /// <summary>
    /// Ultimo dispositivo di input rilevato dall'InputSystem.
    /// Controlla quali hint (icone) vengono mostrati nella InputHintBar.
    /// </summary>
    public enum InputDevice
    {
        Touch          = 0,   // Android: sempre Touch
        MouseKeyboard  = 1,   // Windows: mouse o tastiera
        Gamepad        = 2    // Windows: controller Xbox / DirectInput
    }

    // -------------------------------------------------------------------------
    //  STATO DI GIOCO GLOBALE
    // -------------------------------------------------------------------------

    /// <summary>
    /// Macro-stato del gioco gestito dal GameStateManager.
    /// Supporta push/pop per overlay (pausa sopra battaglia, ecc.).
    /// </summary>
    public enum GameState
    {
        Splash      = 0,
        MainMenu    = 1,
        Loading     = 2,
        Game        = 3,
        Pause       = 4,
        Dialog      = 5,
        Battle      = 6,
        WorldMap    = 7,
        CardSelect  = 8,
        DeckBuilder = 9,
        GameOver    = 10,
        Credits     = 11,
        Options     = 12,
        LevelUp     = 13,
        Shop        = 14,
        Bestiary    = 15,
        Achievement = 16,
        PostGame    = 17
    }

    // -------------------------------------------------------------------------
    //  TIPO EFFETTO CARTA
    // -------------------------------------------------------------------------

    /// <summary>
    /// Tipo di effetto che una CardEffect produce sul bersaglio.
    /// </summary>
    public enum EffectType
    {
        Danno     = 0,
        Cura      = 1,
        Buff      = 2,
        Debuff    = 3,
        Scudo     = 4,
        Evoca     = 5,
        Stato     = 6,   // applica uno StatusEffect
        DrawCard  = 7    // pesca una carta aggiuntiva (meccanica futura)
    }

    // -------------------------------------------------------------------------
    //  BERSAGLIO EFFETTO
    // -------------------------------------------------------------------------

    /// <summary>
    /// A chi si applica un CardEffect.
    /// </summary>
    public enum TargetType
    {
        Self        = 0,
        SingleEnemy = 1,
        AllEnemies  = 2,
        SingleAlly  = 3,
        AllAllies   = 4,
        Random      = 5
    }

    // -------------------------------------------------------------------------
    //  STATO DI ALTERAZIONE
    // -------------------------------------------------------------------------

    /// <summary>
    /// Tutti i possibili stati applicabili a un personaggio o a un nemico.
    /// </summary>
    public enum StatusEffectType
    {
        // Negativi
        Avvelenato   = 0,
        Accecato     = 1,
        Rallentato   = 2,
        Stordito     = 3,
        Depresso     = 4,   // riduce Morale (solo Kael)
        Dubbio       = 5,   // attacco psicologico: riduce ATK/MAG

        // Positivi
        Potenziato   = 10,
        Protetto     = 11,
        Ispirato     = 12,  // bonus a SP regen
        Ancorato     = 13,  // immune a stati mentali per N turni
        Velocizzato  = 14
    }

    // -------------------------------------------------------------------------
    //  LIVELLO DI DIFFICOLTÀ
    // -------------------------------------------------------------------------

    /// <summary>
    /// Livello di difficoltà selezionabile dal giocatore.
    /// </summary>
    public enum DifficultyLevel
    {
        /// <summary>Racconto: danni ridotti, nessun game over, encounter dimezzati.</summary>
        Racconto  = 0,

        /// <summary>Viandante: bilanciamento standard.</summary>
        Viandante = 1,

        /// <summary>Capitano: danni aumentati, Morale più fragile, loot ridotto.</summary>
        Capitano  = 2
    }

    // -------------------------------------------------------------------------
    //  TIPO VFX
    // -------------------------------------------------------------------------

    /// <summary>
    /// Effetti visivi gestiti dal VFXSystem in battaglia.
    /// </summary>
    public enum VFXType
    {
        DamageNumber    = 0,
        HealNumber      = 1,
        HitFlash        = 2,
        ScreenShake     = 3,
        ElementalEffect = 4,
        SigilActivation = 5,
        MoraleShiftUp   = 6,
        MoraleShiftDown = 7
    }

    // -------------------------------------------------------------------------
    //  STATO SIGILLO (Lyra)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stato corrente di un singolo Sigillo Primordiale di Lyra.
    /// </summary>
    public enum SigilState
    {
        Scarico    = 0,   // 0 cariche
        Carico1    = 1,   // 1 carica
        Carico2    = 2,   // 2 cariche
        Attivato   = 3,   // 3 cariche: bonus attivo
        Corrotto   = 4    // boss finale: elemento trasformato in Ombra
    }

    // -------------------------------------------------------------------------
    //  TEMA UI
    // -------------------------------------------------------------------------

    /// <summary>
    /// Tema visivo dell'interfaccia. Gestito da ThemeManager.
    /// </summary>
    public enum ThemeType
    {
        Marshen   = 0,   // default: grigio-blu, brughiera
        Drevath   = 1,   // grigio-verde, oppressivo (sblocco cap.IV)
        Santuario = 2    // oro e bianco, rune (sblocco post-game)
    }

    // -------------------------------------------------------------------------
    //  TIPO ACQUISIZIONE CARTA
    // -------------------------------------------------------------------------

    /// <summary>
    /// Come è stata ottenuta una carta (usato da CardAcquiredEvent e statistiche).
    /// </summary>
    public enum CardAcquisitionMethod
    {
        Loot    = 0,
        Shop    = 1,
        Storia  = 2,
        Fusione = 3,
        NG_Plus = 4
    }

    // -------------------------------------------------------------------------
    //  EMOZIONE PERSONAGGIO (per sprite e dialoghi)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stato emotivo mostrato dallo sprite durante i dialoghi.
    /// </summary>
    public enum CharacterEmotion
    {
        Neutro      = 0,
        Triste      = 1,
        Arrabbiato  = 2,
        Spaventato  = 3,
        Determinato = 4,
        Ironico     = 5
    }

    // -------------------------------------------------------------------------
    //  STATO LOCAZIONE (Mappa del Mondo)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stato di esplorazione di un nodo sulla mappa di Valdrath.
    /// </summary>
    public enum LocationState
    {
        NonVisitato = 0,
        Visitato    = 1,
        Completato  = 2,
        Locked      = 3    // non ancora raggiungibile nella storia
    }
}
