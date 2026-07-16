# SCA Application — Data & Excel Flow

> Verified against [ExcelService.cs](../Services/ExcelService.cs),
> [SharePointService.cs](../Services/SharePointService.cs), the pages that call
> them, and the real workbook `_ExcelSharePoint/CDPV.xlsx` (sheet + header dump,
> 2026-07-16).

## 1. SharePoint = the entire backend

Site: `https://shiftup.sharepoint.com/sites/APLICACION_SCA` — Documents library.
Access: Microsoft Graph, app-only token (client credentials) minted per call by
[SharePointService.ConseguirTokenSilenciosoAsync](../Services/SharePointService.cs#L18).

### Folder map (as referenced by code)

| SharePoint path | Content | Read by |
|---|---|---|
| `02_Datos_App_SCA/01_Usuarios/Usuarios.xlsx` | user accounts | [Pages/MainPage.xaml.cs:145](../Pages/MainPage.xaml.cs#L145) |
| `02_Datos_App_SCA/02_Configuraciones/Vehiculos.xlsx` | models & motors | [Pages/MainPage.xaml.cs:146](../Pages/MainPage.xaml.cs#L146) |
| `02_Datos_App_SCA/03_Documentos_pdf/*.pdf` | manuals (listed + opened on demand) | AuditMode/MenuEstandar/Estandar/Rodaje/ControlJapon/RRU pages |
| `02_Datos_App_SCA/04_Control_Japon/AudioJapon.xlsx` | Japan audit steps | [ControlJapon.xaml.cs:53](../Pages/ControlJapon.xaml.cs#L53) |
| `02_Datos_App_SCA/04_Control_Japon/CheckListJapon.xlsx` | Japan checklist | [ControlJapon.xaml.cs:54](../Pages/ControlJapon.xaml.cs#L54) |
| `02_Datos_App_SCA/04_Control_Japon/Imagenes/*` | alert images | ControlJapon |
| `02_Datos_App_SCA/05_CDPV_Formacion/CDPV.xlsx` | **C-DPV + Formación audit content** | [SelectionPage.xaml.cs:389](../Pages/SelectionPage.xaml.cs#L389) |
| `02_Datos_App_SCA/05_CDPV_Formacion/CDPV_traducido.xlsx` | translated copy awaiting review (v1.4.0) | not read by app |
| `02_Datos_App_SCA/06_DPV/DPV.xlsx` | DPV audit content (12-col schema) | [SelectionPage.xaml.cs:391](../Pages/SelectionPage.xaml.cs#L391) |
| `02_Datos_App_SCA/07_RRU/RRU.xlsx` (+ `Imagenes/`) | RRU stops; existence = feature flag | SelectionPage / AuditModePage / RRUPage |
| `02_Datos_App_SCA/08_Otros/*.xlsx` | one dynamic audit mode per file (12-col schema) | [AuditModePage.xaml.cs:319](../Pages/AuditModePage.xaml.cs#L319) |
| `02_Datos_App_SCA/09_Capturas/` | map screenshots **uploaded** by ResultsPage | [ResultsPage.xaml.cs:570](../Pages/ResultsPage.xaml.cs#L570) |
| `02_Datos_App_SCA/10_Notificaciones/Notificaciones.xlsx` (+ `Imagenes/`) | login notifications | [LoginPage.xaml.cs:171](../Pages/LoginPage.xaml.cs#L171) |
| SharePoint **List** (id **empty in code!**) | audit results | ResultsPage / ResultsJapon |

Downloads are cached: PDFs and images to `FileSystem.CacheDirectory`; Excels are
parsed in-memory (`MemoryStream`) and **re-downloaded on every audit start**.

## 2. Excel schemas (column-by-column)

All parsers read **`Worksheet(1)`** (first sheet only) and stop at the first empty
key cell. Except where noted, columns are addressed **by position**, so column
order is a hard contract.

### 2.1 Usuarios.xlsx — [LeerExcelUsuarios](../Services/ExcelService.cs#L196)
| Col | Field | Notes |
|---|---|---|
| 1 | CV | login user (case-insensitive) |
| 2 | PSA | password (plain text, exact match) |
| 3 | NOMBRE | |
| 4 | APELLIDOS | |
| 5 | ROL | `Admin` triggers DB-summary popup at login |
| 6 | TURNO | letter A/B/C/N/W → Spanish shift name |

### 2.2 Vehiculos.xlsx — [LeerExcelVehiculos](../Services/ExcelService.cs#L224)
| Col | Field | Notes |
|---|---|---|
| 1 | Modelo | e.g. K9VP. UI shows **distinct** values as buttons |
| 2 | Motor | e.g. "Térmico…", "Híbrido…". Distinct values shown as buttons |

⚠ Rows are read as pairs but used as two independent distinct lists — there is
no model↔motor compatibility matrix. The **motor name text is business logic**:
step filtering matches Spanish substrings `termic`/`hibrid`/`electr` against it.
**The CDPV/DPV workbooks have no model column** — today the selected model is
only a report label for C-DPV/DPV audits; only Japan mode filters by model.

### 2.3 CDPV.xlsx (sheet `Audio`, 418 data rows) — [LeerExcelAuditoria](../Services/ExcelService.cs#L48)
Used by modes: CORE_DPV (C-DPV), SCA_Formacion, and anything whose name contains "SCA".

| Col | Header | Mandatory | Used for |
|---|---|---|---|
| 1 (A) | `Estandar` | ✔ (loop key) | phase grouping key (Spanish name = stable key) |
| 2 (B) | `Fase` | – | **ignored by parser** |
| 3 (C) | `AudioFormacion` | for Formación | TTS text in formation mode |
| 4 (D) | `TiempoFormacion` | for Formación | seconds, or `MANUAL` (wait for voice command) |
| 5 (E) | `AudioAuditoria` | ✔ for audits | TTS text in audit mode (empty ⇒ step skipped) |
| 6 (F) | `TiempoAuditoria` | ✔ | seconds or `MANUAL` |
| 7-9 (G-I) | `MotorTermico/Hibrido/Electrico` | ✔ | 0/1 flags; all-0 = step applies to every motor |
| 10-12 (J-L) | `Latitud/Longitud/Radio` | RODAJE only | GPS waypoint + arrival radius (m). Coordinates in the current file are the **Vigo track** (42.20°N, −8.7°W) |
| 13 (M) | `Exterior` | ✔ | 0 = both tracks, 1 = interior, 2 = exterior |
| 14 (N) | `TipoPlantilla` | ✔ | `ESTATICO` → EstandarPage; contains `RODAJE` → RodajeExterior |
| 15-23 (O-W) | `Estandar_EN/FR/DE`, `AudioFormacion_EN/FR/DE`, `AudioAuditoria_EN/FR/DE` | – | translations, **located by header name** (fallback: Spanish) |

Current phases in the file: Toma del Vehículo (16), Estático (128),
Dinámico (164), Exterior (87), Restitución del Vehículo (23).

### 2.4 DPV.xlsx & 08_Otros/*.xlsx (12-col schema) — [LeerExcelDPV](../Services/ExcelService.cs#L124)
Same idea, shifted positions, **no formation columns, no translation support**:
1 Estandar · 3 AudioAuditoria · 4 Tiempo · 5-7 motor flags · 8-10 Lat/Lon/Radio ·
11 Exterior · 12 TipoPlantilla. (Column 2 ignored.)

### 2.5 AudioJapon.xlsx — [LeerExcelAudioJapon](../Services/ExcelService.cs#L248)
1 `Tipo` (`General` = playable step, `Alerta` = visual alert) · 2 `Controles` ·
3 `Audio` · 4 `Tiempo` (secs or MANUAL) · 5 `Imagen` (filename in `Imagenes/`) ·
6 `ModeloVehiculo` (comma-separated model whitelist; empty = all models).

### 2.6 CheckListJapon.xlsx — [LeerExcelCheckListJapon](../Services/ExcelService.cs#L276)
1 `FaseCheck` (`TT` | `Control` | `Visto…` | `Especifico`) · 2 `Check` (text) ·
3 `ModeloCheck` (comma-separated models; used only for `Especifico` rows).

### 2.7 RRU.xlsx — [LeerExcelRRU](../Services/ExcelService.cs#L301)
1 `Punto` · 2 `Mensaje` · 3 `Imagen`.

### 2.8 Notificaciones.xlsx — inline parse in [LoginPage.xaml.cs:176-198](../Pages/LoginPage.xaml.cs#L176-L198)
1 `Mensaje` · 2 `Imagen` (filename in `10_Notificaciones/Imagenes/`).

## 3. Results (writes)

### C-DPV / DPV / Formación / RRU — [ResultsPage.GenerarJsonParaListaSharePoint](../Pages/ResultsPage.xaml.cs#L674)
SharePoint list fields: `Title` (=chassis), `Fecha`, `Auditor`, `Turno`, `Modo`,
`Modelo`, `Motor`, `Velocidad_Max`, `Hora_Inicio`, `Hora_Final`, `Tiempo_Total`,
`Resumen_Tiempos` (per-phase minutes), `Rodaje_Exterior` (SI/NO),
`Ruta_Exterior` (Google-Maps directions URL built from GPS trace),
`Ruta_Interior` (webUrl of the PNG map snapshot uploaded to `09_Capturas`),
`Puntos_RRU` (Google-Maps URL of validated stops).

### Japan — [ResultsJapon.GenerarJsonParaListaSharePoint](../Pages/ResultsJapon.xaml.cs#L446)
`Title`, `Fecha`, `Auditor`, `Turno`, `Modelo`, `Motor`, times, plus formatted
checklist blobs: `Resultados_TT`, `Aspecto`, `Nicho`, `Bajo_Caja`, `Pistas`
(items bucketed by keywords in the check text), `Resultados_Visto`,
`Resultados_Especificos`.

⚠ Both callers pass `listId = ""` — see risk register. No result is stored
locally after submission; if the POST fails the data is lost once the user leaves
the page (a warning alert is the only signal).

## 4. End-to-end data flow diagram

```
                    ┌────────────────────────── SharePoint (Graph API) ─────────────────────────┐
                    │ Usuarios.xlsx  Vehiculos.xlsx  CDPV/DPV/Otros.xlsx  Japon xlsx  RRU.xlsx  │
                    │ Notificaciones.xlsx   PDFs   Imagenes/   09_Capturas (PNG)   Results List │
                    └───────┬───────────┬──────────────┬──────────────────────────▲───────▲─────┘
   app start                │           │              │ on audit start           │       │
  MainPage.EMPEZAR ─────────┴───────────┘              │                          │       │
        │            ExcelService parsers              │                          │       │
        ▼                                              ▼                          │       │
  SesionGlobal.ListaUsuarios / ListaVehiculos   SesionGlobal.Estandares(±DPV)     │       │
        │                                              │                          │       │
     LoginPage ──► AuditModePage ──► SelectionPage ────┤ filter by motor/track/   │       │
                                                       │ template per step        │       │
                                                       ▼                          │       │
                                     MenuEstandar → Estandar / RodajeExterior     │       │
                                                       │  TTS + Vosk + GPS        │       │
                                                       ▼                          │       │
                                                  ResultsPage ── map snapshot ────┘       │
                                                       └───────── JSON item ──────────────┘
   crash safety: SesionGlobal ⇄ auditoria_encurso_backup.json (AutoGuardadoService)
```
