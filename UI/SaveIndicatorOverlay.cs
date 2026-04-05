// =============================================================================
//  La Via della Redenzione — UI/SaveIndicatorOverlay.cs
//  Package : com.refa.valdrath
//
//  Descrizione : Overlay flottante che mostra l'icona di salvataggio in
//                alto a destra dello schermo. Si iscrive agli eventi del
//                SaveSystem e gestisce autonomamente visibilità e animazioni.
//
//  Stati visivi:
//    Saving  → icona floppy disk 🖫 + testo "Salvataggio..." + fade-in
//    Success → icona ✓ verde + testo "Salvato"  + pausa 2s + fade-out
//    Failed  → icona ⚠ giallo + testo "Errore"  + pausa 3s + fade-out
//
//  Posizionamento:
//    AbsoluteLayout in alto a destra, sempre sopra tutto il resto (ZIndex alto).
//    Margine: 12px dal bordo superiore, 12px dal bordo destro.
//    Su Android tiene conto del notch tramite SafeAreaInsets.
//
//  Integrazione:
//    GameManager aggiunge questo overlay al layout principale dell'app
//    una sola volta. L'overlay si gestisce da solo per tutto il ciclo di vita.
//    Non è legato a nessuna schermata specifica — funziona su WorldMap,
//    SideScroll, Battle e qualsiasi altra schermata.
//
//  Animazioni MAUI:
//    FadeTo() per entrata/uscita fluida (200ms).
//    ScaleTo() per feedback visivo al completamento (pulse 1.0→1.15→1.0).
//    Nessuna libreria esterna necessaria.
// =============================================================================

using LaViaDellaRedenzione.Systems;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LaViaDellaRedenzione.UI
{
    /// <summary>
    /// Overlay flottante per l'icona di salvataggio.
    /// Deve essere aggiunto al layout root dell'applicazione con ZIndex elevato.
    ///
    /// UTILIZZO in App.xaml.cs o GameManager:
    ///   var saveIndicator = new SaveIndicatorOverlay();
    ///   AbsoluteLayout.SetLayoutBounds(saveIndicator, new Rect(1, 0, 160, 44));
    ///   AbsoluteLayout.SetLayoutFlags(saveIndicator,
    ///       AbsoluteLayoutFlags.XProportional);
    ///   rootLayout.Add(saveIndicator);
    /// </summary>
    public sealed class SaveIndicatorOverlay : ContentView
    {
        // ------------------------------------------------------------------
        //  Stato interno
        // ------------------------------------------------------------------

        private enum IndicatorState { Hidden, Saving, Success, Failed }
        private IndicatorState _state = IndicatorState.Hidden;

        // Token per cancellare il timer di auto-hide
        private CancellationTokenSource? _hideCts;

        // ------------------------------------------------------------------
        //  Componenti UI
        // ------------------------------------------------------------------

        private readonly Label _iconLabel;
        private readonly Label _textLabel;
        private readonly Frame _container;

        // ------------------------------------------------------------------
        //  Colori per stato
        // ------------------------------------------------------------------

        private static readonly Color ColorSaving  = Color.FromRgba(30,  30,  46,  210);
        private static readonly Color ColorSuccess = Color.FromRgba(20,  60,  30,  210);
        private static readonly Color ColorFailed  = Color.FromRgba(60,  30,  10,  210);

        private static readonly Color TextSaving   = Color.FromRgba(200, 200, 220, 255);
        private static readonly Color TextSuccess  = Color.FromRgba(100, 220, 120, 255);
        private static readonly Color TextFailed   = Color.FromRgba(255, 200,  60, 255);

        // ------------------------------------------------------------------
        //  COSTRUTTORE
        // ------------------------------------------------------------------

        public SaveIndicatorOverlay()
        {
            // ── Label icona ───────────────────────────────────────────────
            _iconLabel = new Label
            {
                Text                  = "🖫",
                FontSize              = 16,
                TextColor             = TextSaving,
                VerticalTextAlignment = TextAlignment.Center,
                Margin                = new Thickness(0, 0, 4, 0)
            };

            // ── Label testo ───────────────────────────────────────────────
            _textLabel = new Label
            {
                Text                  = "Salvataggio...",
                FontSize              = 12,
                TextColor             = TextSaving,
                VerticalTextAlignment = TextAlignment.Center,
                FontAttributes        = FontAttributes.None
            };

            // ── Stack orizzontale icona + testo ───────────────────────────
            var row = new StackLayout
            {
                Orientation       = StackOrientation.Horizontal,
                VerticalOptions   = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Spacing           = 2,
                Children          = { _iconLabel, _textLabel }
            };

            // ── Container con sfondo arrotondato ─────────────────────────
            _container = new Frame
            {
                Content         = row,
                BackgroundColor = ColorSaving,
                CornerRadius    = 10,
                Padding         = new Thickness(10, 6),
                HasShadow       = true,
                BorderColor     = Color.FromRgba(255, 255, 255, 30)
            };

            Content         = _container;
            Opacity         = 0;      // nascosto all'avvio
            IsVisible       = false;
            InputTransparent = true;  // non intercetta touch/click

            // ZIndex alto — sempre sopra tutto
            ZIndex = 9999;

            // ── Iscrizione agli eventi SaveSystem ─────────────────────────
            var save = SaveSystem.Instance;
            save.OnSaveStarted  += HandleSaveStarted;
            save.OnSaveComplete += HandleSaveComplete;
            save.OnSaveFailed   += HandleSaveFailed;
        }

        // ------------------------------------------------------------------
        //  HANDLER EVENTI SAVE SYSTEM
        // ------------------------------------------------------------------

        private void HandleSaveStarted()
            => MainThread.BeginInvokeOnMainThread(() => ShowAsync(IndicatorState.Saving));

        private void HandleSaveComplete()
            => MainThread.BeginInvokeOnMainThread(() => ShowThenHideAsync(
                IndicatorState.Success,
                displaySeconds: 2.0));

        private void HandleSaveFailed(string error)
            => MainThread.BeginInvokeOnMainThread(() => ShowThenHideAsync(
                IndicatorState.Failed,
                displaySeconds: 3.0));

        // ------------------------------------------------------------------
        //  ANIMAZIONI
        // ------------------------------------------------------------------

        /// <summary>
        /// Mostra l'overlay con fade-in e imposta lo stato visivo.
        /// </summary>
        private async void ShowAsync(IndicatorState state)
        {
            // Cancella eventuale timer di hide precedente
            _hideCts?.Cancel();
            _hideCts = null;

            _state = state;
            ApplyVisualState(state);

            IsVisible = true;

            // Fade-in 200ms
            await this.FadeTo(1.0, 200, Easing.CubicOut);
        }

        /// <summary>
        /// Mostra l'overlay, lo mantiene per displaySeconds, poi lo nasconde.
        /// Usato per Success e Failed (stati temporanei).
        /// </summary>
        private async void ShowThenHideAsync(
            IndicatorState state,
            double         displaySeconds)
        {
            _hideCts?.Cancel();
            _hideCts = new CancellationTokenSource();
            var token = _hideCts.Token;

            _state = state;
            ApplyVisualState(state);

            IsVisible = true;

            // Se era già visibile (stava mostrando "Salvataggio..."),
            // anima il cambio di stato con un piccolo pulse
            if (Opacity > 0.5)
            {
                await _container.ScaleTo(1.12, 100, Easing.CubicOut);
                await _container.ScaleTo(1.00, 100, Easing.CubicIn);
            }
            else
            {
                await this.FadeTo(1.0, 200, Easing.CubicOut);
            }

            // Pausa visibile
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(displaySeconds),
                    token);
            }
            catch (TaskCanceledException)
            {
                // Un nuovo salvataggio è iniziato prima della fine del timer
                return;
            }

            // Fade-out 300ms
            await this.FadeTo(0.0, 300, Easing.CubicIn);
            IsVisible = false;
            _state    = IndicatorState.Hidden;
        }

        // ------------------------------------------------------------------
        //  STATO VISIVO
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggiorna icona, testo e colori in base allo stato.
        /// </summary>
        private void ApplyVisualState(IndicatorState state)
        {
            switch (state)
            {
                case IndicatorState.Saving:
                    _iconLabel.Text              = "🖫";
                    _textLabel.Text              = "Salvataggio...";
                    _iconLabel.TextColor         = TextSaving;
                    _textLabel.TextColor         = TextSaving;
                    _container.BackgroundColor   = ColorSaving;
                    break;

                case IndicatorState.Success:
                    _iconLabel.Text              = "✓";
                    _textLabel.Text              = "Salvato";
                    _iconLabel.TextColor         = TextSuccess;
                    _textLabel.TextColor         = TextSuccess;
                    _container.BackgroundColor   = ColorSuccess;
                    break;

                case IndicatorState.Failed:
                    _iconLabel.Text              = "⚠";
                    _textLabel.Text              = "Errore salvataggio";
                    _iconLabel.TextColor         = TextFailed;
                    _textLabel.TextColor         = TextFailed;
                    _container.BackgroundColor   = ColorFailed;
                    break;
            }
        }

        // ------------------------------------------------------------------
        //  CLEANUP
        // ------------------------------------------------------------------

        /// <summary>
        /// Deregistra i listener dal SaveSystem.
        /// Chiamare quando l'overlay viene rimosso dal layout.
        /// </summary>
        public void Detach()
        {
            var save = SaveSystem.Instance;
            save.OnSaveStarted  -= HandleSaveStarted;
            save.OnSaveComplete -= HandleSaveComplete;
            save.OnSaveFailed   -= HandleSaveFailed;

            _hideCts?.Cancel();
        }

        // ------------------------------------------------------------------
        //  POSIZIONAMENTO HELPER
        // ------------------------------------------------------------------

        /// <summary>
        /// Configura il posizionamento dell'overlay in un AbsoluteLayout.
        /// Chiamare dopo aver aggiunto l'overlay al layout root.
        /// </summary>
        /// <param name="parent">AbsoluteLayout root dell'applicazione.</param>
        /// <param name="safeAreaTop">
        /// Altezza della safe area superiore (notch Android / menu bar macOS).
        /// Ottenuto da Window.GetDisplayDensity() o SafeAreaInsets.
        /// </param>
        public static SaveIndicatorOverlay CreateAndAttach(
            AbsoluteLayout parent,
            double         safeAreaTop = 0)
        {
            var overlay = new SaveIndicatorOverlay();

            // Posiziona in alto a destra con margine fisso
            // X proporzionale (1.0 = bordo destro), Y fisso
            AbsoluteLayout.SetLayoutBounds(overlay,
                new Rect(1.0, safeAreaTop + 12, 170, 44));

            AbsoluteLayout.SetLayoutFlags(overlay,
                AbsoluteLayoutFlags.XProportional);

            parent.Add(overlay);
            return overlay;
        }
    }
}
