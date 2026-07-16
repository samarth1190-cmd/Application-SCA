using Aplicacion_SCA.Models;
using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using System.IO;
using System.IO.Compression;
using Vosk;
using Microsoft.Maui.Storage;
using System.Text.Json;

namespace Aplicacion_SCA.Pages;

public partial class RodajeExterior : ContentPage
{
    private string _modeloSeleccionado = string.Empty;
    private string _motorSeleccionado = string.Empty;
    private string _modoOperativo = string.Empty;
    private bool _esFormacion;

    private List<ControlFase>? _pasosRodaje;
    private int _indiceActual = 0;
    private int _indiceVisualActivo = 0;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _gpsCts;

    private enum EstadoApp { Parado, Corriendo, Pausado, Finalizado }
    private EstadoApp _estadoActual = EstadoApp.Parado;

    private string _nombreEstandarActual = "";
    private int _miIndiceEstandar;

    private bool _esperandoLlegadaGPS = false;
    private bool _esperandoValidacionManual = false;
    private bool _pausadoAutomatically = false;
    private int _ultimoGpsCompletado = -1;

    private bool _tieneFocoAudio = false;

    private int _totalPasos = 0;
    private bool _bloqueoAccion = false;

    private Location? _ultimaUbicacionConocida;
    private double _ultimaVelocidadConocida = 0;
    private Microsoft.Maui.Controls.Maps.Map? _mapaInyectado;

    private Vosk.Model? _voskModel;
    private VoskRecognizer? _rec;
    private bool _modeloCargado = false;
    private IAudioCaptureService? _audioService;

    private List<string> _listaPdfsDisponibles = new List<string>();
    private static string _carpetaManualesPdf => PlantContext.ResolvePath("03_Documentos_pdf");

    private string ClaveGuardadoPaso => $"PasoGuardadoRodaje_{SesionGlobal.ChasisActual ?? "NA"}_{_miIndiceEstandar}";

#if ANDROID
    private Android.Media.ToneGenerator? _toneGen;
#endif

    public RodajeExterior(string modelo, string motor, string modo)
    {
        InitializeComponent();
        _modeloSeleccionado = modelo;
        _motorSeleccionado = motor;
        _modoOperativo = modo;
        _esFormacion = _modoOperativo.Contains("Formacion", StringComparison.OrdinalIgnoreCase);
        _miIndiceEstandar = SesionGlobal.IndiceEstandarActual;

#if ANDROID
        _audioService = new Aplicacion_SCA.Platforms.Android.AudioCaptureService();
#endif

        LblModeloText.Text = _modeloSeleccionado;
        LblMotorText.Text = _motorSeleccionado;

        _ = PrepararYCargarModeloVoskAsync();
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
            Debug.WriteLine("✅ VOSK: Listo en Rodaje Exterior SIN restricciones.");
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
        DeviceDisplay.Current.KeepScreenOn = true;

        if (Application.Current != null && Application.Current.MainPage != null)
        {
            Application.Current.MainPage.Window.Deactivated += OnWindowDeactivated;
            Application.Current.MainPage.Window.Activated += OnWindowActivated;
        }

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
        catch { }

        SesionGlobal.IndiceEstandarActual = _miIndiceEstandar;
        if (SesionGlobal.RutaTrazadaGPS == null) SesionGlobal.RutaTrazadaGPS = new List<Location>();

        if (SesionGlobal.UsuarioActivo != null)
            LblUsuarioNombre.Text = $"{SesionGlobal.UsuarioActivo.NOMBRE} {SesionGlobal.UsuarioActivo.APELLIDOS}";

        LblChasisText.Text = !string.IsNullOrEmpty(SesionGlobal.ChasisActual) ? $"VIN: {SesionGlobal.ChasisActual}" : "VIN: No registrado";

        _indiceActual = Preferences.Get(ClaveGuardadoPaso, 0);
        if (_indiceActual > 0)
        {
            _estadoActual = EstadoApp.Pausado;
        }

        ConfigurarMapaParaPlataforma();
        CargarDatosEstandarActual();

#if ANDROID || IOS
        await IniciarRastreoGPS();
#else
        LblCoordenadas.Text = "Rastreo simulado (Windows)";
#endif

        await CargarListaPdfsDesdeSharePointAsync();

        _ = AutoGuardadoService.GuardarProgresoAsync();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_estadoActual == EstadoApp.Corriendo)
        {
            _pausadoAutomatically = true;
            PausarSecuencia();
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_estadoActual == EstadoApp.Pausado && _pausadoAutomatically)
        {
            _pausadoAutomatically = false;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IniciarSecuencia();
            });
        }
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

    private void ConfigurarMapaParaPlataforma()
    {
#if ANDROID || IOS
        try
        {
            _mapaInyectado = new Microsoft.Maui.Controls.Maps.Map
            {
                IsShowingUser = true,
                MapType = MapType.Street
            };

            if (ContenedorMapaGPS.Content is Grid gridInterior)
            {
                gridInterior.Children.Insert(0, _mapaInyectado);
            }

            if (SesionGlobal.RutaTrazadaGPS != null && SesionGlobal.RutaTrazadaGPS.Count > 0)
            {
                var mapSpan = MapSpan.FromCenterAndRadius(SesionGlobal.RutaTrazadaGPS.Last(), Distance.FromMeters(150));
                _mapaInyectado.MoveToRegion(mapSpan);
            }

            LblModoEscritorio.IsVisible = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al crear mapa: {ex.Message}");
        }
#else
        LblModoEscritorio.IsVisible = true;
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        DeviceDisplay.Current.KeepScreenOn = false;

        if (Application.Current != null && Application.Current.MainPage != null)
        {
            Application.Current.MainPage.Window.Deactivated -= OnWindowDeactivated;
            Application.Current.MainPage.Window.Activated -= OnWindowActivated;
        }

        PausarSecuencia();
        _gpsCts?.Cancel();

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
        _nombreEstandarActual = estandarActual.NombreEstandar ?? "RODAJE";
        LblTituloEstandar.Text = _nombreEstandarActual.ToUpper();

        string motorElegido = NormalizarTexto(SesionGlobal.MotorSeleccionado?.ToLower().Trim() ?? "");
        var todosLosPasos = estandarActual.ListaControles?.OrderBy(p => p.NumeroFase).ToList();

        if (todosLosPasos != null)
        {
            _pasosRodaje = todosLosPasos.Where(p =>
            {
                string texto = (_esFormacion ? p.AudioFormacion : p.AudioAuditoria) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(texto)) return false;

                var kw = PlantContext.Current.MotorKeywords;
                bool pasoComun = (p.MotorTermico == 0 && p.MotorHibrido == 0 && p.MotorElectrico == 0);
                bool esParaEsteMotor = pasoComun ||
                                       (kw.Termico.Any(k => motorElegido.Contains(k)) && p.MotorTermico == 1) ||
                                       (kw.Hibrido.Any(k => motorElegido.Contains(k)) && p.MotorHibrido == 1) ||
                                       (kw.Electrico.Any(k => motorElegido.Contains(k)) && p.MotorElectrico == 1);

                bool esParaEstaPista = !SesionGlobal.EsRodajeExterior ? (p.Exterior == 0 || p.Exterior == 1) : (p.Exterior == 0 || p.Exterior == 2);
                bool esTipoRodaje = !string.IsNullOrEmpty(p.TipoPlantilla) && p.TipoPlantilla.ToUpper().Contains("RODAJE");

                return esParaEsteMotor && esParaEstaPista && esTipoRodaje;
            }).ToList();
        }

        if (_pasosRodaje != null)
        {
            for (int i = 0; i < _pasosRodaje.Count; i++) _pasosRodaje[i].NumeroFase = i + 1;
            _totalPasos = _pasosRodaje.Count;

            if (_indiceActual >= _totalPasos) _indiceActual = 0;
        }

        ActualizarListaVisual();
        ActualizarTextosPaso();
        ActualizarProgreso();
    }

    private void ActualizarListaVisual()
    {
        if (_pasosRodaje == null || _pasosRodaje.Count == 0) return;

        var listaVisual = new List<ItemTextoAudioRodaje>();
        _indiceVisualActivo = 0;

        for (int i = 0; i < _pasosRodaje.Count; i++)
        {
            var paso = _pasosRodaje[i];
            string texto = (_esFormacion ? paso.AudioFormacion : paso.AudioAuditoria) ?? string.Empty;

            bool isActivo = (i == _indiceActual);
            if (isActivo) _indiceVisualActivo = i;

            listaVisual.Add(new ItemTextoAudioRodaje
            {
                TextoInstruccion = texto,
                EsActivo = isActivo
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

    private async void OnControlClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;

        try
        {
            if (sender is VisualElement btn) await AnimarBoton(btn);

            switch (_estadoActual)
            {
                case EstadoApp.Parado:
                case EstadoApp.Pausado:
                    _pausadoAutomatically = false;
                    IniciarSecuencia();
                    break;
                case EstadoApp.Corriendo:
                    _pausadoAutomatically = false;
                    PausarSecuencia();
                    break;
                case EstadoApp.Finalizado:
                    ReiniciarAuditoria();
                    break;
            }
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async void OnSaltarPasoClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;

        try
        {
            if (sender is VisualElement btn) await AnimarBoton(btn);

            if (_estadoActual == EstadoApp.Corriendo)
            {
                if (_esperandoLlegadaGPS)
                {
                    _esperandoLlegadaGPS = false;
                    _ultimoGpsCompletado = _indiceActual;
                }
                else
                {
                    _cts?.Cancel();
                    _audioService?.StopRecording();
                    _esperandoValidacionManual = false;

                    MainThread.BeginInvokeOnMainThread(() => {
                        var btnManual = this.FindByName<VisualElement>("BtnValidarPaso");
                        if (btnManual != null) btnManual.IsVisible = false;
                        if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#243782");
                    });

                    Task.Run(async () =>
                    {
                        try
                        {
                            var ctsMute = new CancellationTokenSource();
                            ctsMute.Cancel();
                            await TextToSpeech.Default.SpeakAsync("", null, ctsMute.Token);
                        }
                        catch { }
                    });

                    await Task.Delay(100);

                    if (_pasosRodaje != null)
                    {
                        if (_indiceActual < _pasosRodaje.Count - 1)
                        {
                            _indiceActual++;
                            GuardarPasoActual();
                            IniciarSecuencia();
                        }
                        else
                        {
                            FinalizarAuditoriaCompleta();
                        }
                    }
                }
            }
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async void OnAnteriorPasoClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;

        try
        {
            if (sender is VisualElement btn) await AnimarBoton(btn);

            if (_estadoActual == EstadoApp.Corriendo)
            {
                _cts?.Cancel();
                _audioService?.StopRecording();
                _esperandoValidacionManual = false;
                _esperandoLlegadaGPS = false;

                MainThread.BeginInvokeOnMainThread(() => {
                    var btnManual = this.FindByName<VisualElement>("BtnValidarPaso");
                    if (btnManual != null) btnManual.IsVisible = false;
                    if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#243782");
                });

                Task.Run(async () =>
                {
                    try
                    {
                        var ctsMute = new CancellationTokenSource();
                        ctsMute.Cancel();
                        await TextToSpeech.Default.SpeakAsync("", null, ctsMute.Token);
                    }
                    catch { }
                });

                await Task.Delay(100);

                if (_pasosRodaje != null)
                {
                    if (_indiceActual > 0)
                    {
                        _indiceActual--;
                        GuardarPasoActual();
                        IniciarSecuencia();
                    }
                    else
                    {
                        IniciarSecuencia();
                    }
                }
            }
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async void OnContinuarManualClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);
        _esperandoValidacionManual = false;
    }

    private async void IniciarSecuencia()
    {
        if (_pasosRodaje == null || _pasosRodaje.Count == 0 || _indiceActual >= _pasosRodaje.Count) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _estadoActual = EstadoApp.Corriendo;
        ActualizarBotonVisualmente();

        try
        {
            for (int i = _indiceActual; i < _pasosRodaje.Count; i++)
            {
                if (token.IsCancellationRequested || _estadoActual != EstadoApp.Corriendo) break;

                _indiceActual = i;
                GuardarPasoActual();
                _ = AutoGuardadoService.GuardarProgresoAsync();

                ActualizarTextosPaso();
                ActualizarProgreso();

                var paso = _pasosRodaje[i];
                string textoVoz = (_esFormacion ? paso.AudioFormacion : paso.AudioAuditoria) ?? string.Empty;
                string columnaTiempo = (_esFormacion ? paso.TiempoFormacion : paso.TiempoAuditoria) ?? "0";
                if (string.IsNullOrWhiteSpace(columnaTiempo)) columnaTiempo = "0";

                bool esPasoGPS = paso.Latitud != 0 && paso.Longitud != 0;
                bool esManual = columnaTiempo.Contains("MANUAL", StringComparison.OrdinalIgnoreCase);

#if WINDOWS
                esPasoGPS = false; 
#endif

                MainThread.BeginInvokeOnMainThread(() => {
                    var btnManual = this.FindByName<VisualElement>("BtnValidarPaso");
                    if (btnManual != null) btnManual.IsVisible = false;
                    if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#243782");
                    if (LblTiempoActual != null) LblTiempoActual.Text = _indiceActual.ToString();
                });

                if (esPasoGPS && _ultimoGpsCompletado != _indiceActual)
                {
                    ReanudarMusicaFondo();

                    _esperandoLlegadaGPS = true;
                    MainThread.BeginInvokeOnMainThread(() => {
                        if (LblTiempoActual != null) LblTiempoActual.Text = "📍 GPS...";
                    });

                    while (_esperandoLlegadaGPS && !token.IsCancellationRequested)
                    {
                        await Task.Delay(500, token);
                    }

                    if (!token.IsCancellationRequested)
                    {
                        _ultimoGpsCompletado = _indiceActual;
                    }
                }

                MainThread.BeginInvokeOnMainThread(() => {
                    if (LblTiempoActual != null) LblTiempoActual.Text = _indiceActual.ToString();
                });

                SilenciarMusicaFondo();

                if (!string.IsNullOrWhiteSpace(textoVoz) && !token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(300, token);
                        await TextToSpeech.Default.SpeakAsync(textoVoz, null, token);
                    }
                    catch { }
                }

                if (esManual && !token.IsCancellationRequested)
                {
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
                else if (!token.IsCancellationRequested)
                {
                    int tiempoPasoSegundos = int.TryParse(columnaTiempo.Trim(), out int t) ? t : 0;
                    int tiempoPasoMilisegundos = tiempoPasoSegundos * 1000;

                    if (tiempoPasoMilisegundos > 0)
                    {
                        ReanudarMusicaFondo();
                        Stopwatch sw = Stopwatch.StartNew();
                        while (!token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
                        {
                            if ((int)sw.ElapsedMilliseconds >= tiempoPasoMilisegundos) break;
                            await Task.Delay(100, token);
                        }
                        sw.Stop();
                    }
                }

                await Task.Delay(100, token);
            }

            if (!token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo) FinalizarAuditoriaCompleta();
        }
        catch (OperationCanceledException) { }
        finally
        {
            _audioService?.StopRecording();
            ReanudarMusicaFondo();
        }
    }

    private async Task<string> EjecutarValidacionManual(CancellationToken token)
    {
        string comandoDetectado = "";
        _esperandoValidacionManual = true;

        if (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(200, token);
                await TextToSpeech.Default.SpeakAsync(PlantContext.Current.TtsWaitPhrase, null, token);
            }
            catch { }
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var btnManual = this.FindByName<VisualElement>("BtnValidarPaso");
            if (btnManual != null) btnManual.IsVisible = true;
            if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#63F86D");
            if (LblTiempoActual != null) LblTiempoActual.Text = "🎙️ ESCUCHANDO...";
        });

        var isGranted = await CheckSpeechPermissionsAsync();

        if (isGranted && _modeloCargado && _rec != null && _audioService != null)
        {
            _rec.Reset();

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
            }, isEnRodaje: true);

            while (_esperandoValidacionManual && !token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
            {
                await Task.Delay(50, token);
            }

            _audioService.StopRecording();

            if (!string.IsNullOrEmpty(comandoDetectado) && comandoDetectado != "pausa" && comandoDetectado != "repite" && comandoDetectado != "atras")
            {
                MainThread.BeginInvokeOnMainThread(() => {
                    if (LblTiempoActual != null) LblTiempoActual.Text = "✅ ¡OÍDO!";
                });

                ReproducirBeep(esInicio: false);
                await Task.Delay(500, token);
            }
        }
        else
        {
            while (_esperandoValidacionManual && !token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
            {
                await Task.Delay(200, token);
            }
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var btnManual = this.FindByName<VisualElement>("BtnValidarPaso");
            if (btnManual != null) btnManual.IsVisible = false;
            if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#243782");
            if (LblTiempoActual != null) LblTiempoActual.Text = _indiceActual.ToString();
        });

        return comandoDetectado;
    }

    private void procesarResultadoVosk(string json, ref string comando, bool esParcial)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string keyToFind = esParcial ? "partial" : "text";

            if (!root.TryGetProperty(keyToFind, out JsonElement textElement)) return;

            string texto = NormalizarTexto(textElement.GetString()?.ToLower().Trim() ?? "");
            texto = new string(texto.Where(c => char.IsLetter(c) || char.IsWhiteSpace(c)).ToArray()).Trim();

            if (string.IsNullOrWhiteSpace(texto)) return;

            Debug.WriteLine($"📝 EVALUANDO RODAJE [Parcial={esParcial}]: '{texto}'");

            foreach (var (cmd, palabras) in PlantContext.Current.VoiceCommands)
            {
                if (palabras.Any(p => texto.Contains(p)))
                {
                    comando = cmd;
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
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
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
            _cts?.Cancel();
            _audioService?.StopRecording();
            ReanudarMusicaFondo();
            _estadoActual = EstadoApp.Pausado;
            ActualizarBotonVisualmente();

            _esperandoValidacionManual = false;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var btnManual = this.FindByName<VisualElement>("BtnValidarPaso");
                if (btnManual != null) btnManual.IsVisible = false;

                if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#243782");
                if (LblTiempoActual != null) LblTiempoActual.Text = _indiceActual.ToString();
            });

            _ = AutoGuardadoService.GuardarProgresoAsync();
        }
    }

    private void DetenerSecuencia()
    {
        _cts?.Cancel();
        _audioService?.StopRecording();
        ReanudarMusicaFondo();
        _indiceActual = 0;
        GuardarPasoActual();

        _esperandoLlegadaGPS = false;
        _esperandoValidacionManual = false;
        _estadoActual = EstadoApp.Parado;

        _ultimoGpsCompletado = -1;

        ActualizarBotonVisualmente();

        MainThread.BeginInvokeOnMainThread(() => {
            var btnManual = this.FindByName<VisualElement>("BtnValidarPaso");
            if (btnManual != null) btnManual.IsVisible = false;
            if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#243782");
            ActualizarProgreso();
        });
    }

    private void ReiniciarAuditoria()
    {
        DetenerSecuencia();
        ActualizarTextosPaso();
        ActualizarProgreso();
        IniciarSecuencia();
    }

    private void FinalizarAuditoriaCompleta()
    {
        _estadoActual = EstadoApp.Finalizado;
        _esperandoLlegadaGPS = false;
        ReanudarMusicaFondo();
        ActualizarProgreso();
        ActualizarBotonVisualmente();

        LimpiarPasoGuardado();
        _ = AutoGuardadoService.GuardarProgresoAsync();
    }

    private void ActualizarTextosPaso()
    {
        MainThread.BeginInvokeOnMainThread(async () => {
            if (_pasosRodaje != null && _indiceActual < _pasosRodaje.Count)
            {
                LblPasoActual.Text = $"Paso {_indiceActual + 1} de {_pasosRodaje.Count}";
                ActualizarListaVisual();

                await Task.Delay(100);
                if (ContenedorTextosAudio.Children.Count > _indiceVisualActivo)
                {
                    if (ContenedorTextosAudio.Children[_indiceVisualActivo] is VisualElement tarjetaActiva && ScrollInstrucciones != null)
                    {
                        await ScrollInstrucciones.ScrollToAsync(tarjetaActiva, ScrollToPosition.Center, true);
                    }
                }
            }
        });
    }

    private void ActualizarProgreso()
    {
        MainThread.BeginInvokeOnMainThread(async () => {
            if (_totalPasos > 0 && BarraProgreso != null)
            {
                double progreso = (double)_indiceActual / _totalPasos;
                if (_estadoActual == EstadoApp.Finalizado) progreso = 1.0;

                BarraProgreso.ProgressColor = Color.FromArgb("#243782");
                await BarraProgreso.ProgressTo(progreso, 250, Easing.Linear);
            }

            if (LblTiempoActual != null) LblTiempoActual.Text = _indiceActual.ToString();
            if (LblTiempoTotal != null) LblTiempoTotal.Text = _totalPasos.ToString();
        });
    }

    private void ActualizarBotonVisualmente()
    {
        MainThread.BeginInvokeOnMainThread(() => {
            var btnAnt = this.FindByName<VisualElement>("BtnAnteriorPaso");
            switch (_estadoActual)
            {
                case EstadoApp.Corriendo:
                    LblIconoBoton.Text = "PAUSAR RUTA";
                    if (BtnSaltarPaso != null) BtnSaltarPaso.IsVisible = true;
                    if (btnAnt != null) btnAnt.IsVisible = true;
                    break;
                case EstadoApp.Pausado:
                    LblIconoBoton.Text = "▶ REANUDAR";
                    if (BtnSaltarPaso != null) BtnSaltarPaso.IsVisible = false;
                    if (btnAnt != null) btnAnt.IsVisible = false;
                    break;
                case EstadoApp.Finalizado:
                    LblIconoBoton.Text = "↻ REINICIAR";
                    if (BtnSaltarPaso != null) BtnSaltarPaso.IsVisible = false;
                    if (btnAnt != null) btnAnt.IsVisible = false;
                    break;
                default:
                    LblIconoBoton.Text = "▶ COMENZAR";
                    if (BtnSaltarPaso != null) BtnSaltarPaso.IsVisible = false;
                    if (btnAnt != null) btnAnt.IsVisible = false;
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

    private void OnFondoTapped(object sender, TappedEventArgs e)
    {
        OcultarTeclado();
    }

    private async void OnMostrarInstruccionesClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);
        OverlayInstrucciones.IsVisible = true;

        await Task.Delay(100);
        if (ContenedorTextosAudio.Children.Count > _indiceVisualActivo)
        {
            if (ContenedorTextosAudio.Children[_indiceVisualActivo] is VisualElement tarjetaActiva && ScrollInstrucciones != null)
            {
                await ScrollInstrucciones.ScrollToAsync(tarjetaActiva, ScrollToPosition.Center, false);
            }
        }
    }

    private void OnCerrarInstruccionesClicked(object sender, TappedEventArgs e)
    {
        OverlayInstrucciones.IsVisible = false;
    }

    protected override bool OnBackButtonPressed()
    {
        if (OverlayDocumentos != null && OverlayDocumentos.IsVisible)
        {
            OnCerrarDocumentosClicked(this, new TappedEventArgs(null));
            return true;
        }

        if (OverlayInstrucciones != null && OverlayInstrucciones.IsVisible)
        {
            OverlayInstrucciones.IsVisible = false;
            return true;
        }

        _ = SalirAlMenuEstandares();
        return true;
    }

    private async void OnVolverClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;

        try
        {
            if (sender is VisualElement btn) await AnimarBoton(btn);
            await SalirAlMenuPrincipal();
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async void OnAtrasSinGuardarClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;

        try
        {
            if (sender is VisualElement border) await AnimarBoton(border);
            await SalirAlMenuEstandares();
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async Task SalirAlMenuEstandares()
    {
        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
        if (estabaCorriendo) PausarSecuencia();

        if (_estadoActual != EstadoApp.Parado || _indiceActual > 0)
        {
            bool confirmarSalida = await PedirConfirmacionConTimeout("Cancelar ruta", "¿Deseas volver atrás? Se perderá el progreso de ESTA ruta GPS.", "Sí, volver", "Cancelar");
            if (!confirmarSalida)
            {
                if (estabaCorriendo) IniciarSecuencia();
                return;
            }
        }

        DetenerSecuencia();
        LimpiarPasoGuardado();
        AutoGuardadoService.BorrarBackup();

        SesionGlobal.RutaTrazadaGPS?.Clear();

        var animaciones = new List<Task> { ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn) };
        if (this.FindByName<VisualElement>("ContenedorBotonesFlotantes") != null)
            animaciones.Add(this.FindByName<VisualElement>("ContenedorBotonesFlotantes").FadeTo(0, 300, Easing.CubicIn));
        await Task.WhenAll(animaciones);

        await Navigation.PopAsync();
    }

    private async Task SalirAlMenuPrincipal()
    {
        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
        if (estabaCorriendo) PausarSecuencia();

        bool confirmarSalida = await PedirConfirmacionConTimeout("Abortar Auditoría", "¿Estás seguro de que deseas salir? Todo el progreso de TODOS los estándares y el chasis se perderán.", "Sí, abortar", "Cancelar");

        if (!confirmarSalida)
        {
            if (estabaCorriendo) IniciarSecuencia();
            return;
        }

        DetenerSecuencia();
        LimpiarPasoGuardado();
        AutoGuardadoService.BorrarBackup();

        SesionGlobal.ChasisActual = string.Empty;
        SesionGlobal.IndiceEstandarActual = 0;
        if (SesionGlobal.EstandaresCompletados != null) SesionGlobal.EstandaresCompletados.Clear();
        if (SesionGlobal.HistorialRutasGPS != null) SesionGlobal.HistorialRutasGPS.Clear();
        if (SesionGlobal.RutaTrazadaGPS != null) SesionGlobal.RutaTrazadaGPS.Clear();

        var animaciones = new List<Task> { ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn) };
        if (this.FindByName<VisualElement>("ContenedorBotonesFlotantes") != null)
            animaciones.Add(this.FindByName<VisualElement>("ContenedorBotonesFlotantes").FadeTo(0, 300, Easing.CubicIn));
        await Task.WhenAll(animaciones);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Navigation == null || Navigation.NavigationStack == null)
                {
                    if (Application.Current != null)
                        Application.Current.MainPage = new NavigationPage(new SelectionPage());
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
                    if (Application.Current != null)
                        Application.Current.MainPage = new NavigationPage(new SelectionPage());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al salir: {ex.Message}");
                if (Application.Current != null)
                    Application.Current.MainPage = new NavigationPage(new SelectionPage());
            }
        });
    }

    private async void OnContinuarRodajeClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoAccion) return;
        _bloqueoAccion = true;

        try
        {
            if (sender is Border border) await AnimarBoton(border);

            bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
            if (estabaCorriendo) PausarSecuencia();

            if (estabaCorriendo)
            {
                bool confirmar = await PedirConfirmacionConTimeout(_nombreEstandarActual.ToUpper(), "¿Deseas salir antes de que termine?", "Sí, continuar", "No, esperar");
                if (!confirmar)
                {
                    IniciarSecuencia();
                    return;
                }
            }

            DetenerSecuencia();
            LimpiarPasoGuardado();

            if (!SesionGlobal.EstandaresCompletados.Contains(_miIndiceEstandar))
            {
                SesionGlobal.EstandaresCompletados.Add(_miIndiceEstandar);
            }

            if (SesionGlobal.RutaTrazadaGPS != null && SesionGlobal.RutaTrazadaGPS.Any())
            {
                if (SesionGlobal.HistorialRutasGPS == null) SesionGlobal.HistorialRutasGPS = new();

                var rutaPrevia = SesionGlobal.HistorialRutasGPS.FirstOrDefault(r => r.NombreFase == _nombreEstandarActual);
                if (rutaPrevia != null)
                {
                    SesionGlobal.HistorialRutasGPS.Remove(rutaPrevia);
                }

                SesionGlobal.HistorialRutasGPS.Add(new DatosRutaGuardada
                {
                    NombreFase = _nombreEstandarActual,
                    VelocidadMaxima = SesionGlobal.VelocidadMaximaRodaje,
                    Ruta = new List<Location>(SesionGlobal.RutaTrazadaGPS)
                });
            }

            _ = AutoGuardadoService.GuardarProgresoAsync();

            var animaciones = new List<Task> { ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn) };
            if (this.FindByName<VisualElement>("ContenedorBotonesFlotantes") != null)
                animaciones.Add(this.FindByName<VisualElement>("ContenedorBotonesFlotantes").FadeTo(0, 300, Easing.CubicIn));
            await Task.WhenAll(animaciones);

            await Navigation.PopAsync();
        }
        finally
        {
            _bloqueoAccion = false;
        }
    }

    private async Task<bool> PedirConfirmacionConTimeout(string titulo, string mensaje, string txtAceptar = "Sí, Salir", string txtCancelar = "Continuar")
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

        var btnCancelar = new Button { Text = txtCancelar, BackgroundColor = Colors.LightGray, TextColor = Colors.Black, CornerRadius = 10 };
        btnCancelar.Clicked += (s, e) => { tcs.TrySetResult(false); };

        var btnSalir = new Button { Text = txtAceptar, BackgroundColor = Color.FromArgb("#E53935"), TextColor = Colors.White, CornerRadius = 10 };
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

    private async Task CargarListaPdfsDesdeSharePointAsync()
    {
        try
        {
            var sp = new SharePointService();
            _listaPdfsDisponibles = await sp.ObtenerPdfsEnCarpetaAsync(_carpetaManualesPdf);
            GenerarListaPdfsPanel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error cargando PDFs desde SharePoint: {ex.Message}");
        }
    }

    private void GenerarListaPdfsPanel()
    {
        if (ContenedorListaPdfs == null) return;
        ContenedorListaPdfs.Children.Clear();

        if (_listaPdfsDisponibles == null || !_listaPdfsDisponibles.Any())
        {
            ContenedorListaPdfs.Children.Add(new Label { Text = "No hay manuales en la nube", TextColor = Colors.LightGray, HorizontalOptions = LayoutOptions.Center });
            return;
        }

        foreach (var archivoPdf in _listaPdfsDisponibles)
        {
            string nombreLimpio = "📄 " + archivoPdf.Replace(".pdf", "").Replace("_", " ").ToUpper();

            Label lblTexto = new Label
            {
                Text = nombreLimpio,
                TextColor = Colors.White,
                FontSize = 12,
                CharacterSpacing = 1,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 3
            };

            Grid contenidoLayout = new Grid
            {
                Padding = new Thickness(15, 10),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center
            };
            contenidoLayout.Children.Add(lblTexto);

            LinearGradientBrush gradienteBorde = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            gradienteBorde.GradientStops.Add(new GradientStop(Color.FromArgb("#243782"), 0.0f));
            gradienteBorde.GradientStops.Add(new GradientStop(Color.FromArgb("#43AAA0"), 1.0f));

            Border btnPdf = new Border
            {
                MinimumHeightRequest = 55,
                BackgroundColor = Color.FromArgb("#4F7F9E"),
                Stroke = gradienteBorde,
                StrokeThickness = 2,
                Margin = new Thickness(0, 0, 0, 10),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(27.5) },
                Content = contenidoLayout
            };

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
                finally
                {
                    _bloqueoAccion = false;
                }
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
            await DisplayAlert(LocalizationService.Translate("ERR_TITULO"), LocalizationService.Translate("ERR_DESCARGAR_NUBE") + ex.Message, LocalizationService.Translate("BTN_ACEPTAR"));
        }
    }

    private async void OnDocumentosClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn)
        {
            await btn.ScaleTo(0.9, 50);
            await btn.ScaleTo(1.0, 50);
        }

        if (OverlayDocumentos != null)
        {
            OverlayDocumentos.Opacity = 0;
            OverlayDocumentos.IsVisible = true;
            await OverlayDocumentos.FadeTo(1, 200);
        }
    }

    private async void OnCerrarDocumentosClicked(object sender, TappedEventArgs e)
    {
        if (OverlayDocumentos != null)
        {
            await OverlayDocumentos.FadeTo(0, 200);
            OverlayDocumentos.IsVisible = false;
        }
    }

#if ANDROID || IOS
    private async Task IniciarRastreoGPS()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
            {
                if (LblCoordenadas != null) LblCoordenadas.Text = "Permiso de GPS denegado.";
                return;
            }

            _gpsCts = new CancellationTokenSource();
            var token = _gpsCts.Token;

            _ = Task.Run(async () =>
            {
                var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(2));

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var location = await Geolocation.Default.GetLocationAsync(request, token);

                        if (location != null && LblCoordenadas != null && LblVelocidad != null)
                        {
                            double velocidadKmh = location.Speed.HasValue ? location.Speed.Value * 3.6 : 0;

                            if (velocidadKmh > SesionGlobal.VelocidadMaximaRodaje)
                                SesionGlobal.VelocidadMaximaRodaje = velocidadKmh;

                            _ultimaUbicacionConocida = location;
                            _ultimaVelocidadConocida = velocidadKmh;

                            bool esBuenaPrecision = !location.Accuracy.HasValue || location.Accuracy.Value <= 35.0;

                            if (esBuenaPrecision)
                            {
                                if (SesionGlobal.RutaTrazadaGPS == null) SesionGlobal.RutaTrazadaGPS = new List<Location>();

                                if (SesionGlobal.RutaTrazadaGPS.Count == 0 ||
                                    Location.CalculateDistance(location, SesionGlobal.RutaTrazadaGPS.Last(), DistanceUnits.Kilometers) > 0.01)
                                {
                                    SesionGlobal.RutaTrazadaGPS.Add(location);
                                }
                            }

                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                LblCoordenadas.Text = $"Lat: {location.Latitude:F5} | Lon: {location.Longitude:F5}";
                                LblVelocidad.Text = $"{Math.Round(velocidadKmh)} km/h";

                                if (_mapaInyectado != null)
                                {
                                    var mapSpan = MapSpan.FromCenterAndRadius(new Location(location.Latitude, location.Longitude), Distance.FromMeters(150));
                                    _mapaInyectado.MoveToRegion(mapSpan);
                                }
                            });

                            if (_estadoActual == EstadoApp.Corriendo && _esperandoLlegadaGPS && _pasosRodaje != null && _indiceActual < _pasosRodaje.Count)
                            {
                                var pasoObjetivo = _pasosRodaje[_indiceActual];
                                if (pasoObjetivo.Latitud != 0 && pasoObjetivo.Longitud != 0)
                                {
                                    var locationObjetivo = new Location(pasoObjetivo.Latitud, pasoObjetivo.Longitud);
                                    double distanciaEnMetros = Location.CalculateDistance(location, locationObjetivo, DistanceUnits.Kilometers) * 1000;

                                    double radioBase = pasoObjetivo.Radio > 0 ? pasoObjetivo.Radio : 40.0;
                                    double compensacionVelocidad = velocidadKmh * 0.5;
                                    double radioTolerancia = radioBase + compensacionVelocidad;

                                    if (distanciaEnMetros <= radioTolerancia)
                                    {
                                        _esperandoLlegadaGPS = false;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception) { }

                    GC.Collect();
                    await Task.Delay(2500, token);
                }
            }, token);
        }
        catch (Exception ex)
        {
            if (LblCoordenadas != null) LblCoordenadas.Text = $"Error GPS: {ex.Message}";
        }
    }
#endif

    private void SilenciarMusicaFondo()
    {
        if (_tieneFocoAudio) return;
        _tieneFocoAudio = true;
#if ANDROID
        RequestAudioFocus();
#endif
    }

    private void ReanudarMusicaFondo()
    {
        if (!_tieneFocoAudio) return;
        _tieneFocoAudio = false;
#if ANDROID
        AbandonAudioFocus();
#endif
    }

#if ANDROID
    private Android.Media.AudioFocusRequestClass? _audioFocusRequest;
    private Android.Media.AudioManager? _audioManager;

    private void RequestAudioFocus()
    {
        try
        {
            _audioManager = (Android.Media.AudioManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.AudioService);
            if (_audioManager != null)
            {
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                {
                    var attributes = new Android.Media.AudioAttributes.Builder()
                        .SetUsage(Android.Media.AudioUsageKind.AssistanceNavigationGuidance)
                        .SetContentType(Android.Media.AudioContentType.Speech)?
                        .Build();

                    _audioFocusRequest = new Android.Media.AudioFocusRequestClass.Builder(Android.Media.AudioFocus.GainTransientMayDuck)
                        .SetAudioAttributes(attributes)
                        .Build();

                    _audioManager.RequestAudioFocus(_audioFocusRequest);
                }
                else
                {
#pragma warning disable CS0618
                    _audioManager.RequestAudioFocus(null, Android.Media.Stream.Music, Android.Media.AudioFocus.GainTransientMayDuck);
#pragma warning restore CS0618
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Error AudioFocus: {ex.Message}"); }
    }

    private void AbandonAudioFocus()
    {
        try
        {
            if (_audioManager != null)
            {
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O && _audioFocusRequest != null)
                {
                    _audioManager.AbandonAudioFocusRequest(_audioFocusRequest);
                }
                else
                {
#pragma warning disable CS0618
                    _audioManager.AbandonAudioFocus(null);
#pragma warning restore CS0618
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Error Abandon AudioFocus: {ex.Message}"); }
    }
#endif

    private string NormalizarTexto(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        return texto.Replace("á", "a").Replace("é", "e").Replace("í", "i")
                    .Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n");
    }
}

public class ItemTextoAudioRodaje
{
    public string TextoInstruccion { get; set; } = string.Empty;
    public bool EsActivo { get; set; }
    public Color ColorBorde => EsActivo ? Color.FromArgb("#CACACA") : Color.FromArgb("#00000000");
    public double GrosorBorde => EsActivo ? 2.0 : 0.0;
    public Color TextoColor => EsActivo ? Colors.Black : Color.FromArgb("#607D8B");
    public double OpacidadSombra => EsActivo ? 0.15 : 0.05;
    public FontAttributes AtributoLetra => EsActivo ? FontAttributes.Bold : FontAttributes.None;
}