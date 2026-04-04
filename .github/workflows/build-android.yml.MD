# =============================================================================
#  La Via della Redenzione — .github/workflows/build-android.yml
#
#  Workflow: Build Android Release
#  Output  : APK firmato (sideload/test) + AAB firmato (Google Play)
#  Trigger : push su main, pull_request verso main
#
#  Secrets richiesti nel repository:
#    KEYSTORE_BASE64     — keystore Android in base64 (base64 < valdrath.keystore)
#    KEY_ALIAS           — alias della chiave nel keystore
#    KEY_PASSWORD        — password della chiave
#    STORE_PASSWORD      — password del keystore
#    TELEGRAM_BOT_TOKEN  — token bot Telegram (opzionale)
#    TELEGRAM_CHAT_ID    — ID chat Telegram (opzionale)
# =============================================================================

name: Android Release Build

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

env:
  DOTNET_VERSION: '8.0.x'
  PROJECT_PATH: 'LaViaDellaRedenzione.csproj'
  KEYSTORE_PATH: '${{ github.workspace }}/valdrath.keystore'

# ─────────────────────────────────────────────────────────────────────────────
#  JOB: LINT — build + test senza publish (blocca la pipeline se fallisce)
# ─────────────────────────────────────────────────────────────────────────────
jobs:
  lint:
    name: Lint & Test
    runs-on: ubuntu-latest

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Setup .NET ${{ env.DOTNET_VERSION }}
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Install MAUI workload
        run: dotnet workload install maui android --ignore-failed-sources

      - name: Restore dipendenze
        run: dotnet restore ${{ env.PROJECT_PATH }}

      - name: Build (no publish) per verifica errori
        run: |
          dotnet build ${{ env.PROJECT_PATH }} \
            -f net8.0-android \
            -c Release \
            --no-restore \
            --no-publish

      - name: Esegui unit test
        run: |
          dotnet test LaViaDellaRedenzione.Tests/LaViaDellaRedenzione.Tests.csproj \
            --no-restore \
            --logger "console;verbosity=normal"

# ─────────────────────────────────────────────────────────────────────────────
#  JOB: BUILD-ANDROID — produce APK e AAB firmati
# ─────────────────────────────────────────────────────────────────────────────
  build-android:
    name: Build Android (APK + AAB)
    runs-on: ubuntu-latest
    needs: lint

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Setup .NET ${{ env.DOTNET_VERSION }}
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Setup JDK 17
        uses: actions/setup-java@v4
        with:
          java-version: '17'
          distribution: 'temurin'

      - name: Install MAUI + Android workload
        run: dotnet workload install maui android --ignore-failed-sources

      - name: Restore dipendenze
        run: dotnet restore ${{ env.PROJECT_PATH }}

      # ── Leggi versione da VERSION.txt ──────────────────────────────────────
      - name: Leggi versione applicazione
        id: version
        run: |
          VERSION=$(cat VERSION.txt | tr -d '[:space:]')
          echo "version=$VERSION" >> $GITHUB_OUTPUT
          # versionCode = run number (incrementale, univoco per ogni build)
          echo "version_code=${{ github.run_number }}" >> $GITHUB_OUTPUT
          echo "Versione: $VERSION (code: ${{ github.run_number }})"

      # ── Decodifica keystore da Secret ─────────────────────────────────────
      - name: Decodifica keystore Android
        run: |
          echo "${{ secrets.KEYSTORE_BASE64 }}" | base64 --decode > ${{ env.KEYSTORE_PATH }}
          echo "Keystore decodificato: ${{ env.KEYSTORE_PATH }}"

      # ── Build APK firmato ─────────────────────────────────────────────────
      - name: Build APK Release (firmato)
        run: |
          dotnet publish ${{ env.PROJECT_PATH }} \
            -f net8.0-android \
            -c Release \
            -p:AndroidPackageFormat=apk \
            -p:ApplicationVersion=${{ steps.version.outputs.version_code }} \
            -p:ApplicationDisplayVersion=${{ steps.version.outputs.version }} \
            -p:AndroidKeyStore=true \
            -p:AndroidSigningKeyStore=${{ env.KEYSTORE_PATH }} \
            -p:AndroidSigningKeyAlias=${{ secrets.KEY_ALIAS }} \
            -p:AndroidSigningKeyPass=${{ secrets.KEY_PASSWORD }} \
            -p:AndroidSigningStorePass=${{ secrets.STORE_PASSWORD }} \
            --no-restore

      # ── Rinomina APK ───────────────────────────────────────────────────────
      - name: Rinomina APK
        run: |
          APK_SOURCE=$(find . -name "*.apk" -path "*/publish/*" | head -1)
          echo "APK trovato: $APK_SOURCE"
          cp "$APK_SOURCE" "LaViaDellaRedenzione-${{ steps.version.outputs.version }}-release.apk"

      # ── Upload artifact APK ────────────────────────────────────────────────
      - name: Upload APK artifact
        uses: actions/upload-artifact@v4
        with:
          name: LaViaDellaRedenzione-release-apk
          path: LaViaDellaRedenzione-${{ steps.version.outputs.version }}-release.apk
          retention-days: 30

      # ── Build AAB firmato ─────────────────────────────────────────────────
      - name: Build AAB Release (firmato)
        run: |
          dotnet publish ${{ env.PROJECT_PATH }} \
            -f net8.0-android \
            -c Release \
            -p:AndroidPackageFormat=aab \
            -p:ApplicationVersion=${{ steps.version.outputs.version_code }} \
            -p:ApplicationDisplayVersion=${{ steps.version.outputs.version }} \
            -p:AndroidKeyStore=true \
            -p:AndroidSigningKeyStore=${{ env.KEYSTORE_PATH }} \
            -p:AndroidSigningKeyAlias=${{ secrets.KEY_ALIAS }} \
            -p:AndroidSigningKeyPass=${{ secrets.KEY_PASSWORD }} \
            -p:AndroidSigningStorePass=${{ secrets.STORE_PASSWORD }} \
            --no-restore

      # ── Rinomina AAB ───────────────────────────────────────────────────────
      - name: Rinomina AAB
        run: |
          AAB_SOURCE=$(find . -name "*.aab" -path "*/publish/*" | head -1)
          echo "AAB trovato: $AAB_SOURCE"
          cp "$AAB_SOURCE" "LaViaDellaRedenzione-${{ steps.version.outputs.version }}-release.aab"

      # ── Upload artifact AAB ────────────────────────────────────────────────
      - name: Upload AAB artifact
        uses: actions/upload-artifact@v4
        with:
          name: LaViaDellaRedenzione-release-aab
          path: LaViaDellaRedenzione-${{ steps.version.outputs.version }}-release.aab
          retention-days: 30

      # ── Pulizia keystore (sicurezza) ───────────────────────────────────────
      - name: Rimuovi keystore dal runner
        if: always()
        run: rm -f ${{ env.KEYSTORE_PATH }}

# ─────────────────────────────────────────────────────────────────────────────
#  JOB: NOTIFY — notifica Telegram al termine (successo o fallimento)
# ─────────────────────────────────────────────────────────────────────────────
  notify:
    name: Notifica Telegram
    runs-on: ubuntu-latest
    needs: [ lint, build-android ]
    if: always()

    steps:
      - name: Determina stato build
        id: status
        run: |
          LINT_STATUS="${{ needs.lint.result }}"
          BUILD_STATUS="${{ needs.build-android.result }}"

          if [[ "$LINT_STATUS" == "success" && "$BUILD_STATUS" == "success" ]]; then
            echo "icon=✅" >> $GITHUB_OUTPUT
            echo "status=SUCCESSO" >> $GITHUB_OUTPUT
          elif [[ "$BUILD_STATUS" == "cancelled" || "$LINT_STATUS" == "cancelled" ]]; then
            echo "icon=⚠️" >> $GITHUB_OUTPUT
            echo "status=ANNULLATA" >> $GITHUB_OUTPUT
          else
            echo "icon=❌" >> $GITHUB_OUTPUT
            echo "status=FALLITA" >> $GITHUB_OUTPUT
          fi

      - name: Invia notifica Telegram
        if: ${{ secrets.TELEGRAM_BOT_TOKEN != '' }}
        run: |
          curl -s -X POST \
            "https://api.telegram.org/bot${{ secrets.TELEGRAM_BOT_TOKEN }}/sendMessage" \
            -d chat_id="${{ secrets.TELEGRAM_CHAT_ID }}" \
            -d parse_mode="HTML" \
            -d text="${{ steps.status.outputs.icon }} <b>La Via della Redenzione</b>
          📱 Android Build %23${{ github.run_number }} — <b>${{ steps.status.outputs.status }}</b>

          Branch: <code>${{ github.ref_name }}</code>
          Commit: <code>${{ github.sha }}</code>

          📦 APK pronto per sideload
          🏪 AAB pronto per Google Play
          🔗 <a href='${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}'>Dettagli build</a>"
