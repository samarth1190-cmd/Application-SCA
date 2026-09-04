using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using Aplicacion_SCA.Models;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.Maui.Media;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.ApplicationModel;
using Vosk;
using System.IO;
using System.IO.Compression;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;
using System.Text.Json;

namespace Aplicacion_SCA.Pages;

public partial class EstandarPage : ContentPage
{
    private string _modeloAuditoria = string.Empty;
    private string _motorAuditoria = string.Empty;
    private string _modoOperativo = string.Empty;
    private bool _esFormacion;

    private List<ControlFase>? _pasosReales;
    private int _indiceActual = 0;
    private CancellationTokenSource? _cts;

    private enum EstadoApp { Parado, Corriendo, Pausado, Finalizado }
    private EstadoApp _estadoActual = EstadoApp.Parado;

    private string _nombreEstandarActual = "";
    private int _miIndiceEstandar;

    private bool _esperandoValidacionManual = false;
    private bool _bloqueoAccion = false;

    // Comando forzado desde la UI (botón físico "Más Detalle" / "Validar y Continuar")
    // mientras EjecutarValidacionManual está escuchando el micrófono: se trata
    // exactamente igual que un comando reconocido por voz.
    private string? _comandoForzado = null;

    private Vosk.Model? _voskModel;
    private VoskRecognizer? _rec;
    private bool _modeloCargado = false;
    private IAudioCaptureService? _audioService;

    private List<string> _listaPdfsDisponibles = new List<string>();
    private static string _carpetaManualesPdf => PlantContext.ResolvePath("03_Documentos_pdf");

    private string ClaveGuardadoPaso => AuditProgressHelper.ClaveGuardadoPaso(SesionGlobal.ChasisActual ?? "NA", _miIndiceEstandar);

#if ANDROID
    private Android.Media.ToneGenerator? _toneGen;
#endif

    public EstandarPage(string modelo, string motor, string modo)
    {
        InitializeComponent();
        _modeloAuditoria = modelo;
        _motorAuditoria = motor;
        _modoOperativo = modo;
        _esFormacion = _modoOperativo.Contains("Formacion", StringComparison.OrdinalIgnoreCase);
        _miIndiceEstandar = SesionGlobal.IndiceEstandarActual;

#if ANDROID
        _audioService = new Aplicacion_SCA.Platforms.Android.AudioCaptureService();
#endif

        ApplyTranslations();
        LblModeloText.Text = _modeloAuditoria;
        LblMotorText.Text = _motorAuditoria;

        _ = PrepararYCargarModeloVoskAsync();
        ActualizarBotonVisualmente();
    }

    private void ApplyTranslations()
    {
        LblVolverMenuTexto.Text = LocalizationService.Translate("BTN_VOLVER");
        LblSalirTexto.Text = LocalizationService.Translate("BTN_SALIR");
        LblModeloPrefijo.Text = LocalizationService.Translate("LBL_MODELO_PREFIJO");
        LblMotorPrefijo.Text = LocalizationService.Translate("LBL_MOTOR_PREFIJO");
        LblFinalizarEstandar.Text = LocalizationService.Translate("BTN_FINALIZAR_ESTANDAR");
        LblValidarYContinuar.Text = LocalizationService.Translate("BTN_VALIDAR_CONTINUAR");
        LblDocumentosHeader.Text = LocalizationService.Translate("OVERLAY_DOCUMENTOS");
        LblCerrarDocumentos.Text = LocalizationService.Translate("BTN_CERRAR");
    }

    private async Task PrepararYCargarModeloVoskAsync()
    {
        try
        {
            string cacheDir = FileSystem.CacheDirectory;
            string modelDestPath = System.IO.Path.Combine(cacheDir, PlantContext.Current.VoskModelFolder);

            if (!Directory.Exists(modelDestPath) || !File.Exists(System.IO.Path.Combine(modelDestPath, "am", "final.mdl")))
            {
                Debug.WriteLine("⚙️ VOSK: Extrayendo modelo del ZIP...");
                if (!Directory.Exists(modelDestPath)) Directory.CreateDirectory(modelDestPath);

                using var stream = await FileSystem.OpenAppPackageFileAsync(PlantContext.Current.VoskModelAsset);
                using var archive = new ZipArchive(stream);
                archive.ExtractToDirectory(modelDestPath, true);
            }

            Vosk.Vosk.SetLogLevel(0);
            _voskModel = new Vosk.Model(modelDestPath);

            _rec = new VoskRecognizer(_voskModel, 16000.0f);
            _rec.SetMaxAlternatives(0);
            _rec.SetWords(true);

            _modeloCargado = true;
            Debug.WriteLine("✅ VOSK: Motor listo sin restricciones de gramática.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error Vosk: {ex.Message}");
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _bloqueoAccion = false;

#if ANDROID
        var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
        if (window != null)
        {
            window.SetStatusBarColor(Android.Graphics.Color.Transparent);
            window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
            window.SetFlags(Android.Views.WindowManagerFlags.LayoutNoLimits, Android.Views.WindowManagerFlags.LayoutNoLimits);
            var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
            if (controller != null)
            {
                controller.AppearanceLightStatusBars = false;
                controller.AppearanceLightNavigationBars = false;
            }
        }
#endif

        try
        {
            ContenedorTarjeta.Opacity = 0;
            ContenedorTarjeta.TranslationY = 60;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(50);
                await Task.WhenAll(
                    ContenedorTarjeta.FadeTo(1, 700, Easing.CubicOut),
                    ContenedorTarjeta.TranslateTo(0, 0, 700, Easing.CubicOut)
                );
            });
        }
        catch (Exception)
        {
            ContenedorTarjeta.Opacity = 1;
            ContenedorTarjeta.TranslationY = 0;
        }

        try
        {
            ApplyTranslations();
            SesionGlobal.IndiceEstandarActual = _miIndiceEstandar;
            DeviceDisplay.Current.KeepScreenOn = true;

            if (SesionGlobal.UsuarioActivo != null)
                LblUsuarioNombre.Text = $"{SesionGlobal.UsuarioActivo.NOMBRE} {SesionGlobal.UsuarioActivo.APELLIDOS}";
            else
                LblUsuarioNombre.Text = LocalizationService.Translate("MSG_AUDITOR_DESCONOCIDO");

            LblChasisText.Text = !string.IsNullOrEmpty(SesionGlobal.ChasisActual) ? $"VIN: {SesionGlobal.ChasisActual}" : LocalizationService.Translate("MSG_VIN_NO_REGISTRADO");

            _indiceActual = Preferences.Get(ClaveGuardadoPaso, 0);

            if (_indiceActual > 0)
            {
                _estadoActual = EstadoApp.Pausado;
            }

            CargarDatosEstandarActual();
            await CargarListaPdfsDesdeSharePointAsync();

            _ = AutoGuardadoService.GuardarProgresoAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error OnAppearing: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        PausarSecuencia();

        DeviceDisplay.Current.KeepScreenOn = false;

#if ANDROID
        if (_toneGen != null)
        {
            _toneGen.Release();
            _toneGen = null;
        }
#endif
    }

    private void GuardarPasoActual()
    {
        Preferences.Set(ClaveGuardadoPaso, _indiceActual);
    }

    private void LimpiarPasoGuardado()
    {
        Preferences.Remove(ClaveGuardadoPaso);
    }

    private void ReproducirBeep(bool esInicio)
    {
#if ANDROID
        try
        {
            if (_toneGen == null) _toneGen = new Android.Media.ToneGenerator(Android.Media.Stream.Music, 100);
            var toneType = esInicio ? Android.Media.Tone.PropBeep : Android.Media.Tone.PropAck;
            _toneGen.StartTone(toneType, 200);
        }
        catch { }
#endif
    }

    private async Task CargarListaPdfsDesdeSharePointAsync()
    {
        try
        {
            var sp = new SharePointService();
            _listaPdfsDisponibles = await sp.ObtenerPdfsEnCarpetaAsync(_carpetaManualesPdf);
            GenerarListaPdfsPanel();
        }
        catch { }
    }

    private void GenerarListaPdfsPanel()
    {
        if (ContenedorListaPdfs == null) return;
        ContenedorListaPdfs.Children.Clear();

        if (_listaPdfsDisponibles == null || !_listaPdfsDisponibles.Any())
        {
            ContenedorListaPdfs.Children.Add(new Label { Text = "No hay manuales", TextColor = Colors.LightGray, HorizontalOptions = LayoutOptions.Center });
            return;
        }

        foreach (var archivoPdf in _listaPdfsDisponibles)
        {
            string nombreLimpio = "📄 " + archivoPdf.Replace(".pdf", "").Replace("_", " ").ToUpper();

            Label lblTexto = new Label { Text = nombreLimpio, TextColor = Colors.White, FontSize = 12, CharacterSpacing = 1, VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap, MaxLines = 3 };
            Grid contenidoLayout = new Grid { Padding = new Thickness(15, 10), HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center };
            contenidoLayout.Children.Add(lblTexto);

            LinearGradientBrush gradienteBorde = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            gradienteBorde.GradientStops.Add(new GradientStop(Color.FromArgb("#243782"), 0.0f));
            gradienteBorde.GradientStops.Add(new GradientStop(Color.FromArgb("#43AAA0"), 1.0f));

            Border btnPdf = new Border { MinimumHeightRequest = 55, BackgroundColor = Color.FromArgb("#4F7F9E"), Stroke = gradienteBorde, StrokeThickness = 2, Margin = new Thickness(0, 0, 0, 10), StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(27.5) }, Content = contenidoLayout };

            TapGestureRecognizer tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) =>
            {
                if (_bloqueoAccion) return;
                _bloqueoAccion = true;
                try
                {
                    if (btnPdf != null) { await btnPdf.ScaleTo(0.9, 50); await btnPdf.ScaleTo(1.0, 50); }
                    await AbrirPdfDesdeNube(archivoPdf);
                }
                finally { _bloqueoAccion = false; }
            };
            btnPdf.GestureRecognizers.Add(tapGesture);

            ContenedorListaPdfs.Children.Add(btnPdf);
        }
    }

    private async Task AbrirPdfDesdeNube(string nombreArchivo)
    {
        try
        {
            var sp = new SharePointService();
            string token = await sp.ConseguirTokenSilenciosoAsync();
            string rutaPdfCompleta = $"{_carpetaManualesPdf}/{nombreArchivo}";

            byte[] pdfBytes = await sp.DescargarExcelConTokenAsync(rutaPdfCompleta, token);
            string rutaLocal = System.IO.Path.Combine(FileSystem.CacheDirectory, nombreArchivo);

            File.WriteAllBytes(rutaLocal, pdfBytes);
            await Launcher.Default.OpenAsync(new OpenFileRequest("Ver Manual", new ReadOnlyFile(rutaLocal)));
        }
        catch (Exception ex)
        {
            await DisplayAlert(LocalizationService.Translate("ERR_TITULO"), LocalizationService.Translate("ERR_DESCARGAR") + ex.Message, LocalizationService.Translate("BTN_ACEPTAR"));
        }
    }

    private void CargarDatosEstandarActual()
    {
        bool esModo13Columnas = AuditProgressHelper.EsModo13Columnas(_modoOperativo);

        var listaActiva = esModo13Columnas ? SesionGlobal.Estandares : SesionGlobal.EstandaresDPV;

        if (listaActiva == null || _miIndiceEstandar >= listaActiva.Count)
        {
            LblTituloEstandar.Text = "ERROR DE DATOS";
            return;
        }

        var estandarActual = listaActiva[_miIndiceEstandar];
        _nombreEstandarActual = estandarActual.NombreEstandar ?? "";
        LblTituloEstandar.Text = _nombreEstandarActual.ToUpper();

        var todosLosPasos = estandarActual.ListaControles?.OrderBy(p => p.NumeroFase).ToList();
        _pasosReales = AuditProgressHelper.FiltrarPasosReales(todosLosPasos, _esFormacion);

        if (_pasosReales != null)
        {
            for (int i = 0; i < _pasosReales.Count; i++) _pasosReales[i].NumeroFase = i + 1;

            if (_indiceActual >= _pasosReales.Count) _indiceActual = 0;
        }

        ActualizarTextosPaso();
        ActualizarBarraYTiempo();
    }

    // El "detalle" de un paso es AudioFormacion en modo Formación, AudioAuditoria en
    // modo auditoría normal — el mismo campo que antes se hablaba automáticamente.
    // Ahora solo se habla bajo demanda (botón "Más Detalle" o el comando de voz
    // "detalle"/"detail"): por defecto solo se oye la Fase.
    private string TextoDetalleDelPaso(ControlFase paso) => (_esFormacion ? paso.AudioFormacion : paso.AudioAuditoria) ?? string.Empty;

    private void ActualizarInstruccionActual()
    {
        if (_pasosReales == null || _indiceActual >= _pasosReales.Count) return;
        var paso = _pasosReales[_indiceActual];

        LblFaseTitulo.Text = !string.IsNullOrWhiteSpace(paso.Fase) ? paso.Fase : _nombreEstandarActual;

        LblInstruccionActual.Text = string.Empty;
        LblInstruccionActual.IsVisible = false;

        BtnMasDetalle.IsVisible = !string.IsNullOrWhiteSpace(TextoDetalleDelPaso(paso));
        LblMasDetalleTexto.Text = LocalizationService.Translate("BTN_MAS_DETALLE");
    }

    private async void OnMasDetalleClicked(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);

        // Equivalente táctil del comando de voz "detalle": si la secuencia está
        // escuchando el micrófono en este momento, se trata exactamente igual
        // que si el auditor lo hubiera dicho en voz alta.
        _comandoForzado = "mas_detalle";
    }

    private async Task MostrarYHablarDetalleAsync(string textoDetalle, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(textoDetalle)) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            LblInstruccionActual.Text = textoDetalle;
            LblInstruccionActual.IsVisible = true;
        });

        try
        {
            var localeVoz = await SpeechLocaleHelper.GetLocaleAsync();
            await SpeechLocaleHelper.HablarConPausasAsync(textoDetalle, localeVoz, token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task AnimarBoton(VisualElement? boton)
    {
        if (boton == null) return;
        await boton.ScaleTo(0.95, 50, Easing.Linear);
        await boton.ScaleTo(1.0, 50, Easing.Linear);
    }

    private async void OnAnteriorClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;

        try
        {
            if (sender is VisualElement btn) await AnimarBoton(btn);
            if (_pasosReales == null || _pasosReales.Count == 0) return;

            bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;

            DetenerEjecucionAudioYVosk();

            Task.Run(async () =>
            {
                try { var ctsMute = new CancellationTokenSource(); ctsMute.Cancel(); await TextToSpeech.Default.SpeakAsync("", null, ctsMute.Token); } catch { }
            });

            await Task.Delay(100);

            if (_indiceActual > 0)
            {
                _indiceActual--;
                GuardarPasoActual(); 
            }

            if (_estadoActual == EstadoApp.Finalizado)
            {
                _estadoActual = EstadoApp.Pausado;
                ActualizarBotonVisualmente();
            }

            ActualizarBarraYTiempo();
            ActualizarTextosPaso();

            if (estabaCorriendo) IniciarSecuencia();
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async void OnSiguienteClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;

        try
        {
            if (sender is VisualElement btn) await AnimarBoton(btn);
            if (_pasosReales == null || _pasosReales.Count == 0) return;

            bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;

            DetenerEjecucionAudioYVosk();

            Task.Run(async () =>
            {
                try { var ctsMute = new CancellationTokenSource(); ctsMute.Cancel(); await TextToSpeech.Default.SpeakAsync("", null, ctsMute.Token); } catch { }
            });

            await Task.Delay(100);

            if (_indiceActual < _pasosReales.Count - 1)
            {
                _indiceActual++;
                GuardarPasoActual();

                ActualizarBarraYTiempo();
                ActualizarTextosPaso();

                if (estabaCorriendo) IniciarSecuencia();
            }
            else
            {
                FinalizarAuditoriaCompleta();
            }
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async void OnControlClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;

        try
        {
            if (sender is VisualElement btn) await AnimarBoton(btn);
            switch (_estadoActual)
            {
                case EstadoApp.Parado: IniciarSecuencia(); break;
                case EstadoApp.Pausado: IniciarSecuencia(); break;
                case EstadoApp.Corriendo: PausarSecuencia(); break;
                case EstadoApp.Finalizado: ReiniciarAuditoria(); break;
            }
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async void OnValidarManualClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);

        // Equivalente táctil del comando de voz "siguiente": fallback físico para
        // cuando el reconocimiento de voz falla o el auditor prefiere tocar la pantalla.
        _comandoForzado = "siguiente";
    }

    private void DetenerEjecucionAudioYVosk()
    {
        _esperandoValidacionManual = false;
        _cts?.Cancel();
        _audioService?.StopRecording();

        MainThread.BeginInvokeOnMainThread(() => {
            var panelManual = this.FindByName<VisualElement>("PanelValidacionManual");
            if (panelManual != null) panelManual.IsVisible = false;

            if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#243782");
        });
    }

    private async void IniciarSecuencia()
    {
        if (_pasosReales == null || _pasosReales.Count == 0 || _indiceActual >= _pasosReales.Count) return;

        DetenerEjecucionAudioYVosk();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _estadoActual = EstadoApp.Corriendo;
        ActualizarBotonVisualmente();

        try
        {
            for (int i = _indiceActual; i < _pasosReales.Count; i++)
            {
                if (token.IsCancellationRequested || _estadoActual != EstadoApp.Corriendo) break;

                _indiceActual = i;
                GuardarPasoActual();

                ActualizarTextosPaso();
                ActualizarBarraYTiempo();

                _ = AutoGuardadoService.GuardarProgresoAsync();

                var paso = _pasosReales[i];
                string textoDetalle = TextoDetalleDelPaso(paso);

                // Por defecto solo se habla la Fase (breve). El resto — el detalle
                // completo (AudioAuditoria, o AudioFormacion en modo Formación) — solo
                // se dice cuando el auditor lo pide, por voz ("detalle"/"detail") o
                // tocando "Más Detalle": pensado para manos libres mientras se conduce.
                if (!string.IsNullOrWhiteSpace(paso.Fase))
                {
                    await Task.Delay(500, token);
                    var localeVoz = await SpeechLocaleHelper.GetLocaleAsync();
                    await SpeechLocaleHelper.HablarConPausasAsync(paso.Fase, localeVoz, token);
                }

                if (token.IsCancellationRequested || _estadoActual != EstadoApp.Corriendo) break;

                // Escucha indefinida: nunca avanza sola por temporizador. Solo un
                // comando reconocido (voz o botón físico) mueve la auditoría.
                string comandoVoz = "";
                while (string.IsNullOrEmpty(comandoVoz) && !token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
                {
                    comandoVoz = await EjecutarValidacionManual(token);

                    if (comandoVoz == "mas_detalle")
                    {
                        await MostrarYHablarDetalleAsync(textoDetalle, token);
                        comandoVoz = ""; // seguir escuchando en el mismo paso
                    }
                }

                if (token.IsCancellationRequested || _estadoActual != EstadoApp.Corriendo) break;

                if (comandoVoz == "pausa")
                {
                    MainThread.BeginInvokeOnMainThread(() => PausarSecuencia());
                    return;
                }
                else if (comandoVoz == "repite")
                {
                    i--;
                    await Task.Delay(100, token);
                    continue;
                }
                else if (comandoVoz == "atras")
                {
                    if (i > 0) i -= 2; else i = -1;
                    await Task.Delay(100, token);
                    continue;
                }
                // "siguiente" (voz o botón "Validar y Continuar"): cae aquí y el for avanza.
            }

            if (!token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
                FinalizarAuditoriaCompleta();
        }
        catch (OperationCanceledException) { }
        finally { DetenerEjecucionAudioYVosk(); }
    }

    private async Task<string> EjecutarValidacionManual(CancellationToken token)
    {
        string comandoDetectado = "";
        _esperandoValidacionManual = true;
        _comandoForzado = null;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var panelManual = this.FindByName<VisualElement>("PanelValidacionManual");
            if (panelManual != null) panelManual.IsVisible = true;
            if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#63F86D");
        });

        if (await CheckSpeechPermissionsAsync() && _modeloCargado && _rec != null && _audioService != null)
        {
            _rec.Reset();

            ReproducirBeep(esInicio: true);
            await Task.Delay(700, token);

            _audioService.StartRecording((buffer, length) =>
            {
                if (!_esperandoValidacionManual || token.IsCancellationRequested || !string.IsNullOrEmpty(comandoDetectado))
                    return;

                bool accepted = _rec.AcceptWaveform(buffer, length);

                if (accepted)
                {
                    string resultJson = _rec.Result();
                    procesarResultadoVosk(resultJson, ref comandoDetectado, esParcial: false);
                }
                else
                {
                    string partialJson = _rec.PartialResult();
                    if (!string.IsNullOrWhiteSpace(partialJson) && !partialJson.Contains("\"partial\" : \"\""))
                    {
                        procesarResultadoVosk(partialJson, ref comandoDetectado, esParcial: true);
                    }
                }
            });

            while (_esperandoValidacionManual && !token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
            {
                if (_comandoForzado != null)
                {
                    comandoDetectado = _comandoForzado;
                    _comandoForzado = null;
                    _esperandoValidacionManual = false;
                    break;
                }

                await Task.Delay(50, token);
            }

            _audioService.StopRecording();

            if (!string.IsNullOrEmpty(comandoDetectado) && comandoDetectado != "pausa")
            {
                await Task.Delay(200, token);
                ReproducirBeep(esInicio: false);
                await Task.Delay(200, token);
            }
        }
        else
        {
            // Sin micrófono/modelo Vosk disponible, los botones físicos son la
            // única forma de avanzar, así que deben seguir funcionando igual.
            while (_esperandoValidacionManual && !token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
            {
                if (_comandoForzado != null)
                {
                    comandoDetectado = _comandoForzado;
                    _comandoForzado = null;
                    _esperandoValidacionManual = false;
                    break;
                }

                await Task.Delay(200, token);
            }
        }

        return comandoDetectado;
    }

    private void procesarResultadoVosk(string json, ref string comando, bool esParcial)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string keyToFind = esParcial ? "partial" : "text";

            if (!root.TryGetProperty(keyToFind, out JsonElement textElement))
            {
                return;
            }

            string texto = AuditProgressHelper.NormalizarTexto(textElement.GetString()?.ToLower().Trim() ?? "");
            texto = new string(texto.Where(c => char.IsLetter(c) || char.IsWhiteSpace(c)).ToArray()).Trim();

            if (string.IsNullOrWhiteSpace(texto)) return;

            Debug.WriteLine($"📝 EVALUANDO [Parcial={esParcial}]: '{texto}'");

            foreach (var (cmd, palabras) in PlantContext.Current.VoiceCommands)
            {
                if (palabras.Any(p => texto.Contains(p)))
                {
                    comando = cmd;
                    Debug.WriteLine($"✅ COMANDO CAZADO: {cmd}");
                    break;
                }
            }

            if (!string.IsNullOrEmpty(comando))
            {
                _esperandoValidacionManual = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error procesando JSON: {ex.Message}");
        }
    }

    private async Task<bool> CheckSpeechPermissionsAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Microphone>();
        }
        return status == PermissionStatus.Granted;
    }

    private void PausarSecuencia()
    {
        if (_estadoActual == EstadoApp.Corriendo)
        {
            DetenerEjecucionAudioYVosk();
            _estadoActual = EstadoApp.Pausado;
            ActualizarBotonVisualmente();

            _ = AutoGuardadoService.GuardarProgresoAsync();
        }
    }

    private void DetenerSecuencia()
    {
        DetenerEjecucionAudioYVosk();
        _indiceActual = 0;
        GuardarPasoActual();
        _estadoActual = EstadoApp.Parado;
        ActualizarBotonVisualmente();
    }

    private void ReiniciarAuditoria()
    {
        DetenerSecuencia();
        ActualizarBarraYTiempo();
        ActualizarTextosPaso();
        IniciarSecuencia();
    }

    private void FinalizarAuditoriaCompleta()
    {
        _estadoActual = EstadoApp.Finalizado;
        ActualizarBarraYTiempo();
        ActualizarBotonVisualmente();

        LimpiarPasoGuardado(); 
        _ = AutoGuardadoService.GuardarProgresoAsync();
    }

    private void ActualizarTextosPaso()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_pasosReales != null && _indiceActual < _pasosReales.Count)
            {
                LblPasoActual.Text = LocalizationService.TranslateFormat("MSG_PASO_DE", _indiceActual + 1, _pasosReales.Count);
                ActualizarInstruccionActual();

                if (ScrollInstrucciones != null)
                {
                    await ScrollInstrucciones.ScrollToAsync(0, 0, false);
                }
            }
        });
    }

    private void ActualizarBarraYTiempo()
    {
        MainThread.BeginInvokeOnMainThread(async () => {
            int totalPasos = _pasosReales?.Count ?? 0;

            if (totalPasos > 0 && BarraProgreso != null)
            {
                double progreso = (double)(_indiceActual + (_estadoActual == EstadoApp.Finalizado ? 1 : 0)) / totalPasos;
                if (progreso > 1.0) progreso = 1.0;

                BarraProgreso.ProgressColor = Color.FromArgb("#243782");
                await BarraProgreso.ProgressTo(progreso, 250, Easing.Linear);
            }

            if (LblTiempoActual != null) LblTiempoActual.Text = (_estadoActual == EstadoApp.Finalizado ? totalPasos : _indiceActual).ToString();
            if (LblTiempoTotal != null) LblTiempoTotal.Text = totalPasos.ToString();
        });
    }

    private void ActualizarBotonVisualmente()
    {
        MainThread.BeginInvokeOnMainThread(() => {
            var borderBtn = this.FindByName<Border>("BtnControlSecuencia");

            switch (_estadoActual)
            {
                case EstadoApp.Corriendo:
                    LblIconoBoton.Text = LocalizationService.Translate("BTN_PAUSAR");
                    if (borderBtn != null) borderBtn.BackgroundColor = Color.FromArgb("#FF9800");
                    break;
                case EstadoApp.Pausado:
                    LblIconoBoton.Text = LocalizationService.Translate("BTN_REPRODUCIR");
                    if (borderBtn != null) borderBtn.BackgroundColor = Color.FromArgb("#243782");
                    break;
                case EstadoApp.Finalizado:
                    LblIconoBoton.Text = LocalizationService.Translate("BTN_REINICIAR");
                    if (borderBtn != null) borderBtn.BackgroundColor = Color.FromArgb("#4CAF50");
                    break;
                default:
                    LblIconoBoton.Text = LocalizationService.Translate("BTN_COMENZAR_PASO");
                    if (borderBtn != null) borderBtn.BackgroundColor = Color.FromArgb("#243782");
                    break;
            }
        });
    }

    private void OcultarTeclado()
    {
#if ANDROID
        var actividadActual = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var vistaEnFoco = actividadActual?.CurrentFocus;
        if (actividadActual != null && vistaEnFoco != null)
        {
            var managerDeTeclado = (Android.Views.InputMethods.InputMethodManager?)actividadActual.GetSystemService(Android.Content.Context.InputMethodService);
            managerDeTeclado?.HideSoftInputFromWindow(vistaEnFoco.WindowToken, Android.Views.InputMethods.HideSoftInputFlags.None);
        }
#endif
    }

    private void OnFondoTapped(object sender, TappedEventArgs e) { OcultarTeclado(); }

    protected override bool OnBackButtonPressed()
    {
        if (OverlayDocumentos != null && OverlayDocumentos.IsVisible)
        {
            OnCerrarDocumentosClicked(this, EventArgs.Empty);
            return true;
        }

        _ = VolverAtrasSinGuardar();
        return true;
    }

    private async Task<bool> PedirConfirmacionConTimeout(string titulo, string mensaje)
    {
        var rootGrid = this.Content as Grid;
        if (rootGrid == null) return false;

        var tcs = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();

        var overlay = new Grid { BackgroundColor = Color.FromArgb("#E6000000"), ZIndex = 9999, Opacity = 0 };

        var tarjeta = new Border
        {
            BackgroundColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 320,
            Padding = 25,
            StrokeThickness = 0
        };
        tarjeta.StrokeShape = new RoundRectangle { CornerRadius = 15 };

        var layout = new VerticalStackLayout { Spacing = 15 };

        layout.Children.Add(new Label { Text = titulo, FontAttributes = FontAttributes.Bold, FontSize = 18, TextColor = Color.FromArgb("#E53935"), HorizontalTextAlignment = TextAlignment.Center });
        layout.Children.Add(new Label { Text = mensaje, FontSize = 14, TextColor = Colors.Black, HorizontalTextAlignment = TextAlignment.Center });
        layout.Children.Add(new Label { Text = LocalizationService.Translate("MSG_CANCELANDO_AUTO"), FontSize = 12, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center, FontAttributes = FontAttributes.Italic });

        var btnGrid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Star } }, ColumnSpacing = 10, Margin = new Thickness(0, 10, 0, 0) };

        var btnCancelar = new Button { Text = LocalizationService.Translate("BTN_CONTINUAR"), BackgroundColor = Colors.LightGray, TextColor = Colors.Black, CornerRadius = 10 };
        btnCancelar.Clicked += (s, e) => { tcs.TrySetResult(false); };

        var btnSalir = new Button { Text = LocalizationService.Translate("BTN_SI_SALIR"), BackgroundColor = Color.FromArgb("#E53935"), TextColor = Colors.White, CornerRadius = 10 };
        btnSalir.Clicked += (s, e) => { tcs.TrySetResult(true); };

        btnGrid.Children.Add(btnCancelar);
        Grid.SetColumn(btnCancelar, 0);
        btnGrid.Children.Add(btnSalir);
        Grid.SetColumn(btnSalir, 1);

        layout.Children.Add(btnGrid);
        tarjeta.Content = layout;
        overlay.Children.Add(tarjeta);

        rootGrid.Children.Add(overlay);
        await overlay.FadeTo(1, 200);

        _ = Task.Run(async () => {
            try
            {
                await Task.Delay(5000, cts.Token);
                if (!tcs.Task.IsCompleted) tcs.TrySetResult(false);
            }
            catch { }
        });

        bool resultado = await tcs.Task;
        cts.Cancel();

        await overlay.FadeTo(0, 200);
        rootGrid.Children.Remove(overlay);

        return resultado;
    }

    private async void OnAtrasSinGuardarClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;
        try { if (sender is VisualElement border) await AnimarBoton(border); await VolverAtrasSinGuardar(); }
        finally { _bloqueoAccion = false; }
    }

    private async Task VolverAtrasSinGuardar()
    {
        // El paso actual ya se guarda en cada avance/retroceso (GuardarPasoActual,
        // llamado desde OnAnteriorClicked/OnSiguienteClicked/PausarSecuencia), así
        // que salir aquí ya no descarta nada: el auditor puede volver más tarde a
        // esta fase y seguir justo donde lo dejó. Por eso ya no se pide confirmación
        // ni se llama a LimpiarPasoGuardado() - y, a propósito, NO se llama a
        // DetenerSecuencia() (esa pone _indiceActual a 0 y lo guarda así,
        // pensada para "reiniciar esta fase desde el paso 1", no para salir sin
        // perder el sitio). Aquí solo se para el audio/reconocimiento de voz.
        if (_estadoActual == EstadoApp.Corriendo) PausarSecuencia();
        DetenerEjecucionAudioYVosk();
        _estadoActual = EstadoApp.Pausado;

        await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
        await Navigation.PopAsync();
    }

    private async void OnVolverClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;
        try { if (sender is VisualElement btn) await AnimarBoton(btn); await SalirAlMenuPrincipal(); }
        finally { _bloqueoAccion = false; }
    }

    private async Task SalirAlMenuPrincipal()
    {
        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
        if (estabaCorriendo) PausarSecuencia();

        bool confirmarSalida = await PedirConfirmacionConTimeout(LocalizationService.Translate("TITLE_ABORTAR_AUDITORIA"), LocalizationService.Translate("MSG_ABORTAR_AUDITORIA_CONFIRM"));
        if (!confirmarSalida)
        {
            if (estabaCorriendo) IniciarSecuencia();
            return;
        }

        DetenerSecuencia();
        LimpiarPasoGuardado();
        AutoGuardadoService.BorrarBackup(); // Destruimos el archivo porque aborta la auditoría

        SesionGlobal.ChasisActual = string.Empty;
        SesionGlobal.IndiceEstandarActual = 0;
        SesionGlobal.ModeloSeleccionado = null;
        SesionGlobal.MotorSeleccionado = null;
        if (SesionGlobal.EstandaresCompletados != null) SesionGlobal.EstandaresCompletados.Clear();
        if (SesionGlobal.HistorialRutasGPS != null) SesionGlobal.HistorialRutasGPS.Clear();
        if (SesionGlobal.RutaTrazadaGPS != null) SesionGlobal.RutaTrazadaGPS.Clear();
        if (SesionGlobal.TiemposPorFase != null) SesionGlobal.TiemposPorFase.Clear();

        await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Navigation == null || Navigation.NavigationStack == null)
                {
                    if (Application.Current != null) Application.Current.MainPage = new NavigationPage(new SelectionPage());
                    return;
                }

                var paginas = Navigation.NavigationStack.ToList();
                var targetPage = paginas.FirstOrDefault(p => p != null && p.GetType().Name == "SelectionPage");

                if (targetPage != null)
                {
                    int targetIndex = paginas.IndexOf(targetPage);
                    for (int i = paginas.Count - 2; i > targetIndex; i--)
                    {
                        var pageToRemove = paginas[i];
                        if (pageToRemove != null) Navigation.RemovePage(pageToRemove);
                    }
                    await Navigation.PopAsync();
                }
                else
                {
                    if (Application.Current != null) Application.Current.MainPage = new NavigationPage(new SelectionPage());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al salir: {ex.Message}");
                if (Application.Current != null) Application.Current.MainPage = new NavigationPage(new SelectionPage());
            }
        });
    }

    private async void OnContinuarClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;
        try
        {
            if (sender is VisualElement border) await AnimarBoton(border);
            if (_estadoActual == EstadoApp.Corriendo)
            {
                bool confirmar = await DisplayAlert(LocalizationService.Translate("ALERT_INSTRUCCIONES_CURSO"), LocalizationService.Translate("ALERT_INSTRUCCIONES_CURSO_MSG"), LocalizationService.Translate("BTN_SI_FINALIZAR"), LocalizationService.Translate("BTN_NO_ESPERAR"));
                if (!confirmar) return;
            }

            DetenerSecuencia();
            LimpiarPasoGuardado(); // Ha terminado la fase, borramos para que la proxima vez empiece en 0

            if (!SesionGlobal.EstandaresCompletados.Contains(_miIndiceEstandar)) SesionGlobal.EstandaresCompletados.Add(_miIndiceEstandar);

            _ = AutoGuardadoService.GuardarProgresoAsync();

            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
            await Navigation.PopAsync();
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async void OnDocumentosClicked(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) { await btn.ScaleTo(0.9, 50); await btn.ScaleTo(1.0, 50); }
        if (OverlayDocumentos != null)
        {
            OverlayDocumentos.Opacity = 0;
            OverlayDocumentos.IsVisible = true;
            await OverlayDocumentos.FadeTo(1, 200);
        }
    }

    private async void OnCerrarDocumentosClicked(object sender, EventArgs e)
    {
        if (OverlayDocumentos != null)
        {
            await OverlayDocumentos.FadeTo(0, 200);
            OverlayDocumentos.IsVisible = false;
        }
    }

    private async void OnAyudaClicked(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) { await btn.ScaleTo(0.9, 50); await btn.ScaleTo(1.0, 50); }
        var overlayAyuda = this.FindByName<Grid>("OverlayAyuda");
        if (overlayAyuda != null)
        {
            overlayAyuda.Opacity = 0;
            overlayAyuda.IsVisible = true;
            await overlayAyuda.FadeTo(1, 200);
        }
    }

    private async void OnCerrarAyudaClicked(object sender, EventArgs e)
    {
        var overlayAyuda = this.FindByName<Grid>("OverlayAyuda");
        if (overlayAyuda != null)
        {
            await overlayAyuda.FadeTo(0, 200);
            overlayAyuda.IsVisible = false;
        }
    }
}