# Phases 4–9 — Multi-plant architecture proposal & migration plan (Vigo + MAC)

> **STATUS 2026-07-16 (v1.6.0):** milestones 0–4 are IMPLEMENTED (git history
> `8b9ada9..28a96f6`). Plant selection is live on C-DPV; MAC skeleton data is on
> SharePoint under `02_Datos_App_MAC/` (SAMPLE content — replace with real data);
> English Vosk model shipped. Still pending: per-plant results list ids
> (milestone 5), real MAC CDPV content + GPS coordinates, per-plant VIN-format
> hint messages. Owner answers to §7 are recorded in CHANGELOG 1.6.0.

> Goal: add plant **MAC** (models **WD**, **WL**) behind a plant-selection step in
> the *Audit Mode → C-DPV* flow, **without duplicating Vigo code and without
> changing Vigo behavior**. Designed so Toluca / Windsor / Brampton / Melfi /
> Cassino are later "one registry entry + one SharePoint folder" each.
> **No code here has been written — this is the proposal awaiting approval.**

## 1. Design principles

1. **Vigo is the default plant everywhere.** Until a user explicitly picks MAC,
   every code path resolves to exactly today's values (same paths, same regex,
   same strings). This makes step 1 a pure, testable refactor.
2. **A plant is data, not code.** All plant differences live in one
   `PlantDefinition` record; pages never ask "which plant?" — they ask the
   definition for the value they need (path, regex, culture, coordinates…).
   No `if (plant == "MAC")` scattered in pages — that's how the mode-string
   mess happened.
3. **Per-plant SharePoint folder, identical layout.** Vigo keeps
   `02_Datos_App_SCA/` untouched (zero migration risk). MAC gets a sibling tree
   with the *same relative structure*, so one path-resolver serves all plants.
4. **Keep the existing UI pattern** (code-behind pages). No MVVM rewrite, no DI
   container introduction in this effort — smallest possible diff per milestone.

## 2. Proposed abstraction (Phase 4)

### 2.1 New files (additive — nothing existing is touched yet)

```
Services/Plants/
├── PlantDefinition.cs      // the record below
├── PlantRegistry.cs        // static list: { Vigo, Mac }; lookup by code
└── PlantContext.cs         // Current (defaults to Vigo), persisted with the session
Pages/PlantSelectionPage.*  // "Select Plant" screen (Vigo / MAC buttons)
```

### 2.2 `PlantDefinition` (proposed shape — to be confirmed by your answers)

```csharp
public sealed class PlantDefinition
{
    public string Code { get; init; }              // "VIGO", "MAC"
    public string DisplayName { get; init; }       // "Stellantis Vigo", "Stellantis MAC"
    public string FooterText { get; init; }        // replaces "Stellantis Vigo - QCP/AVCF"
    public string SharePointRoot { get; init; }    // "02_Datos_App_SCA"  |  "02_Datos_App_MAC" (TBD)
    public string ResultsListId { get; init; }     // per-plant SharePoint list (TBD)
    public Regex ChassisRegex { get; init; }       // Vigo: ^[A-Z]{2}[0-9]{6}$ ; MAC: TBD (17-char VIN?)
    public int ChassisMaxLength { get; init; }     // 8 | 17
    public string DefaultContentLanguage { get; init; }   // "es" | "en"
    public string VoskModelAsset { get; init; }    // "model_es.zip" | "model_en.zip" (TBD)
    public IReadOnlyDictionary<MotorKind,string[]> MotorKeywords { get; init; }
                                                   // VIGO: termic/hibrid/electr
                                                   // MAC:  gas|ice / hybrid / electric (TBD)
    public IReadOnlyDictionary<string,string> ShiftNames { get; init; }  // A/B/C/N/W → label
    public Location SimulatedGps { get; init; }    // Windows dev stub (Vigo today)
}
```

Everything above maps 1:1 to a row in `docs/03_VIGO_COUPLING.md`. If an item
turns out identical across plants (e.g. shift codes), it simply has the same
value in both definitions — the abstraction costs nothing.

`PlantContext.Current` is set by PlantSelectionPage, saved into `SesionGlobal` +
the AutoGuardado backup (so crash-resume restores the right plant), and reset to
Vigo on logout.

### 2.3 UI flow change (exactly what you asked for)

```
AuditModePage ── C-DPV tap ──► PlantSelectionPage ──► Vigo ─► SelectionPage("CORE_DPV")   (unchanged path)
                                       └──────────► MAC  ─► SelectionPage("CORE_DPV") with PlantContext = MAC
Other buttons (Japón, DPV, Formación, RRU, dynamic) ─► unchanged, PlantContext stays Vigo
```

`SelectionPage`, `MenuEstandarPage`, `EstandarPage`, `RodajeExterior`,
`ResultsPage` need **no plant knowledge** — they already receive their data via
`SesionGlobal` and will resolve paths/validation through `PlantContext.Current`.
The model/motor lists automatically become MAC's WD/WL because MainPage…
— ⚠ correction: `Vehiculos.xlsx` is downloaded **once at app start** (before
login, before plant choice). For MAC the vehicle list must be (re)loaded after
plant selection. Plan: PlantSelectionPage triggers a re-download of
`{SharePointRoot}/02_Configuraciones/Vehiculos.xlsx` into `SesionGlobal.ListaVehiculos`
when the chosen plant differs from the one the list was loaded for. Vigo path:
list already loaded at startup → no change, no extra network call.

### 2.4 Why this design (vs. alternatives considered)

- **Vs. duplicating pages per plant** (e.g. `EstandarPageMac`): rejected — the
  audit engine is ~3,400 lines across 4 pages; a fork doubles every future bug fix.
- **Vs. `if (plant == …)` branches in pages**: rejected — that is exactly the
  existing mode-string anti-pattern that already produced an inconsistent
  4× copy-pasted check ([MenuEstandarPage.xaml.cs:273](../Pages/MenuEstandarPage.xaml.cs#L273)).
- **Vs. a plants config file in SharePoint** (`Plantas.xlsx`): attractive later
  (plants without app releases), but v1 keeps definitions in code — simpler,
  type-safe, testable, and plants change rarely. The registry makes moving to a
  config file trivial afterwards.
- **Model abstraction:** models stay *data* (per-plant `Vehiculos.xlsx`); the code
  keeps treating them as strings. WD/WL therefore require **zero model-specific
  code** — the class of change that must never need code again.

## 3. Migration roadmap (Phase 5) — smallest safe steps

Each milestone compiles, runs, and leaves Vigo behavior byte-identical.
Regression-check Vigo C-DPV after every step (checklist in §6).

| # | Milestone | Touches | Risk |
|---|---|---|---|
| 0 | **Safety net**: `git init` + initial commit (project has no VCS!); record the Vigo smoke-test checklist; confirm the real `ResultsListId` from the production APK | nothing functional | none |
| 1 | **Introduce PlantDefinition/Registry/Context with only VIGO**, values copied verbatim from today's constants. Replace the hardcoded SharePoint paths, chassis regex/length, footer strings, Windows GPS stub, shift map and motor keywords with reads from `PlantContext.Current` | Services/* (new), ~10 pages (mechanical substitutions) | low (pure refactor) — the milestone that needs the most careful review |
| 2 | **PlantSelectionPage** inserted in the C-DPV flow. Vigo button → identical behavior; MAC button present but marked "no data yet" until milestone 4 | AuditModePage (1 handler), new page | low |
| 3 | **Create MAC SharePoint tree** (`{MAC root}/01_Usuarios? 02_Configuraciones/Vehiculos.xlsx (WD/WL), 05_CDPV_Formacion/CDPV.xlsx skeleton, 03_Documentos_pdf/…`) mirroring Vigo layout; add MAC `PlantDefinition`; implement vehicle-list reload on plant switch | SharePoint (data), PlantRegistry, PlantSelectionPage | low-medium (no Vigo surface) |
| 4 | **MAC functional gaps**, driven by your answers: chassis/VIN rule, motor keyword sets, English content columns, TTS locale, voice-command language (Vosk `model_en` — app size +~40 MB — or button-only validation for MAC v1) | Estandar/Rodaje pages (keyword lookup already centralized in step 1) | medium |
| 5 | **Results**: per-plant list id (and `Planta` column in the payload); fix the empty-listId defect for Vigo at the same time | ResultsPage, ResultsJapon | medium (verify with SharePoint owner) |
| 6 | **Pilot & hardening**: MAC dry-run with real WD/WL data; then decide follow-ups (extend plant choice to other modes, config-file registry, secret removal) | — | — |

Deliberately **out of scope** (recommended separately): moving the client secret
out of the APK, MVVM rewrite, unifying the 4× mode checks (worth doing in
milestone 1 only if trivial), fixing RRU emoji corruption.

## 4. Excel strategy for MAC (Phase 7)

**Recommendation: one workbook per plant in a per-plant folder — do NOT add
sheets to the Vigo workbook.**

Reasons (all verified against the parser):
- Every parser reads `Worksheet(1)` only ([ExcelService.cs:55](../Services/ExcelService.cs#L55) etc.);
  multi-sheet would require parser changes → risk to Vigo.
- Separate files mean separate ownership/permissions per plant team, and a broken
  MAC upload can never take Vigo down.
- The path already varies by mode; making it vary by plant root reuses the same mechanism.

MAC `CDPV.xlsx` skeleton (same 14 columns; header row identical — the translation
columns O-W are optional):
- Content language: **primary text columns in the plant's language** is the
  simplest (Spanish base columns are only "special" as fallback keys). If MAC
  authors in English, two options — (a) put English in the base columns
  (`Estandar`, `AudioAuditoria`, …) and leave O-W empty; or (b) keep the Vigo
  convention (base = Spanish) and fill `*_EN`. **(a)** is recommended for MAC:
  no dependence on Spanish master text. Needs decision (§ Open questions).
- `Estandar` phase names: keep the `EXTERIOR` substring convention for the
  exterior-track phase (or we make the rule configurable in milestone 4).
- `Latitud/Longitud/Radio`: MAC track waypoints (only for RODAJE rows).
- `MotorTermico/Hibrido/Electrico`: same 0/1 semantics; motor **names** in MAC's
  `Vehiculos.xlsx` must match the MAC keyword set we define.
- `Vehiculos.xlsx`: rows for WD and WL with their motor variants.

Long-term: when a third plant arrives, consider a `00_Plantas/Plantas.xlsx`
registry (code, display name, root folder, list id, chassis regex, language) so
new plants need no app release. Not needed for MAC.

## 5. Risk assessment (Phase 8)

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| 1 | **No version control** — refactor without rollback | 🔴 high | milestone 0: `git init` |
| 2 | **Results list id is empty** ([ResultsPage.xaml.cs:764](../Pages/ResultsPage.xaml.cs#L764)) — uploads may be failing silently in this source copy | 🔴 high | confirm real id; add failure surfacing + local result backup |
| 3 | **Client secret hardcoded & shipped in APK** ([SharePointService.cs:15](../Services/SharePointService.cs#L15)) | 🔴 high (security) | out of scope here, but flag to IT: rotate secret, move to a token-broker or at least device-side secure storage |
| 4 | Milestone 1 touches 10 files of string constants — a typo silently points at a wrong folder | 🟠 medium | mechanical review + Vigo smoke test after; paths asserted equal to old literals in a unit test |
| 5 | Positional Excel parsing — any column reorder in a MAC-authored workbook breaks parsing with no error message | 🟠 medium | provide the MAC team a locked template; longer term: header-map parsing like the translation columns already use |
| 6 | Motor keyword matching fails silently (steps just vanish) with English motor names | 🟠 medium | keyword sets per plant + a startup sanity check ("N steps matched 0 motors") |
| 7 | Crash-resume (AutoGuardado) restoring a MAC session as Vigo | 🟠 medium | persist plant code in the backup (milestone 1 includes it in `EstadoAuditoriaBackup`) |
| 8 | Copy-pasted sequencer logic in 3 pages — a MAC fix applied to one page only | 🟡 low-med | keep plant values in PlantDefinition so pages need no edits; document the duplication |
| 9 | Global static state (`SesionGlobal`) — stale plant/vehicle data across sessions | 🟡 low-med | plant reset in `CerrarSesion()`; vehicle list tagged with the plant it was loaded for |
| 10 | Windows head is dev-only; maps/GPS stubbed — MAC GPS flows testable only on Android | 🟡 low | plan Android device testing for milestone 4+ |
| 11 | Vosk English model adds ~40 MB to the APK if MAC needs voice commands | 🟡 low | decide in Phase 6; v1 can use button validation at MAC |

## 6. Regression testing strategy (Phase 9)

Manual smoke checklist (no test infra exists; keep it cheap and repeatable):

**Vigo — after every milestone**
1. Start → language ES → EMPEZAR (users+vehicles load) → login real user & admin/admin.
2. AuditMode: notifications popup, PDFs list, RRU button visibility, dynamic buttons.
3. C-DPV → model/motor selection → COMENZAR → phase list correct (Exterior disabled
   when switch off) → run 2 steps of Estático with TTS → voice "siguiente" advances →
   pause/resume → complete phase → phase marked green.
4. Rodaje exterior on (Android): GPS wait step, route drawn, speed shown.
5. Finalizar → VIN validation (bad format rejected; `AB123456` accepted) → send →
   verify SharePoint list row (once listId fixed) + map PNG in 09_Capturas.
6. Kill app mid-phase → relaunch → resume offer restores phase/step.
7. Japón + RRU + DPV: enter, one step/stop each, results page opens.
8. Language EN: UI translated, content falls back correctly.

**MAC — from milestone 3**
- Plant screen appears only on C-DPV; Vigo path identical to before.
- MAC → WD/WL and MAC motors listed; MAC CDPV phases load; chassis rule = MAC rule;
  results row carries plant identity; crash-resume returns to MAC.

Suggested (optional, additive): a tiny xUnit project for `ExcelService` +
`PlantRegistry` with fixture workbooks — the only logic that's trivially unit-testable
without UI.

## 7. Open questions (Phase 6 — need your answers before coding)

**Blocking milestone 2-3**
1. **The documents you attached did not reach me** — no attachments arrived in the
   conversation and none are in the project folder. Please re-attach or drop them
   into the workspace (e.g. a `_Docs/` folder) — I'll re-validate this analysis against them.
2. Same SharePoint **site** for MAC (`shiftup.sharepoint.com/sites/APLICACION_SCA`)
   or a different site/tenant? Who creates the MAC folder tree?
3. MAC folder root name preference (e.g. `02_Datos_App_MAC`)?
4. Plant selection scope: **only C-DPV** for now (my plan) — confirm. Should the
   other modes (Japón/DPV/Formación/RRU) stay Vigo-only forever or become plant-aware later?
5. Chassis/VIN at MAC: 17-char ISO VIN, or an internal short code? Exact format/validation?

**Blocking milestone 4**
6. Are WD/WL audited with the **same 5-phase C-DPV structure** (vehicle intake,
   static, dynamic, exterior, restitution) and same step semantics as K9VP? Any
   scoring differences?
7. Motor variants for WD/WL, and the exact motor names MAC will write in
   `Vehiculos.xlsx` (English?) — needed for the keyword mapping.
8. Working language at MAC: UI in English (already supported), but (a) audit
   content authored in English in the base columns? (b) TTS in English?
   (c) **voice commands** — English Vosk model (+~40 MB APK) or button-only validation for v1?
9. Does MAC have GPS coordinates / an exterior track for RODAJE steps, or is the
   MAC audit fully static initially?
10. Users: shared `Usuarios.xlsx` or per-plant? MAC shift codes (A/B/C/N/W?)?

**Blocking milestone 5**
11. Results: same SharePoint list with a new `Planta` column, or a separate list
    per plant? And: the **list id is empty in this source** — what is the
    production id / is result upload currently working at Vigo?

**Nice to know**
12. Confirm the Vigo model list (Vehiculos.xlsx isn't in the repo — I could not
    verify "K9VP and others").
13. Footer branding for MAC (exact wording)?
14. Different defect categories / reports / equipment lists at MAC that today's
    result schema doesn't cover?
