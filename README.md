# La Via della Redenzione
### Una storia di colpa, amore e oscurità — in tre atti
**Valdrath, anno 847**

---

## Panoramica

JRPG dark fantasy multi-platform con sistema di carte per abilità ed equipaggiamento.
Protagonista: **Kael Dawnford**, ex-capitano imperiale in viaggio verso il Santuario dei Sigilli Primordiali.

**Piattaforme:** Android (portrait, D-Pad virtuale) · Windows (landscape, mouse + tastiera + gamepad)
**Tecnologia:** .NET 8 · .NET MAUI · C#

---

## Requisiti di sviluppo

| Componente | Versione minima |
|---|---|
| .NET SDK | 8.0.100 |
| .NET MAUI workload | 8.0.x |
| Android workload | incluso in MAUI |
| Visual Studio / VS Code | VS 2022 17.8+ / VS Code con C# Dev Kit |
| JDK (per Android) | 17 |
| Android SDK | API 26–34 |
| Windows 10 | 19041 (per build Windows) |

---

## Setup iniziale

```bash
# Installa i workload necessari
dotnet workload install maui
dotnet workload install android

# Ripristina le dipendenze
dotnet restore

# Verifica l'installazione
dotnet maui-check
```

---

## Build Android

### Debug (APK non firmato — sideload per test)
```bash
dotnet build -f net8.0-android -c Debug
# Output: bin/Debug/net8.0-android/com.refa.valdrath-Signed.apk
```

### Release APK (firma manuale)
```bash
# Imposta variabili d'ambiente per la firma
export ANDROID_KEYSTORE_PATH=/path/to/valdrath.keystore
export KEY_ALIAS=valdrath-key
export KEY_PASSWORD=tua_password_chiave
export STORE_PASSWORD=tua_password_store

dotnet publish -f net8.0-android -c Release -p:AndroidPackageFormat=apk
# Output: bin/Release/net8.0-android/publish/com.refa.valdrath-Signed.apk
```

### Release AAB (Google Play)
```bash
dotnet publish -f net8.0-android -c Release -p:AndroidPackageFormat=aab
# Output: bin/Release/net8.0-android/publish/com.refa.valdrath.aab
```

### Generazione keystore (prima volta)
```bash
keytool -genkeypair \
  -v \
  -keystore valdrath.keystore \
  -alias valdrath-key \
  -keyalg RSA \
  -keysize 2048 \
  -validity 10000
# NON committare il file .keystore nel repository
```

---

## Build Windows

### Debug (EXE standalone)
```bash
dotnet build -f net8.0-windows10.0.19041.0 -c Debug
# Output: bin/Debug/net8.0-windows10.0.19041.0/LaViaDellaRedenzione.exe
```

### Release EXE standalone
```bash
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None
# Output: bin/Release/net8.0-windows10.0.19041.0/publish/
```

### Creazione MSI (richiede WiX Toolset)
```bash
# Installa WiX
dotnet tool install --global wix

# Build EXE prima
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None

# Crea MSI con WiX
wix build Installer/windows.wxs \
  -d ProductVersion=$(cat VERSION.txt) \
  -d PublishDir=bin/Release/net8.0-windows10.0.19041.0/publish \
  -o LaViaDellaRedenzione-setup.msi
```

---

## Build via GitHub Actions (CI/CD)

Il repository include tre workflow in `.github/workflows/`:

| File | Trigger | Output |
|---|---|---|
| `build-android.yml` | push main / PR | APK + AAB firmati |
| `build-windows.yml` | push main / PR | MSI firmato |
| `build-debug.yml` | ogni commit | APK debug + EXE debug (non firmati) |

### Secrets richiesti nel repository GitHub

```
KEYSTORE_BASE64          # keystore Android in base64
KEY_ALIAS                # alias chiave Android
KEY_PASSWORD             # password chiave Android
STORE_PASSWORD           # password keystore Android
WINDOWS_CERT_PFX_BASE64  # certificato Windows in base64 (opzionale)
WINDOWS_CERT_PASSWORD    # password certificato Windows (opzionale)
TELEGRAM_BOT_TOKEN       # notifica Telegram al termine build (opzionale)
TELEGRAM_CHAT_ID         # ID chat Telegram (opzionale)
```

---

## Struttura del progetto

```
LaViaDellaRedenzione/
├── Core/                          # Logica pura — nessuna dipendenza UI
│   ├── GameEnums.cs               # Tutte le enumerazioni
│   ├── GameStateManager.cs        # State machine con push/pop overlay
│   ├── GameLoop.cs                # Loop 60 FPS, spiral-of-death protection
│   ├── InputActions.cs            # Azioni logiche di input
│   ├── Camera2D.cs                # Camera isometrica / side-scroll / battle
│   ├── SpriteSheet.cs             # Animazioni row-based con OnAttackHitFrame
│   ├── PlayerEntity.cs            # Kael, Lyra, Voran, Sera — logica entità
│   └── ParallaxBackground.cs      # 9 zone con palette e silhouette procedurali
│
├── Data/                          # Classi dati serializzabili JSON
│   └── ...
│
├── Systems/                       # Sistemi di gioco
│   ├── InputSystem.cs             # Input unificato cross-platform
│   ├── AssetManager.cs            # Cache LRU 50MB, caricamento asincrono
│   ├── GameManager.cs             # (prossimo) Bootstrap e orchestrazione
│   ├── SaveSystem.cs              # (prossimo) 3 slot + autosave
│   ├── BattleSystem.cs            # (prossimo) Combattimento CTB FF-style
│   ├── AudioSystem.cs             # (prossimo) BGM + SFX
│   └── ...
│
├── Screens/                       # Una classe per schermata
│   ├── MainMenuScreen.cs          # (prossimo)
│   ├── BattleScreen.cs            # (prossimo) Side view FF-style
│   ├── WorldMapScreen.cs          # (prossimo) Isometrica fissa
│   └── ...
│
├── UI/                            # Componenti UI riutilizzabili
│   ├── VirtualDPad.cs             # (prossimo) D-Pad touch Android
│   ├── VirtualActionButtons.cs    # (prossimo) 4 pulsanti touch Android
│   └── InputHintBar.cs            # (prossimo) Hint contestuali
│
├── Platforms/
│   ├── Android/
│   │   ├── AndroidManifest.xml
│   │   ├── TouchInputHandler.cs   # (prossimo)
│   │   └── AndroidNotificationSystem.cs
│   └── Windows/
│       ├── KeyboardInputHandler.cs # (prossimo)
│       ├── MouseInputHandler.cs    # (prossimo)
│       └── GamepadInputHandler.cs  # (prossimo)
│
├── Assets/
│   ├── Data/                      # JSON: carte, nemici, locazioni, dialoghi
│   ├── Sprites/                   # Sprite sheet personaggi e nemici
│   ├── Backgrounds/               # Sfondi tileable per zone
│   ├── Audio/                     # BGM (OGG) e SFX (WAV)
│   └── Localization/              # it-IT.json, en-US.json
│
├── Installer/
│   └── windows.wxs                # WiX descriptor per MSI
│
├── LaViaDellaRedenzione.Tests/    # xUnit test suite
├── LaViaDellaRedenzione.csproj
├── LaViaDellaRedenzione.sln
├── VERSION.txt                    # es. "0.1.0" — letto da CI
└── README.md
```

---

## Architettura renderer

```
GameLoop.Update()
    └── GameStateManager.Update()
            ├── WorldMap  → WorldMapRenderer  (isometrico fisso, FF8-style)
            ├── Game      → SideScrollRenderer (2D laterale, parallax 6 layer)
            └── Battle    → BattleScreen       (side view FF1/FF3-style)
                               ├── Gruppo (destra): sprite con animazioni attacco
                               └── Nemici (sinistra): sprite flippati orizzontalmente
```

### Visuale per schermata

| Schermata | Visuale | Camera |
|---|---|---|
| World Map | Isometrica fissa (FF8-style) | Fissa, mappa sempre visibile |
| Micro aree | Side-scroll 2D laterale | Lerp esponenziale, parallax 6 layer |
| Battaglia | Side view frontale (FF1/FF3-style) | Fissa, solo screen shake |

---

## Sistema di input

| Azione | Android (touch) | Windows (tastiera) | Windows (gamepad) |
|---|---|---|---|
| Naviga | D-Pad virtuale | WASD / Frecce | Stick sinistro / D-Pad |
| Conferma | Tap | Invio / Spazio | A |
| Annulla / Pausa | Pulsante pausa | Esc | B |
| Usa Carta (A) | Cerchio blu (alto) | Z | X |
| Difendi (B) | Cerchio verde (destra) | X | Y |
| Oggetti (C) | Cerchio giallo (basso) | A | LB |
| Fuggi (D) | Cerchio rosso (sinistra) | S | RB |

---

## Meccaniche principali

### Sistema Morale (Kael)
- Range: 0–100
- < 30: −15% ATK, dialogo interiore di Edric
- < 10: 25% chance di rifiuto ordine
- = 100: sblocca carta Leggendaria "Redenzione"

### Sigilli di Lyra
- 5 sigilli attivi + 1 sbiadito (Vento instabile)
- Carica: 0→3 per ogni carta dello stesso elemento
- Tutti e 5 attivati: "Custode dei Cinque" (attacco devastante)

### Animazioni d'attacco (battle screen)
- `OnAttackHitFrame` sincronizza danno + VFX al frame visivo corretto
- Kael: swing lama (hit frame 3/6)
- Lyra: rune illuminate (hit frame 3/5)
- Voran: cast lento (hit frame 4/6)
- Sera: colpo rapido (hit frame 1/4)

---

## VERSION.txt

```
0.1.0
```

Aggiornare prima di ogni release. GitHub Actions legge questo file per
impostare `versionCode` Android e `ProductVersion` nel MSI.

---

*Valdrath, anno 847 — La via è ancora aperta.*
