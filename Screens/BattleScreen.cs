// =============================================================================
//  La Via della Redenzione — Screens/BattleScreen.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Schermata di battaglia side view FF1/FF3-style con:
//
//  LAYOUT (ispirato all'immagine di riferimento):
//
//    ┌─────────────────────────────────────────────────────────┐
//    │  AREA BATTAGLIA (60% superiore)                         │
//    │  Sfondo zona corrente (ParallaxBackground statico)      │
//    │  Gruppo a SINISTRA  |  Nemici a DESTRA                  │
//    │  Sprite pixel art con animazioni (SpriteSheet)          │
//    └──────────────────┬──────────────────────────────────────┘
//    ┌─────────────────┐│┌────────────────────────────────────┐
//    │ PANNELLO GRUPPO ││ LOG BATTAGLIA + COMMAND MENU / CARTE│
//    │ ▶ Kael  HP 120  ││ *Kael prepara l'azione!*            │
//    │   Lyra  HP  85  ││ *Ombra Vuota ruggisce.*             │
//    │   Voran HP  98  ││ ─────────────────────────────────── │
//    │   Sera  HP  70  ││ COMMAND MENU:                       │
//    │ [Morale Kael]   ││  ⚔ ATTACCA                         │
//    │ [Sigilli Lyra]  ││  🃏 CARTE  ← apre hand panel        │
//    └─────────────────┘│  🛡 DIFENDI                         │
//                       │  👟 FUGGI                           │
//                       └────────────────────────────────────┘
//    ┌─────────────────────────────────────────────────────────┐
//    │ TIMELINE CTB: [Kael][Ombra][Lyra][Goblin][Voran]...    │
//    └─────────────────────────────────────────────────────────┘
//
//  HAND PANEL (appare quando si seleziona CARTE):
//    ┌─────────────────────────────────────────────────────────┐
//    │ ◀ [1:TAGLIO] [2:DISCIPLINA] [3:RITUALE] [4:VEGLIA] ▶  │
//    │ MAZZO: 32 carte  |  MANO: 4 carte                      │
//    └─────────────────────────────────────────────────────────┘
//
//  PIATTAFORME:
//    Android  → VirtualDPad + VirtualActionButtons sovrapposti
//    Windows  → tastiera (Z/X/A/S + frecce) + mouse click
//
//  INTEGRAZIONE:
//    BattleScreen ascolta gli eventi di BattleSystem (StateChanged,
//    DamageResolved, TurnChanged, BattleEnded, MoraleChanged) e
//    aggiorna la UI di conseguenza.
//    SpriteSheet.OnAttackHitFrame → BattleSystem.EndPlayerAnimationPhase()
// =============================================================================

using LaViaDellaRedenzione.Core;
using LaViaDellaRedenzione.Systems;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LaViaDellaRedenzione.Screens
{
    // =========================================================================
    //  STATI INTERNI DELLA BATTLE SCREEN
    // =========================================================================

    /// <summary>
    /// Sotto-stato visivo della BattleScreen (indipendente da BattleState).
    /// </summary>
    internal enum BattleScreenPhase
    {
        /// <summary>Mostra il Command Menu principale.</summary>
        CommandMenu,

        /// <summary>Mostra la mano di carte del personaggio attivo.</summary>
        HandPanel,

        /// <summary>Seleziona il bersaglio dopo aver scelto una carta.</summary>
        TargetSelection,

        /// <summary>Animazione in corso — input disabilitato.</summary>
        Animating,

        /// <summary>Battaglia terminata — transizione in uscita.</summary>
        Ended
    }

    // =========================================================================
    //  BATTLE SCREEN
    // =========================================================================

    /// <summary>
    /// ContentPage MAUI che gestisce tutta la UI della battaglia.
    ///
    /// UTILIZZO da GameStateManager:
    ///   var screen = new BattleScreen(party, enemies, zone);
    ///   await Navigation.PushModalAsync(screen);
    /// </summary>
    public sealed class BattleScreen : ContentPage, IGameScreen
    {
        // ------------------------------------------------------------------
        //  Dipendenze
        // ------------------------------------------------------------------

        private readonly BattleSystem              _battle;
        private readonly List<Character>           _party;
        private readonly List<Enemy>               _enemies;
        private readonly ZoneType                  _zone;
        private readonly ParallaxBackground        _background;

        // ------------------------------------------------------------------
        //  Stato UI
        // ------------------------------------------------------------------

        private BattleScreenPhase _phase = BattleScreenPhase.Animating;
        private int               _selectedCommandIndex = 0;
        private int               _selectedCardIndex    = 0;
        private int               _selectedTargetIndex  = 0;
        private CardModel?        _pendingCard;

        // Mano di carte del personaggio attivo (massimo 4 visibili)
        private readonly List<CardModel> _hand = new();

        // Bersagli disponibili per la selezione
        private List<BattleTarget> _availableTargets = new();

        // Log di battaglia (ultimi 4 messaggi visibili)
        private readonly Queue<string> _battleLog = new();
        private const int              MAX_LOG    = 4;

        // ------------------------------------------------------------------
        //  COMPONENTI UI MAUI
        // ------------------------------------------------------------------

        // Area battaglia (60% superiore)
        private readonly AbsoluteLayout _battleArea;

        // Pannello sinistro — lista gruppo
        private readonly VerticalStackLayout _partyPanel;

        // Pannello destro — log + menu
        private readonly VerticalStackLayout _rightPanel;
        private readonly Label               _logLabel;
        private readonly VerticalStackLayout _commandMenu;
        private readonly AbsoluteLayout      _handPanel;
        private readonly Label               _handInfoLabel;

        // Timeline CTB (barra inferiore)
        private readonly HorizontalStackLayout _timeline;

        // Root layout
        private readonly AbsoluteLayout _rootLayout;

        // ------------------------------------------------------------------
        //  SPRITE LABELS (placeholder testo finché gli asset non sono pronti)
        // ------------------------------------------------------------------

        private readonly Dictionary<string, Label> _characterLabels  = new();
        private readonly Dictionary<int,    Label> _enemyLabels      = new();

        // Comandi disponibili
        private static readonly string[] Commands = { "⚔  ATTACCA", "🃏  CARTE", "🛡  DIFENDI", "👟  FUGGI" };

        // ------------------------------------------------------------------
        //  COLORI TEMA
        // ------------------------------------------------------------------

        private static readonly Color ColBg         = Color.FromRgb(10,  14,  35);   // blu notte
        private static readonly Color ColPanel      = Color.FromRgb(18,  24,  60);   // blu pannello
        private static readonly Color ColBorder     = Color.FromRgb(60,  90, 180);   // bordo blu chiaro
        private static readonly Color ColText       = Color.FromRgb(220, 225, 255);  // testo principale
        private static readonly Color ColTextDim    = Color.FromRgb(140, 150, 200);  // testo secondario
        private static readonly Color ColActive     = Color.FromRgb(255, 220,  60);  // giallo turno attivo
        private static readonly Color ColHP         = Color.FromRgb( 80, 220,  80);  // verde HP
        private static readonly Color ColHPLow      = Color.FromRgb(220,  80,  80);  // rosso HP bassa
        private static readonly Color ColMorale     = Color.FromRgb(255, 160,  40);  // arancio Morale
        private static readonly Color ColSigil      = Color.FromRgb(100, 180, 255);  // blu sigilli
        private static readonly Color ColSigilActive= Color.FromRgb(255, 255, 100);  // giallo sigillo attivo
        private static readonly Color ColSelected   = Color.FromRgb( 40,  80, 200);  // sfondo selezione

        // ------------------------------------------------------------------
        //  COSTRUTTORE
        // ------------------------------------------------------------------

        public BattleScreen(
            List<Character> party,
            List<Enemy>     enemies,
            ZoneType        zone = ZoneType.Marshen)
        {
            _party   = party;
            _enemies = enemies;
            _zone    = zone;

            _battle     = new BattleSystem();
            _background = new ParallaxBackground();
            _background.SetZone(zone);

            BackgroundColor = ColBg;
            Shell.SetNavBarIsVisible(this, false);

            // ── Root layout assoluto ──────────────────────────────────────
            _rootLayout = new AbsoluteLayout();

            // ── Area battaglia ────────────────────────────────────────────
            _battleArea = new AbsoluteLayout
            {
                BackgroundColor = Colors.Transparent
            };

            // ── Pannello gruppo (sinistra bassa) ──────────────────────────
            _partyPanel = new VerticalStackLayout
            {
                BackgroundColor = ColPanel,
                Padding         = new Thickness(10, 8),
                Spacing         = 4
            };

            // ── Pannello destro (log + menu) ──────────────────────────────
            _logLabel = new Label
            {
                Text            = string.Empty,
                TextColor       = ColTextDim,
                FontSize        = 11,
                FontFamily      = "Courier New",
                LineBreakMode   = LineBreakMode.WordWrap
            };

            _commandMenu = new VerticalStackLayout { Spacing = 2 };

            _handPanel = new AbsoluteLayout
            {
                BackgroundColor = ColPanel,
                IsVisible       = false
            };

            _handInfoLabel = new Label
            {
                TextColor = ColTextDim,
                FontSize  = 10,
                FontFamily = "Courier New"
            };

            _rightPanel = new VerticalStackLayout
            {
                BackgroundColor = ColPanel,
                Padding         = new Thickness(10, 8),
                Spacing         = 6,
                Children        = { _logLabel, new BoxView { HeightRequest = 1, Color = ColBorder },
                                    _commandMenu }
            };

            // ── Timeline CTB ──────────────────────────────────────────────
            _timeline = new HorizontalStackLayout
            {
                BackgroundColor = Color.FromRgb(8, 12, 28),
                Padding         = new Thickness(8, 4),
                Spacing         = 6
            };

            BuildLayout();
            BuildEventListeners();

            // Avvia battaglia
            _battle.StartBattle(_party, _enemies);

            // Popola la mano iniziale
            RefreshHand();
        }

        // ------------------------------------------------------------------
        //  COSTRUZIONE LAYOUT
        // ------------------------------------------------------------------

        private void BuildLayout()
        {
            // Bordo superiore della battle area
            var battleBorder = new BoxView
            {
                Color         = ColBorder,
                HeightRequest = 2
            };

            // Pannelli inferiori affiancati
            var bottomRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),     // 35% pannello gruppo
                    new ColumnDefinition(new GridLength(2)),   // separatore
                    new ColumnDefinition(new GridLength(1.8, GridUnitType.Star)) // 65% pannello destro
                },
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Star)
                },
                BackgroundColor = ColPanel
            };

            // Separatore verticale
            var separator = new BoxView { Color = ColBorder, WidthRequest = 2 };

            bottomRow.Add(_partyPanel, 0, 0);
            bottomRow.Add(separator,   1, 0);
            bottomRow.Add(_rightPanel, 2, 0);

            // Layout principale verticale
            var mainStack = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    _battleArea,
                    battleBorder,
                    bottomRow,
                    new BoxView { HeightRequest = 1, Color = ColBorder },
                    _timeline
                }
            };

            // Proporzioni: area battaglia 55%, pannelli 35%, timeline 10%
            AbsoluteLayout.SetLayoutBounds(_battleArea,   new Rect(0, 0, 1, 0.55));
            AbsoluteLayout.SetLayoutFlags(_battleArea,    AbsoluteLayoutFlags.All);
            AbsoluteLayout.SetLayoutBounds(bottomRow,     new Rect(0, 0.55, 1, 0.38));
            AbsoluteLayout.SetLayoutFlags(bottomRow,      AbsoluteLayoutFlags.All);
            AbsoluteLayout.SetLayoutBounds(_timeline,     new Rect(0, 0.93, 1, 0.07));
            AbsoluteLayout.SetLayoutFlags(_timeline,      AbsoluteLayoutFlags.All);

            _rootLayout.Add(_battleArea);
            _rootLayout.Add(bottomRow);
            _rootLayout.Add(_timeline);

            Content = _rootLayout;
        }

        // ------------------------------------------------------------------
        //  SPRITE AREA — personaggi e nemici (placeholder testo)
        // ------------------------------------------------------------------

        private void BuildSpriteArea()
        {
            _battleArea.Children.Clear();
            _characterLabels.Clear();
            _enemyLabels.Clear();

            // ── Personaggi — lato sinistro ────────────────────────────────
            // Disposti in diagonale ascendente (come nell'immagine di riferimento)
            float charBaseX = 0.05f;
            float charBaseY = 0.55f;

            for (int i = 0; i < _party.Count; i++)
            {
                var c = _party[i];

                var label = new Label
                {
                    Text            = GetCharacterSprite(c.CharacterId),
                    FontSize        = 28,
                    TextColor       = c.CurrentHP > 0 ? Colors.White : ColTextDim,
                    HorizontalTextAlignment = TextAlignment.Center
                };

                // Offset diagonale: ogni personaggio è più in alto e più a destra
                float x = charBaseX + i * 0.10f;
                float y = charBaseY - i * 0.15f;

                AbsoluteLayout.SetLayoutBounds(label, new Rect(x, y, 0.12, 0.20));
                AbsoluteLayout.SetLayoutFlags(label,
                    AbsoluteLayoutFlags.PositionProportional |
                    AbsoluteLayoutFlags.SizeProportional);

                _battleArea.Add(label);
                _characterLabels[c.CharacterId] = label;
            }

            // ── Nemici — lato destro ──────────────────────────────────────
            float enemyBaseX = 0.62f;

            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];

                var label = new Label
                {
                    Text            = GetEnemySprite(e),
                    FontSize        = e.IsBoss ? 42 : 32,
                    TextColor       = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center
                };

                float x = enemyBaseX + (i % 2) * 0.18f;
                float y = 0.20f + (i / 2) * 0.25f;

                AbsoluteLayout.SetLayoutBounds(label, new Rect(x, y, 0.20, 0.35));
                AbsoluteLayout.SetLayoutFlags(label,
                    AbsoluteLayoutFlags.PositionProportional |
                    AbsoluteLayoutFlags.SizeProportional);

                _battleArea.Add(label);
                _enemyLabels[i] = label;
            }
        }

        // ------------------------------------------------------------------
        //  PANNELLO GRUPPO — aggiornamento
        // ------------------------------------------------------------------

        private void RefreshPartyPanel()
        {
            _partyPanel.Children.Clear();

            var activeActor = _battle.ActiveActor;

            foreach (var c in _party)
            {
                bool isActive = activeActor?.IsAlly == true
                             && activeActor.Ally?.CharacterId == c.CharacterId;

                // ── Riga personaggio ──────────────────────────────────────
                var row = new HorizontalStackLayout { Spacing = 4 };

                // Freccia turno attivo
                row.Add(new Label
                {
                    Text      = isActive ? "▶" : " ",
                    TextColor = ColActive,
                    FontSize  = 12,
                    FontFamily = "Courier New",
                    WidthRequest = 14
                });

                // Nome
                row.Add(new Label
                {
                    Text      = c.DisplayName.Length > 6
                                    ? c.DisplayName[..6]
                                    : c.DisplayName,
                    TextColor = isActive ? ColActive : ColText,
                    FontSize  = 12,
                    FontFamily = "Courier New",
                    WidthRequest = 56
                });

                // HP
                float hpPct  = c.MaxHP > 0 ? (float)c.CurrentHP / c.MaxHP : 0f;
                Color hpColor = hpPct > 0.30f ? ColHP : ColHPLow;

                row.Add(new Label
                {
                    Text      = "HP",
                    TextColor = ColTextDim,
                    FontSize  = 10,
                    FontFamily = "Courier New",
                    WidthRequest = 18
                });
                row.Add(new Label
                {
                    Text      = $"{c.CurrentHP,3}/{c.MaxHP,3}",
                    TextColor = hpColor,
                    FontSize  = 10,
                    FontFamily = "Courier New"
                });

                _partyPanel.Add(row);

                // ── Morale (solo Kael) ─────────────────────────────────────
                if (c.HasMorale)
                {
                    var moraleRow = new HorizontalStackLayout
                    {
                        Spacing  = 2,
                        Margin   = new Thickness(14, 0, 0, 0)
                    };

                    moraleRow.Add(new Label
                    {
                        Text      = "Morale",
                        TextColor = ColTextDim,
                        FontSize  = 9,
                        FontFamily = "Courier New",
                        WidthRequest = 44
                    });

                    // Barra Morale testuale: ▓▓▓▓▓░░░░░
                    int moraleBlocks = c.Morale / 10;
                    string moraleBar = new string('▓', moraleBlocks)
                                     + new string('░', 10 - moraleBlocks);

                    moraleRow.Add(new Label
                    {
                        Text      = moraleBar,
                        TextColor = c.IsMoraleCritical ? ColHPLow
                                  : c.IsMoraleLow      ? Color.FromRgb(255, 200, 60)
                                  : ColMorale,
                        FontSize  = 9,
                        FontFamily = "Courier New"
                    });

                    _partyPanel.Add(moraleRow);
                }

                // ── Sigilli (solo Lyra) ────────────────────────────────────
                if (c.Class == CharacterClass.Custode)
                {
                    var sigilRow = new HorizontalStackLayout
                    {
                        Spacing  = 2,
                        Margin   = new Thickness(14, 0, 0, 0)
                    };

                    sigilRow.Add(new Label
                    {
                        Text      = "Sigilli",
                        TextColor = ColTextDim,
                        FontSize  = 9,
                        FontFamily = "Courier New",
                        WidthRequest = 44
                    });

                    // 6 indicatori: ◆ attivo, ◇ scarico, ✦ sbiadito
                    for (int s = 0; s < 6; s++)
                    {
                        bool isLast = s == 5;
                        sigilRow.Add(new Label
                        {
                            Text      = isLast ? "✦" : "◆",
                            TextColor = isLast ? Color.FromRgb(180, 180, 180)
                                               : ColSigilActive,
                            FontSize  = 9
                        });
                    }

                    _partyPanel.Add(sigilRow);
                }

                // Separatore sottile tra personaggi
                _partyPanel.Add(new BoxView
                {
                    HeightRequest   = 1,
                    Color           = Color.FromRgba(60, 90, 180, 80)
                });
            }
        }

        // ------------------------------------------------------------------
        //  COMMAND MENU
        // ------------------------------------------------------------------

        private void RefreshCommandMenu()
        {
            _commandMenu.Children.Clear();

            // Intestazione
            _commandMenu.Add(new Label
            {
                Text      = "── Command Menu ──",
                TextColor = ColBorder,
                FontSize  = 10,
                FontFamily = "Courier New",
                HorizontalTextAlignment = TextAlignment.Center
            });

            for (int i = 0; i < Commands.Length; i++)
            {
                int idx = i;
                bool selected = (_phase == BattleScreenPhase.CommandMenu)
                             && i == _selectedCommandIndex;

                var bg = selected ? ColSelected : Colors.Transparent;

                var cmd = new Label
                {
                    Text            = Commands[i],
                    TextColor       = selected ? ColActive : ColText,
                    FontSize        = 13,
                    FontFamily      = "Courier New",
                    BackgroundColor = bg,
                    Padding         = new Thickness(4, 2)
                };

                // Tap su comando (mouse/touch)
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => OnCommandSelected(idx);
                cmd.GestureRecognizers.Add(tap);

                _commandMenu.Add(cmd);
            }
        }

        // ------------------------------------------------------------------
        //  HAND PANEL (mano di carte)
        // ------------------------------------------------------------------

        private void RefreshHand()
        {
            _hand.Clear();

            var activeChar = _battle.ActiveActor?.Ally;
            if (activeChar == null) return;

            // Per ora mostra le carte dell'equipaggiamento attivo del personaggio
            // DeckSystem fornirà la mano reale al Prompt 11 completo
            // Placeholder: prime 4 carte dell'inventario
            var db = CardDatabase.Instance;
            var cards = db.GetCardsForCharacter(activeChar.Class, activeChar.Level)
                         .Take(4)
                         .ToList();
            _hand.AddRange(cards);
        }

        private void ShowHandPanel()
        {
            _phase = BattleScreenPhase.HandPanel;

            // Svuota il pannello destro e mostra le carte
            _commandMenu.Children.Clear();

            // Intestazione
            _commandMenu.Add(new Label
            {
                Text      = "ABILITÀ MAZZO  (◄/►)",
                TextColor = ColBorder,
                FontSize  = 10,
                FontFamily = "Courier New",
                HorizontalTextAlignment = TextAlignment.Center
            });

            // Griglia carte (max 4 visibili)
            var cardsRow = new HorizontalStackLayout { Spacing = 6 };

            for (int i = 0; i < Math.Min(_hand.Count, 4); i++)
            {
                int  idx      = i;
                var  card     = _hand[i];
                bool selected = i == _selectedCardIndex;

                var cardView = BuildCardView(card, idx + 1, selected);

                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => OnCardSelected(idx);
                cardView.GestureRecognizers.Add(tap);

                cardsRow.Add(cardView);
            }

            _commandMenu.Add(cardsRow);

            // Info mazzo
            _commandMenu.Add(new Label
            {
                Text      = $"MAZZO: -- carte  |  MANO: {_hand.Count} carte",
                TextColor = ColTextDim,
                FontSize  = 9,
                FontFamily = "Courier New"
            });

            // Pulsante indietro
            var back = new Label
            {
                Text      = "◄ Indietro",
                TextColor = ColTextDim,
                FontSize  = 10,
                FontFamily = "Courier New",
                Padding   = new Thickness(4, 2)
            };
            back.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ReturnToCommandMenu())
            });
            _commandMenu.Add(back);
        }

        private Frame BuildCardView(CardModel card, int number, bool selected)
        {
            Color elementColor = GetElementColor(card.ElementType);

            var inner = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text            = number.ToString(),
                        TextColor       = ColTextDim,
                        FontSize        = 8,
                        FontFamily      = "Courier New",
                        HorizontalTextAlignment = TextAlignment.End
                    },
                    new Label
                    {
                        Text            = GetElementIcon(card.ElementType),
                        FontSize        = 22,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text            = card.Name.Length > 8
                                              ? card.Name[..8]
                                              : card.Name,
                        TextColor       = ColText,
                        FontSize        = 8,
                        FontFamily      = "Courier New",
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text      = $"{card.SpCost}SP",
                        TextColor = Color.FromRgb(100, 180, 255),
                        FontSize  = 8,
                        FontFamily = "Courier New",
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            };

            return new Frame
            {
                Content         = inner,
                BackgroundColor = selected
                    ? elementColor.WithAlpha(0.35f)
                    : elementColor.WithAlpha(0.15f),
                BorderColor     = selected ? elementColor : ColBorder,
                CornerRadius    = 6,
                Padding         = new Thickness(6, 4),
                WidthRequest    = 64,
                HasShadow       = false
            };
        }

        // ------------------------------------------------------------------
        //  TARGET SELECTION
        // ------------------------------------------------------------------

        private void ShowTargetSelection(CardModel card)
        {
            _pendingCard    = card;
            _phase          = BattleScreenPhase.TargetSelection;
            _selectedTargetIndex = 0;

            _availableTargets = _battle.Enemies
                .Where(e => e.IsAlive)
                .Select(e => BattleTarget.FromEnemy(e))
                .ToList();

            _commandMenu.Children.Clear();

            _commandMenu.Add(new Label
            {
                Text      = $"Usa: {card.Name}",
                TextColor = ColActive,
                FontSize  = 11,
                FontFamily = "Courier New"
            });
            _commandMenu.Add(new Label
            {
                Text      = "Scegli bersaglio:",
                TextColor = ColTextDim,
                FontSize  = 10,
                FontFamily = "Courier New"
            });

            for (int i = 0; i < _availableTargets.Count; i++)
            {
                int  idx      = i;
                bool selected = i == _selectedTargetIndex;
                var  t        = _availableTargets[i];

                // Cerca l'istanza EnemyInstance per HP
                var inst = _battle.Enemies
                    .FirstOrDefault(e => e.Template.Name == t.DisplayName);

                string hpText = inst != null
                    ? $" HP {inst.CurrentHP}/{inst.Template.MaxHP}"
                    : string.Empty;

                var targetLabel = new Label
                {
                    Text            = $"{(selected ? "▶" : " ")} {t.DisplayName}{hpText}",
                    TextColor       = selected ? ColActive : ColText,
                    FontSize        = 11,
                    FontFamily      = "Courier New",
                    BackgroundColor = selected ? ColSelected : Colors.Transparent,
                    Padding         = new Thickness(4, 2)
                };

                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => ConfirmTarget(idx);
                targetLabel.GestureRecognizers.Add(tap);

                _commandMenu.Add(targetLabel);
            }

            var back = new Label
            {
                Text      = "◄ Annulla",
                TextColor = ColTextDim,
                FontSize  = 10,
                FontFamily = "Courier New",
                Padding   = new Thickness(4, 2)
            };
            back.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ShowHandPanel())
            });
            _commandMenu.Add(back);
        }

        // ------------------------------------------------------------------
        //  TIMELINE CTB
        // ------------------------------------------------------------------

        private void RefreshTimeline()
        {
            _timeline.Children.Clear();

            var preview = _battle.GetTimelinePreview(7);
            var active  = _battle.ActiveActor;

            foreach (var t in preview)
            {
                bool isActive = active != null && t.DisplayName == active.DisplayName;

                var cell = new Frame
                {
                    BackgroundColor = isActive
                        ? ColActive.WithAlpha(0.3f)
                        : ColPanel,
                    BorderColor     = isActive ? ColActive : ColBorder,
                    CornerRadius    = 4,
                    Padding         = new Thickness(6, 2),
                    HasShadow       = false,
                    Content         = new Label
                    {
                        Text      = t.DisplayName.Length > 6
                                        ? t.DisplayName[..6]
                                        : t.DisplayName,
                        TextColor = isActive ? ColActive : ColText,
                        FontSize  = 9,
                        FontFamily = "Courier New"
                    }
                };

                _timeline.Add(cell);
            }
        }

        // ------------------------------------------------------------------
        //  LOG DI BATTAGLIA
        // ------------------------------------------------------------------

        private void AddLog(string message)
        {
            _battleLog.Enqueue($"*{message}*");
            while (_battleLog.Count > MAX_LOG)
                _battleLog.Dequeue();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _logLabel.Text = string.Join("\n", _battleLog);
            });
        }

        // ------------------------------------------------------------------
        //  EVENT LISTENERS — BattleSystem
        // ------------------------------------------------------------------

        private void BuildEventListeners()
        {
            _battle.StateChanged    += OnStateChanged;
            _battle.TurnChanged     += OnTurnChanged;
            _battle.DamageResolved  += OnDamageResolved;
            _battle.BattleLog       += (_, msg) => AddLog(msg);
            _battle.BattleEnded     += OnBattleEnded;
            _battle.MoraleChanged   += OnMoraleChanged;
        }

        private void OnStateChanged(object? sender, BattleState state)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                switch (state)
                {
                    case BattleState.PlayerTurn:
                        _phase = BattleScreenPhase.CommandMenu;
                        RefreshAll();
                        break;

                    case BattleState.EnemyTurn:
                    case BattleState.AnimatingAction:
                        _phase = BattleScreenPhase.Animating;
                        RefreshAll();
                        break;

                    case BattleState.Victory:
                    case BattleState.Defeat:
                    case BattleState.Fleeing:
                        _phase = BattleScreenPhase.Ended;
                        break;
                }
            });
        }

        private void OnTurnChanged(object? sender, TurnChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (e.CurrentActor != null)
                    AddLog($"{e.CurrentActor.DisplayName} prepara l'azione!");

                RefreshPartyPanel();
                RefreshTimeline();

                if (e.CurrentActor?.IsAlly == true)
                {
                    RefreshHand();
                    _selectedCommandIndex = 0;
                }
            });
        }

        private void OnDamageResolved(object? sender, DamageResolvedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Flash visivo sullo sprite colpito
                await AnimateHitAsync(e.Target, e.WasCritical);

                // Aggiorna HP nel pannello gruppo
                RefreshPartyPanel();
                RefreshEnemySprites();
            });
        }

        private void OnBattleEnded(object? sender, BattleEndedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _phase = BattleScreenPhase.Ended;

                string result = e.Outcome switch
                {
                    BattleState.Victory => "Vittoria!",
                    BattleState.Defeat  => "Sconfitta.",
                    BattleState.Fleeing => "Ritirata...",
                    _                   => string.Empty
                };

                AddLog(result);

                if (e.Outcome == BattleState.Victory)
                    AddLog($"EXP: +{e.ExpGained}  Oro: +{e.GoldGained}");

                await Task.Delay(1800);

                // Torna alla scena precedente
                GameStateManager.Instance.GoToWorldMap();
            });
        }

        private void OnMoraleChanged(object? sender, MoraleChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                string direction = e.NewValue > e.OldValue ? "▲" : "▼";
                AddLog($"Morale Kael {direction} {e.OldValue}→{e.NewValue} ({e.Cause})");
                RefreshPartyPanel();
            });
        }

        // ------------------------------------------------------------------
        //  AZIONI GIOCATORE
        // ------------------------------------------------------------------

        private void OnCommandSelected(int index)
        {
            if (_phase != BattleScreenPhase.CommandMenu) return;

            _selectedCommandIndex = index;

            switch (index)
            {
                case 0: // ATTACCA — attacco base senza carta
                    ExecuteBasicAttack();
                    break;

                case 1: // CARTE
                    ShowHandPanel();
                    break;

                case 2: // DIFENDI
                    var defender = _battle.ActiveActor?.Ally;
                    if (defender != null)
                    {
                        _battle.Defend(defender);
                        _ = PostActionAsync();
                    }
                    break;

                case 3: // FUGGI
                    var runner = _battle.ActiveActor?.Ally;
                    if (runner != null)
                        _battle.TryFlee(runner);
                    break;
            }

            RefreshCommandMenu();
        }

        private void OnCardSelected(int index)
        {
            if (_phase != BattleScreenPhase.HandPanel) return;
            if (index >= _hand.Count) return;

            _selectedCardIndex = index;
            var card = _hand[index];

            // Carte con target SingleEnemy richiedono selezione bersaglio
            bool needsTarget = card.Effects.Any(e =>
                e.Target == TargetType.SingleEnemy);

            if (needsTarget && _battle.Enemies.Any(e => e.IsAlive))
                ShowTargetSelection(card);
            else
                ExecuteCard(card, new List<BattleTarget>());
        }

        private void ConfirmTarget(int targetIndex)
        {
            if (_phase != BattleScreenPhase.TargetSelection) return;
            if (targetIndex >= _availableTargets.Count) return;

            _selectedTargetIndex = targetIndex;
            var target = _availableTargets[targetIndex];

            ExecuteCard(_pendingCard!, new List<BattleTarget> { target });
        }

        private void ExecuteBasicAttack()
        {
            // Attacco base: usa la carta "attacco" del personaggio se disponibile
            // Altrimenti usa un effetto danno diretto basato su ATK
            var user = _battle.ActiveActor?.Ally;
            if (user == null) return;

            // Cerca la prima carta danno fisico nel mazzo
            var attackCard = _hand.FirstOrDefault(c =>
                c.Effects.Any(e => e.EffectType == EffectType.Danno));

            if (attackCard != null)
            {
                var targets = _battle.Enemies
                    .Where(e => e.IsAlive)
                    .Take(1)
                    .Select(e => BattleTarget.FromEnemy(e))
                    .ToList();

                ExecuteCard(attackCard, targets);
            }
        }

        private void ExecuteCard(CardModel card, List<BattleTarget> targets)
        {
            var user = _battle.ActiveActor?.Ally;
            if (user == null) return;

            bool ok = _battle.TryPlayCard(user, card, targets);
            if (ok)
                _ = PostActionAsync();
            else
                ReturnToCommandMenu();
        }

        private async Task PostActionAsync()
        {
            _phase = BattleScreenPhase.Animating;
            RefreshAll();

            // Pausa animazione (SpriteSheet.OnAttackHitFrame la gestirà
            // con asset reali — qui placeholder temporale)
            await Task.Delay(600);

            _battle.EndPlayerAnimationPhase();
        }

        private void ReturnToCommandMenu()
        {
            _phase                = BattleScreenPhase.CommandMenu;
            _selectedCommandIndex = 0;
            RefreshAll();
        }

        // ------------------------------------------------------------------
        //  ANIMAZIONI PLACEHOLDER
        // ------------------------------------------------------------------

        private async Task AnimateHitAsync(BattleTarget target, bool critical)
        {
            // Flash bianco sullo sprite colpito (placeholder — VFXSystem nel Prompt 36)
            if (target.IsAlly && _characterLabels.TryGetValue(
                target.Ally!.CharacterId, out var charLabel))
            {
                await charLabel.FadeTo(0.2, 80);
                await charLabel.FadeTo(1.0, 80);
                if (critical)
                {
                    await charLabel.FadeTo(0.2, 60);
                    await charLabel.FadeTo(1.0, 60);
                }
            }
            else if (target.IsEnemy)
            {
                var inst = _battle.Enemies.FirstOrDefault(
                    e => e.Template.Name == target.DisplayName);

                if (inst != null && _enemyLabels.TryGetValue(inst.SlotIndex, out var enemyLabel))
                {
                    await enemyLabel.FadeTo(0.1, 80);
                    await enemyLabel.FadeTo(1.0, 80);
                    if (critical)
                    {
                        await enemyLabel.FadeTo(0.1, 60);
                        await enemyLabel.FadeTo(1.0, 60);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        //  AGGIORNAMENTO SPRITE NEMICI
        // ------------------------------------------------------------------

        private void RefreshEnemySprites()
        {
            for (int i = 0; i < _battle.Enemies.Count; i++)
            {
                var inst = _battle.Enemies[i];
                if (_enemyLabels.TryGetValue(i, out var label))
                {
                    label.TextColor = inst.IsAlive ? Colors.White : Color.FromRgba(80, 80, 80, 100);
                    label.Text      = inst.IsAlive
                        ? GetEnemySprite(inst.Template)
                        : "✝";
                }
            }
        }

        // ------------------------------------------------------------------
        //  REFRESH COMPLETO
        // ------------------------------------------------------------------

        private void RefreshAll()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BuildSpriteArea();
                RefreshPartyPanel();
                RefreshCommandMenu();
                RefreshTimeline();
            });
        }

        // ------------------------------------------------------------------
        //  IGameScreen
        // ------------------------------------------------------------------

        public void OnEnter(StateTransitionArgs args)
        {
            RefreshAll();
        }

        public void OnUpdate(float deltaTime)
        {
            // Input da tastiera Windows — gestito da KeyboardInputHandler
            // che chiama i metodi pubblici di BattleScreen tramite GameManager
        }

        public void OnRender(float deltaTime) { }

        public void OnPause()  { }
        public void OnResume() { RefreshAll(); }

        public void OnExit()
        {
            // Deregistra eventi
            _battle.StateChanged   -= OnStateChanged;
            _battle.TurnChanged    -= OnTurnChanged;
            _battle.DamageResolved -= OnDamageResolved;
            _battle.BattleEnded    -= OnBattleEnded;
            _battle.MoraleChanged  -= OnMoraleChanged;
        }

        // ------------------------------------------------------------------
        //  LIFECYCLE MAUI
        // ------------------------------------------------------------------

        protected override void OnAppearing()
        {
            base.OnAppearing();
            RefreshAll();
        }

        // ------------------------------------------------------------------
        //  HELPER — icone sprite placeholder e colori elementi
        // ------------------------------------------------------------------

        private static string GetCharacterSprite(string id) => id.ToUpperInvariant() switch
        {
            "KAEL"  => "🗡️",   // lama
            "LYRA"  => "✋",   // mano con rune
            "VORAN" => "🧙",   // mago
            "SERA"  => "👧",   // bambina
            _       => "👤"
        };

        private static string GetEnemySprite(Enemy e)
        {
            if (e.IsBoss) return "👹";
            return e.Tags.FirstOrDefault() switch
            {
                "oscurita" => "👻",
                "bestia"   => "🐺",
                "vegetale" => "🌿",
                "spettro"  => "💀",
                "soldato"  => "⚔️",
                _          => "👾"
            };
        }

        private static Color GetElementColor(ElementType el) => el switch
        {
            ElementType.Luce     => Color.FromRgb(255, 240, 100),
            ElementType.Ombra    => Color.FromRgb(140,  60, 200),
            ElementType.Fuoco    => Color.FromRgb(220,  80,  20),
            ElementType.Ghiaccio => Color.FromRgb( 80, 180, 240),
            ElementType.Terra    => Color.FromRgb(140, 100,  40),
            ElementType.Vento    => Color.FromRgb(100, 220, 160),
            _                    => Color.FromRgb(120, 130, 160)
        };

        private static string GetElementIcon(ElementType el) => el switch
        {
            ElementType.Luce     => "✨",
            ElementType.Ombra    => "🌑",
            ElementType.Fuoco    => "🔥",
            ElementType.Ghiaccio => "❄️",
            ElementType.Terra    => "🪨",
            ElementType.Vento    => "🌀",
            _                    => "⚡"
        };
    }
}
