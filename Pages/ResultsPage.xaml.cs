using Aplicacion_SCA.Models;
using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using System.Text.RegularExpressions;
using System.Text;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.ApplicationModel;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace Aplicacion_SCA.Pages;

public partial class ResultsPage : ContentPage
{
    private string _modelo = string.Empty;
    private string _motor = string.Empty;
    private string _modo = string.Empty;
    private Microsoft.Maui.Controls.Maps.Map _mapaInterior = null;

    private bool _estaEnModoEdicion = false;
    private bool _bloqueoBotonConfirmar = false;

    public ResultsPage(string modelo, string motor, string modo)
    {
        InitializeComponent();
        _modelo = modelo;
        _motor = motor;
        _modo = modo;

        if (EntryChasisEditar != null)
        {
            EntryChasisEditar.MaxLength = PlantContext.Current.ChassisMaxLength;
            EntryChasisEditar.TextChanged += OnChasisTextChanged;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _bloqueoBotonConfirmar = false;

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

        Task.Run(async () =>
        {
            var modelosUnicos = new List<string>();
            var motoresUnicos = new List<string>();

            if (SesionGlobal.ListaVehiculos != null && SesionGlobal.ListaVehiculos.Any())
            {
                modelosUnicos = SesionGlobal.ListaVehiculos
                    .Where(v => !string.IsNullOrWhiteSpace(v.Modelo))
                    .Select(v => v.Modelo.Trim()).Distinct().OrderBy(m => m).ToList();

                motoresUnicos = SesionGlobal.ListaVehiculos
                    .Where(v => !string.IsNullOrWhiteSpace(v.Motor))
                    .Select(v => v.Motor.Trim()).Distinct().OrderBy(m => m).ToList();
            }

            await Task.Delay(700);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                PickerModeloEditar.ItemsSource = modelosUnicos;
                PickerMotorEditar.ItemsSource = motoresUnicos;

                CargarDatosInforme();
            });
        });
    }

    private void CargarDatosInforme()
    {
        if (SesionGlobal.UsuarioActivo != null)
        {
            LblAuditor.Text = $"{SesionGlobal.UsuarioActivo.NOMBRE} {SesionGlobal.UsuarioActivo.APELLIDOS}";
            LblTurno.Text = SesionGlobal.UsuarioActivo.TURNO;
        }
        else
        {
            LblAuditor.Text = "Auditor Desconocido";
            LblTurno.Text = "Turno Desconocido";
        }

        LblFecha.Text = SesionGlobal.HoraFinInspeccion.ToString("dd/MM/yyyy");
        LblHoraInicio.Text = SesionGlobal.HoraInicioInspeccion.ToString("HH:mm:ss");
        LblHora.Text = SesionGlobal.HoraFinInspeccion.ToString("HH:mm:ss");
        LblTiempoTotal.Text = string.IsNullOrEmpty(SesionGlobal.TiempoTotalInspeccionReal) ? "00:00" : SesionGlobal.TiempoTotalInspeccionReal;

        if (_modo.Contains("RRU", StringComparison.OrdinalIgnoreCase))
            LblModo.Text = "RRU";
        else if (_modo.Contains("Formacion", StringComparison.OrdinalIgnoreCase))
            LblModo.Text = "Formación";
        else if (_modo.Equals("DPV", StringComparison.OrdinalIgnoreCase))
            LblModo.Text = "DPV";
        else if (_modo.Contains("SCA", StringComparison.OrdinalIgnoreCase) || _modo.Contains("CORE", StringComparison.OrdinalIgnoreCase))
            LblModo.Text = "Auditoría";
        else
            LblModo.Text = _modo.ToUpper().Replace("_", " ");

        ActualizarUILecturaVehiculo();
        VerificarYMostrarHistorialRodajes();
    }

    private void ActualizarUILecturaVehiculo()
    {
        if (string.IsNullOrWhiteSpace(_modelo) || string.IsNullOrWhiteSpace(_motor))
        {
            LblVehiculoLectura.Text = "Faltan datos";
            LblVehiculoLectura.TextColor = Colors.Red;
        }
        else
        {
            LblVehiculoLectura.Text = $"{_modelo} - {_motor}";
            LblVehiculoLectura.TextColor = Color.FromArgb("#0F2166");
        }

        if (string.IsNullOrWhiteSpace(SesionGlobal.ChasisActual) || SesionGlobal.ChasisActual == "Sin registrar")
        {
            LblChasisLectura.Text = "Sin registrar";
            LblChasisLectura.TextColor = Colors.Red;
        }
        else
        {
            LblChasisLectura.Text = SesionGlobal.ChasisActual;
            LblChasisLectura.TextColor = Color.FromArgb("#43AAA0");
        }
    }

    private void OnChasisTextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.NewTextValue)) return;

        string textoLimpio = e.NewTextValue.ToUpper();

        int maxLen = PlantContext.Current.ChassisMaxLength;
        if (textoLimpio.Length > maxLen)
        {
            textoLimpio = textoLimpio.Substring(0, maxLen);
        }

        if (Regex.IsMatch(textoLimpio, PlantContext.Current.ChassisPattern))
        {
            EntryChasisEditar.TextColor = Color.FromArgb("#43AAA0");
        }
        else
        {
            EntryChasisEditar.TextColor = Colors.Red;
        }

        if (EntryChasisEditar.Text != textoLimpio)
        {
            EntryChasisEditar.Text = textoLimpio;
        }
    }

    private async void OnModificarEnLineaClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) { await btn.ScaleTo(0.9, 50); await btn.ScaleTo(1.0, 50); }

        if (!_estaEnModoEdicion)
        {
            _estaEnModoEdicion = true;

            EntryChasisEditar.Text = SesionGlobal.ChasisActual == "Sin registrar" ? "" : SesionGlobal.ChasisActual;

            if (PickerModeloEditar.ItemsSource is IEnumerable<string> modelos)
                PickerModeloEditar.SelectedItem = modelos.FirstOrDefault(m => m.Trim().Equals(_modelo?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (PickerMotorEditar.ItemsSource is IEnumerable<string> motores)
                PickerMotorEditar.SelectedItem = motores.FirstOrDefault(m => m.Trim().Equals(_motor?.Trim(), StringComparison.OrdinalIgnoreCase));

            VistaLecturaVehiculo.IsVisible = false;
            VistaEdicionVehiculo.IsVisible = true;

            BordeEditarCentral.BackgroundColor = Color.FromArgb("#28a745");
            IconoEditarCentral.Text = "✓";
        }
        else
        {
            string nuevoVin = EntryChasisEditar.Text?.ToUpper().Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(nuevoVin) && !Regex.IsMatch(nuevoVin, PlantContext.Current.ChassisPattern))
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_FORMATO_INVALIDO"), LocalizationService.Translate("ALERT_CHASIS_FORMATO_MSG"), LocalizationService.Translate("BTN_ACEPTAR"));
                return;
            }

            if (PickerModeloEditar.SelectedItem == null || PickerMotorEditar.SelectedItem == null)
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_FALTAN_DATOS"), LocalizationService.Translate("ALERT_FALTAN_DATOS_MODELO_MSG"), LocalizationService.Translate("BTN_ACEPTAR"));
                return;
            }

            SesionGlobal.ChasisActual = string.IsNullOrWhiteSpace(nuevoVin) ? "Sin registrar" : nuevoVin;
            _modelo = PickerModeloEditar.SelectedItem.ToString();
            _motor = PickerMotorEditar.SelectedItem.ToString();

            ActualizarUILecturaVehiculo();

            VistaEdicionVehiculo.IsVisible = false;
            VistaLecturaVehiculo.IsVisible = true;

            BordeEditarCentral.BackgroundColor = Color.FromArgb("#4F7F9E");
            IconoEditarCentral.Text = "✎";

            _estaEnModoEdicion = false;
            OcultarTeclado();
        }
    }

    private void VerificarYMostrarHistorialRodajes()
    {
        try
        {
            if (SesionGlobal.HistorialRutasGPS != null && SesionGlobal.HistorialRutasGPS.Any())
            {
                ContenedorMapa.IsVisible = true;
                GenerarMapasMultiples();
            }
            else
            {
                ContenedorMapa.IsVisible = false;
            }
        }
        catch (Exception)
        {
            ContenedorMapa.IsVisible = false;
        }
    }

    private void GenerarMapasMultiples()
    {
        var stackMapas = new VerticalStackLayout { Spacing = 30 };

        foreach (var rutaGuardada in SesionGlobal.HistorialRutasGPS)
        {
            if (rutaGuardada.Ruta == null || !rutaGuardada.Ruta.Any()) continue;

            var frameRuta = new Border
            {
                StrokeThickness = 2.5,
                Padding = 15,
                BackgroundColor = Colors.White,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Stroke = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                    new GradientStop(Color.FromArgb("#243782"), 0.0f),
                    new GradientStop(Color.FromArgb("#43AAA0"), 1.0f)
                    },
                    new Point(0, 0),
                    new Point(1, 1)
                )
            };

            var layoutInterno = new VerticalStackLayout { Spacing = 5 };

            var lblTitulo = new Label
            {
                Text = $"📍 {rutaGuardada.NombreFase.ToUpper()}",
                FontAttributes = FontAttributes.Bold,
                FontSize = 16,
                TextColor = Color.FromArgb("#243782"),
                Margin = new Thickness(0, 0, 0, 0)
            };

            var lblVelocidad = new Label
            {
                Text = $"Velocidad Máxima (Rodaje): {Math.Round(rutaGuardada.VelocidadMaxima)} km/h",
                FontAttributes = FontAttributes.Bold,
                FontSize = 13,
                TextColor = Color.FromArgb("#43AAA0"),
                Margin = new Thickness(0, 0, 0, 10)
            };

            layoutInterno.Children.Add(lblTitulo);
            layoutInterno.Children.Add(lblVelocidad);

#if ANDROID || IOS
            try
            {
                var mapa = new Microsoft.Maui.Controls.Maps.Map
                {
                    MapType = MapType.Street,
                    IsScrollEnabled = true,
                    IsZoomEnabled = true,
                    HeightRequest = 250
                };

                double minLat = double.MaxValue, maxLat = double.MinValue;
                double minLon = double.MaxValue, maxLon = double.MinValue;

                var puntosRuta = rutaGuardada.Ruta.ToList();

                for (int i = 1; i < puntosRuta.Count; i++)
                {
                    var puntoAnterior = puntosRuta[i - 1];
                    var puntoActual = puntosRuta[i];

                    if (puntoActual.Latitude < minLat) minLat = puntoActual.Latitude;
                    if (puntoActual.Latitude > maxLat) maxLat = puntoActual.Latitude;
                    if (puntoActual.Longitude < minLon) minLon = puntoActual.Longitude;
                    if (puntoActual.Longitude > maxLon) maxLon = puntoActual.Longitude;

                    double velocidadKmh = puntoActual.Speed.HasValue ? puntoActual.Speed.Value * 3.6 : 0;

                    Color colorLinea;
                    if (velocidadKmh <= 25)
                        colorLinea = Color.FromArgb("#28a745");
                    else if (velocidadKmh > 25 && velocidadKmh <= 40)
                        colorLinea = Color.FromArgb("#fd7e14");
                    else
                        colorLinea = Color.FromArgb("#dc3545");

                    var segmento = new Microsoft.Maui.Controls.Maps.Polyline
                    {
                        StrokeColor = colorLinea,
                        StrokeWidth = 6
                    };

                    segmento.Geopath.Add(puntoAnterior);
                    segmento.Geopath.Add(puntoActual);

                    mapa.MapElements.Add(segmento);
                }

                if (puntosRuta.Any())
                {
                    var primer = puntosRuta.First();
                    if (primer.Latitude < minLat) minLat = primer.Latitude;
                    if (primer.Latitude > maxLat) maxLat = primer.Latitude;
                    if (primer.Longitude < minLon) minLon = primer.Longitude;
                    if (primer.Longitude > maxLon) maxLon = primer.Longitude;
                }

                if (rutaGuardada.Defectos != null && rutaGuardada.Defectos.Any())
                {
                    int contador = 1;
                    foreach (var defecto in rutaGuardada.Defectos)
                    {
                        var pinDefecto = new Microsoft.Maui.Controls.Maps.Pin
                        {
                            Label = $"⚠️ Defecto #{contador}",
                            Address = $"{defecto.Velocidad} km/h - {defecto.Hora}",
                            Type = PinType.SearchResult,
                            Location = new Location(defecto.Latitud, defecto.Longitud)
                        };
                        mapa.Pins.Add(pinDefecto);
                        contador++;
                    }
                }

                var centerLat = (minLat + maxLat) / 2;
                var centerLon = (minLon + maxLon) / 2;
                var distance = Location.CalculateDistance(minLat, minLon, maxLat, maxLon, DistanceUnits.Kilometers);
                double zoomRadius = Math.Max(distance / 2, 0.3);

                mapa.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(centerLat, centerLon), Distance.FromKilometers(zoomRadius + 0.1)));

                if (!rutaGuardada.NombreFase.ToUpper().Contains("EXTERIOR"))
                {
                    _mapaInterior = mapa;
                }

                layoutInterno.Children.Add(mapa);
            }
            catch (Exception)
            {
            }
#else
            layoutInterno.Children.Add(new Label { Text = "Mapas no disponibles en Windows.", TextColor = Colors.Gray });
#endif

            frameRuta.Content = layoutInterno;
            stackMapas.Children.Add(frameRuta);
        }

        ContenedorMapa.Content = stackMapas;
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

    private async void OnVolverClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn) { await btn.ScaleTo(0.95, 50); await btn.ScaleTo(1.0, 50); }

        var animaciones = new List<Task> { ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn) };
        if (this.FindByName<VisualElement>("ContenedorBotonesFlotantes") != null)
            animaciones.Add(this.FindByName<VisualElement>("ContenedorBotonesFlotantes").FadeTo(0, 300, Easing.CubicIn));

        await Task.WhenAll(animaciones);
        await Navigation.PopAsync();
    }

#if ANDROID
    private class SnapshotReadyCallback : Java.Lang.Object, Android.Gms.Maps.GoogleMap.ISnapshotReadyCallback
    {
        private readonly TaskCompletionSource<byte[]> _tcs;
        public SnapshotReadyCallback(TaskCompletionSource<byte[]> tcs) => _tcs = tcs;

        public void OnSnapshotReady(Android.Graphics.Bitmap snapshot)
        {
            if (snapshot == null)
            {
                _tcs.TrySetResult(null);
                return;
            }
            using var ms = new System.IO.MemoryStream();
            snapshot.Compress(Android.Graphics.Bitmap.CompressFormat.Png, 100, ms);
            _tcs.TrySetResult(ms.ToArray());
        }
    }

    private class MapReadyCallback : Java.Lang.Object, Android.Gms.Maps.IOnMapReadyCallback
    {
        private readonly TaskCompletionSource<byte[]> _tcs;
        public MapReadyCallback(TaskCompletionSource<byte[]> tcs) => _tcs = tcs;

        public void OnMapReady(Android.Gms.Maps.GoogleMap googleMap)
        {
            googleMap.Snapshot(new SnapshotReadyCallback(_tcs));
        }
    }

    private async Task<byte[]> CapturarMapaNativoAsync(Microsoft.Maui.Controls.Maps.Map mapaMAUI)
    {
        var tcs = new TaskCompletionSource<byte[]>();

        if (mapaMAUI?.Handler?.PlatformView is Android.Gms.Maps.MapView mapView)
        {
            mapView.GetMapAsync(new MapReadyCallback(tcs));
        }
        else
        {
            return null;
        }

        var tareaCompletada = await Task.WhenAny(tcs.Task, Task.Delay(3000));

        if (tareaCompletada == tcs.Task)
        {
            return await tcs.Task; 
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("⏳ Timeout: El mapa de Google no respondió para la captura.");
            return null; 
        }
    }
#endif

    
    private async void OnConfirmarClicked(object sender, TappedEventArgs e)
    {
        if (_bloqueoBotonConfirmar) return; 
        _bloqueoBotonConfirmar = true;

        try
        {
            if (sender is VisualElement btn)
            {
                await btn.ScaleTo(0.95, 50);
                await btn.ScaleTo(1.0, 50);
            }

            if (_estaEnModoEdicion)
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_ATENCION"), LocalizationService.Translate("ALERT_GUARDA_VEHICULO_MSG"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
                _bloqueoBotonConfirmar = false;
                return;
            }

            if (LblChasisLectura.Text == "Sin registrar" || string.IsNullOrWhiteSpace(LblChasisLectura.Text))
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_ATENCION"), LocalizationService.Translate("ALERT_INTRODUCE_VIN_MSG"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
                _bloqueoBotonConfirmar = false;
                return;
            }

            bool confirmar = await DisplayAlert(LocalizationService.Translate("ALERT_FINALIZAR"), LocalizationService.Translate("ALERT_CONFIRMA_ENVIO_MSG"), LocalizationService.Translate("BTN_SI_ENVIAR"), LocalizationService.Translate("BTN_CANCELAR"));

            if (confirmar)
            {
                
                var btnGuardar = this.FindByName<VisualElement>("BtnGuardar");
                if (btnGuardar != null) btnGuardar.Opacity = 0.5;

                byte[] imagenMapaBytes = null;
                string enlaceCapturaSharePoint = "N/A";

#if ANDROID
                if (_mapaInterior != null)
                {
                    try
                    {
                        imagenMapaBytes = await CapturarMapaNativoAsync(_mapaInterior);
                    }
                    catch (Exception mapEx)
                    {
                        Debug.WriteLine($"Error capturando mapa: {mapEx.Message}");
                    }
                }
#endif

                if (imagenMapaBytes != null)
                {
                    try
                    {
                        var spConexion = new SharePointService();
                        string nombreArchivo = $"Mapa_{SesionGlobal.ChasisActual}_{DateTime.Now.ToString("dd-MM-yyyy_HHmm")}.png";
                        enlaceCapturaSharePoint = await spConexion.SubirArchivoADocumentosAsync(PlantContext.ResolvePath("09_Capturas"), nombreArchivo, imagenMapaBytes);
                    }
                    catch (Exception)
                    {
                        enlaceCapturaSharePoint = "Error al guardar imagen";
                    }
                }

                string payloadSharePoint = await Task.Run(() => GenerarJsonParaListaSharePoint(enlaceCapturaSharePoint));

                await EnviarASharePointAsync(payloadSharePoint);

                
                AutoGuardadoService.BorrarBackup();

                SesionGlobal.ChasisActual = string.Empty;
                SesionGlobal.VelocidadMaximaRodaje = 0;
                SesionGlobal.RutaTrazadaGPS?.Clear();
                SesionGlobal.PuntosRuta?.Clear();
                SesionGlobal.HistorialRutasGPS?.Clear();
                SesionGlobal.ParadasRRU?.Clear();
                SesionGlobal.TiemposPorFase?.Clear();
                _mapaInterior = null;

                
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert(LocalizationService.Translate("ALERT_EXITO"), LocalizationService.Translate("ALERT_AUDITORIA_GUARDADA_MSG"), "OK");

                    var animaciones = new List<Task>();
                    if (ContenedorTarjeta != null) animaciones.Add(ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn));
                    if (btnGuardar != null) animaciones.Add(btnGuardar.FadeTo(0, 300, Easing.CubicIn));

                    if (animaciones.Any()) await Task.WhenAll(animaciones);

                    if (Navigation != null && Navigation.NavigationStack != null)
                    {
                        var historial = Navigation.NavigationStack.ToList();
                        var auditModePage = historial.FirstOrDefault(p => p != null && p.GetType().Name == "AuditModePage");

                        if (auditModePage != null)
                        {
                            int indexObjetivo = historial.IndexOf(auditModePage);
                            for (int i = historial.Count - 2; i > indexObjetivo; i--)
                            {
                                var paginaABorrar = historial[i];
                                if (paginaABorrar != null) Navigation.RemovePage(paginaABorrar);
                            }
                            await Navigation.PopAsync();
                        }
                        else await Navigation.PopToRootAsync();
                    }
                });
            }
            else
            {
                _bloqueoBotonConfirmar = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Crash evitado al guardar: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert(LocalizationService.Translate("ERR_TITULO"), LocalizationService.Translate("ALERT_ERROR_ENVIO_MSG"), "OK");
                var btnGuardar = this.FindByName<VisualElement>("BtnGuardar");
                if (btnGuardar != null) btnGuardar.Opacity = 1.0;
            });
            _bloqueoBotonConfirmar = false;
        }
    }

    private string GenerarEnlaceGoogleMaps(List<Location> ruta)
    {
        if (ruta == null || !ruta.Any()) return "N/A";

        try
        {
            var start = ruta.First();
            var end = ruta.Last();

            string baseUrl = "https://www.google.com/maps/dir/?api=1";
            string origin = $"&origin={start.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{start.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            string destination = $"&destination={end.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{end.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            string travelMode = "&travelmode=driving";

            string waypoints = "";
            if (ruta.Count > 10)
            {
                int step = ruta.Count / 10;
                var listaPuntos = new List<string>();
                for (int i = step; i < ruta.Count - 1; i += step)
                {
                    listaPuntos.Add($"{ruta[i].Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{ruta[i].Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                    if (listaPuntos.Count >= 9) break;
                }
                waypoints = "&waypoints=" + string.Join("|", listaPuntos);
            }

            return $"{baseUrl}{origin}{destination}{waypoints}{travelMode}";
        }
        catch { return "Error al generar enlace de ruta"; }
    }

    private string GenerarJsonParaListaSharePoint(string urlCapturaInterior)
    {
        string resumenTiemposHumanos = "Sin datos";
        if (SesionGlobal.TiemposPorFase != null && SesionGlobal.TiemposPorFase.Any())
        {
            var listaBonita = SesionGlobal.TiemposPorFase.Select(kvp => $"{kvp.Key} ({Math.Round(kvp.Value)} min)");
            resumenTiemposHumanos = string.Join("; ", listaBonita);
        }

        string rutaInteriorStr = "N/A";
        string rutaExteriorStr = "N/A";

        if (SesionGlobal.HistorialRutasGPS != null)
        {
            var rutaExt = SesionGlobal.HistorialRutasGPS.FirstOrDefault(r => r.NombreFase.ToUpper().Contains("EXTERIOR"));
            if (rutaExt != null && rutaExt.Ruta.Any()) rutaExteriorStr = GenerarEnlaceGoogleMaps(rutaExt.Ruta);

            var rutaInt = SesionGlobal.HistorialRutasGPS.FirstOrDefault(r => !r.NombreFase.ToUpper().Contains("EXTERIOR"));
            if (rutaInt != null && rutaInt.Ruta.Any()) rutaInteriorStr = urlCapturaInterior;
        }

        string puntosRRUStr = "Sin puntos registrados";
        var listaCoordenadasParaEnlace = new List<Microsoft.Maui.Devices.Sensors.Location>();

        if (SesionGlobal.HistorialRutasGPS != null)
        {
            foreach (var rutaGuardada in SesionGlobal.HistorialRutasGPS)
            {
                if (rutaGuardada.Defectos != null && rutaGuardada.Defectos.Any())
                {
                    foreach (var punto in rutaGuardada.Defectos)
                    {
                        listaCoordenadasParaEnlace.Add(new Microsoft.Maui.Devices.Sensors.Location(punto.Latitud, punto.Longitud));
                    }
                }
            }
        }

        if (listaCoordenadasParaEnlace.Count == 0 && SesionGlobal.PuntosRuta != null && SesionGlobal.PuntosRuta.Any())
        {
            foreach (var punto in SesionGlobal.PuntosRuta)
            {
                listaCoordenadasParaEnlace.Add(new Microsoft.Maui.Devices.Sensors.Location(punto.Latitud, punto.Longitud));
            }
        }

        if (listaCoordenadasParaEnlace.Count == 0 && SesionGlobal.ParadasRRU != null && SesionGlobal.ParadasRRU.Any())
        {
            foreach (var parada in SesionGlobal.ParadasRRU)
            {
                listaCoordenadasParaEnlace.Add(new Microsoft.Maui.Devices.Sensors.Location(parada.Latitude, parada.Longitude));
            }
        }

        if (listaCoordenadasParaEnlace.Any())
        {
            puntosRRUStr = GenerarEnlaceGoogleMaps(listaCoordenadasParaEnlace);
        }

        var datosAuditoria = new
        {
            fields = new
            {
                Title = LblChasisLectura.Text,
                Fecha = LblFecha.Text,
                Auditor = LblAuditor.Text,
                Turno = LblTurno.Text,
                Modo = LblModo.Text,
                Modelo = _modelo,
                Motor = _motor,
                Velocidad_Max = $"{Math.Round(SesionGlobal.VelocidadMaximaRodaje)} km/h",
                Hora_Inicio = LblHoraInicio.Text,
                Hora_Final = LblHora.Text,
                Tiempo_Total = LblTiempoTotal.Text,
                Resumen_Tiempos = resumenTiemposHumanos,
                Rodaje_Exterior = SesionGlobal.EsRodajeExterior ? "SI" : "NO",
                Ruta_Exterior = rutaExteriorStr,
                Ruta_Interior = rutaInteriorStr,
                Puntos_RRU = puntosRRUStr
            }
        };

        return JsonSerializer.Serialize(datosAuditoria, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private async Task EnviarASharePointAsync(string payloadJson)
    {
        try
        {
            var spConexion = new SharePointService();
            string listId = PlantContext.Current.ResultsListId;   // una lista por planta (id pendiente)
            await spConexion.InsertarEnListaAsync(listId, payloadJson);
        }
        catch (Exception ex)
        {
            
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_AVISO_NUBE"), LocalizationService.Translate("ALERT_AVISO_NUBE_MSG") + ex.Message, "OK");
            });
        }
    }
}