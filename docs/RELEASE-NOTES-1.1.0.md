## VHD ISO Studio 1.1.0 — edizione completamente portabile / fully portable edition

### 🇮🇹 Novità
- **Completamente portabile**: impostazioni, log, download e file di lavoro (compreso lo scratch di DISM) in `Data\` accanto all'exe.
- **oscdimg portabile** in `tools\oscdimg` senza installare l'ADK: copia da un ADK già installato, oppure download da Microsoft (firma Authenticode verificata) + estrazione del solo pacchetto Oscdimg con `msiexec /a`.
- **ZIP portabile**: librerie native WPF accanto all'exe, nessuna estrazione in `%TEMP%`.

### 🇬🇧 What's new
- **Fully portable**: settings, logs, downloads and work files (DISM scratch included) in `Data\` next to the exe.
- **Portable oscdimg** in `tools\oscdimg` without installing the ADK (copy from installed ADK, or Microsoft download with signature check + `msiexec /a` extraction of the Oscdimg package only).
- **Portable ZIP**: native WPF libraries next to the exe, no extraction to `%TEMP%`.

### Download
| File | Uso / Use |
|---|---|
| `VhdIsoStudio_1.1.0_win-x64_portable.zip` | **Consigliato** / **Recommended** (Windows x64) |
| `VhdIsoStudio_1.1.0_win-arm64_portable.zip` | Windows ARM64 |
| `VhdIsoStudio_1.1.0_win-x64.exe` / `_win-arm64.exe` | Exe singolo / single file (estrae le DLL native in `%TEMP%\.net`) |

Requisiti / Requirements: Windows 10 1809+ / 11, account amministratore / administrator account. Il runtime .NET 10 è incluso / .NET 10 runtime included.
oscdimg (Microsoft Windows ADK) non è incluso: l'app lo scarica da Microsoft al primo uso / is not bundled: the app downloads it from Microsoft on first use.

⚠️ Exe non firmato: SmartScreen può mostrare un avviso / Unsigned exe: SmartScreen may warn. Verifica i `.sha256` / check the `.sha256` files.
