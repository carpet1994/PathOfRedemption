// =============================================================================
//  La Via della Redenzione — Systems/CardDatabase.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Singleton che carica tutte le carte dai file JSON all'avvio
//                e le rende accessibili per ID, tag, rarità e personaggio.
//
//  CORREZIONE BUG 2:
//    Questo file è stato estratto da Core/CardModel.cs perché CardDatabase
//    usa FileSystem.OpenAppPackageFileAsync (API MAUI), che non può stare
//    nel namespace Core (che deve restare privo di dipendenze piattaforma).
//    CardDatabase appartiene correttamente a Systems/ come tutti gli altri
//    singleton del gioco (SaveSystem, DeckSystem, InputSystem...).
//
//  CORREZIONE BUG 3:
//    I percorsi nei CardFiles ora includono il prefisso "Assets/" davanti
//    a "Data/...". FileSystem.OpenAppPackageFileAsync riceve il logical name
//    dell'asset come dichiarato nel .csproj:
//      <MauiAsset Include="Assets\**\*.*"
//                 LogicalName="%(RecursiveDir)%(Filename)%(Extension)" />
//    Il LogicalName prodotto da MSBuild per Assets\Data\cards_kael.json è
//    "Assets\Data\cards_kael.json" (con backslash su Windows) oppure
//    "Assets/Data/cards_kael.json" su Android. FileSystem normalizza i
//    separatori in modo cross-platform, quindi si usa il formato forward-slash.
//    Il path "Data/cards_kael.json" (senza prefisso Assets/) era errato e
//    avrebbe causato FileNotFoundException silenzioso al primo avvio.
//
//  INIZIALIZZAZIONE (da GameManager):
//    await CardDatabase.Instance.LoadAllAsync();
//    // Da questo momento tutte le query sono sincrone e veloci.
//
//  QUERY TIPICHE:
//    var card  = CardDatabase.Instance.GetById("CARD_KAEL_SLASH_001");
//    var cards = CardDatabase.Instance.GetCardsByTag("luce");
//    var deck  = CardDatabase.Instance.GetCardsForCharacter(CharacterClass.Guerriero, level: 5);
// =============================================================================

using LaViaDellaRedenzione.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LaViaDellaRedenzione.Systems
{
    /// <summary>
    /// Singleton che carica tutte le carte dai file JSON all'avvio
    /// e le rende accessibili per ID, tag, rarità e personaggio.
    /// </summary>
    public sealed class CardDatabase
    {
        // ------------------------------------------------------------------
        //  Singleton
        // ------------------------------------------------------------------

        private static CardDatabase? _instance;
        public static CardDatabase Instance => _instance ??= new CardDatabase();
        private CardDatabase() { }

        // ------------------------------------------------------------------
        //  Storage interno
        // ------------------------------------------------------------------

        /// <summary>Tutte le carte indicizzate per ID.</summary>
        private readonly Dictionary<string, CardModel> _byId = new();

        /// <summary>Carte indicizzate per tag (ogni carta può avere più tag).</summary>
        private readonly Dictionary<string, List<CardModel>> _byTag
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Carte indicizzate per rarità.</summary>
        private readonly Dictionary<CardRarity, List<CardModel>> _byRarity = new();

        /// <summary>Carte indicizzate per CharacterClass.</summary>
        private readonly Dictionary<CharacterClass, List<CardModel>> _byClass = new();

        public bool IsLoaded { get; private set; } = false;

        // ------------------------------------------------------------------
        //  FILE JSON DA CARICARE
        //
        //  CORREZIONE BUG 3: percorsi con prefisso "Assets/" obbligatorio.
        //  Il LogicalName MAUI per un file in Assets\Data\ è "Assets/Data/...".
        //  Senza "Assets/" FileSystem.OpenAppPackageFileAsync restituisce null
        //  silenziosamente, lasciando il database vuoto senza errori visibili.
        // ------------------------------------------------------------------

        private static readonly string[] CardFiles =
        {
            "Assets/Data/cards_kael.json",
            "Assets/Data/cards_lyra.json",
            "Assets/Data/cards_voran.json",
            "Assets/Data/cards_sera.json",
            "Assets/Data/cards_legendary.json"
        };

        // ------------------------------------------------------------------
        //  CARICAMENTO
        // ------------------------------------------------------------------

        /// <summary>
        /// Carica tutti i file JSON e costruisce gli indici.
        /// Da chiamare una sola volta all'avvio, prima del menu principale.
        /// Idempotente: chiamate successive vengono ignorate.
        /// </summary>
        public async Task LoadAllAsync()
        {
            if (IsLoaded) return;

            _byId.Clear();
            _byTag.Clear();
            _byRarity.Clear();
            _byClass.Clear();

            foreach (var filePath in CardFiles)
            {
                await LoadFileAsync(filePath);
            }

            IsLoaded = true;

            System.Diagnostics.Debug.WriteLine(
                $"[CardDatabase] Caricate {_byId.Count} carte da {CardFiles.Length} file.");
        }

        private async Task LoadFileAsync(string relativePath)
        {
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(relativePath);
                if (stream == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[CardDatabase] File non trovato: {relativePath}");
                    return;
                }

                using var reader = new StreamReader(stream);
                string json      = await reader.ReadToEndAsync();

                var cards = JsonConvert.DeserializeObject<List<CardModel>>(json);
                if (cards == null) return;

                foreach (var card in cards)
                    Register(card);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CardDatabase] Errore caricamento {relativePath}: {ex.Message}");
            }
        }

        /// <summary>Registra una carta in tutti gli indici.</summary>
        private void Register(CardModel card)
        {
            if (string.IsNullOrEmpty(card.Id)) return;

            // Indice per ID
            _byId[card.Id] = card;

            // Indice per tag
            foreach (var tag in card.Tags)
            {
                if (!_byTag.TryGetValue(tag, out var tagList))
                {
                    tagList = new List<CardModel>();
                    _byTag[tag] = tagList;
                }
                tagList.Add(card);
            }

            // Indice per rarità
            if (!_byRarity.TryGetValue(card.CardRarity, out var rarityList))
            {
                rarityList = new List<CardModel>();
                _byRarity[card.CardRarity] = rarityList;
            }
            rarityList.Add(card);

            // Indice per classe
            var classes = card.AllowedClasses;
            if (classes == null || classes.Count == 0)
            {
                // Carta universale — registra in tutte le classi
                foreach (CharacterClass cls in Enum.GetValues<CharacterClass>())
                    AddToClassIndex(cls, card);
            }
            else
            {
                foreach (var cls in classes)
                    AddToClassIndex(cls, card);
            }
        }

        private void AddToClassIndex(CharacterClass cls, CardModel card)
        {
            if (!_byClass.TryGetValue(cls, out var list))
            {
                list = new List<CardModel>();
                _byClass[cls] = list;
            }
            list.Add(card);
        }

        // ------------------------------------------------------------------
        //  QUERY
        // ------------------------------------------------------------------

        /// <summary>
        /// Restituisce la carta per ID. Null se non trovata.
        /// </summary>
        public CardModel? GetById(string id)
            => _byId.TryGetValue(id, out var card) ? card : null;

        /// <summary>
        /// Restituisce tutte le carte con il tag specificato.
        /// Case-insensitive.
        /// </summary>
        public IReadOnlyList<CardModel> GetCardsByTag(string tag)
            => _byTag.TryGetValue(tag, out var list)
                ? list
                : Array.Empty<CardModel>();

        /// <summary>
        /// Restituisce tutte le carte di una certa rarità.
        /// </summary>
        public IReadOnlyList<CardModel> GetCardsByRarity(CardRarity rarity)
            => _byRarity.TryGetValue(rarity, out var list)
                ? list
                : Array.Empty<CardModel>();

        /// <summary>
        /// Restituisce tutte le carte equipaggiabili da un personaggio
        /// al livello specificato (applica il filtro UnlockLevel).
        /// </summary>
        public IReadOnlyList<CardModel> GetCardsForCharacter(
            CharacterClass characterClass,
            int            level = 1)
        {
            if (!_byClass.TryGetValue(characterClass, out var list))
                return Array.Empty<CardModel>();

            return list
                .Where(c => c.IsUnlockedAt(level))
                .ToList();
        }

        /// <summary>
        /// Restituisce tutte le carte con un tipo specifico.
        /// </summary>
        public IReadOnlyList<CardModel> GetCardsByType(CardType type)
            => _byId.Values
                .Where(c => c.CardType == type)
                .ToList();

        /// <summary>
        /// Restituisce tutte le carte caricate.
        /// </summary>
        public IReadOnlyCollection<CardModel> GetAll()
            => _byId.Values;

        /// <summary>
        /// Numero totale di carte caricate.
        /// </summary>
        public int TotalCards => _byId.Count;
    }
}
