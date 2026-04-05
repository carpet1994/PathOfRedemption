// =============================================================================
//  La Via della Redenzione — UI/InputHintBar.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Barra in fondo allo schermo che mostra le azioni contestuali
//                disponibili con la loro icona di input, adattata al dispositivo
//                corrente (Touch / MouseKeyboard / Gamepad).
//
//  Funzionamento:
//    Ogni schermata chiama SetHints() con le azioni rilevanti per quel contesto.
//    Quando InputSystem.CurrentDevice cambia (es. giocatore collega un controller)
//    la barra si ricostruisce automaticamente con le icone corrette.
//
//  Esempio in battaglia:
//    hintBar.SetHints(
//        (InputAction.ActionA, "Usa Carta"),
//        (InputAction.ActionB, "Difendi"),
//        (InputAction.ActionC, "Oggetti"),
//        (InputAction.ActionD, "Fuggi"));
//
//  CORREZIONE BUG:
//    Originariamente definita dentro Platforms/Windows/GamepadInputHandler.cs
//    nel namespace LaViaDellaRedenzione.Platforms.Windows. Essendo UI
//    cross-platform (usata anche su Android), veniva esclusa dalla build
//    Android dal .csproj (<Compile Remove="Platforms\Windows\**\*.cs" />),
//    rendendo impossibile il riferimento da qualsiasi schermata compilata
//    per Android.
//    Spostata qui in /UI/ (namespace LaViaDellaRedenzione.UI) e compilata
//    su tutte le piattaforme.
//    GetHint() per il gamepad ora viene risolto tramite il delegate statico
//    GamepadHintResolver per evitare una dipendenza diretta su
//    Platforms.Windows da codice cross-platform.
// =============================================================================

using LaViaDellaRedenzione.Core;
using LaViaDellaRedenzione.Systems;
using Microsoft.Maui.Controls;
using System;

namespace LaViaDellaRedenzione.UI
{
    /// <summary>
    /// Barra UI cross-platform che mostra gli hint dei tasti contestuali.
    /// Si aggiorna automaticamente al cambio di InputDevice.
    /// </summary>
    public sealed class InputHintBar : ContentView
    {
        // ------------------------------------------------------------------
        //  Delegate per la risoluzione degli hint gamepad
        //  Evita una dipendenza diretta su Platforms.Windows da codice
        //  cross-platform. GameManager inietta il resolver su Windows:
        //    InputHintBar.GamepadHintResolver = GamepadInputHandler.GetHint;
        //  Su Android il delegate non viene mai invocato perché
        //  CurrentDevice è sempre Touch.
        // ------------------------------------------------------------------

        /// <summary>
        /// Funzione che converte un InputAction nel label dell'icona gamepad
        /// (es. "[A]", "[LB]"). Iniettata da GameManager su Windows.
        /// Null-safe: se non iniettata restituisce stringa vuota.
        /// </summary>
        public static Func<InputAction, string>? GamepadHintResolver { get; set; }

        // ------------------------------------------------------------------
        //  Layout
        // ------------------------------------------------------------------

        private readonly StackLayout _container;

        // ------------------------------------------------------------------
        //  Hint correnti
        // ------------------------------------------------------------------

        private (InputAction Action, string Description)[] _hints
            = Array.Empty<(InputAction, string)>();

        // ------------------------------------------------------------------
        //  Costruttore
        // ------------------------------------------------------------------

        public InputHintBar()
        {
            _container = new StackLayout
            {
                Orientation       = StackOrientation.Horizontal,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
                Spacing           = 16
            };

            Content         = _container;
            BackgroundColor = Color.FromRgba(0, 0, 0, 140);
            Padding         = new Thickness(12, 4);

            // Ascolta il cambio di dispositivo — evento sparato da InputSystem
            // dal GameLoop thread, quindi va rimarsciato sul MainThread.
            InputSystem.Instance.OnDeviceChanged += OnDeviceChanged;
        }

        // ------------------------------------------------------------------
        //  API PUBBLICA
        // ------------------------------------------------------------------

        /// <summary>
        /// Imposta gli hint contestuali da mostrare nella barra.
        /// Chiamare ad ogni cambio di contesto (ingresso in battaglia,
        /// apertura menu, ecc.).
        /// </summary>
        public void SetHints(params (InputAction Action, string Description)[] hints)
        {
            _hints = hints;
            Rebuild();
        }

        // ------------------------------------------------------------------
        //  REBUILD — ricostruisce i widget al cambio device o contesto
        // ------------------------------------------------------------------

        private void OnDeviceChanged(InputDevice device)
        {
            // L'evento arriva dal thread del GameLoop: dispatch sul MainThread
            // prima di toccare qualsiasi elemento MAUI.
            Microsoft.Maui.Controls.Application.Current?.Dispatcher.Dispatch(Rebuild);
        }

        private void Rebuild()
        {
            _container.Children.Clear();

            var device = InputSystem.Instance.CurrentDevice;

            for (int i = 0; i < _hints.Length; i++)
            {
                var (action, description) = _hints[i];

                string keyLabel = GetKeyLabel(action, device);
                if (string.IsNullOrEmpty(keyLabel)) continue;

                // Capsula "icona tasto"
                var keyView = new Label
                {
                    Text                  = keyLabel,
                    TextColor             = Colors.White,
                    FontSize              = 12,
                    FontAttributes        = FontAttributes.Bold,
                    BackgroundColor       = Color.FromRgba(255, 255, 255, 40),
                    Padding               = new Thickness(6, 2),
                    VerticalTextAlignment = TextAlignment.Center
                };

                // Testo descrizione azione
                var desc = new Label
                {
                    Text                  = description,
                    TextColor             = Color.FromRgba(255, 255, 255, 180),
                    FontSize              = 11,
                    VerticalTextAlignment = TextAlignment.Center
                };

                var cell = new StackLayout
                {
                    Orientation = StackOrientation.Horizontal,
                    Spacing     = 4,
                    Children    = { keyView, desc }
                };

                _container.Children.Add(cell);

                // Separatore verticale tra gli hint (non dopo l'ultimo)
                if (i < _hints.Length - 1)
                {
                    _container.Children.Add(new Label
                    {
                        Text                  = "│",
                        TextColor             = Color.FromRgba(255, 255, 255, 60),
                        FontSize              = 11,
                        VerticalTextAlignment = TextAlignment.Center
                    });
                }
            }
        }

        // ------------------------------------------------------------------
        //  LABEL TASTO PER DISPOSITIVO
        // ------------------------------------------------------------------

        private static string GetKeyLabel(InputAction action, InputDevice device)
        {
            return device switch
            {
                InputDevice.Gamepad      => GamepadHintResolver?.Invoke(action) ?? string.Empty,
                InputDevice.Touch        => GetTouchLabel(action),
                InputDevice.MouseKeyboard=> GetKeyboardLabel(action),
                _                        => GetKeyboardLabel(action)
            };
        }

        private static string GetKeyboardLabel(InputAction action) => action switch
        {
            InputAction.Confirm       => "[Invio]",
            InputAction.Cancel        => "[Esc]",
            InputAction.ActionA       => "[Z]",
            InputAction.ActionB       => "[X]",
            InputAction.ActionC       => "[A]",
            InputAction.ActionD       => "[S]",
            InputAction.OpenMenu      => "[Tab]",
            InputAction.ScrollUp      => "[PgSu]",
            InputAction.ScrollDown    => "[PgGiù]",
            InputAction.NavigateUp    => "[W/↑]",
            InputAction.NavigateDown  => "[S/↓]",
            InputAction.NavigateLeft  => "[A/←]",
            InputAction.NavigateRight => "[D/→]",
            InputAction.Interact      => "[E]",
            _                         => string.Empty
        };

        private static string GetTouchLabel(InputAction action) => action switch
        {
            InputAction.Confirm  => "●",
            InputAction.Cancel   => "II",
            InputAction.ActionA  => "🔵",
            InputAction.ActionB  => "🟢",
            InputAction.ActionC  => "🟡",
            InputAction.ActionD  => "🔴",
            InputAction.Interact => "👆",
            _                    => string.Empty
        };
    }
}
