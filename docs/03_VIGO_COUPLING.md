# Phase 3 — Complete inventory of Vigo / Spain / model coupling

> Every location where the application assumes Vigo, Spain, Spanish, or the
> current vehicle catalogue. This is the checklist the MAC refactor must clear.
> Grouped by *kind* of coupling, because each kind needs a different fix.

## 1. Plant identity in the UI (cosmetic)

| Where | What |
|---|---|
| [Pages/MainPage.xaml:165](../Pages/MainPage.xaml#L165) | footer `"Stellantis Vigo - QCP/AVCF"` |
| [Pages/LoginPage.xaml:173](../Pages/LoginPage.xaml#L173) | footer `"Stellantis Vigo - QCP/AVCF"` |
| [Pages/AuditModePage.xaml:321](../Pages/AuditModePage.xaml#L321) | footer `"Stellantis Vigo - Centro de Control"` |

## 2. VIN / chassis format (functional — blocks MAC)

Vigo uses an internal 8-character chassis (`^[A-Z]{2}[0-9]{6}$`). A North-American
plant will almost certainly use a different format (17-char ISO VIN or its own
internal number — **open question**).

| Where | What |
|---|---|
| [Pages/ResultsPage.xaml.cs:41](../Pages/ResultsPage.xaml.cs#L41), [185](../Pages/ResultsPage.xaml.cs#L185), [225](../Pages/ResultsPage.xaml.cs#L225) | MaxLength 8 + regex validation + block on save |
| [Pages/ResultsJapon.xaml.cs:33](../Pages/ResultsJapon.xaml.cs#L33), [179](../Pages/ResultsJapon.xaml.cs#L179), [215](../Pages/ResultsJapon.xaml.cs#L215), [262](../Pages/ResultsJapon.xaml.cs#L262), [379](../Pages/ResultsJapon.xaml.cs#L379) | same regex ×4 |
| [Pages/MenuEstandarPage.xaml.cs:405](../Pages/MenuEstandarPage.xaml.cs#L405) | "chassis present" check = length ≥ 6 |

## 3. SharePoint paths (functional — where the plant's data lives)

The `02_Datos_App_SCA/…` tree is hardcoded in:

| File | Constants |
|---|---|
| [Pages/MainPage.xaml.cs:145-146](../Pages/MainPage.xaml.cs#L145-L146) | Usuarios, Vehiculos |
| [Pages/LoginPage.xaml.cs:171](../Pages/LoginPage.xaml.cs#L171) | Notificaciones |
| [Pages/AuditModePage.xaml.cs:19-21](../Pages/AuditModePage.xaml.cs#L19-L21), [308](../Pages/AuditModePage.xaml.cs#L308) | PDFs, 08_Otros, notification images, RRU probe |
| [Pages/SelectionPage.xaml.cs:389-393](../Pages/SelectionPage.xaml.cs#L389-L393), [433](../Pages/SelectionPage.xaml.cs#L433) | CDPV, DPV, Otros, RRU |
| [Pages/MenuEstandarPage.xaml.cs:32](../Pages/MenuEstandarPage.xaml.cs#L32) | PDFs |
| [Pages/EstandarPage.xaml.cs:48](../Pages/EstandarPage.xaml.cs#L48) | PDFs |
| [Pages/RodajeExterior.xaml.cs:64](../Pages/RodajeExterior.xaml.cs#L64) | PDFs |
| [Pages/ControlJapon.xaml.cs:53-56](../Pages/ControlJapon.xaml.cs#L53-L56) | AudioJapon, CheckListJapon, Imagenes, PDFs |
| [Pages/RRUPage.xaml.cs:32-34](../Pages/RRUPage.xaml.cs#L32-L34) | PDFs, RRU images |
| [Pages/ResultsPage.xaml.cs:570](../Pages/ResultsPage.xaml.cs#L570) | 09_Capturas upload |

Plus the tenant/site itself: [SharePointService.cs:13-16](../Services/SharePointService.cs#L13-L16)
(single ClientId/TenantId/Secret/SiteId — is MAC on the same site? **open question**).

## 4. Geography (functional)

| Where | What |
|---|---|
| `_ExcelSharePoint/CDPV.xlsx` cols J-L | all RODAJE waypoints are Vigo-track coordinates (42.20°N, −8.7°W) — **data, not code**; MAC needs its own coordinates in its own workbook |
| [Pages/RRUPage.xaml.cs:212](../Pages/RRUPage.xaml.cs#L212) | Windows GPS simulation hardcoded to Vigo `(42.2037, -8.7428)` |

## 5. Language / speech (functional — biggest hidden cost for MAC)

| Where | What |
|---|---|
| [Resources/Raw/model_es.zip](../Aplicacion_SCA.csproj) + [EstandarPage.xaml.cs:76-107](../Pages/EstandarPage.xaml.cs#L76-L107) (same in RodajeExterior, ControlJapon) | Vosk **Spanish** acoustic model; extraction path `model_es` |
| [EstandarPage.xaml.cs:693-711](../Pages/EstandarPage.xaml.cs#L693-L711) (+ RodajeExterior:783-798, ControlJapon:813-832) | voice commands matched on Spanish words *sigue / siguiente / continuar / atrás / anterior / pausa / parar / repetir* |
| [RodajeExterior.xaml.cs:688](../Pages/RodajeExterior.xaml.cs#L688) | hardcoded TTS phrase `"Espero para continuar."` |
| [ExcelService.SufijoIdioma](../Services/ExcelService.cs#L13) | content languages limited to ES/EN/FR/DE column suffixes |
| Misc hardcoded Spanish UI strings that bypass LocalizationService: e.g. section titles in [MenuEstandarPage.xaml.cs:51-60](../Pages/MenuEstandarPage.xaml.cs#L51-L60) ("HOJA DE RUTA…"), `"AUDITORÍA C-DPV"` in [SelectionPage.xaml.cs:60](../Pages/SelectionPage.xaml.cs#L60), confirm dialogs in [EstandarPage.xaml.cs:948](../Pages/EstandarPage.xaml.cs#L948), [RodajeExterior.xaml.cs:1052-1079](../Pages/RodajeExterior.xaml.cs#L1052-L1079), step labels `"Paso X de Y"`, `"VIN: PENDIENTE"`, etc. |
| TTS voice | `TextToSpeech.SpeakAsync(text, null, …)` uses the device default locale — reads Spanish text with whatever voice the tablet has (works at Vigo because tablets are es-ES) |

## 6. Motor-type matching (functional — breaks with English motor names)

Steps are matched to the selected motor by **Spanish substrings**:
`motorElegido.Contains("termic") / ("hibrid") / ("electr")` —
[EstandarPage.xaml.cs:327-329](../Pages/EstandarPage.xaml.cs#L327-L329),
[RodajeExterior.xaml.cs:327-330](../Pages/RodajeExterior.xaml.cs#L327-L330).
If MAC's `Vehiculos.xlsx` lists motors as "Gas", "Hybrid", "Electric", "ICE", etc.,
**no motor-specific step will ever match** (only common steps would play).

## 7. Vehicle models

- Model names (K9VP, …) exist **only in data**: `Vehiculos.xlsx` (not in the local
  repo — lives in SharePoint `02_Configuraciones`), `AudioJapon.xlsx` col 6, and
  `CheckListJapon.xlsx` col 3. **No model name is hardcoded in C# or XAML** (verified
  by grep). Adding WD/WL is therefore a *data* exercise plus plant scoping.
- Phase names are business keys: `"EXTERIOR"` substring drives track gating
  ([MenuEstandarPage.xaml.cs:294-307](../Pages/MenuEstandarPage.xaml.cs#L294-L307))
  and report routing ([ResultsPage.xaml.cs:688-692](../Pages/ResultsPage.xaml.cs#L688-L692)).
  A MAC workbook written in English ("EXTERIOR ROAD TEST"?) must keep that
  substring — or the rule must become configurable.

## 8. Shift system & users

[Usuario.TransformarTurno](../Services/SesionGlobal.cs#L22-L35): letters A/B/C/N/W
→ Spanish names ("Turno Central", "Turno Fin de Semana"). MAC shift codes unknown
(**open question**). One shared `Usuarios.xlsx` for all plants or one per plant —
**open question**.

## 9. Results schema

SharePoint list columns (`Turno`, `Rodaje_Exterior` = "SI"/"NO", etc.) are
Spanish-named and Vigo-shaped; list id is empty in code
([ResultsPage.xaml.cs:764](../Pages/ResultsPage.xaml.cs#L764)). Whether MAC posts
to the same list, a new list, or a different site is an **open question**.

## 10. Mode-name dispatch

Not plant coupling per se, but the same stringly-typed pattern the plant fix must
not multiply: the "13-column mode" test is copy-pasted 4× with one inconsistent
copy (see `01_ARCHITECTURE.md` §7). Any plant conditionals must NOT be added the
same way (`if plant == "MAC"` sprinkled in pages) — see `04_MAC_PLAN.md`.
