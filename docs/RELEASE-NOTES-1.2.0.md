## VHD ISO Studio 1.2.0 — piccole comodità / quality of life

### 🇮🇹 Novità
- **Niente sospensione durante i lavori**: mentre l'app monta, cattura o crea l'ISO, Windows non va in sospensione automatica (lo schermo può comunque spegnersi). Al termine, o se annulli, torna il comportamento normale.
- **Avanzamento nel titolo**: la percentuale compare nel titolo della finestra (es. `42% · VHD ISO Studio 1.2.0`), quindi la vedi anche in taskbar e in Alt+Tab.
- **Velocità media** nel riepilogo finale, accanto alla dimensione dell'ISO.
- Le release su GitHub ora mostrano automaticamente queste note.

### 🇬🇧 What's new
- **No sleep during jobs**: while the app mounts, captures or builds the ISO, Windows won't auto-sleep (the display may still turn off). Normal behaviour returns when the job ends or is cancelled.
- **Progress in the title**: the percentage shows in the window title (e.g. `42% · VHD ISO Studio 1.2.0`), so it's visible in the taskbar and Alt+Tab.
- **Average speed** in the final summary, next to the ISO size.
- GitHub releases now show these notes automatically.

### Download
| File | Uso / Use |
|---|---|
| `VhdIsoStudio_1.2.0_win-x64_portable.zip` | **Consigliato** / **Recommended** (Windows x64) |
| `VhdIsoStudio_1.2.0_win-arm64_portable.zip` | Windows ARM64 |
| `VhdIsoStudio_1.2.0_win-x64.exe` / `_win-arm64.exe` | Exe singolo / single file (estrae le DLL native in `%TEMP%\.net`) |

Requisiti / Requirements: Windows 10 1809+ / 11, account amministratore / administrator account. Il runtime .NET 10 è incluso / .NET 10 runtime included.
oscdimg (Microsoft Windows ADK) non è incluso: l'app lo scarica da Microsoft al primo uso / is not bundled: the app downloads it from Microsoft on first use.

⚠️ Exe non firmato: SmartScreen può mostrare un avviso / Unsigned exe: SmartScreen may warn. Verifica i `.sha256` / check the `.sha256` files.
