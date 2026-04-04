# =============================================================================
#  La Via della Redenzione — .github/workflows/build-windows.yml
#
#  Workflow: Build Windows Release
#  Output  : MSI firmato (o non firmato se WINDOWS_CERT_PFX_BASE64 assente)
#  Trigger : push su main, pull_request verso main
#
#  Secrets richiesti nel repository:
#    WINDOWS_CERT_PFX_BASE64  — certificato PFX in base64 (opzionale)
#    WINDOWS_CERT_PASSWORD    — password certificato PFX (opzionale)
#    TELEGRAM_BOT_TOKEN       — token bot Telegram (opzionale)
#    TELEGRAM_CHAT_ID         — ID chat Telegram (opzionale)
#
#  Note:
#    - Runner OBBLIGATORIO windows-latest (build MAUI Windows richiede WinUI)
#    - Se WINDOWS_CERT_PFX_BASE64 non è presente, il MSI viene prodotto
#      non firmato con un warning — utile per fork pubblici e PR da fork
#    - La versione viene letta da VERSION.txt nella root del repository
# =============================================================================

name: Windows Release Build

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

env:
  DOTNET_VERSION: '8.0.x'
  PROJECT_PATH: 'LaViaDellaRedenzione.csproj'
  TARGET_FRAMEWORK: 'net8.0-windows10.0.19041.0'
  PUBLISH_DIR: 'bin\Release\net8.0-windows10.0.19041.0\publish'
  WXS_PATH: 'Installer\windows.wxs'
  CERT_PATH: '${{ github.workspace }}\valdrath-cert.pfx'

# ─────────────────────────────────────────────────────────────────────────────
#  JOB: LINT — build + test (blocca pipeline se fallisce)
# ─────────────────────────────────────────────────────────────────────────────
jobs:
  lint:
    name: Lint & Test
    runs-on: windows-latest

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Setup .NET ${{ env.DOTNET_VERSION }}
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Install MAUI + Windows workload
        run: dotnet workload install maui windows --ignore-failed-sources

      - name: Restore dipendenze
        run: dotnet restore ${{ env.PROJECT_PATH }}

      - name: Build (no publish) per verifica errori
        run: |
          dotnet build ${{ env.PROJECT_PATH }} `
            -f ${{ env.TARGET_FRAMEWORK }} `
            -c Release `
            --no-restore `
            --no-publish

      - name: Esegui unit test
        run: |
          dotnet test LaViaDellaRedenzione.Tests\LaViaDellaRedenzione.Tests.csproj `
            --no-restore `
            --logger "console;verbosity=normal"

# ─────────────────────────────────────────────────────────────────────────────
#  JOB: BUILD-WINDOWS — produce EXE standalone e MSI
# ─────────────────────────────────────────────────────────────────────────────
  build-windows:
    name: Build Windows (EXE + MSI)
    runs-on: windows-latest
    needs: lint

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Setup .NET ${{ env.DOTNET_VERSION }}
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Install MAUI + Windows workload
        run: dotnet workload install maui windows --ignore-failed-sources

      - name: Restore dipendenze
        run: dotnet restore ${{ env.PROJECT_PATH }}

      # ── Leggi versione da VERSION.txt ──────────────────────────────────────
      - name: Leggi versione applicazione
        id: version
        shell: bash
        run: |
          VERSION=$(cat VERSION.txt | tr -d '[:space:]')
          echo "version=$VERSION" >> $GITHUB_OUTPUT
          echo "version_code=${{ github.run_number }}" >> $GITHUB_OUTPUT
          echo "Versione: $VERSION (build: ${{ github.run_number }})"

      # ── Pubblica EXE standalone ───────────────────────────────────────────
      - name: Pubblica EXE Windows standalone
        run: |
          dotnet publish ${{ env.PROJECT_PATH }} `
            -f ${{ env.TARGET_FRAMEWORK }} `
            -c Release `
            -p:WindowsPackageType=None `
            -p:SelfContained=true `
            -p:ApplicationVersion=${{ steps.version.outputs.version_code }} `
            -p:ApplicationDisplayVersion=${{ steps.version.outputs.version }} `
            --no-restore

      - name: Verifica output publish
        run: |
          Write-Host "Contenuto publish dir:"
          Get-ChildItem "${{ env.PUBLISH_DIR }}" | Format-Table Name, Length
        shell: pwsh

      # ── Installa WiX Toolset ──────────────────────────────────────────────
      - name: Installa WiX Toolset
        run: dotnet tool install --global wix

      # ── Crea MSI con WiX ─────────────────────────────────────────────────
      - name: Crea MSI con WiX
        run: |
          wix build ${{ env.WXS_PATH }} `
            -d ProductVersion=${{ steps.version.outputs.version }} `
            -d PublishDir="${{ env.PUBLISH_DIR }}" `
            -d BuildNumber=${{ github.run_number }} `
            -o "LaViaDellaRedenzione-${{ steps.version.outputs.version }}-setup.msi"

      # ── Firma MSI (opzionale — skippa se il Secret non è presente) ────────
      - name: Controlla presenza certificato di firma
        id: cert_check
        shell: bash
        run: |
          if [ -n "${{ secrets.WINDOWS_CERT_PFX_BASE64 }}" ]; then
            echo "has_cert=true" >> $GITHUB_OUTPUT
            echo "Certificato presente — il MSI verrà firmato."
          else
            echo "has_cert=false" >> $GITHUB_OUTPUT
            echo "::warning::WINDOWS_CERT_PFX_BASE64 non trovato. MSI prodotto senza firma."
          fi

      - name: Decodifica certificato PFX
        if: steps.cert_check.outputs.has_cert == 'true'
        shell: pwsh
        run: |
          $certBytes = [Convert]::FromBase64String("${{ secrets.WINDOWS_CERT_PFX_BASE64 }}")
          [IO.File]::WriteAllBytes("${{ env.CERT_PATH }}", $certBytes)
          Write-Host "Certificato decodificato."

      - name: Firma MSI con signtool.exe
        if: steps.cert_check.outputs.has_cert == 'true'
        shell: pwsh
        run: |
          $signtool = Get-ChildItem `
            "C:\Program Files (x86)\Windows Kits\10\bin" `
            -Filter "signtool.exe" -Recurse | `
            Sort-Object FullName -Descending | `
            Select-Object -First 1 -ExpandProperty FullName

          Write-Host "signtool trovato: $signtool"

          & "$signtool" sign `
            /f "${{ env.CERT_PATH }}" `
            /p "${{ secrets.WINDOWS_CERT_PASSWORD }}" `
            /tr "http://timestamp.digicert.com" `
            /td sha256 `
            /fd sha256 `
            "LaViaDellaRedenzione-${{ steps.version.outputs.version }}-setup.msi"

          Write-Host "MSI firmato con successo."

      - name: Rimuovi certificato dal runner
        if: always() && steps.cert_check.outputs.has_cert == 'true'
        run: Remove-Item -Force "${{ env.CERT_PATH }}" -ErrorAction SilentlyContinue
        shell: pwsh

      # ── Upload artifact MSI ────────────────────────────────────────────────
      - name: Upload MSI artifact
        uses: actions/upload-artifact@v4
        with:
          name: LaViaDellaRedenzione-windows-setup
          path: LaViaDellaRedenzione-${{ steps.version.outputs.version }}-setup.msi
          retention-days: 30

      # ── Upload EXE standalone (opzionale, per testing rapido) ─────────────
      - name: Upload EXE artifact
        uses: actions/upload-artifact@v4
        with:
          name: LaViaDellaRedenzione-windows-exe
          path: ${{ env.PUBLISH_DIR }}\
          retention-days: 14

# ─────────────────────────────────────────────────────────────────────────────
#  JOB: NOTIFY — notifica Telegram al termine (successo o fallimento)
#  Attende ENTRAMBI i job build (android e windows se in pipeline combinata)
# ─────────────────────────────────────────────────────────────────────────────
  notify:
    name: Notifica Telegram
    runs-on: ubuntu-latest
    needs: [ lint, build-windows ]
    if: always()

    steps:
      - name: Determina stato build
        id: status
        shell: bash
        run: |
          LINT_STATUS="${{ needs.lint.result }}"
          BUILD_STATUS="${{ needs.build-windows.result }}"

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
          💻 Windows Build %23${{ github.run_number }} — <b>${{ steps.status.outputs.status }}</b>

          Branch: <code>${{ github.ref_name }}</code>
          Commit: <code>${{ github.sha }}</code>

          🗂️ MSI installer pronto
          🔗 <a href='${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}'>Dettagli build</a>"
