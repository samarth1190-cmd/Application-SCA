# SCA Application — Architecture Documentation

> Reverse-engineered from source code on 2026-07-16. Everything in this document was
> verified against the code (file paths cited). Where documentation and code could
> disagree, **the code is the authority**.

## 1. Executive Summary

**Aplicacion_SCA** ("Sistema de Control de Auditoría") is a **.NET MAUI 9** tablet
application built for the **Stellantis Vigo plant (QCP/AVCF quality department)**.
Auditors use it to run guided, voice-driven vehicle audits on finished vehicles:
the app reads audit instructions aloud (TTS), listens for voice commands
(offline speech recognition via Vosk), tracks the vehicle by GPS during dynamic
driving phases ("rodaje"), and uploads results to SharePoint.

- **Primary target:** Android (tablets). iOS/MacCatalyst/Windows heads exist; the
  Windows head is used for development only (maps and GPS are stubbed on Windows).
- **Backend:** There is **no custom server**. All data lives in a **SharePoint
  Online site** (`shiftup.sharepoint.com/sites/APLICACION_SCA`) accessed through
  the **Microsoft Graph API** with app-only (client-credentials) auth.
- **Database:** **Excel workbooks** stored in the SharePoint document library are
  the entire database (users, vehicles, audit content, checklists, notifications).
  Results are posted to a SharePoint **List**.
- **Architecture style:** classic **code-behind / event-driven** MAUI pages.
  No MVVM framework, no dependency injection, no unit tests, not under git
  (changes are tracked manually in `CHANGELOG.md`).
- **State:** one global static class ([SesionGlobal](../Services/SesionGlobal.cs))
  holds the whole session; a JSON auto-save
  ([AutoGuardadoService](../Services/AutoGuardadoService.cs)) provides crash recovery.

## 2. Technology stack & dependencies

| Dependency (csproj) | Version | Why it is used |
|---|---|---|
| Microsoft.Maui.Controls | 9.0.120 | UI framework (retargeted from .NET 10 → 9 on 2026-07-13, see CHANGELOG 1.0.1) |
| Microsoft.Maui.Controls.Maps | 9.0.120 | Google-Maps view for GPS routes (Android/iOS only; `#if ANDROID || IOS`) |
| CommunityToolkit.Maui | 12.3.0 | registered in [MauiProgram.cs](../MauiProgram.cs) (`UseMauiCommunityToolkit`) |
| ClosedXML | 0.105.0 | Reading all Excel workbooks ([ExcelService](../Services/ExcelService.cs)) |
| Vosk | 0.3.38 | **Offline** Spanish speech recognition (voice commands). Native `libvosk.so` for 4 Android ABIs in `Platforms/Android/lib/`; Spanish model shipped as `Resources/Raw/model_es.zip` (~40 MB) |
| Microsoft.Identity.Client | 4.83.3 | referenced but **not used** — auth is a raw HTTP client-credentials call in [SharePointService.cs:18](../Services/SharePointService.cs#L18) |
| Microsoft.Extensions.Logging.Debug | 9.0.0 | debug logging |

Platform APIs used directly: `TextToSpeech` (MAUI Essentials), `Geolocation`,
`Permissions`, `Preferences`, `Launcher` (PDF viewing), Android `AudioRecord` +
Bluetooth SCO ([Platforms/Android/AudioCaptureService.cs](../Platforms/Android/AudioCaptureService.cs)),
Android `ToneGenerator` (beeps), Google Maps snapshot API (report screenshots,
[ResultsPage.xaml.cs:454-508](../Pages/ResultsPage.xaml.cs#L454-L508)).

## 3. Folder structure

```
Aplicacion_SCA/
├── App.xaml(.cs)            App entry: global crash handlers, creates Window(AppShell)
├── AppShell.xaml(.cs)       Shell with a single route → Pages/MainPage
├── MainPage.xaml(.cs)       ⚠ LEGACY duplicate at root — NOT used (AppShell points to Pages/MainPage)
├── MauiProgram.cs           MAUI bootstrap (fonts, toolkit, maps for Android/iOS)
├── Models/                  Plain data classes (one per Excel row type)
├── Pages/                   All UI. One XAML + code-behind pair per screen. ALL logic lives here.
├── Services/                ExcelService (parsing), SharePointService (Graph API),
│                            LocalizationService (UI strings ES/EN/FR/DE),
│                            SesionGlobal (global state), AutoGuardadoService (crash backup),
│                            IAudioCaptureService (mic abstraction)
├── Platforms/               Per-OS heads. Android: AudioCaptureService, libvosk.so, manifest
├── Resources/               Fonts, images, splash, Raw/model_es.zip (Vosk model), Raw/*.pdf?
├── _APK/                    Built APKs (manual releases; v1.4.0, v1.5.0)
├── _ExcelSharePoint/        Local working copies of SharePoint Excels (CDPV.xlsx, CDPV_traducido.xlsx)
├── _Versiones/              (empty) manual version snapshots
├── firma_sca.keystore       Android signing key (⚠ committed in the project folder)
└── CHANGELOG.md             Manual change log (project is not under git)
```

## 4. Startup sequence & navigation map

```
App (App.xaml.cs)
 └─ Window(AppShell) ── Shell.Loaded → shows saved crash report if any ("UltimoCrash" preference)
     └─ Pages/MainPage         Language pick (ES/EN/FR/DE) + [EMPEZAR]
         │  EMPEZAR → downloads Usuarios.xlsx + Vehiculos.xlsx (parallel) → SesionGlobal
         └─ LoginPage           CV/PSA vs user list (offline test account admin/admin)
             │  on login → downloads Notificaciones.xlsx
             └─ AuditModePage   HUB. Static buttons + dynamic ones
                 │   • CONTROL JAPÓN → SelectionPage("Japon")
                 │   • C-DPV        → SelectionPage("CORE_DPV")     ← the flow to make plant-aware
                 │   • DPV          → SelectionPage("DPV")
                 │   • FORMACIÓN SCA→ SelectionPage("SCA_Formacion")
                 │   • RRU          → SelectionPage("RRU")   (button visible only if RRU.xlsx exists)
                 │   • one button per .xlsx found in SharePoint folder 08_Otros (dynamic audits)
                 └─ SelectionPage(modo)   pick Modelo + Motor (+ "rodaje exterior" switch)
                     │  downloads the mode's Excel on [COMENZAR]
                     ├─ modo Japon → ControlJapon ──→ ResultsJapon ──→ SharePoint list
                     ├─ modo RRU   → RRUPage ───────→ ResultsPage ───→ SharePoint list
                     └─ else       → MenuEstandarPage (phase list "hoja de ruta")
                                      ├─ phase w/o RODAJE steps → EstandarPage   (TTS+Vosk sequence)
                                      ├─ phase with RODAJE steps → RodajeExterior (TTS+Vosk+GPS)
                                      └─ FINALIZAR → ResultsPage → SharePoint list + map snapshot upload
```

Navigation is `NavigationPage` push/pop with manual stack surgery
(`Navigation.RemovePage`, searching the stack by page type name, e.g.
[ResultsPage.xaml.cs:608](../Pages/ResultsPage.xaml.cs#L608)). Shell routing is
**not** used beyond hosting the first page.

## 5. State management

Everything session-scoped is a **static property on
[SesionGlobal](../Services/SesionGlobal.cs)**: active user, chassis (VIN), model,
motor, selected mode string, loaded audit content (`Estandares` for 14-column
audits, `EstandaresDPV` for 12-column audits), Japan lists, RRU stops, GPS route,
phase timings, notifications. Pages read/write it freely.

Three persistence layers:
1. **`Preferences`** — language choice, last crash text (`UltimoCrash`), and
   per-phase resume pointers (`PasoGuardado_{VIN}_{index}`, `PasoGuardadoRodaje_…`,
   `PasoGuardadoJapon_…`).
2. **[AutoGuardadoService](../Services/AutoGuardadoService.cs)** — serializes a
   snapshot of SesionGlobal to `auditoria_encurso_backup.json` in app data;
   [SelectionPage.OnAppearing](../Pages/SelectionPage.xaml.cs#L181) offers to
   resume an interrupted audit from it.
3. **RRU backup** — separate `rru_backup.json`
   ([RRUPage.xaml.cs:29](../Pages/RRUPage.xaml.cs#L29)).

## 6. The audit engine (core business logic)

The heart of the app is the step sequencer, duplicated (with variations) in
[EstandarPage](../Pages/EstandarPage.xaml.cs#L512),
[RodajeExterior](../Pages/RodajeExterior.xaml.cs#L540) and
[ControlJapon](../Pages/ControlJapon.xaml.cs#L626):

1. Steps come from the mode's Excel (see `02_DATA_FLOW.md`).
2. Steps are **filtered** for the current audit:
   - text non-empty (formation vs audit column depends on mode containing "Formacion"),
   - **motor match**: step flags `MotorTermico/Hibrido/Electrico` (0/1) vs the
     *selected motor name* — matched by Spanish substrings `"termic"`, `"hibrid"`,
     `"electr"` ([EstandarPage.xaml.cs:325-329](../Pages/EstandarPage.xaml.cs#L325-L329)),
   - **track match**: `Exterior` column 0 = both, 1 = interior track, 2 = exterior track
     (driven by the "rodaje exterior" switch),
   - **template match**: `TipoPlantilla` `ESTATICO` → EstandarPage, contains
     `RODAJE` → RodajeExterior.
3. Each step: TTS speaks the text, then either waits `Tiempo` seconds, or if the
   time cell says **`MANUAL`**, records the microphone and feeds Vosk until it
   hears a Spanish command: *sigue/siguiente/continuar* (next), *atrás/anterior*
   (back), *pausa/parar* (pause), *repetir* (repeat). A green button is a manual
   fallback.
4. RODAJE steps with `Latitud/Longitud/Radio` block until GPS enters the radius
   (speed-compensated tolerance, [RodajeExterior.xaml.cs:1467-1484](../Pages/RodajeExterior.xaml.cs#L1467-L1484));
   the GPS route is recorded, colored by speed, and screenshot to SharePoint.
5. Completion marks the phase index in `SesionGlobal.EstandaresCompletados`;
   `ResultsPage` composes a JSON payload and posts it to a SharePoint list.

**Business rules found in code (not configurable):**
- Phases whose name contains `"EXTERIOR"` are disabled unless the exterior-track
  switch is on ([MenuEstandarPage.xaml.cs:294-307](../Pages/MenuEstandarPage.xaml.cs#L294-L307));
  route reporting also keys off the phase name containing `"EXTERIOR"`
  ([ResultsPage.xaml.cs:688-692](../Pages/ResultsPage.xaml.cs#L688-L692)).
- Chassis/VIN format: exactly **2 letters + 6 digits** (`^[A-Z]{2}[0-9]{6}$`,
  MaxLength 8) — Vigo's internal chassis number, *not* a 17-char ISO VIN
  ([ResultsPage.xaml.cs:185](../Pages/ResultsPage.xaml.cs#L185),
  [ResultsJapon.xaml.cs:179](../Pages/ResultsJapon.xaml.cs#L179)).
  MenuEstandarPage only requires length ≥ 6 to finish
  ([MenuEstandarPage.xaml.cs:405](../Pages/MenuEstandarPage.xaml.cs#L405)).
- Speed color thresholds 25 / 40 km/h ([ResultsPage.xaml.cs:351-356](../Pages/ResultsPage.xaml.cs#L351-L356)).
- GPS arrival default radius 40 m + 0.5 m per km/h ([RodajeExterior.xaml.cs:1475-1477](../Pages/RodajeExterior.xaml.cs#L1475-L1477)).
- Shift letter mapping A/B/C/N/W → Spanish shift names
  ([SesionGlobal.cs:22-35](../Services/SesionGlobal.cs#L22-L35)).
- Japan checklist: an item is mandatory unless it is a "Control" item without
  "Aspecto" in its text ([ControlJapon.xaml.cs:1031-1045](../Pages/ControlJapon.xaml.cs#L1031-L1045)).

## 7. Mode dispatch (important for the MAC work)

The "mode" is a **plain string** stored in `SesionGlobal.ModoSeleccionado` and
passed through page constructors. Dispatch is by `Contains(...)` matching, and the
same test is **copy-pasted in four places** ("¿es modo 13 columnas?" — i.e. does
this mode use the CDPV 14-column parser or the DPV 12-column parser):

| Location | Test |
|---|---|
| [SelectionPage.xaml.cs:382-386](../Pages/SelectionPage.xaml.cs#L382-L386) | CORE_DPV ∨ C-DPV ∨ C_DPV ∨ Formacion ∨ SCA |
| [EstandarPage.xaml.cs:296-300](../Pages/EstandarPage.xaml.cs#L296-L300) | same 5 variants |
| [RodajeExterior.xaml.cs:298-302](../Pages/RodajeExterior.xaml.cs#L298-L302) | same 5 variants |
| [MenuEstandarPage.xaml.cs:273-274](../Pages/MenuEstandarPage.xaml.cs#L273-L274) | ⚠ only CORE_DPV ∨ Formacion (inconsistent — latent bug for "SCA"-named dynamic modes) |

Excel path resolution per mode: [SelectionPage.DescargarExcelEstandarAsync](../Pages/SelectionPage.xaml.cs#L372-L423)
→ `05_CDPV_Formacion/CDPV.xlsx`, `06_DPV/DPV.xlsx`, or `08_Otros/{modo}.xlsx`.

## 8. Configuration

There is **no configuration file**. All configuration is hardcoded constants:

- **Azure AD credentials** — ClientId, TenantId, **ClientSecret in plain text**,
  SiteId: [SharePointService.cs:13-16](../Services/SharePointService.cs#L13-L16).
  ⚠ This secret ships inside every APK. (Security debt, see risk register.)
- **SharePoint folder paths** — the `02_Datos_App_SCA/…` tree, hardcoded as
  private fields/literals in 9 files (see `03_VIGO_COUPLING.md` §3).
- **Results list id** — `string listId = "";` (**empty**) in both
  [ResultsPage.xaml.cs:764](../Pages/ResultsPage.xaml.cs#L764) and
  [ResultsJapon.xaml.cs:539](../Pages/ResultsJapon.xaml.cs#L539). As written,
  every result upload fails (Graph URL `…/lists//items`) and the app shows a
  "cloud warning" but continues. Either production APKs carry a real id, or
  result upload is currently broken. **Open question for the owner.**
- **Feature flag by data**: the RRU button appears only if
  `07_RRU/RRU.xlsx` is downloadable ([AuditModePage.xaml.cs:301-317](../Pages/AuditModePage.xaml.cs#L301-L317));
  extra audit modes appear per file in `08_Otros`
  ([AuditModePage.xaml.cs:319-401](../Pages/AuditModePage.xaml.cs#L319-L401)).
  This is the app's existing pattern for "configuration lives in SharePoint".

## 9. Localization

[LocalizationService](../Services/LocalizationService.cs) — a static in-code
dictionary of ~200 keys × 4 languages (ES/EN/FR/DE) for **UI chrome**; language
persisted in Preferences; Spanish is the fallback. **Audit content** localization
is column-based in the Excel (`Estandar_EN`, `AudioFormacion_EN`, … — read by
[ExcelService.TextoLocalizado](../Services/ExcelService.cs#L38), added in v1.3.0).
Voice recognition (Vosk `model_es`) and voice commands are **Spanish-only**, and
one TTS phrase is hardcoded Spanish ("Espero para continuar.",
[RodajeExterior.xaml.cs:688](../Pages/RodajeExterior.xaml.cs#L688)).

## 10. Known defects / oddities (verified)

- Root [MainPage.xaml](../MainPage.xaml) is dead code (AppShell uses `Pages/MainPage`).
- `Navigation stack surgery` by type-name strings (`"SelectionPage"`, `"AuditModePage"`).
- [ControlFaseJapon.RutaImagenCompleta](../Models/ControlFaseJapon.cs) contains a
  hardcoded path to the **original developer's desktop** (`C:\Users\ta32124\...`) for Windows.
- RRUPage labels contain `??` where emojis were lost to a past encoding corruption
  (CHANGELOG 1.2.0).
- `GC.Collect()` called in the GPS loop every 2.5 s ([RodajeExterior.xaml.cs:1489](../Pages/RodajeExterior.xaml.cs#L1489)).
- Startup crash-loop on Windows fixed 2026-07-15 (alert shown before XamlRoot
  existed) — see [App.xaml.cs](../App.xaml.cs) `shell.Loaded` handler.
- The one interface that exists, `IAudioCaptureService`, is instantiated with
  `#if ANDROID` rather than DI — the pattern to follow/improve for plant abstraction.
