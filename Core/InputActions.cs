// =============================================================================
//  La Via della Redenzione — Core/InputActions.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Definizione di tutte le azioni logiche di input del gioco.
//                Completamente agnostica alla piattaforma — nessun riferimento
//                a tasti fisici, touch o gamepad.
//
//  CORREZIONE BUG C1:
//    Originalmente questo enum era nella prima metà di Systems/InputSystem.cs
//    con i `using` della classe InputSystem scritti DOPO la chiusura del
//    namespace Core (illegale in C#). Ora è un file separato nel namespace
//    corretto, e Systems/InputSystem.cs ha i propri using in cima al file.
//
//  Contesti d'uso per schermata:
//    WorldMap (isometrico)  : Navigate, Confirm, Cancel, OpenMenu
//    SideScroll (micro area): Navigate, Confirm, Cancel, Interact, OpenMenu
//    Battle (side view FF)  : ActionA..D, Confirm, Cancel, OpenMenu,
//                             ScrollUp, ScrollDown
//    Dialogo / UI           : Confirm, Cancel, AnyInput, ScrollUp, ScrollDown
// =============================================================================

namespace LaViaDellaRedenzione.Core
{
    /// <summary>
    /// Azioni logiche di input riconosciute dal gioco.
    /// Ogni handler di piattaforma mappa i propri input fisici su questi valori.
    /// </summary>
    public enum InputAction
    {
        // ------------------------------------------------------------------
        //  NAVIGAZIONE
        // ------------------------------------------------------------------

        /// <summary>
        /// Movimento su/giù/sinistra/destra.
        /// WorldMap: sposta il gruppo tra i nodi.
        /// SideScroll: sposta il personaggio.
        /// Battle: naviga tra carte e bersagli.
        /// Menu: sposta il cursore tra le voci.
        /// </summary>
        NavigateUp    = 0,
        NavigateDown  = 1,
        NavigateLeft  = 2,
        NavigateRight = 3,

        // ------------------------------------------------------------------
        //  CONFERMA / ANNULLA
        // ------------------------------------------------------------------

        /// <summary>Conferma selezione corrente.</summary>
        Confirm = 10,

        /// <summary>Torna indietro / annulla / apre pausa se non c'è nulla da annullare.</summary>
        Cancel  = 11,

        // ------------------------------------------------------------------
        //  AZIONI DI BATTAGLIA (side view FF-style)
        //  Mappate sui 4 pulsanti virtuali Android (rombo) e sui tasti ZXAS Windows.
        //  In battaglia:
        //    ActionA = Usa Carta selezionata
        //    ActionB = Difendi (recupera 1 SP, raddoppia DEF per il turno)
        //    ActionC = Apri inventario Oggetti
        //    ActionD = Tenta la Fuga
        // ------------------------------------------------------------------

        /// <summary>Pulsante azione A — Usa Carta. Android: cerchio blu (alto rombo).</summary>
        ActionA = 20,

        /// <summary>Pulsante azione B — Difendi. Android: cerchio verde (destra rombo).</summary>
        ActionB = 21,

        /// <summary>Pulsante azione C — Oggetti. Android: cerchio giallo (basso rombo).</summary>
        ActionC = 22,

        /// <summary>Pulsante azione D — Fuggi. Android: cerchio rosso (sinistra rombo).</summary>
        ActionD = 23,

        // ------------------------------------------------------------------
        //  MENU E SCROLL
        // ------------------------------------------------------------------

        /// <summary>Apre il menu di pausa da qualsiasi schermata.</summary>
        OpenMenu   = 30,

        /// <summary>Scorre liste verso l'alto (galleria carte, bestiario, log dialogo).</summary>
        ScrollUp   = 31,

        /// <summary>Scorre liste verso il basso.</summary>
        ScrollDown = 32,

        /// <summary>
        /// Interagisce con un hotspot nel side-scroll (NPC, oggetto, porta).
        /// Distinto da Confirm per permettere binding separati su Windows.
        /// </summary>
        Interact   = 33,

        // ------------------------------------------------------------------
        //  UTILITY
        // ------------------------------------------------------------------

        /// <summary>
        /// Qualsiasi input — usato per skippare cutscene e splash screen.
        /// True se almeno un'altra azione è IsJustPressed in questo frame.
        /// </summary>
        AnyInput   = 99
    }
}
