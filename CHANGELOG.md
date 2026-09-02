# CHANGELOG — Aplicacion_SCA

Registro manual de cambios (no se usa Git). Cada cambio incrementa la versión
y se describe aquí. La versión de la app se define en `Aplicacion_SCA.csproj`
(`ApplicationDisplayVersion` / `ApplicationVersion`).

Formato: [versión] - fecha — autor
Tipos: AÑADIDO / CAMBIADO / CORREGIDO / ELIMINADO

---

## [1.8.3] - 2026-09-02 — Claude (asistencia)

### CORREGIDO — El TTS leía en voz alta los ">>" del Excel como si fueran palabras
- El contenido del Excel usa ">>" como viñeta/separador de puntos dentro de
  una misma celda (p.ej. ">> Comprobar presión >> Comprobar luces"), pero el
  motor de texto a voz lo leía literalmente. Nuevo
  `SpeechLocaleHelper.LimpiarParaVoz(texto)`, que quita cualquier secuencia
  de ">" antes de enviar el texto a `SpeakAsync` — el texto en pantalla no
  se toca, solo lo que se pronuncia. Aplicado en los 4 puntos reales de habla
  de la app: `EstandarPage` (Fase y "Más Detalle"), `RodajeExterior` y
  `ControlJapon`, ya que todos leen el mismo tipo de contenido del Excel.

### AÑADIDO — Más variantes de voz para "Más Detalle"
- `PlantRegistry`: añadidas "detalles"/"mas detalle"/"masdetalle" (Vigo) y
  "details"/"more detail"/"moredetail" (MACK) al comando `mas_detalle`, para
  cubrir cómo Vosk puede transcribir la frase completa o pegar las palabras
  sin espacio.

---

## [1.8.2] - 2026-09-02 — Claude (asistencia)

### CAMBIADO — EstandarPage: secuencia manos libres por voz, pensada para auditar mientras se conduce
- **Antes**: cada paso hablaba Fase + AudioAuditoria automáticamente y, tras una
  pausa fija, avanzaba solo al siguiente. El comando de voz solo se escuchaba
  en los pasos marcados `MANUAL` en el Excel.
- **Ahora**: por defecto solo se habla la `Fase` (breve). El resto — AudioAuditoria
  (o AudioFormacion en modo Formación) — solo se dice cuando el auditor lo pide,
  por voz ("detalle"/"detail" en EN) o tocando "Más Detalle". El paso **nunca
  avanza solo por temporizador**: solo un comando de voz reconocido ("siguiente"/
  "next") o el botón físico mueven la auditoría. Decisión explícita del
  propietario del producto: pensado para poder auditar con las manos en el
  volante, sin mirar ni tocar la pantalla salvo que el reconocimiento de voz
  falle.
- Nuevo comando de voz `mas_detalle` añadido a `PlantRegistry` para ambas
  plantas (`detalle/explica/informacion` en Vigo, `detail/explain/information`
  en MACK) — reutiliza el motor Vosk y el flujo de escucha que ya existían
  para los pasos `MANUAL`, ahora activo en todos los pasos.
- El botón físico "Más Detalle" y el botón verde "Validar y Continuar" (que
  aparece mientras se escucha el micrófono) son el equivalente táctil de los
  comandos de voz — nunca se elimina la vía sin voz, por si el reconocimiento
  falla o el auditor prefiere tocar la pantalla.
- **Bug preexistente corregido de paso**: el panel verde "Validar y Continuar"
  nunca se mostraba mientras se escuchaba porque el código buscaba un control
  llamado `BtnValidarPaso`, que no existe en el XAML (el control real se llama
  `PanelValidacionManual`). Corregido para que el indicador visual de "te estoy
  escuchando" — y su botón físico — aparezcan de verdad.
- Eliminados los campos y el retraso de 3(+3)s que ya no aplican con este
  modelo (`_masDetallePulsado`, `EsperaTrasInstruccionMs`,
  `EsperaExtraMasDetalleMs`) y el panel de texto separado para AudioFormacion
  (`PanelMasDetalle`/`LblMasDetalleContenido`): ahora "Más Detalle" reutiliza
  `LblInstruccionActual`, oculto hasta que se pide.
- **Hallazgo de datos, no de código**: probando contra el Excel real de C-DPV
  Vigo, la columna "Fase" (columna B) no siempre es un título corto — para
  varios pasos contiene un párrafo tan largo como AudioAuditoria. El código
  lee la columna correctamente (columna B, confirmado contra `ExcelService.cs`);
  si se quiere que el anuncio automático sea realmente breve en todos los
  pasos, el contenido de esa columna debería revisarse en el Excel, no es algo
  que el código pueda corregir por sí solo.
- Verificado en tablet físico (SM_X210) que la secuencia habla la Fase, entra
  en escucha y responde a un comando reconocido (observado con un falso
  positivo real de "pausa" por ruido ambiente durante la prueba — riesgo ya
  existente del reconocimiento por subcadena de Vosk, más expuesto ahora que
  todos los pasos escuchan). Pendiente de probar en campo con voz real por el
  auditor — no reproducible por adb.
- Nueva APK `_APK/Aplicacion_SCA_v1.8.2.apk`.

---

## [1.8.1] - 2026-09-02 — Claude (asistencia)

### CORREGIDO — El audio seguía sonando en el idioma del teléfono, no el de la app (persistía tras 1.7.3)
- **Reporte**: en un teléfono con el sistema operativo en alemán, seleccionando
  inglés en la app (probado con planta MACK, C-DPV), el audio seguía sonando
  en alemán. En un teléfono con el sistema operativo en inglés, todo sonaba
  correctamente en el idioma elegido.
- **Causa raíz real**: `SpeechLocaleHelper` (añadido en 1.7.3) resuelve el
  `Locale` de voz llamando a `TextToSpeech.Default.GetLocalesAsync()` la
  primera vez que se necesita hablar, y cachea el resultado para el resto de
  la sesión. Justo al entrar a una pantalla con audio, el motor de TTS del
  dispositivo puede no haber terminado de inicializarse todavía, así que esa
  primera llamada puede devolver una lista sin el idioma pedido. El código
  anterior cacheaba ese fallo como definitivo (`Locale = null` para siempre),
  y con `Locale = null` el motor usa el idioma por defecto del sistema
  operativo — en un teléfono en inglés ese fallback "por accidente" es
  correcto (por eso el bug era invisible ahí); en uno en alemán, no.
- **Arreglo**: `Services/SpeechLocaleHelper.cs` ahora reintenta una vez tras
  400 ms si el primer intento no encuentra el idioma (dando tiempo al motor a
  terminar de inicializarse), solo cachea un resultado **encontrado** (un
  fallo ya no se guarda como definitivo — la siguiente frase hablada lo
  reintentará), y usa un `SemaphoreSlim` para que dos resoluciones casi
  simultáneas (p. ej. "Fase" + texto principal, que ahora se hablan seguidos
  tras el rediseño de 1.8.0) no se pisen entre sí.
- **Diagnóstico añadido**: un log Android (`adb logcat -s SCA_TTS`) que
  registra, cada vez que se resuelve el idioma, qué código se pidió, si se
  encontró, y cuántos locales reportó el motor — para tener datos reales si
  el problema reaparece en vez de tener que adivinar de nuevo a ciegas.
- Pendiente de verificar en el teléfono físico con sistema operativo en
  alemán que reportó el bug (no había ninguno disponible al compilar este
  arreglo). Nueva APK `_APK/Aplicacion_SCA_v1.8.1.apk`.

---

## [1.8.0] - 2026-08-21 — Claude (asistencia)

### CAMBIADO — Rediseño de EstandarPage (pantalla de instrucción única)
- Antes se mostraba una lista scrollable con todos los pasos del estándar a
  la vez (`BindableLayout`). Ahora se muestra **una sola instrucción por
  página**: "Fase" (columna B) como título principal grande, y el texto de
  `AudioAuditoria` como instrucción central en negrita y con fuente más
  grande. El texto de `AudioFormacion` ya no se reproduce ni se muestra
  automáticamente: queda oculto detrás de un botón "Más Detalle" de solo
  texto, que al pulsarlo únicamente muestra/oculta el panel — nunca
  reproduce audio.
- Botones "Anterior"/"Siguiente" agrandados (35x35 → 50x50, icono 18 → 24)
  para facilitar el uso en tablet.
- La **secuencia automática** (avance por voz/temporizado) ahora espera 3
  segundos tras terminar de hablar antes de avanzar al siguiente paso, y 3
  segundos adicionales (6 en total) si el auditor pulsó "Más Detalle" en
  ese paso. Esta espera es exclusiva de la secuencia automática: el botón
  físico "Siguiente" sigue avanzando sin retraso añadido.
- Limpieza: eliminado el método `ActualizarListaVisual()` y la clase
  `ItemTextoAudio`, ya sin uso tras el rediseño.

### CORREGIDO — Solapamientos de layout en Android (AuditModePage)
- **Cabecera**: con barras de sistema transparentes/edge-to-edge, el botón
  "Logout" y la campana de notificaciones quedaban debajo de la barra de
  estado del teléfono/tablet en algunos dispositivos. Nuevo
  `Platforms/Android/SafeAreaHelper.cs`, que lee la altura real de las
  barras de sistema (`ViewCompat.GetRootWindowInsets` /
  `WindowInsetsCompat.Type.SystemBars()`) y ajusta dinámicamente el
  `Padding` de la cabecera en `OnAppearing` — un valor fijo en dp no sirve
  para todos los dispositivos (notch, cámara perforada, barra de gestos
  vs. botones, etc.).
- **Pie de página**: el texto "Stellantis Vigo - Centro de Control" era una
  `Label` flotante posicionada de forma absoluta sobre la tarjeta blanca
  scrollable, así que en pantallas más pequeñas o con más botones dinámicos
  (SharePoint) quedaba tapado por la lista de modos de auditoría. Primer
  intento (añadir padding dentro del contenido scrollable) no funcionaba
  porque el pie era una capa aparte, no parte del contenido. **Arreglo
  definitivo**: el texto del pie ahora es el último elemento dentro de la
  propia lista scrollable, en vez de una capa flotante — así nunca puede
  solaparse con nada, sea cual sea la cantidad de botones cargados.
- `MainActivity`: `ResizeableActivity = false` para evitar que Samsung
  ofrezca modo ventana libre/redimensionable (defensa adicional; la causa
  raíz de la "franja gris" reportada en tablet fue el "Modo Escritorio" de
  Samsung a nivel de sistema, no del código de la app — se resuelve
  desactivándolo en Ajustes rápidos del dispositivo).
- Verificado en tablet físico (Samsung, `SM_X210`): cabecera y pie ya no se
  solapan con ningún elemento tras el arreglo.

---

## [1.7.3] - 2026-08-21 — Claude (asistencia)

### CORREGIDO — El audio TTS seguía el idioma del teléfono, no el de la app
- **Causa raíz**: cada llamada a `TextToSpeech.Default.SpeakAsync(texto, null,
  token)` pasaba `null` como `SpeechOptions`, así que el motor de voz del
  dispositivo elegía el idioma/voz por su propia cuenta — que en la práctica
  es el idioma por defecto del sistema operativo del teléfono, no el idioma
  seleccionado dentro de la app. El texto en pantalla ya respetaba el idioma
  de la app correctamente; solo la voz que lo leía ignoraba esa selección.
  Resultado reportado: con el teléfono en alemán, seleccionar inglés en la
  app seguía leyendo el texto (correcto, en inglés) con voz/acento alemán.
- **Arreglo**: nuevo `Services/SpeechLocaleHelper.cs`, que resuelve el
  `Locale` de TTS a partir de `LocalizationService.CurrentLanguage` (el
  idioma elegido en la app) contra `TextToSpeech.Default.GetLocalesAsync()`,
  con caché simple invalidada solo si cambia el idioma de la app. Se pasa
  explícitamente en las 4 llamadas reales de voz (`EstandarPage`,
  `RodajeExterior` ×2, `ControlJapon`), nunca `null`.
- Comportamiento esperado ahora: el audio suena en el idioma elegido en la
  app, sin importar el idioma por defecto configurado en el teléfono.
- Nueva APK `_APK/Aplicacion_SCA_v1.7.3.apk`. Pendiente de verificar en
  dispositivo físico con el teléfono en alemán (no había ningún dispositivo
  conectado en el momento de compilar este arreglo).

---

## [1.7.2] - 2026-08-18 — Claude (asistencia)

### CORREGIDO
- Diálogo personalizado de confirmación "Abortar Auditoría" / "Cancelar
  Fase" / "Cancelar ruta" (el popup con temporizador de 5s que aparece al
  intentar salir de un paso o de una auditoría) estaba **fijo en español**
  en `EstandarPage` y `RodajeExterior`, ignorando el idioma seleccionado.
  Ahora todos los títulos, mensajes y botones de ese diálogo (en las 4
  variantes usadas: cancelar fase, abortar auditoría en Estándar, cancelar
  ruta GPS, abortar auditoría en Rodaje) usan `LocalizationService`
  (ES/EN/FR/DE), igual que el resto de la app.
- Nueva APK `_APK/Aplicacion_SCA_v1.7.2.apk`, instalada y verificada sin
  crash en la Samsung Galaxy Tab física.

---

## [1.7.1] - 2026-08-18 — Claude (asistencia)

### CORREGIDO — Crash de arranque en la APK de Release (Android)
- **Causa raíz**: `AndroidLinkTool=r8` (activo solo en Release) eliminaba la
  clase Java `com.microsoft.maui.PlatformDispatcher`, a la que MAUI solo
  accede por reflexión JNI en tiempo de ejecución — invisible para el
  análisis estático de r8, que la borraba por "no usada". Resultado:
  `java.lang.ClassNotFoundException` envuelta en un
  `System.TypeInitializationException` de `VisualElement` en cuanto arrancaba
  la app (pantalla en blanco → cierre inmediato).
  - Diagnóstico: el runtime de Android/Mono no imprime el `InnerException`
    real al cruzar el límite JNI (se veía "Unknown Source" en todas las
    líneas). Hubo que envolver `MauiProgram.CreateMauiApp()` en un
    try/catch temporal con `Android.Util.Log.Error` para sacar la excepción
    completa por logcat; ya retirado una vez confirmada la causa.
  - **Arreglo permanente**: `AndroidLinkTool=none` en el `PropertyGroup` de
    Release del `.csproj` (en vez de mantener r8 con reglas Proguard
    frágiles — esta app no tiene restricciones de tamaño de Play Store).
  - De paso, `PublishTrimmed=false` también quedó fijado en el `.csproj`
    (antes había que pasarlo por línea de comandos): el IL Trimmer
    (`illink`) revienta de forma nativa con el grafo de dependencias de este
    proyecto (Vosk + ClosedXML, mucha reflexión/interop nativo).
- Verificado con instalación limpia (`adb uninstall` + `adb install`, sin
  relación de despliegue de desarrollo) en una Samsung Galaxy Tab física:
  arranca correctamente, sin crash.
- Nueva APK de Release, autocontenida y portable, en
  `_APK/Aplicacion_SCA_v1.7.1.apk` (158 MB; sigue firmada con clave de
  depuración automática, no con `firma_sca.keystore` — ver limitación de
  firma en 1.7.0). Sustituye a `v1.7.0.apk`, que era en realidad el stub de
  *Fast Deployment* de una build Debug y **no arrancaba de forma autónoma**
  (necesitaba el paso de sincronización de `dotnet build -t:Run`).

---

## [1.7.0] - 2026-08-18 — Claude (asistencia)

### CAMBIADO
- Planta **MAC renombrada a MACK** en todo el código, carpeta local
  (`_ExcelSharePoint/MACK/`) y carpeta de SharePoint (`02_Datos_App_MACK`,
  renombrada in situ, hijos preservados).
- Localización completa (ES/EN/FR/DE) de las pantallas del flujo C-DPV/MACK:
  selección de planta, título "AUDITORÍA C-DPV"/"AUDITORÍA RRU", "HOJA DE
  RUTA..." (las 4 variantes), cabecera de chasis, "VIN: PENDIENTE"/"No
  registrado", "Auditor Desconocido", Volver/Salir, "Paso X de Y", los 4
  estados del botón de reproducción, "FINALIZAR ESTÁNDAR"/"FINALIZAR
  AUDITORÍA", Documentos/Cerrar, "VALIDAR Y CONTINUAR". Todo ahora sigue el
  idioma elegido al arrancar la app en vez de texto fijo en español.
- Corregido el `MaxLength` del campo VIN en `MenuEstandarPage` (estaba fijo a
  8, formato de Vigo); ahora usa `PlantContext.Current.ChassisMaxLength`, así
  que el VIN de 17 caracteres de MACK ya se puede escribir.

### AÑADIDO
- `ControlFase.Fase` (antes se descartaba la columna B del Excel). Se muestra
  junto con `AudioFormacion` como referencia visual bajo cada instrucción en
  `EstandarPage`, para que el auditor vea la fase y el texto de formación
  mientras suena el audio.
- Contenido real de `CDPV.xlsx` para MACK (401 filas, 5 fases: Static, Static
  - After driving, Dynamic - Outside driving, Dynamic - Test Track, All along
  dynamic test), subido a SharePoint sustituyendo las filas de muestra.
- Repositorio git subido a GitHub (`github.com/samarth1190-cmd/Application-SCA`)
  como copia de seguridad fuera de esta máquina.
- APK de depuración `_APK/Aplicacion_SCA_v1.7.0.apk` (firmado con clave de
  depuración automática, no con `firma_sca.keystore` — solo para pruebas en
  tablet, no para distribución).

### PENDIENTE
- Firmar una APK de release con `firma_sca.keystore` (se necesita la
  contraseña, que no está en el repositorio).
- `TipoPlantilla` de las fases dinámicas de MACK usa "DINAMICO", no "RODAJE"
  (el valor que el código busca para activar la pantalla guiada por GPS); sin
  coordenadas GPS todavía no importa, pero habrá que decidir esto cuando se
  añadan las coordenadas de la pista de pruebas.
- Id real de la lista de resultados por planta (sigue vacío, ver 1.6.0).

---

## [1.6.0] - 2026-07-16 — Claude (asistencia)

### AÑADIDO — Soporte multi-planta (Vigo + MAC) en el flujo C-DPV
- Proyecto puesto bajo **git** (commits por hito; antes no había control de versiones).
- Nueva abstracción de planta en `Services/Plants/`:
  - `PlantDefinition` (raíz SharePoint, formato de chasis/VIN, palabras clave de
    motor, modelo Vosk, comandos de voz, lista de resultados, GPS simulado).
  - `PlantRegistry` (VIGO con los valores exactos anteriores; MAC nueva) y
    `PlantContext` (planta activa; por defecto Vigo).
- Todos los valores que estaban repartidos por el código (rutas
  `02_Datos_App_SCA/...`, regex de chasis, `model_es.zip`, palabras
  termic/hibrid/electr, comandos de voz, GPS simulado de RRU en Windows) ahora
  se leen de `PlantContext.Current`. Con Vigo activo el comportamiento es
  idéntico al anterior.
- Nueva `Pages/PlantSelectionPage`: al pulsar **C-DPV** se elige la planta
  (VIGO / MAC). El resto de modos (Japón, DPV, Formación, RRU) siguen siendo
  solo Vigo: `AuditModePage` resetea la planta al aparecer.
- Al elegir una planta distinta se recarga `Vehiculos.xlsx` desde su carpeta
  (MAC: modelos **WD** y **WL**, motores **GAS** e **Hybrid**).
- Planta MAC: VIN ISO de **17 caracteres** (`^[A-HJ-NPR-Z0-9]{17}$`), contenido
  en inglés, comandos de voz en inglés (next/back/pause/repeat) con nuevo modelo
  Vosk `Resources/Raw/model_en.zip` (vosk-model-small-en-us-0.15, +40 MB de APK).
- Creada estructura en SharePoint `02_Datos_App_MAC/` (01_Usuarios,
  02_Configuraciones, 03_Documentos_pdf, 05_CDPV_Formacion, 09_Capturas) con
  Excels esqueleto subidos (mismas cabeceras que Vigo; filas de ejemplo "SAMPLE"
  en CDPV.xlsx a sustituir por el contenido real). Copia local en
  `_ExcelSharePoint/MAC/`.
- La copia de seguridad de auditoría (`AutoGuardadoService`) guarda ahora la
  planta activa y la restaura al recuperar.
- Usuario de pruebas MAC **sam / admin** inyectado en LoginPage
  (> eliminar antes de producción, igual que admin/admin).
- Documentación nueva en `docs/` (arquitectura, flujo de datos, acoplamientos
  de Vigo, plan MAC).

### PENDIENTE
- Id real de la lista de resultados por planta (`PlantDefinition.ResultsListId`
  está vacío para ambas; el envío de resultados ya fallaba antes por esto).
- Contenido real de CDPV MAC (fases, pasos, coordenadas GPS de la pista MAC).
- Decidir mensajes de ayuda de formato de VIN por planta (el aviso de formato
  sigue describiendo el formato de Vigo).

---

## [1.4.0] - 2026-07-13 — Claude (asistencia)

### AÑADIDO — Traducción completa del contenido SCA (CDPV)
- Traducidas las **406 cadenas distintas** del contenido de auditoría SCA
  (nombres de fase + AudioFormacion + AudioAuditoria) a **EN / FR / DE**.
  Cobertura verificada: 406/406, 0 faltantes.
- Rellenadas las columnas `Estandar_EN/FR/DE`, `AudioFormacion_EN/FR/DE` y
  `AudioAuditoria_EN/FR/DE` en una copia del Excel.
- Subido a SharePoint como **copia no destructiva**:
  `02_Datos_App_SCA/05_CDPV_Formacion/CDPV_traducido.xlsx`
  (el `CDPV.xlsx` de producción NO se ha tocado).
- Copia local también en `_ExcelSharePoint/CDPV_traducido.xlsx`.

### PARA ACTIVAR EN LA APP
- La app descarga `CDPV.xlsx`. Para que los idiomas surtan efecto hay que, tras
  revisar la traducción, **reemplazar** `CDPV.xlsx` por el contenido de
  `CDPV_traducido.xlsx` (o renombrar). Mientras tanto la app sigue en español.

### IMPORTANTE — Revisión humana
- Las traducciones son automáticas (hechas por el asistente) como punto de
  partida. Son instrucciones **técnicas y de seguridad**; deben ser revisadas
  por alguien que conozca el proceso de fábrica antes de usarse en producción.
- Versión de la app subida a **1.4.0** (ApplicationVersion 6).

---

## [1.3.0] - 2026-07-13 — Claude (asistencia)

### AÑADIDO — Contenido de auditoría multi-idioma (SCA / CDPV.xlsx)
- `Services/ExcelService.cs`: `LeerExcelAuditoria` ahora lee columnas por idioma.
  - Nuevos helpers reutilizables: `SufijoIdioma()` (devuelve "_EN"/"_FR"/"_DE"),
    `ConstruirMapaCabeceras()` (mapa nombre-de-cabecera → columna, por nombre, no
    por posición) y `TextoLocalizado()` (lee `Columna+sufijo`; si falta o está
    vacía, usa el español).
  - Columnas localizadas soportadas: `Estandar`, `AudioFormacion`, `AudioAuditoria`.
  - Las fases se agrupan por el nombre en **español** (clave estable) y se muestran
    con el nombre traducido, evitando que un idioma incompleto parta una fase en dos.
- Estructura del Excel `CDPV.xlsx`: añadidas 9 columnas nuevas (15–23):
  `Estandar_EN/FR/DE`, `AudioFormacion_EN/FR/DE`, `AudioAuditoria_EN/FR/DE`.
  Copia local en `_ExcelSharePoint/CDPV.xlsx`.

### ESTADO — Traducción del contenido
- Traducidos y verificados: los **5 nombres de fase** (Toma del Vehículo, Estático,
  Dinámico, Exterior, Restitución del Vehículo) + los **primeros ~10 pasos de audio**
  como demostración del flujo completo (EN/FR/DE).
- Pendientes: ~395 cadenas de audio restantes (contenido técnico de seguridad).
  El sistema hace *fallback* a español para las celdas vacías, así que la app
  funciona sin romperse mientras se completan.
- El archivo local NO se ha subido a SharePoint todavía (evita sobrescribir datos
  de producción sin confirmación).

### Acceso a los datos (SharePoint)
- Sitio: https://shiftup.sharepoint.com/sites/APLICACION_SCA
- Biblioteca *Documents* → `02_Datos_App_SCA/05_CDPV_Formacion/CDPV.xlsx`.
- Versión de la app subida a **1.3.0** (ApplicationVersion 5).

---

## [1.2.0] - 2026-07-13 — Claude (asistencia)

### AÑADIDO / CAMBIADO — Traducción de la interfaz (resto de páginas)
- `Services/LocalizationService.cs`: añadidas ~35 claves nuevas (ES/EN/FR/DE)
  para los avisos restantes.
- Cableados **25 diálogos** más para usar `LocalizationService.Translate(...)`:
  - `Pages/ControlJapon.xaml.cs` — 9 avisos.
  - `Pages/ResultsJapon.xaml.cs` — 7 avisos.
  - `Pages/RodajeExterior.xaml.cs` — 1 aviso.
  - `Pages/RRUPage.xaml.cs` — 8 avisos.

### CORREGIDO — Codificación de RRUPage
- `Pages/RRUPage.xaml.cs` estaba guardado en **Windows-1252**, lo que corrompía
  las tildes y la ñ (aparecían como `�`: "ubicaci�n", "se�al", "Aseg�rate").
  Reconvertido a **UTF-8 (con BOM)**; ahora todos los textos en español se ven
  correctamente. (Los emoji perdidos previamente, que aparecían como `??` en
  etiquetas tipo "?? VALIDAR PARADA", no se pueden recuperar del texto corrupto;
  quedan pendientes de re-añadir si se desea.)

### ESTADO
- Con esto, **todos los `DisplayAlert` en español fijo del proyecto (41 en total)**
  pasan por el sistema de traducción y cambian con el idioma seleccionado.
- Sigue pendiente: el **contenido de auditoría** (nombres de fase, textos de
  audio de los Excel) que sólo existe en un idioma; requiere columnas por idioma
  en los Excel + cambios en `ExcelService`.
- Versión de la app subida a **1.2.0** (ApplicationVersion 4).

---

## [1.1.0] - 2026-07-13 — Claude (asistencia)

### AÑADIDO / CAMBIADO — Traducción de la interfaz (avisos del flujo SCA)
- `Services/LocalizationService.cs`: añadidas ~33 claves nuevas de traducción
  (ES/EN/FR/DE) para los diálogos de aviso (`DisplayAlert`) del flujo de
  auditoría SCA, además de botones/títulos comunes reutilizables
  (BTN_ACEPTAR, BTN_ENTENDIDO_OK, ERR_TITULO, BTN_SI_ENVIAR, etc.).
- Cableados **16 diálogos** que estaban en español fijo para que usen
  `LocalizationService.Translate(...)` y cambien con el idioma seleccionado:
  - `Pages/MenuEstandarPage.xaml.cs` — 6 avisos (abrir manual, rodaje no aplica,
    fase completada, falta chasis, confirmar salida, guía rápida de auditoría).
  - `Pages/EstandarPage.xaml.cs` — 2 avisos (error de descarga, instrucciones en curso).
  - `Pages/ResultsPage.xaml.cs` — 8 avisos (formato de chasis, faltan datos,
    guardar vehículo, introducir VIN, confirmar envío, éxito, error de envío,
    aviso de nube).
- Versión de la app subida a **1.1.0** (ApplicationVersion 3) en el csproj.

### PENDIENTE (siguiente iteración de traducción)
- Faltan por cablear los avisos en español de: `ControlJapon` (9),
  `ResultsJapon` (7), `RRUPage` (8, ojo: el archivo tiene caracteres corruptos
  de codificación que hay que arreglar), `RodajeExterior` (1).
- El contenido de auditoría (nombres de fase, textos de audio) proviene de Excel
  y sigue en un solo idioma; para traducirlo hacen falta columnas por idioma en
  los Excel + cambios en el parser (`ExcelService`). No incluido aquí.

---

## [1.0.1] - 2026-07-13 — Claude (asistencia)

### CAMBIADO
- Reorientado el proyecto de **.NET 10** a **.NET 9** para poder compilar y
  ejecutar en esta máquina (solo tiene el SDK .NET 9 instalado).
  - `Aplicacion_SCA.csproj`: `TargetFrameworks` net10.0-* → net9.0-*
    (android / ios / maccatalyst / windows).
  - Paquetes NuGet degradados a la línea compatible con .NET 9:
    - `Microsoft.Maui.Controls` 10.0.50 → 9.0.120
    - `Microsoft.Maui.Controls.Maps` 10.0.50 → 9.0.120
    - `CommunityToolkit.Maui` 14.1.0 → 12.3.0
    - `Microsoft.Extensions.Logging.Debug` 10.0.0 → 9.0.0

### CORREGIDO
- `Pages/ControlJapon.xaml.cs`: las animaciones usaban la API nueva de MAUI 10
  (`ScaleToAsync` / `TranslateToAsync`), que no existe en MAUI 9.
  Renombradas a `ScaleTo` / `TranslateTo` (5 llamadas).

### AÑADIDO
- `Pages/LoginPage.xaml.cs`: cuenta local de pruebas **admin / admin**
  (ROL Admin) que se inyecta en `SesionGlobal.ListaUsuarios` si no hay datos de
  usuarios (permite entrar sin conexión a SharePoint durante el desarrollo).
  > NOTA: es una cuenta de prueba. Debe eliminarse antes de publicar en producción.

### NOTAS DE COMPILACIÓN
- Compila Android (APK, 0 errores) y Windows (0 errores) con .NET 9.
- Copia de seguridad del csproj original (.NET 10) guardada en el scratchpad de la sesión.

---

<!-- Plantilla para el próximo cambio:

## [X.Y.Z] - AAAA-MM-DD — autor
### AÑADIDO / CAMBIADO / CORREGIDO / ELIMINADO
- ...
-->
