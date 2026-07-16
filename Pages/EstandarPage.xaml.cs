using Aplicacion_SCA.Services;
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

    private Vosk.Model? _voskModel;
    private VoskRecognizer? _rec;
    private bool _modeloCargado = false;
    private IAudioCaptureService? _audioService;

    private List<string> _listaPdfsDisponibles = new List<string>();
    private readonly string _carpetaManualesPdf = "02_Datos_App_SCA/03_Documentos_pdf";

    private string ClaveGuardadoPaso => $"PasoGuardado_{SesionGlobal.ChasisActual ?? "NA"}_{_miIndiceEstandar}";

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

        LblModeloText.Text = _modeloAuditoria;
        LblMotorText.Text = _motorAuditoria;

        _ = PrepararYCargarModeloVoskAsync();
        ActualizarBotonVisualmente();
    }

    private async Task PrepararYCargarModeloVoskAsync()
    {
        try
        {
            string cacheDir = FileSystem.CacheDirectory;
            string modelDestPath = System.IO.Path.Combine(cacheDir, "model_es");

            if (!Directory.Exists(modelDestPath) || !File.Exists(System.IO.Path.Combine(modelDestPath, "am", "final.mdl")))
            {
                Debug.WriteLine("⚙️ VOSK: Extrayendo modelo del ZIP...");
                if (!Directory.Exists(modelDestPath)) Directory.CreateDirectory(modelDestPath);

                using var stream = await FileSystem.OpenAppPackageFileAsync("model_es.zip");
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
            SesionGlobal.IndiceEstandarActual = _miIndiceEstandar;
            DeviceDisplay.Current.KeepScreenOn = true;

            if (SesionGlobal.UsuarioActivo != null)
                LblUsuarioNombre.Text = $"{SesionGlobal.UsuarioActivo.NOMBRE} {SesionGlobal.UsuarioActivo.APELLIDOS}";
            else
                LblUsuarioNombre.Text = "Auditor Desconocido";

            LblChasisText.Text = !string.IsNullOrEmpty(SesionGlobal.ChasisActual) ? $"VIN: {SesionGlobal.ChasisActual}" : "VIN: No registrado";

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
        bool esModo13Columnas = _modoOperativo.Contains("CORE_DPV", StringComparison.OrdinalIgnoreCase) ||
                                _modoOperativo.Contains("C-DPV", StringComparison.OrdinalIgnoreCase) ||
                                _modoOperativo.Contains("C_DPV", StringComparison.OrdinalIgnoreCase) ||
                                _modoOperativo.Contains("Formacion", StringComparison.OrdinalIgnoreCase) ||
                                _modoOperativo.Contains("SCA", StringComparison.OrdinalIgnoreCase);

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

        if (todosLosPasos != null)
        {
            string motorElegido = NormalizarTexto(SesionGlobal.MotorSeleccionado?.ToLower().Trim() ?? "");

            _pasosReales = todosLosPasos.Where(p =>
            {
                string texto = (_esFormacion ? p.AudioFormacion : p.AudioAuditoria) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(texto)) return false;

                bool pasoComun = (p.MotorTermico == 0 && p.MotorHibrido == 0 && p.MotorElectrico == 0);
                bool esParaEsteMotor = pasoComun ||
                                       (motorElegido.Contains("termic") && p.MotorTermico == 1) ||
                                       (motorElegido.Contains("hibrid") && p.MotorHibrido == 1) ||
                                       (motorElegido.Contains("electr") && p.MotorElectrico == 1);

                bool esParaEstaPista = !SesionGlobal.EsRodajeExterior
                                       ? (p.Exterior == 0 || p.Exterior == 1)
                                       : (p.Exterior == 0 || p.Exterior == 2);

                bool esTipoEstandar = string.IsNullOrEmpty(p.TipoPlantilla) ||
                                      !p.TipoPlantilla.ToUpper().Contains("RODAJE");

                return esParaEsteMotor && esParaEstaPista && esTipoEstandar;

            }).ToList();
        }

        if (_pasosReales != null)
        {
            for (int i = 0; i < _pasosReales.Count; i++) _pasosReales[i].NumeroFase = i + 1;

            if (_indiceActual >= _pasosReales.Count) _indiceActual = 0;
        }

        ActualizarListaVisual();
        ActualizarTextosPaso();
        ActualizarBarraYTiempo();
    }

    private void ActualizarListaVisual()
    {
        if (_pasosReales == null) return;
        var listaVisual = new List<ItemTextoAudio>();

        for (int i = 0; i < _pasosReales.Count; i++)
        {
            var paso = _pasosReales[i];
            string texto = (_esFormacion ? paso.AudioFormacion : paso.AudioAuditoria) ?? string.Empty;

            listaVisual.Add(new ItemTextoAudio
            {
                TextoInstruccion = texto,
                EsActivo = (i == _indiceActual)
            });
        }
        BindableLayout.SetItemsSource(ContenedorTextosAudio, listaVisual);
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
        _esperandoValidacionManual = false;
    }

    private void DetenerEjecucionAudioYVosk()
    {
        _esperandoValidacionManual = false;
        _cts?.Cancel();
        _audioService?.StopRecording();

        MainThread.BeginInvokeOnMainThread(() => {
            var panelManual = this.FindByName<VisualElement>("PanelValidacionManual");
            if (panelManual != null) panelManual.IsVisible = false;

            var btnManual = this.FindByName<VisualElement>("BtnValidarPaso");
            if (btnManual != null) btnManual.IsVisible = false;

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
                string textoVoz = (_esFormacion ? paso.AudioFormacion : paso.AudioAuditoria) ?? string.Empty;
                string textoTiempo = (_esFormacion ? paso.TiempoFormacion : paso.TiempoAuditoria) ?? "0";

                bool esManual = textoTiempo.Trim().Equals("MANUAL", StringComparison.OrdinalIgnoreCase);
                int tiempoPasoSegundos = int.TryParse(textoTiempo.Trim(), out int t) ? t : 0;
                int tiempoPasoMilisegundos = tiempoPasoSegundos * 1000;

                Task audioTask = Task.CompletedTask;

                if (!string.IsNullOrWhiteSpace(textoVoz))
                {
                    await Task.Delay(500, token);
                    audioTask = TextToSpeech.Default.SpeakAsync(textoVoz, null, token);
                }

                if (esManual)
                {
                    await audioTask;

                    if (token.IsCancellationRequested || _estadoActual != EstadoApp.Corriendo) break;

                    string comandoVoz = "";
                    while (string.IsNullOrEmpty(comandoVoz) && !token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
                    {
                        comandoVoz = await EjecutarValidacionManual(token);
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
                }
                else
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    while (!token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
                    {
                        if (audioTask.IsCompleted && (int)sw.ElapsedMilliseconds >= tiempoPasoMilisegundos) break;
                        await Task.Delay(100, token);
                    }
                    sw.Stop();
                }

                await Task.Delay(100, token);
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

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var btnManual = this.FindByName<VisualElement>("BtnValidarPaso");
            if (btnManual != null) btnManual.IsVisible = true;
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
            while (_esperandoValidacionManual && !token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
            {
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

            string texto = NormalizarTexto(textElement.GetString()?.ToLower().Trim() ?? "");
            texto = new string(texto.Where(c => char.IsLetter(c) || char.IsWhiteSpace(c)).ToArray()).Trim();

            if (string.IsNullOrWhiteSpace(texto)) return;

            Debug.WriteLine($"📝 EVALUANDO [Parcial={esParcial}]: '{texto}'");

            if (texto.Contains("sigue") || texto.Contains("siguiente") || texto.Contains("Continuar"))
            {
                comando = "siguiente";
                Debug.WriteLine("✅ COMANDO CAZADO: Siguiente");
            }
            else if (texto.Contains("atras") || texto.Contains("anterior"))
            {
                comando = "atras";
                Debug.WriteLine("✅ COMANDO CAZADO: Atrás");
            }
            else if (texto.Contains("pausa") || texto.Contains("parar"))
            {
                comando = "pausa";
                Debug.WriteLine("✅ COMANDO CAZADO: Pausa");
            }
            else if (texto.Contains("repetir"))
            {
                comando = "repite";
                Debug.WriteLine("✅ COMANDO CAZADO: Repetir");
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
                LblPasoActual.Text = $"Paso {_indiceActual + 1} de {_pasosReales.Count}";
                ActualizarListaVisual();

                await Task.Delay(150); 
                if (ContenedorTextosAudio.Children.Count > _indiceActual)
                {
                    if (ContenedorTextosAudio.Children[_indiceActual] is VisualElement tarjetaActiva && ScrollInstrucciones != null)
                    {
                        await ScrollInstrucciones.ScrollToAsync(tarjetaActiva, ScrollToPosition.Center, true);
                    }
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
                    LblIconoBoton.Text = "⏸ PAUSAR";
                    if (borderBtn != null) borderBtn.BackgroundColor = Color.FromArgb("#FF9800");
                    break;
                case EstadoApp.Pausado:
                    LblIconoBoton.Text = "▶ REPRODUCIR";
                    if (borderBtn != null) borderBtn.BackgroundColor = Color.FromArgb("#243782");
                    break;
                case EstadoApp.Finalizado:
                    LblIconoBoton.Text = "↻ REINICIAR";
                    if (borderBtn != null) borderBtn.BackgroundColor = Color.FromArgb("#4CAF50");
                    break;
                default:
                    LblIconoBoton.Text = "▶ COMENZAR";
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
        layout.Children.Add(new Label { Text = "(Cancelando automáticamente en 5s...)", FontSize = 12, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center, FontAttributes = FontAttributes.Italic });

        var btnGrid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Star } }, ColumnSpacing = 10, Margin = new Thickness(0, 10, 0, 0) };

        var btnCancelar = new Button { Text = "Continuar", BackgroundColor = Colors.LightGray, TextColor = Colors.Black, CornerRadius = 10 };
        btnCancelar.Clicked += (s, e) => { tcs.TrySetResult(false); };

        var btnSalir = new Button { Text = "Sí, Salir", BackgroundColor = Color.FromArgb("#E53935"), TextColor = Colors.White, CornerRadius = 10 };
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
        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
        if (_estadoActual != EstadoApp.Parado || _indiceActual > 0)
        {
            if (estabaCorriendo) PausarSecuencia();

            bool confirmar = await PedirConfirmacionConTimeout("Cancelar Fase", "¿Deseas volver al menú de estándares? Se perderá el progreso de esta fase.");
            if (!confirmar) { if (estabaCorriendo) IniciarSecuencia(); return; }
        }

        DetenerSecuencia();
        LimpiarPasoGuardado(); 
        AutoGuardadoService.BorrarBackup(); 

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

        bool confirmarSalida = await PedirConfirmacionConTimeout("Abortar Auditoría", "¿Estás seguro de que deseas salir? Todo el progreso se perderá.");
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

    private string NormalizarTexto(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        return texto.Replace("á", "a").Replace("é", "e").Replace("í", "i")
                    .Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n");
    }
}

public class ItemTextoAudio
{
    public string TextoInstruccion { get; set; } = string.Empty;
    public bool EsActivo { get; set; }
    public Color ColorBorde => EsActivo ? Color.FromArgb("#CACACA") : Color.FromArgb("#00000000");
    public double GrosorBorde => EsActivo ? 2.0 : 0.0;
    public Color TextoColor => EsActivo ? Colors.Black : Color.FromArgb("#607D8B");
    public double OpacidadSombra => EsActivo ? 0.15 : 0.05;
    public FontAttributes AtributoLetra => EsActivo ? FontAttributes.Bold : FontAttributes.None;
}