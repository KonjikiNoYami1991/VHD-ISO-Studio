<p align="center">
  <img src="src/VhdIsoStudio/Assets/app.png" width="96" alt="VHD ISO Studio logo">
</p>

<h1 align="center">VHD ISO Studio</h1>

<p align="center">
  Monta un VHD/VHDX, cattura il contenuto e crea un'ISO con <code>oscdimg</code>: tutti i parametri, oppure un clic in modalità automatica.<br>
  <em>Mount a VHD/VHDX, capture its content and build an ISO with <code>oscdimg</code>: every parameter, or one click in automatic mode.</em>
</p>

<p align="center">
  Windows 10/11 · x64 e ARM64 · .NET 10 LTS incluso · <b>100% portabile</b> · Italiano / English · MIT
</p>

---

🇮🇹 [Italiano](#-italiano) · 🇬🇧 [English](#-english)

## 🇮🇹 Italiano

### Download e uso portabile
Scarica `VhdIsoStudio_<versione>_win-x64_portable.zip` (o `win-arm64`) dagli artifact della CI o dalle Release, estrailo in una tua cartella (anche su chiavetta USB, **non** in Program Files) e avvia `VhdIsoStudio.exe`.

| Cartella | Contenuto |
|---|---|
| `VhdIsoStudio.exe` + DLL native | app e runtime .NET 10 (nessuna installazione, nessuna estrazione in `%TEMP%`) |
| `Data\` | `settings.json`, `Logs\`, `Downloads\`, `Work\` (incluso lo scratch di DISM) |
| `tools\oscdimg\` | `oscdimg.exe`, `etfsboot.com`, `efisys.bin`, `efisys_noprompt.bin` |

Al primo bisogno di oscdimg l'app propone la **copia portabile**: se il Windows ADK è già installato copia i file da lì, altrimenti scarica da Microsoft l'installer ADK (firma verificata), crea un layout dei soli *Deployment Tools* ed estrae il pacchetto Oscdimg con `msiexec /a` (installazione amministrativa = semplice estrazione, **niente viene installato o registrato**). oscdimg non è incluso nello ZIP per motivi di licenza Microsoft.

Per spostare l'app basta copiare l'intera cartella. Un file vuoto `no-portable.txt` accanto all'exe riporta i dati in `%ProgramData%`/`%LOCALAPPDATA%`. Unica scrittura fuori dalla cartella non evitabile: le finestre Apri/Salva di Windows ricordano le ultime cartelle.

È disponibile anche l'**exe singolo** (`VhdIsoStudio_<versione>_<rid>.exe`): stessi dati in `Data\`, ma all'avvio .NET estrae le librerie native WPF in `%TEMP%\.net`.

### Cosa fa
| Funzione | Dettagli |
|---|---|
| **Montaggio** | VHD e VHDX tramite il modulo Storage di Windows (`Mount-DiskImage`), in **sola lettura** di default; assegnazione automatica delle lettere ai volumi; i dischi montati dall'app vengono smontati all'uscita (anche in caso di errore). |
| **Cattura** | 3 modalità: **diretta** dal volume (più veloce), **copia** con robocopy in una cartella di lavoro (esclude pagefile, hiberfil, cestino, System Volume Information), **immagine WIM** con DISM (`/Capture-Image` con compressione, `/CheckIntegrity`, `/Verify`, `/Bootable`, `/NoRpFix`, `/EA`, `/ConfigFile`). |
| **Supporto base** | Il WIM catturato può essere inserito in un supporto Windows (ISO o cartella) come `sources\install.wim` per ottenere un'**ISO di installazione avviabile**. |
| **oscdimg completo** | Tutte le opzioni documentate dall'help di oscdimg 2.56: file system (`-u1 -u2 -j1 -j2 -n -nt -d -uv -ut`), UDF (`-udfver102/150/200 -us -ue -uf -yl -ur`), Joliet (`-js`), boot (`-b -p -e`, `-bootdata` BIOS+UEFI, load segment `t`), ottimizzazione (`-o -oc -oi -os`), ordine (`-y5 -yo`), messaggi (`-w1..-w4 -yd -a`), generali (`-l -t -g -h -c -y6 -yw -k -q -m -maxsize -r`) + argomenti extra. Anteprima del comando in tempo reale, validazione dei conflitti, preset. |
| **Modalità automatica** | Monta in sola lettura → riconosce il contenuto → supporto avviabile = ISO BIOS+UEFI; Windows installato = cattura WIM (+ supporto base = ISO d'installazione); dati = ISO UDF → smonta → SHA-256. |
| **Dipendenze** | Runtime .NET incluso. `oscdimg.exe` + file di boot recuperati **in modo portabile** in `tools\oscdimg` (vedi sopra) con **verifica della firma Authenticode Microsoft**; in alternativa installazione silenziosa dei soli Deployment Tools dell'ADK nel sistema (ripiego `winget`). DISM, robocopy e il modulo Storage di PowerShell sono parte di Windows. |
| **Sicurezza** | Solo account **amministratore** (locale o di dominio) con token elevato (manifest `requireAdministrator` + controllo interno). Opera solo su dischi `File Backed Virtual`. Cartelle di lavoro pericolose (radici di unità, Windows, Program Files, profili) bloccate. Nessun riavvio mai. |
| **Interfaccia** | WPF moderna, temi **Scuro / Intermedio / Chiaro / Segui Windows**, barra del titolo coordinata, icona in titlebar, taskbar ed exe, avanzamento con percentuale, tempo stimato e progresso nella taskbar, annullamento, drag & drop (anche da Esplora file non elevato), istanza singola. |
| **Accessibilità** | Navigazione completa da tastiera, tasti di accesso (Alt+lettera), anello di focus ad alta visibilità, nomi/descrizioni UI Automation per screen reader, annunci di stato (live region), contrasti **WCAG AA** verificati in tutti i temi, supporto all'**Alto contrasto** di Windows, DPI Per-Monitor V2, opzione "Riduci animazioni". |

### Scorciatoie
| Tasti | Azione |
|---|---|
| `F5` / `Ctrl+Invio` | Avvia (scheda corrente) |
| `Esc` | Annulla l'operazione |
| `Ctrl+O` | Scegli disco virtuale |
| `Ctrl+M` / `Ctrl+U` | Monta / Smonta |
| `Ctrl+1…4` | Cambia scheda |
| `Ctrl+L` | Italiano ↔ English |
| `Ctrl+T` | Tema successivo |
| `Ctrl+Maiusc+C` | Copia il comando oscdimg |
| `Ctrl+Maiusc+P` | oscdimg portabile |
| `Ctrl+I` | Installa ADK nel sistema |
| `Ctrl+Maiusc+L` | Apri la cartella dei log |
| `F1` | Informazioni e guida |

### Riga di comando
```
VhdIsoStudio.exe disco.vhdx                       precompila la sorgente
VhdIsoStudio.exe --auto disco.vhdx [cartella]     avvia subito la modalità automatica
VhdIsoStudio.exe --selftest                       verifica risorse ed esce (0 = ok)
```

### File e log
- Log: `Data\Logs` accanto all'exe (conservati 14 giorni; anche i log di DISM, adksetup e msiexec)
- Impostazioni: `Data\settings.json`
- Se la cartella dell'app non è scrivibile (o c'è `no-portable.txt`): `%ProgramData%\VhdIsoStudio\Logs` e `%LOCALAPPDATA%\VhdIsoStudio\settings.json`

### Compilare
Requisiti: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download) (lo script propone di installarlo con winget).
```
build.bat                                   (doppio clic)
.\build.ps1 -Runtime win-x64,win-arm64      (da PowerShell, meglio se elevata per il selftest)
```
In `dist\` trovi l'exe singolo e lo ZIP portabile (`..._portable.zip`), ognuno con il suo `.sha256`.
La CI (GitHub Actions) esegue test, verifica statica e build x64/ARM64 a ogni push; sui tag `v*` pubblica una release.

Su Linux/macOS si possono eseguire i controlli statici (non la build dell'exe): `bash tools/verify.sh` e `dotnet test tests/VhdIsoStudio.Tests`.

### Note e limiti noti
- **SmartScreen/antivirus**: l'exe non è firmato digitalmente; al primo avvio SmartScreen può mostrare un avviso. La soluzione definitiva è firmarlo con un certificato di code signing.
- I dischi protetti da **BitLocker** vanno sbloccati prima. Dischi **differenziali** (`.avhdx`) e VHD in uso da una VM accesa non possono essere montati.
- Per un'ISO d'installazione da un Windows installato, l'immagine dovrebbe essere **generalizzata con Sysprep**.
- Su Windows Server con SAN policy "offline", l'app porta online il disco virtuale che ha montato.

---

## 🇬🇧 English

### Portable download
Get `VhdIsoStudio_<version>_<rid>_portable.zip` from CI artifacts or Releases, extract it to a folder of yours (USB stick is fine, **not** Program Files) and run `VhdIsoStudio.exe`. Everything stays in that folder: `Data\` (settings, logs, downloads, work/DISM scratch) and `tools\oscdimg\` (oscdimg + boot files). On first need the app makes a **portable copy of oscdimg**: copied from an installed ADK, or downloaded from Microsoft (signature verified) as a Deployment Tools layout and extracted with `msiexec /a` — nothing is installed or registered. oscdimg is not bundled for Microsoft licensing reasons. An empty `no-portable.txt` next to the exe disables portable mode. The single-file exe is also provided (same `Data\` folder, but .NET extracts native WPF libraries to `%TEMP%\.net`).

### What it does
| Feature | Details |
|---|---|
| **Mounting** | VHD and VHDX via the Windows Storage module (`Mount-DiskImage`), **read-only** by default; automatic drive letter assignment; disks mounted by the app are dismounted on exit (even after errors). |
| **Capture** | 3 modes: **direct** from the volume (fastest), **copy** with robocopy to a working folder (skips pagefile, hiberfil, recycle bin, System Volume Information), **WIM image** with DISM (`/Capture-Image` with compression, `/CheckIntegrity`, `/Verify`, `/Bootable`, `/NoRpFix`, `/EA`, `/ConfigFile`). |
| **Base media** | The captured WIM can be injected into Windows media (ISO or folder) as `sources\install.wim` to get a **bootable setup ISO**. |
| **Full oscdimg** | Every option documented by oscdimg 2.56 help: file systems (`-u1 -u2 -j1 -j2 -n -nt -d -uv -ut`), UDF (`-udfver102/150/200 -us -ue -uf -yl -ur`), Joliet (`-js`), boot (`-b -p -e`, BIOS+UEFI `-bootdata`, load segment `t`), optimization (`-o -oc -oi -os`), order (`-y5 -yo`), messages (`-w1..-w4 -yd -a`), general (`-l -t -g -h -c -y6 -yw -k -q -m -maxsize -r`) + extra arguments. Live command preview, conflict validation, presets. |
| **Automatic mode** | Mounts read-only → detects content → bootable media = BIOS+UEFI ISO; installed Windows = WIM capture (+ base media = setup ISO); data = UDF ISO → dismounts → SHA-256. |
| **Dependencies** | .NET runtime bundled. `oscdimg.exe` + boot files fetched **portably** into `tools\oscdimg` with **Microsoft Authenticode signature check**; alternatively silent system install of the ADK Deployment Tools only (`winget` fallback). DISM, robocopy and the PowerShell Storage module ship with Windows. |
| **Safety** | **Administrator** accounts only (local or domain) with an elevated token (`requireAdministrator` manifest + internal check). Works only on `File Backed Virtual` disks. Dangerous working folders (drive roots, Windows, Program Files, profiles) are blocked. Never reboots. |
| **UI** | Modern WPF, **Dark / Medium / Light / Follow Windows** themes, matching title bar, icon in title bar, taskbar and exe, progress with percentage, ETA and taskbar progress, cancellation, drag & drop (also from non-elevated File Explorer), single instance. |
| **Accessibility** | Full keyboard navigation, access keys (Alt+letter), high-visibility focus ring, UI Automation names/descriptions for screen readers, status announcements (live region), **WCAG AA** contrast verified in every theme, Windows **High Contrast** support, Per-Monitor V2 DPI, "Reduce motion" option. |

### Shortcuts
`F5`/`Ctrl+Enter` start · `Esc` cancel · `Ctrl+O` choose disk · `Ctrl+M`/`Ctrl+U` mount/dismount · `Ctrl+1…4` tabs · `Ctrl+L` language · `Ctrl+T` theme · `Ctrl+Shift+C` copy oscdimg command · `Ctrl+Shift+P` portable oscdimg · `Ctrl+I` install ADK on system · `Ctrl+Shift+L` log folder · `F1` about.

### Command line
```
VhdIsoStudio.exe disk.vhdx                        pre-fills the source
VhdIsoStudio.exe --auto disk.vhdx [folder]        starts automatic mode right away
VhdIsoStudio.exe --selftest                       checks resources and exits (0 = ok)
```

### Build
Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download). Run `build.bat` or `.\build.ps1 -Runtime win-x64,win-arm64`. Output: single exe and `_portable.zip` in `dist\`, each with `.sha256`. CI builds x64/ARM64 on every push and publishes a release on `v*` tags.

### Known limitations
Unsigned exe (SmartScreen may warn); BitLocker volumes must be unlocked first; differencing disks and VHDs used by a running VM cannot be mounted; for setup ISOs the captured Windows should be Sysprep-generalized.

---

### Fonti / Sources
- Opzioni di oscdimg: output di `oscdimg -help` v2.56 (mirror: [cnman.github.io/oscdimg.html](https://cnman.github.io/oscdimg.html)); riferimento Microsoft: [Oscdimg Command-Line Options](https://learn.microsoft.com/windows-hardware/manufacture/desktop/oscdimg-command-line-options)
- Recupero portabile di oscdimg (layout ADK + MSI "Oscdimg (DesktopEditions)"): [winutil discussion #3313](https://github.com/ChrisTitusTech/winutil/discussions/3313), [tiny11builder issue #42](https://github.com/ntdevlabs/tiny11builder/issues/42)
- Installazione silenziosa ADK: [Install the Windows ADK offline](https://learn.microsoft.com/windows-hardware/get-started/adk-offline-install); URL dell'installer: manifest winget [Microsoft.WindowsADK](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/WindowsADK)
- DISM: [DISM Image Management Command-Line Options](https://learn.microsoft.com/windows-hardware/manufacture/desktop/dism-image-management-command-line-options-s14)
- Mount-DiskImage: [Storage module](https://learn.microsoft.com/powershell/module/storage/mount-diskimage)

## Licenza / License
[MIT](LICENSE). oscdimg, DISM e Windows ADK sono componenti Microsoft con licenze proprie e **non** sono inclusi. / oscdimg, DISM and the Windows ADK are Microsoft components under their own licenses and are **not** bundled.
