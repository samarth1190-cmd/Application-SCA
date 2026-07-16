using System.Text.Json;
using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using Aplicacion_SCA.Models;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls.Shapes;
using System.IO;
using Microsoft.Maui.Storage;
using System.Linq;

namespace Aplicacion_SCA.Pages;

public partial class RRUPage : ContentPage
{
    private string _modelo;
    private string _motor;
    private string _chasis;

    private List<ItemParadaRRU> _listaParadas = new List<ItemParadaRRU>();
    private int _paradasCompletadas = 0;

    private bool _vieneDeResultados = false;

    private readonly string _rutaArchivoRespaldo = System.IO.Path.Combine(FileSystem.AppDataDirectory, "rru_backup.json");

    private List<string> _listaPdfsDisponibles = new();
    private static string _carpetaManualesPdf => PlantContext.ResolvePath("03_Documentos_pdf");

    private static string _carpetaImagenesRRU => PlantContext.ResolvePath("07_RRU/Imagenes");

    public RRUPage(string modelo, string motor, string chasis)
    {
        InitializeComponent();
        _modelo = modelo;
        _motor = motor;
        _chasis = chasis;

        LblModeloText.Text = _modelo;
        LblMotorText.Text = _motor;
        LblChasisText.Text = $"VIN: {_chasis}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

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

        if (SesionGlobal.ParadasRRU == null)
            SesionGlobal.ParadasRRU = new List<Location>();

        if (SesionGlobal.UsuarioActivo != null)
            LblUsuarioNombre.Text = $"{SesionGlobal.UsuarioActivo.NOMBRE} {SesionGlobal.UsuarioActivo.APELLIDOS}";

        if (!_vieneDeResultados)
        {
            SesionGlobal.ParadasRRU.Clear();
            _paradasCompletadas = 0;

            _listaParadas = SesionGlobal.ListaParadasRRU ?? new List<ItemParadaRRU>();

            foreach (var parada in _listaParadas)
            {
                parada.ColorBorde = Color.FromArgb("#EAECEF");
                parada.ColorIcono = Color.FromArgb("#EAECEF");
                parada.NumeroStr = (_listaParadas.IndexOf(parada) + 1).ToString();

                parada.InfoValidacion = string.IsNullOrEmpty(parada.Imagen)
                    ? "Sin imagen asignada"
                    : "Pendiente de validación...";

                parada.ColorTextoInfo = Color.FromArgb("#90A4AE");
            }

            _ = ComprobarYRestaurarRespaldo();
        }

        _vieneDeResultados = false;

        ActualizarInterfazLista();

        if (_paradasCompletadas >= _listaParadas.Count && _listaParadas.Count > 0)
        {
            BtnValidar.IsVisible = false;
            BtnFinalizar.IsVisible = true;
        }
        else
        {
            BtnValidar.IsVisible = _listaParadas.Count > 0;
            BtnFinalizar.IsVisible = false;
            LblTxtValidar.Text = $"VALIDAR PARADA {_paradasCompletadas + 1}";
        }

        _ = Task.Run(async () => await CargarListaPdfsDesdeSharePointAsync());
        _ = Task.Run(async () => await CargarImagenesDesdeSharePointAsync());
    }

    private void ActualizarInterfazLista()
    {
        BindableLayout.SetItemsSource(ContenedorParadas, null);
        BindableLayout.SetItemsSource(ContenedorParadas, _listaParadas);
    }

    private async Task CargarImagenesDesdeSharePointAsync()
    {
        try
        {
            if (_listaParadas == null || !_listaParadas.Any()) return;

            var sp = new SharePointService();
            string token = await sp.ConseguirTokenSilenciosoAsync();
            bool huboCambios = false;

            foreach (var parada in _listaParadas)
            {
                if (string.IsNullOrEmpty(parada.Imagen) || parada.Imagen.Contains(FileSystem.CacheDirectory))
                    continue;

                string nombreArchivo = parada.Imagen.Trim();
                string rutaLocalCache = System.IO.Path.Combine(FileSystem.CacheDirectory, nombreArchivo);

                if (!File.Exists(rutaLocalCache))
                {
                    string rutaSharePointCompleta = $"{_carpetaImagenesRRU}/{nombreArchivo}";
                    byte[] imagenBytes = await sp.DescargarExcelConTokenAsync(rutaSharePointCompleta, token);

                    if (imagenBytes != null && imagenBytes.Length > 0)
                    {
                        await File.WriteAllBytesAsync(rutaLocalCache, imagenBytes);
                    }
                }

                if (File.Exists(rutaLocalCache))
                {
                    parada.Imagen = rutaLocalCache;
                    huboCambios = true;
                }
            }

            if (huboCambios)
            {
                MainThread.BeginInvokeOnMainThread(() => ActualizarInterfazLista());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando imágenes desde SharePoint: {ex.Message}");
        }
    }

    private async Task AnimarBoton(VisualElement? boton)
    {
        if (boton == null) return;
        await boton.ScaleTo(0.95, 50);
        await boton.ScaleTo(1.0, 50);
    }

    private async void OnValidarClicked(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);

        if (_paradasCompletadas >= _listaParadas.Count) return;

        BtnValidar.IsEnabled = false;
        LblTxtValidar.Text = "Obteniendo GPS...";

        try
        {
            Location? location = null;

#if WINDOWS
            await Task.Delay(1000);
            location = new Location(PlantContext.Current.SimulatedLatitude, PlantContext.Current.SimulatedLongitude);
#else
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (status != PermissionStatus.Granted)
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_PERMISO_DENEGADO"), LocalizationService.Translate("ALERT_GPS_SIN_ACCESO_MSG"), LocalizationService.Translate("BTN_ACEPTAR"));
                LblTxtValidar.Text = $"?? VALIDAR PARADA {_paradasCompletadas + 1}";
                BtnValidar.IsEnabled = true;
                return;
            }

            var request = new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(5));
            location = await Geolocation.Default.GetLocationAsync(request);
#endif

            if (location != null)
            {
                SesionGlobal.ParadasRRU.Add(location);

                var paradaActual = _listaParadas[_paradasCompletadas];
                string hora = DateTime.Now.ToString("HH:mm:ss");

                paradaActual.ColorBorde = Color.FromArgb("#3BF1E5");
                paradaActual.ColorIcono = Color.FromArgb("#3BF1E5");
                paradaActual.NumeroStr = "?";
#if WINDOWS
                paradaActual.InfoValidacion = $"Validado a las {hora} | Simulación PC";
#else
                paradaActual.InfoValidacion = $"Validado a las {hora} | Lat: {location.Latitude:F4}, Lon: {location.Longitude:F4}";
#endif
                paradaActual.ColorTextoInfo = Color.FromArgb("#002175");

                _paradasCompletadas++;
                ActualizarInterfazLista();

                if (_paradasCompletadas >= _listaParadas.Count)
                {
                    BtnValidar.IsVisible = false;
                    BtnFinalizar.IsVisible = true;
                }
                else
                {
                    LblTxtValidar.Text = $"?? VALIDAR PARADA {_paradasCompletadas + 1}";
                }

                _ = GuardarRespaldoRRU();
            }
            else
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_ERROR_GPS"), LocalizationService.Translate("ALERT_GPS_OBTENER_MSG"), LocalizationService.Translate("BTN_ACEPTAR"));
                LblTxtValidar.Text = $"?? VALIDAR PARADA {_paradasCompletadas + 1}";
            }
        }
        catch (FeatureNotSupportedException)
        {
            await DisplayAlert(LocalizationService.Translate("ALERT_AVISO"), LocalizationService.Translate("ALERT_GPS_NO_SOPORTA_MSG"), LocalizationService.Translate("BTN_ACEPTAR"));
            LblTxtValidar.Text = $"?? VALIDAR PARADA {_paradasCompletadas + 1}";
        }
        catch (Exception ex)
        {
            await DisplayAlert(LocalizationService.Translate("ALERT_AVISO_GPS"), LocalizationService.Translate("ALERT_GPS_LEER_MSG"), LocalizationService.Translate("BTN_ACEPTAR"));
            System.Diagnostics.Debug.WriteLine($"Error controlado: {ex.Message}");
            LblTxtValidar.Text = $"?? VALIDAR PARADA {_paradasCompletadas + 1}";
        }
        finally
        {
            BtnValidar.IsEnabled = true;
        }
    }

    private async void OnFinalizarClicked(object? sender, TappedEventArgs e)
    {
        if (File.Exists(_rutaArchivoRespaldo))
        {
            File.Delete(_rutaArchivoRespaldo);
        }
        if (sender is VisualElement btn) await AnimarBoton(btn);

        SesionGlobal.HoraFinInspeccion = DateTime.Now;
        var duracion = SesionGlobal.HoraFinInspeccion - SesionGlobal.HoraInicioInspeccion;
        SesionGlobal.TiempoTotalInspeccionReal = $"{(int)duracion.TotalMinutes:D2}:{duracion.Seconds:D2}";

        await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);

        _vieneDeResultados = true;

        await Navigation.PushAsync(new ResultsPage(_modelo, _motor, "RRU"));
    }

    private async void OnVolverClicked(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) await AnimarBoton(btn);

        bool confirmarSalida = await DisplayAlert(LocalizationService.Translate("ALERT_CONFIRMAR_SALIDA"), LocalizationService.Translate("ALERT_SALIR_RRU_MSG"), LocalizationService.Translate("BTN_SI_SALIR"), LocalizationService.Translate("BTN_CANCELAR"));
        if (confirmarSalida)
        {
            if (File.Exists(_rutaArchivoRespaldo))
            {
                File.Delete(_rutaArchivoRespaldo);
            }

            SesionGlobal.ChasisActual = string.Empty;
            SesionGlobal.ModeloSeleccionado = string.Empty;
            SesionGlobal.MotorSeleccionado = string.Empty;

            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);

            await Navigation.PopAsync();
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (OverlayDocumentos != null && OverlayDocumentos.IsVisible)
        {
            OnCerrarDocumentosClicked(this, EventArgs.Empty);
            return true;
        }

        if (OverlayImagen != null && OverlayImagen.IsVisible)
        {
            OnCerrarImagenClicked(this, EventArgs.Empty);
            return true;
        }

        Dispatcher.Dispatch(async () =>
        {
            bool confirmarSalida = await DisplayAlert(LocalizationService.Translate("ALERT_CONFIRMAR_SALIDA"), LocalizationService.Translate("ALERT_SALIR_RRU_MSG"), LocalizationService.Translate("BTN_SI_SALIR"), LocalizationService.Translate("BTN_CANCELAR"));
            if (confirmarSalida)
            {
                if (File.Exists(_rutaArchivoRespaldo))
                {
                    File.Delete(_rutaArchivoRespaldo);
                }

                SesionGlobal.ChasisActual = string.Empty;
                await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
                await Navigation.PopAsync();
            }
        });
        return true;
    }

    private double _currentScale = 1;
    private double _xOffset = 0;
    private double _yOffset = 0;

    private void OnImagenTapped(object sender, TappedEventArgs e)
    {
        if (sender is Image miniatura && miniatura.Source != null)
        {
            ImgAmpliada.Source = miniatura.Source;
            OverlayImagen.IsVisible = true;

            _currentScale = 1;
            ImgAmpliada.Scale = 1;
            ImgAmpliada.TranslationX = 0;
            ImgAmpliada.TranslationY = 0;
        }
    }

    private void OnCerrarImagenClicked(object sender, EventArgs e)
    {
        OverlayImagen.IsVisible = false;
    }

    private void OnZoomInClicked(object sender, TappedEventArgs e)
    {
        _currentScale += 0.5;
        ImgAmpliada.ScaleTo(_currentScale, 250, Easing.CubicOut);
    }

    private void OnZoomOutClicked(object sender, TappedEventArgs e)
    {
        if (_currentScale > 1)
        {
            _currentScale -= 0.5;
            ImgAmpliada.ScaleTo(_currentScale, 250, Easing.CubicOut);
        }
    }

    private void OnPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Running:
                ImgAmpliada.TranslationX = _xOffset + e.TotalX;
                ImgAmpliada.TranslationY = _yOffset + e.TotalY;
                break;
            case GestureStatus.Completed:
                _xOffset = ImgAmpliada.TranslationX;
                _yOffset = ImgAmpliada.TranslationY;
                break;
        }
    }

    private void OnDoubleTappedReset(object sender, TappedEventArgs e)
    {
        _currentScale = 1;
        _xOffset = 0;
        _yOffset = 0;
        ImgAmpliada.ScaleTo(1, 250, Easing.CubicIn);
        ImgAmpliada.TranslateTo(0, 0, 250, Easing.CubicIn);
    }

    private async void OnDocumentosClicked(object? sender, TappedEventArgs e)
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

    private async void OnCerrarDocumentosClicked(object sender, EventArgs e)
    {
        if (OverlayDocumentos != null)
        {
            await OverlayDocumentos.FadeTo(0, 200);
            OverlayDocumentos.IsVisible = false;
        }
    }

    private async Task CargarListaPdfsDesdeSharePointAsync()
    {
        try
        {
            var sp = new SharePointService();
            _listaPdfsDisponibles = await sp.ObtenerPdfsEnCarpetaAsync(_carpetaManualesPdf);
            MainThread.BeginInvokeOnMainThread(() => GenerarListaPdfsPanel());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando PDFs desde SharePoint: {ex.Message}");
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
            string nombreLimpio = "?? " + archivoPdf.Replace(".pdf", "").Replace("_", " ").ToUpper();

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
                if (btnPdf != null)
                {
                    await btnPdf.ScaleTo(0.9, 50);
                    await btnPdf.ScaleTo(1.0, 50);
                }
                await AbrirPdfDesdeNube(archivoPdf);
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

    private async Task GuardarRespaldoRRU()
    {
        try
        {
            var backup = new
            {
                Chasis = _chasis,
                ParadasCompletadas = _paradasCompletadas,
                RutaGPS = SesionGlobal.ParadasRRU.Select(l => new { l.Latitude, l.Longitude }).ToList()
            };

            string json = JsonSerializer.Serialize(backup);
            await File.WriteAllTextAsync(_rutaArchivoRespaldo, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al guardar respaldo RRU: {ex.Message}");
        }
    }

    private async Task ComprobarYRestaurarRespaldo()
    {
        try
        {
            if (File.Exists(_rutaArchivoRespaldo))
            {
                string json = await File.ReadAllTextAsync(_rutaArchivoRespaldo);
                using JsonDocument doc = JsonDocument.Parse(json);

                string chasisGuardado = doc.RootElement.GetProperty("Chasis").GetString() ?? "";

                if (chasisGuardado == _chasis)
                {
                    bool restaurar = await DisplayAlert(LocalizationService.Translate("ALERT_RUTA_PAUSA"),
                        LocalizationService.TranslateFormat("ALERT_RUTA_PAUSA_MSG", _chasis),
                        LocalizationService.Translate("BTN_SI_CONTINUAR"), LocalizationService.Translate("BTN_EMPEZAR_CERO"));

                    if (restaurar)
                    {
                        _paradasCompletadas = doc.RootElement.GetProperty("ParadasCompletadas").GetInt32();

                        SesionGlobal.ParadasRRU.Clear();
                        var arrayGps = doc.RootElement.GetProperty("RutaGPS").EnumerateArray();
                        foreach (var punto in arrayGps)
                        {
                            double lat = punto.GetProperty("Latitude").GetDouble();
                            double lon = punto.GetProperty("Longitude").GetDouble();
                            SesionGlobal.ParadasRRU.Add(new Location(lat, lon));
                        }

                        for (int i = 0; i < _paradasCompletadas; i++)
                        {
                            var p = _listaParadas[i];
                            p.ColorBorde = Color.FromArgb("#3BF1E5");
                            p.ColorIcono = Color.FromArgb("#3BF1E5");
                            p.NumeroStr = "?";
                            p.InfoValidacion = "Recuperado de la copia de seguridad";
                            p.ColorTextoInfo = Color.FromArgb("#002175");
                        }

                        if (_paradasCompletadas >= _listaParadas.Count)
                        {
                            BtnValidar.IsVisible = false;
                            BtnFinalizar.IsVisible = true;
                        }
                        else
                        {
                            LblTxtValidar.Text = $"?? VALIDAR PARADA {_paradasCompletadas + 1}";
                        }

                        ActualizarInterfazLista();
                    }
                    else
                    {
                        File.Delete(_rutaArchivoRespaldo);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al restaurar respaldo RRU: {ex.Message}");
        }
    }
}