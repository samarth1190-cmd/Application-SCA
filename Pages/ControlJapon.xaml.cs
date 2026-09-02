using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using Aplicacion_SCA.Models;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.Maui.Media;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Collections.Generic;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using System.IO;
using System.Diagnostics;
using Vosk;
using System.IO.Compression;
using Microsoft.Maui.Storage;
using System.Text.Json;

namespace Aplicacion_SCA.Pages;

public partial class ControlJapon : ContentPage
{
    private List<ControlFaseJapon>? _pasos;
    private int _indiceActual = 0;
    private CancellationTokenSource? _cts;
    private string _chasis;
    private string _modeloSeleccionado;
    private string _motorSeleccionado;
    private int _tiempoTranscurridoSegundos = 0;
    private int _segundosEnPasoActual = 0;
    private int[]? _tiemposInicioEstandar;
    private bool _esperandoValidacion = false;
    private enum EstadoApp { Parado, Corriendo, Pausado, Finalizado }
    private EstadoApp _estadoActual = EstadoApp.Parado;
    private double _currentScale = 1;
    private double _xOffset = 0;
    private double _yOffset = 0;
    private List<ItemCheckList>? _puntosTT;
    private List<ItemCheckList>? _puntosControl;
    private List<ItemCheckList>? _puntosVisto;
    private List<ItemCheckList>? _puntosOtros;
    private bool _datosCargados = false;

    private bool _estaFinalizando = false;

    private Vosk.Model? _voskModel;
    private VoskRecognizer? _rec;
    private bool _modeloCargado = false;
    private IAudioCaptureService? _audioService;

    private List<string> _listaPdfsDisponibles = new List<string>();
    private static string _rutaAudioJapon => PlantContext.ResolvePath("04_Control_Japon/AudioJapon.xlsx");
    private static string _rutaCheckListJapon => PlantContext.ResolvePath("04_Control_Japon/CheckListJapon.xlsx");
    private static string _carpetaImagenes => PlantContext.ResolvePath("04_Control_Japon/Imagenes");
    private static string _carpetaManualesPdf => PlantContext.ResolvePath("03_Documentos_pdf");

    private string ClaveGuardadoPaso => $"PasoGuardadoJapon_{SesionGlobal.ChasisActual ?? "NA"}";

#if ANDROID
    private Android.Media.ToneGenerator? _toneGen;
#endif

    public ControlJapon(string chasis, string modeloSeleccionado, string motorSeleccionado)
    {
        InitializeComponent();
        _chasis = chasis;
        _modeloSeleccionado = modeloSeleccionado;
        _motorSeleccionado = motorSeleccionado;

#if ANDROID
        _audioService = new Aplicacion_SCA.Platforms.Android.AudioCaptureService();
#endif

        _ = PrepararYCargarModeloVoskAsync();
    }

    private async Task PrepararYCargarModeloVoskAsync()
    {
        try
        {
            string cacheDir = FileSystem.CacheDirectory;
            string modelDestPath = System.IO.Path.Combine(cacheDir, "model_es");

            if (!Directory.Exists(modelDestPath) || !File.Exists(System.IO.Path.Combine(modelDestPath, "am", "final.mdl")))
            {
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
            Debug.WriteLine("✅ VOSK: Motor listo sin restricciones de gramática (Control Japon).");
        }
        catch (Exception ex) { Debug.WriteLine($"Error VOSK: {ex.Message}"); }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        DeviceDisplay.Current.KeepScreenOn = true;

        ConfigurarBarraNativaAndroid();

        LblUsuarioNombre.Text = SesionGlobal.UsuarioActivo != null
            ? $"{SesionGlobal.UsuarioActivo.NOMBRE} {SesionGlobal.UsuarioActivo.APELLIDOS}"
            : "Auditor Desconocido";

        LblModeloText.Text = _modeloSeleccionado;
        LblMotorText.Text = _motorSeleccionado;
        LblChasisText.Text = !string.IsNullOrEmpty(_chasis) ? $"VIN: {_chasis}" : "VIN: No registrado";

        try
        {
            ContenedorTarjeta.Opacity = 0;
            ContenedorTarjeta.TranslationY = 60;
            await Task.Delay(100);
            await Task.WhenAll(
                ContenedorTarjeta.FadeTo(1, 700, Easing.CubicOut),
                ContenedorTarjeta.TranslateTo(0, 0, 700, Easing.CubicOut)
            );

            _indiceActual = Preferences.Get(ClaveGuardadoPaso, 0);

            if (_indiceActual > 0)
            {
                _estadoActual = EstadoApp.Pausado;
            }

            if (!_datosCargados) await CargarDatosEspecificosDesdeNubeAsync();
            else { await CargarDatos(); GenerarListaPdfsPanel(); }

            _ = AutoGuardadoService.GuardarProgresoAsync();
        }
        catch (Exception)
        {
            ContenedorTarjeta.Opacity = 1;
            ContenedorTarjeta.TranslationY = 0;
            if (_datosCargados) { await CargarDatos(); GenerarListaPdfsPanel(); }
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

    private void ConfigurarBarraNativaAndroid()
    {
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
    }

    private async Task CargarDatosEspecificosDesdeNubeAsync()
    {
        try
        {
            var sp = new SharePointService();
            var excelService = new ExcelService();
            string token = await sp.ConseguirTokenSilenciosoAsync();

            var tareaPdfs = sp.ObtenerPdfsEnCarpetaAsync(_carpetaManualesPdf);
            var taskAudio = sp.DescargarExcelConTokenAsync(_rutaAudioJapon, token);

            // Verificamos si ya hemos recuperado el checklist de la RAM (por un cierre)
            if (SesionGlobal.ListaCheckListJapon == null || SesionGlobal.ListaCheckListJapon.Count == 0)
            {
                var taskCheck = sp.DescargarExcelConTokenAsync(_rutaCheckListJapon, token);
                await Task.WhenAll(taskAudio, taskCheck);

                using var s2 = new MemoryStream(taskCheck.Result);
                SesionGlobal.ListaCheckListJapon = excelService.LeerExcelCheckListJapon(s2);
            }
            else
            {
                await taskAudio;
            }

            using var s1 = new MemoryStream(taskAudio.Result);
            SesionGlobal.ListaControlesJapon = excelService.LeerExcelAudioJapon(s1);

            _listaPdfsDisponibles = await tareaPdfs;
            _datosCargados = true;

            await CargarDatos();
            GenerarListaPdfsPanel();
        }
        catch (Exception ex)
        {
            await DisplayAlert(LocalizationService.Translate("ALERT_ERROR_RED"), LocalizationService.Translate("ALERT_JAPON_DESCARGA_MSG") + ex.Message, LocalizationService.Translate("BTN_REINTENTAR"));
        }
    }

    private async Task CargarDatos()
    {
        _pasos = SesionGlobal.ListaControlesJapon?
            .Where(c => c.Tipo != null && c.Tipo.Trim().Equals("General", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (_pasos == null || _pasos.Count == 0)
        {
            LblPasoActual.Text = "Error: Sin datos";
            await DisplayAlert(LocalizationService.Translate("ERR_TITULO"), LocalizationService.Translate("ALERT_JAPON_SIN_PASOS_MSG"), "OK");
        }
        else
        {
            CalularLineaDeTiempo();

            if (_indiceActual >= _pasos.Count) _indiceActual = 0;

            MainThread.BeginInvokeOnMainThread(async () => {
                LblTiempoTotal.IsVisible = true;
                ActualizarUITiempoInterno();

                if (_pasos != null && _pasos.Count > 0)
                {
                    LblPasoActual.Text = $"Paso {_indiceActual + 1} de {_pasos.Count}";
                    double progreso = _estadoActual == EstadoApp.Finalizado ? 1.0 : (double)_indiceActual / _pasos.Count;
                    BarraProgreso.ProgressColor = Color.FromArgb("#243782");
                    await BarraProgreso.ProgressTo(progreso, 250, Easing.Linear);
                }
            });
        }

        string modeloLimpio = _modeloSeleccionado?.Trim() ?? "";
        var alertasVisuales = SesionGlobal.ListaControlesJapon?
            .Where(c => c.Tipo != null && c.Tipo.Trim().Equals("Alerta", StringComparison.OrdinalIgnoreCase))
            .Where(c => {
                if (string.IsNullOrWhiteSpace(c.ModeloVehiculo)) return true;

                var modelosPermitidos = c.ModeloVehiculo.Split(',')
                                         .Select(m => m.Trim())
                                         .ToList();

                return modelosPermitidos.Any(m => m.Equals(modeloLimpio, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        if (alertasVisuales != null && alertasVisuales.Any())
        {
            try
            {
                var sp = new SharePointService();
                string token = await sp.ConseguirTokenSilenciosoAsync();

                foreach (var alerta in alertasVisuales)
                {
                    if (!string.IsNullOrWhiteSpace(alerta.Imagen) && !alerta.Imagen.Contains(FileSystem.CacheDirectory))
                    {
                        string nombreImagen = alerta.Imagen.Trim();
                        string rutaLocal = System.IO.Path.Combine(FileSystem.CacheDirectory, nombreImagen);

                        if (!File.Exists(rutaLocal))
                        {
                            try
                            {
                                byte[] imgBytes = await sp.DescargarExcelConTokenAsync($"{_carpetaImagenes}/{nombreImagen}", token);
                                File.WriteAllBytes(rutaLocal, imgBytes);
                            }
                            catch { }
                        }
                        alerta.Imagen = rutaLocal;
                    }
                }
            }
            catch { }
        }

        MainThread.BeginInvokeOnMainThread(() => { BindableLayout.SetItemsSource(ContenedorAlertas, alertasVisuales); });
        CargarChecklist();
    }

    private void CalularLineaDeTiempo()
    {
        if (_pasos == null) return;
        _tiemposInicioEstandar = new int[_pasos.Count];
        int acumulado = 0;
        for (int i = 0; i < _pasos.Count; i++)
        {
            _tiemposInicioEstandar[i] = acumulado;
            string tStr = _pasos[i].Tiempo ?? "0";
            if (!tStr.Trim().Equals("MANUAL", StringComparison.OrdinalIgnoreCase))
            {
                int t = int.TryParse(tStr, out int val) ? val : 0;
                acumulado += t;
            }
        }
    }

    private void CargarChecklist()
    {
        _puntosTT = SesionGlobal.ListaCheckListJapon?.Where(c => c.FaseCheck != null && c.FaseCheck.Trim().Equals("TT", StringComparison.OrdinalIgnoreCase)).ToList();
        _puntosControl = SesionGlobal.ListaCheckListJapon?.Where(c => c.FaseCheck != null && c.FaseCheck.Trim().Equals("Control", StringComparison.OrdinalIgnoreCase)).ToList();
        _puntosVisto = SesionGlobal.ListaCheckListJapon?.Where(c => c.FaseCheck != null && c.FaseCheck.Trim().StartsWith("Visto", StringComparison.OrdinalIgnoreCase)).ToList();

        string modeloLimpio = _modeloSeleccionado?.Trim() ?? "";

        _puntosOtros = SesionGlobal.ListaCheckListJapon?
            .Where(c => c.FaseCheck != null && c.FaseCheck.Trim().Equals("Especifico", StringComparison.OrdinalIgnoreCase))
            .Where(c => {
                if (string.IsNullOrWhiteSpace(c.ModeloCheck)) return false;

                var modelosCheck = c.ModeloCheck.Split(',')
                                    .Select(m => m.Trim())
                                    .ToList();

                return modelosCheck.Any(m => m.Equals(modeloLimpio, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            BindableLayout.SetItemsSource(SeccionTT, _puntosTT);
            BindableLayout.SetItemsSource(SeccionControl, _puntosControl);
            BindableLayout.SetItemsSource(SeccionVisto, _puntosVisto);
            ContenedorOtros.IsVisible = (_puntosOtros != null && _puntosOtros.Count > 0);
            if (ContenedorOtros.IsVisible) BindableLayout.SetItemsSource(SeccionOtros, _puntosOtros);
        });
    }

    private async Task AnimarBoton(VisualElement? boton)
    {
        if (boton == null) return;
        await boton.ScaleTo(0.9, 50);
        await boton.ScaleTo(1.0, 50);
    }

    private void GenerarListaPdfsPanel()
    {
        if (ContenedorListaPdfs == null) return;
        ContenedorListaPdfs.Children.Clear();

        if (_listaPdfsDisponibles == null || !_listaPdfsDisponibles.Any())
        {
            ContenedorListaPdfs.Children.Add(new Label { Text = "No hay manuales disponibles", TextColor = Colors.LightGray, HorizontalOptions = LayoutOptions.Center });
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

            Grid contenidoLayout = new Grid { Padding = new Thickness(15, 10), HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center };
            contenidoLayout.Children.Add(lblTexto);

            LinearGradientBrush gradienteBorde = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            gradienteBorde.GradientStops.Add(new GradientStop(Color.FromArgb("#243782"), 0.0f));
            gradienteBorde.GradientStops.Add(new GradientStop(Color.FromArgb("#43AAA0"), 1.0f));

            Shadow sombraBoton = new Shadow
            {
                Brush = new SolidColorBrush(Colors.Black),
                Offset = new Point(0, 8),
                Opacity = 0.2f,
                Radius = 10
            };

            Border btnPdf = new Border
            {
                MinimumHeightRequest = 55,
                BackgroundColor = Color.FromArgb("#4F7F9E"),
                Stroke = gradienteBorde,
                StrokeThickness = 2,
                Margin = new Thickness(0, 0, 0, 10),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(27.5) },
                Shadow = sombraBoton,
                Content = contenidoLayout
            };

            TapGestureRecognizer tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) =>
            {
                await AnimarBoton(btnPdf);
                await AbrirPdfDesdeSharePoint(archivoPdf);
            };
            btnPdf.GestureRecognizers.Add(tapGesture);

            ContenedorListaPdfs.Children.Add(btnPdf);
        }
    }

    private async Task AbrirPdfDesdeSharePoint(string nombreArchivo)
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
            await DisplayAlert(LocalizationService.Translate("ERR_TITULO"), LocalizationService.Translate("ERR_DESCARGA_MANUAL") + ex.Message, LocalizationService.Translate("BTN_ACEPTAR"));
        }
    }

    private void DetenerEjecucionAudioYVosk()
    {
        _esperandoValidacion = false;
        _cts?.Cancel();
        _audioService?.StopRecording();

        MainThread.BeginInvokeOnMainThread(() => {
            if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#243782");
            if (LblTiempoActual != null) LblTiempoActual.Text = _indiceActual.ToString();
        });
    }

    private void AvanzarPasoSiguiente()
    {
        if (_estadoActual == EstadoApp.Parado) return;

        if (_pasos == null || _pasos.Count == 0) return;

        if (_indiceActual >= _pasos.Count - 1)
        {
            FinalizarAuditoriaCompleta();
            return;
        }

        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;

        DetenerEjecucionAudioYVosk();
        _estaFinalizando = false;

        Task.Run(async () =>
        {
            try { var ctsMute = new CancellationTokenSource(); ctsMute.Cancel(); await TextToSpeech.Default.SpeakAsync("", null, ctsMute.Token); } catch { }
        });

        _indiceActual++;
        GuardarPasoActual(); 

        _segundosEnPasoActual = 0;

        if (_tiemposInicioEstandar != null)
            _tiempoTranscurridoSegundos = _tiemposInicioEstandar[_indiceActual];

        MainThread.BeginInvokeOnMainThread(async () => {
            ActualizarUITiempoInterno();

            if (_pasos != null && _pasos.Count > 0)
            {
                LblPasoActual.Text = $"Paso {_indiceActual + 1} de {_pasos.Count}";
                double progreso = _estadoActual == EstadoApp.Finalizado ? 1.0 : (double)_indiceActual / _pasos.Count;
                await BarraProgreso.ProgressTo(progreso, 250, Easing.Linear);
            }

            if (_estadoActual == EstadoApp.Finalizado)
            {
                _estadoActual = EstadoApp.Pausado;
                ActualizarBotonVisualmenteInterno();
            }
            else if (estabaCorriendo)
            {
                IniciarSecuencia();
            }
        });
    }

    private async void OnAnteriorClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);

        if (_estadoActual == EstadoApp.Parado) return;

        if (_pasos == null || _pasos.Count == 0) return;

        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
        bool estabaFinalizado = _estadoActual == EstadoApp.Finalizado;

        DetenerEjecucionAudioYVosk();
        _estaFinalizando = false;

        Task.Run(async () =>
        {
            try { var ctsMute = new CancellationTokenSource(); ctsMute.Cancel(); await TextToSpeech.Default.SpeakAsync("", null, ctsMute.Token); } catch { }
        });

        if (_indiceActual > 0)
        {
            _indiceActual--;
            GuardarPasoActual(); 

            _segundosEnPasoActual = 0;
            if (_tiemposInicioEstandar != null)
                _tiempoTranscurridoSegundos = _tiemposInicioEstandar[_indiceActual];
        }

        MainThread.BeginInvokeOnMainThread(async () => {
            ActualizarUITiempoInterno();

            if (_pasos != null && _pasos.Count > 0)
            {
                LblPasoActual.Text = $"Paso {_indiceActual + 1} de {_pasos.Count}";
                double progreso = _estadoActual == EstadoApp.Finalizado ? 1.0 : (double)_indiceActual / _pasos.Count;
                await BarraProgreso.ProgressTo(progreso, 250, Easing.Linear);
            }

            if (estabaFinalizado)
            {
                _estadoActual = EstadoApp.Pausado;
                ActualizarBotonVisualmenteInterno();
            }
            else if (estabaCorriendo)
            {
                IniciarSecuencia();
            }
        });
    }

    private async void OnSiguienteClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);
        if (_estadoActual == EstadoApp.Parado) return;
        AvanzarPasoSiguiente();
    }

    private void FinalizarAuditoriaCompleta()
    {
        if (_estaFinalizando || _estadoActual == EstadoApp.Finalizado) return;

        _estaFinalizando = true;
        _estadoActual = EstadoApp.Finalizado;
        _esperandoValidacion = false;

        LimpiarPasoGuardado(); 
        _ = AutoGuardadoService.GuardarProgresoAsync();

        MainThread.BeginInvokeOnMainThread(async () => {
            ActualizarBotonVisualmenteInterno();

            if (_pasos != null && _pasos.Count > 0)
            {
                LblPasoActual.Text = $"Paso {_indiceActual + 1} de {_pasos.Count}";
                BarraProgreso.ProgressColor = Color.FromArgb("#243782");
                await BarraProgreso.ProgressTo(1.0, 250, Easing.Linear);
            }
        });
    }

    private async void OnControlClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);
        switch (_estadoActual)
        {
            case EstadoApp.Parado:
            case EstadoApp.Pausado: IniciarSecuencia(); break;
            case EstadoApp.Corriendo: PausarSecuencia(); break;
            case EstadoApp.Finalizado: ReiniciarAuditoria(); break;
        }
    }

    private async void IniciarSecuencia()
    {
        if (_pasos == null || _pasos.Count == 0) return;
        DetenerEjecucionAudioYVosk();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _estadoActual = EstadoApp.Corriendo;
        _estaFinalizando = false;
        ActualizarBotonVisualmente();

        try
        {
            for (int i = _indiceActual; i < _pasos.Count; i++)
            {
                if (token.IsCancellationRequested || _estadoActual != EstadoApp.Corriendo) break;

                _indiceActual = i;
                GuardarPasoActual(); 
                _ = AutoGuardadoService.GuardarProgresoAsync(); 

                var paso = _pasos[i];
                ActualizarUIProgreso();
                string textoTiempo = paso.Tiempo ?? string.Empty;
                bool esManual = textoTiempo.Trim().Equals("MANUAL", StringComparison.OrdinalIgnoreCase);
                int tiempoPaso = int.TryParse(textoTiempo, out int t) ? t : 0;
                _esperandoValidacion = esManual;

                Task audioTask = Task.CompletedTask;
                if (!string.IsNullOrWhiteSpace(paso.Audio))
                {
                    await Task.Delay(500, token);
                    var localeVoz = await SpeechLocaleHelper.GetLocaleAsync();
                    audioTask = SpeechLocaleHelper.HablarConPausasAsync(paso.Audio, localeVoz, token);
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
                        if (audioTask.IsCompleted && (int)sw.ElapsedMilliseconds >= (tiempoPaso * 1000)) break;
                        await Task.Delay(100, token);

                        if ((int)sw.ElapsedMilliseconds / 1000 > _segundosEnPasoActual)
                        {
                            _segundosEnPasoActual++;
                            _tiempoTranscurridoSegundos++;
                            ActualizarUITiempo();
                        }
                    }
                    sw.Stop();
                }

                _esperandoValidacion = false;
                _segundosEnPasoActual = 0;
                await Task.Delay(500, token);
            }
            if (!token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo) FinalizarAuditoriaCompleta();
        }
        catch (OperationCanceledException) { }
        finally { DetenerEjecucionAudioYVosk(); }
    }

    private async Task<string> EjecutarValidacionManual(CancellationToken token)
    {
        string comandoDetectado = "";
        _esperandoValidacion = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (LblTiempoActual != null) LblTiempoActual.Text = "🎙️ ESCUCHANDO...";
            if (BarraProgreso != null) BarraProgreso.ProgressColor = Color.FromArgb("#63F86D");
        });

        var isGranted = await CheckSpeechPermissionsAsync();

        if (isGranted && _modeloCargado && _rec != null && _audioService != null)
        {
            _rec.Reset();

            ReproducirBeep(esInicio: true);
            await Task.Delay(700, token);

            _audioService.StartRecording((buffer, length) =>
            {
                if (!_esperandoValidacion || token.IsCancellationRequested || !string.IsNullOrEmpty(comandoDetectado))
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

            while (_esperandoValidacion && !token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
            {
                await Task.Delay(50, token);
            }

            _audioService.StopRecording();

            if (!string.IsNullOrEmpty(comandoDetectado) && comandoDetectado != "pausa" && comandoDetectado != "repite" && comandoDetectado != "atras")
            {
                MainThread.BeginInvokeOnMainThread(() => {
                    if (LblTiempoActual != null) LblTiempoActual.Text = "✅ ¡OÍDO!";
                });
                await Task.Delay(100, token);
                ReproducirBeep(esInicio: false);
                await Task.Delay(400, token);
            }
        }
        else
        {
            while (_esperandoValidacion && !token.IsCancellationRequested && _estadoActual == EstadoApp.Corriendo)
            {
                await Task.Delay(200, token);
            }
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
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

            Debug.WriteLine($"📝 EVALUANDO [Parcial={esParcial}]: '{texto}'");

            if (texto.Contains("sigue") || texto.Contains("siguiente") || texto.Contains("continuar"))
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
                _esperandoValidacion = false;
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
        if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.Microphone>();
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
        _tiempoTranscurridoSegundos = 0;
        _segundosEnPasoActual = 0;
        _estadoActual = EstadoApp.Parado;
        _estaFinalizando = false;
        ActualizarBotonVisualmente();
        ActualizarUIProgreso();
        ActualizarUITiempo();
    }

    private void ReiniciarAuditoria()
    {
        DetenerSecuencia();
        IniciarSecuencia();
    }

    private void ActualizarUIProgreso()
    {
        MainThread.BeginInvokeOnMainThread(async () => {
            if (_pasos != null && _pasos.Count > 0)
            {
                LblPasoActual.Text = $"Paso {_indiceActual + 1} de {_pasos.Count}";
                double progreso = _estadoActual == EstadoApp.Finalizado ? 1.0 : (double)_indiceActual / _pasos.Count;
                BarraProgreso.ProgressColor = Color.FromArgb("#243782");
                await BarraProgreso.ProgressTo(progreso, 250, Easing.Linear);
            }
        });
    }

    private void ActualizarUITiempo()
    {
        MainThread.BeginInvokeOnMainThread(() => {
            ActualizarUITiempoInterno();
        });
    }

    private void ActualizarUITiempoInterno()
    {
        int total = _pasos?.Count ?? 0;
        if (LblTiempoActual != null) LblTiempoActual.Text = _esperandoValidacion ? $"{_indiceActual} (ESPERANDO)" : $"{_indiceActual}";
        if (LblTiempoTotal != null) LblTiempoTotal.Text = $"{total}";
    }

    private void ActualizarBotonVisualmente()
    {
        MainThread.BeginInvokeOnMainThread(() => {
            ActualizarBotonVisualmenteInterno();
        });
    }

    private void ActualizarBotonVisualmenteInterno()
    {
        switch (_estadoActual)
        {
            case EstadoApp.Corriendo: LblIconoBoton.Text = "PAUSAR"; LblIconoBoton.FontSize = 13; break;
            case EstadoApp.Pausado: LblIconoBoton.Text = "▶ REANUDAR"; LblIconoBoton.FontSize = 13; break;
            case EstadoApp.Finalizado: LblIconoBoton.Text = "↻ REINICIAR"; LblIconoBoton.FontSize = 13; break;
            default: LblIconoBoton.Text = "▶ COMENZAR"; LblIconoBoton.FontSize = 13; break;
        }
    }

    private async void OnChecklistClicked(object sender, TappedEventArgs e)
    {
        OcultarTeclado();
        if (OverlayChecklist != null)
        {
            OverlayChecklist.Opacity = 0;
            OverlayChecklist.IsVisible = true;
            await OverlayChecklist.FadeTo(1, 200);
        }
    }

    private async void OnCerrarChecklistClicked(object sender, TappedEventArgs e)
    {
        OcultarTeclado();
        if (OverlayChecklist != null)
        {
            await OverlayChecklist.FadeTo(0, 200);
            OverlayChecklist.IsVisible = false;
        }
    }

    private void OnOkClicked(object sender, EventArgs e)
    {
        OcultarTeclado();
        if (sender is Button btnOk && btnOk.BindingContext is ItemCheckList item)
        {
            item.Estado = "OK";
            _ = AutoGuardadoService.GuardarProgresoAsync(); 

            btnOk.Opacity = 1.0;
            if (btnOk.Parent is HorizontalStackLayout contenedor)
            {
                foreach (var child in contenedor.Children)
                {
                    if (child is Button btn && btn != btnOk && btn.Text != "BORRAR")
                    {
                        btn.Opacity = 0.3;
                    }
                }
            }
        }
    }

    private void OnNoClicked(object sender, EventArgs e)
    {
        OcultarTeclado();
        if (sender is Button btnNo && btnNo.BindingContext is ItemCheckList item)
        {
            item.Estado = "NOK";
            _ = AutoGuardadoService.GuardarProgresoAsync(); 

            btnNo.Opacity = 1.0;
            if (btnNo.Parent is HorizontalStackLayout contenedor)
            {
                foreach (var child in contenedor.Children)
                {
                    if (child is Button btn && btn != btnNo && btn.Text != "BORRAR")
                    {
                        btn.Opacity = 0.3;
                    }
                }
            }
        }
    }

    private void OnBorrarClicked(object sender, EventArgs e)
    {
        OcultarTeclado();
        if (sender is Button btnBorrar && btnBorrar.BindingContext is ItemCheckList item)
        {
            item.Estado = string.Empty;
            _ = AutoGuardadoService.GuardarProgresoAsync(); 

            if (btnBorrar.Parent is HorizontalStackLayout contenedor)
            {
                foreach (var child in contenedor.Children)
                {
                    if (child is Button btn && btn.Text != "BORRAR")
                    {
                        btn.Opacity = 1.0;
                    }
                }
            }
        }
    }

    private async void OnGuardarChecklistClicked(object sender, TappedEventArgs e)
    {
        OcultarTeclado();
        _ = AutoGuardadoService.GuardarProgresoAsync(); 

        LblMensajeGuardado.IsVisible = true;
        await Task.Delay(1000);
        LblMensajeGuardado.IsVisible = false;

        if (OverlayChecklist != null)
        {
            await OverlayChecklist.FadeTo(0, 200);
            OverlayChecklist.IsVisible = false;
        }
    }

    private bool FaltanRespuestasEnChecklist()
    {
        if (_puntosTT != null && _puntosTT.Any(p => string.IsNullOrWhiteSpace(p.Estado))) return true;
        if (_puntosVisto != null && _puntosVisto.Any(p => string.IsNullOrWhiteSpace(p.Estado))) return true;
        if (_puntosOtros != null && _puntosOtros.Any(p => string.IsNullOrWhiteSpace(p.Estado))) return true;
        if (_puntosControl != null)
        {
            foreach (var punto in _puntosControl)
            {
                bool esAspecto = punto.Check != null && punto.Check.Contains("Aspecto", StringComparison.OrdinalIgnoreCase);
                if (esAspecto && string.IsNullOrWhiteSpace(punto.Estado)) return true;
            }
        }
        return false;
    }

    private async void OnFinalizarYGenerarClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);

        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
        if (_estadoActual != EstadoApp.Finalizado)
        {
            if (estabaCorriendo) PausarSecuencia();
            await DisplayAlert(LocalizationService.Translate("ALERT_REPRODUCTOR_INCOMPLETO"), LocalizationService.Translate("ALERT_REPRODUCTOR_INCOMPLETO_MSG"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
            if (estabaCorriendo) IniciarSecuencia();
            return;
        }

        if (_esperandoValidacion)
        {
            if (estabaCorriendo) PausarSecuencia();
            await DisplayAlert(LocalizationService.Translate("ALERT_VALIDACION_PENDIENTE"), LocalizationService.Translate("ALERT_VALIDACION_PENDIENTE_MSG"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
            if (estabaCorriendo) IniciarSecuencia();
            return;
        }

        if (FaltanRespuestasEnChecklist())
        {
            if (estabaCorriendo) PausarSecuencia();
            await DisplayAlert(LocalizationService.Translate("ALERT_CHECKLIST_INCOMPLETO"), LocalizationService.Translate("ALERT_CHECKLIST_INCOMPLETO_MSG"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
            if (estabaCorriendo) IniciarSecuencia();
            return;
        }

        DetenerSecuencia();

        SesionGlobal.HoraFinInspeccion = DateTime.Now;
        TimeSpan duracionTotal = SesionGlobal.HoraFinInspeccion - SesionGlobal.HoraInicioInspeccion;
        SesionGlobal.TiempoTotalInspeccionReal = $"{(int)duracionTotal.TotalMinutes}:{duracionTotal.Seconds:D2}";

        _ = AutoGuardadoService.GuardarProgresoAsync(); 

        await Task.WhenAll(
            ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn),
            BtnFinalizarFlotante.FadeTo(0, 300, Easing.CubicIn)
        );

        await Navigation.PushAsync(new ResultsJapon(
            _chasis,
            _modeloSeleccionado,
            _motorSeleccionado,
            _puntosTT ?? new List<ItemCheckList>(),
            _puntosControl ?? new List<ItemCheckList>(),
            _puntosVisto ?? new List<ItemCheckList>(),
            _puntosOtros ?? new List<ItemCheckList>()));
    }

    private async Task SalirAlMenuPrincipal()
    {
        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
        if (estabaCorriendo) PausarSecuencia();
        bool confirmarSalida = await DisplayAlert(LocalizationService.Translate("ALERT_CONFIRMAR_SALIDA"), LocalizationService.Translate("ALERT_SALIR_PROGRESO_TODO_MSG"), LocalizationService.Translate("BTN_SI_SALIR"), LocalizationService.Translate("BTN_CANCELAR"));

        if (!confirmarSalida)
        {
            if (estabaCorriendo) IniciarSecuencia();
            return;
        }

        DetenerSecuencia();
        LimpiarPasoGuardado();
        AutoGuardadoService.BorrarBackup(); 

        await Task.WhenAll(
            ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn),
            BtnFinalizarFlotante.FadeTo(0, 300, Easing.CubicIn)
        );

        SesionGlobal.ChasisActual = string.Empty;
        SesionGlobal.IndiceEstandarActual = 0;
        SesionGlobal.ModeloSeleccionado = string.Empty;
        SesionGlobal.MotorSeleccionado = string.Empty;

        await Navigation.PopAsync();
    }

    private async void OnVolverSelectionClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);

        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
        if (estabaCorriendo) PausarSecuencia();

        bool confirmarSalida = await DisplayAlert(LocalizationService.Translate("ALERT_CONFIRMAR"), LocalizationService.Translate("ALERT_VOLVER_SELECCION_MSG"), LocalizationService.Translate("BTN_SI_VOLVER"), LocalizationService.Translate("BTN_CANCELAR"));

        if (confirmarSalida)
        {
            DetenerSecuencia();
            LimpiarPasoGuardado();
            AutoGuardadoService.BorrarBackup(); 

            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
            await Navigation.PopAsync();
        }
        else if (estabaCorriendo)
        {
            IniciarSecuencia();
        }
    }

    private async void OnSalirAuditModeClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);

        bool estabaCorriendo = _estadoActual == EstadoApp.Corriendo;
        if (estabaCorriendo) PausarSecuencia();

        bool confirmarSalida = await DisplayAlert(LocalizationService.Translate("ALERT_CONFIRMAR_SALIDA"), LocalizationService.Translate("ALERT_SALIR_COMPLETO_MSG"), LocalizationService.Translate("BTN_SI_SALIR"), LocalizationService.Translate("BTN_CANCELAR"));

        if (confirmarSalida)
        {
            DetenerSecuencia();
            LimpiarPasoGuardado();
            AutoGuardadoService.BorrarBackup(); 

            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);

            SesionGlobal.ChasisActual = string.Empty;
            SesionGlobal.ModeloSeleccionado = string.Empty;
            SesionGlobal.MotorSeleccionado = string.Empty;

            var pageToRemove = Navigation.NavigationStack.FirstOrDefault(p => p is SelectionPage);
            if (pageToRemove != null)
            {
                Navigation.RemovePage(pageToRemove);
            }
            await Navigation.PopAsync();
        }
        else if (estabaCorriendo)
        {
            IniciarSecuencia();
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (OverlayDocumentos != null && OverlayDocumentos.IsVisible)
        {
            OnCerrarDocumentosClicked(this, EventArgs.Empty);
            return true;
        }
        if (OverlayChecklist != null && OverlayChecklist.IsVisible)
        {
            OverlayChecklist.IsVisible = false;
            return true;
        }
        if (OverlayImagen != null && OverlayImagen.IsVisible)
        {
            OverlayImagen.IsVisible = false;
            ResetImageTransform();
            return true;
        }

        OnVolverSelectionClicked(null, new TappedEventArgs(null));
        return true;
    }
    private async void OnDocumentosClicked(object? sender, TappedEventArgs e)
    {
        OcultarTeclado();
        if (sender is VisualElement btn) { await btn.ScaleTo(0.9, 50); await btn.ScaleTo(1.0, 50); }
        if (OverlayDocumentos != null) { OverlayDocumentos.Opacity = 0; OverlayDocumentos.IsVisible = true; await OverlayDocumentos.FadeTo(1, 200); }
    }

    private async void OnCerrarDocumentosClicked(object sender, EventArgs e)
    {
        OcultarTeclado();
        if (OverlayDocumentos != null) { await OverlayDocumentos.FadeTo(0, 200); OverlayDocumentos.IsVisible = false; }
    }

    private async void OnImagenAlertaTapped(object sender, TappedEventArgs e)
    {
        if (sender is Image img && img.Source != null)
        {
            ImgAmpliada.Source = img.Source;
            ResetImageTransform();
            OverlayImagen.Opacity = 0;
            OverlayImagen.IsVisible = true;
            await OverlayImagen.FadeTo(1, 200);
        }
    }

    private async void OnCerrarImagenClicked(object sender, TappedEventArgs e)
    {
        await OverlayImagen.FadeTo(0, 200);
        OverlayImagen.IsVisible = false;
        ResetImageTransform();
    }

    private async void OnZoomInClicked(object sender, TappedEventArgs e)
    {
        _currentScale = Math.Clamp(_currentScale + 0.5, 1, 4);
        await ContenedorZoom.ScaleTo(_currentScale, 150, Easing.CubicOut);
    }

    private async void OnZoomOutClicked(object sender, TappedEventArgs e)
    {
        _currentScale = Math.Clamp(_currentScale - 0.5, 1, 4);
        await ContenedorZoom.ScaleTo(_currentScale, 150, Easing.CubicOut);
        if (_currentScale == 1)
        {
            await ContenedorZoom.TranslateTo(0, 0, 150, Easing.CubicOut);
            _xOffset = 0; _yOffset = 0;
        }
    }

    private async void OnDoubleTappedReset(object sender, TappedEventArgs e)
    {
        ResetImageTransform();
        await Task.WhenAll(
            ContenedorZoom.ScaleTo(1, 250, Easing.SpringOut),
            ContenedorZoom.TranslateTo(0, 0, 250, Easing.SpringOut)
        );
    }

    private void ResetImageTransform()
    {
        _currentScale = 1; _xOffset = 0; _yOffset = 0;
        ContenedorZoom.Scale = 1; ContenedorZoom.TranslationX = 0; ContenedorZoom.TranslationY = 0;
    }

    private void OnPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        if (_currentScale <= 1) return;
        switch (e.StatusType)
        {
            case GestureStatus.Running:
                ContenedorZoom.TranslationX = _xOffset + e.TotalX;
                ContenedorZoom.TranslationY = _yOffset + e.TotalY;
                break;
            case GestureStatus.Completed:
                _xOffset = ContenedorZoom.TranslationX;
                _yOffset = ContenedorZoom.TranslationY;
                break;
        }
    }
    private void OcultarTeclado()
    {
        this.Focus();
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

    private void OnFondoTapped(object sender, TappedEventArgs e) => OcultarTeclado();

    private void OnEntryCompleted(object sender, EventArgs e)
    {
        if (sender is Entry entry)
        {
            entry.Unfocus();
#if ANDROID || IOS
            entry.HideSoftInputAsync(System.Threading.CancellationToken.None);
#endif
        }
        OcultarTeclado();
    }

    private string NormalizarTexto(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        return texto.Replace("á", "a").Replace("é", "e").Replace("í", "i")
                    .Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n");
    }
}