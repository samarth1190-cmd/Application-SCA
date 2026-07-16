using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using Aplicacion_SCA.Models;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using System.IO;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aplicacion_SCA.Pages;

public partial class MenuEstandarPage : ContentPage
{
    private string _modelo;
    private string _motor;
    private string _modo;
    private string _nombreFaseEnCurso = string.Empty;
    private bool _datosCargados = false;
    private bool _isNavegando = false;

    private ObservableCollection<EstandarUIItem> _itemsEstandarVisual = new();
    private List<string> _listaPdfsDisponibles = new List<string>();

    private static string _carpetaManualesPdf => PlantContext.ResolvePath("03_Documentos_pdf");

    public MenuEstandarPage(string modelo, string motor, string modo)
    {
        InitializeComponent();
        _modelo = modelo;
        _motor = motor;
        _modo = modo;

        ConfigurarTituloModo();

        LblModeloText.Text = _modelo;
        LblMotorText.Text = _motor;

        ListaFases.ItemsSource = _itemsEstandarVisual;
    }

    private void ConfigurarTituloModo()
    {
        if (_modo.Contains("Japon", StringComparison.OrdinalIgnoreCase))
            LblTipoAuditoria.Text = "HOJA DE RUTA JAPÓN";
        else if (_modo.Contains("Formacion", StringComparison.OrdinalIgnoreCase))
            LblTipoAuditoria.Text = "HOJA DE RUTA FORMACIÓN";
        else if (_modo.Contains("CORE_DPV", StringComparison.OrdinalIgnoreCase))
            LblTipoAuditoria.Text = "HOJA DE RUTA AUDITORÍA";
        else if (_modo.Equals("DPV", StringComparison.OrdinalIgnoreCase))
            LblTipoAuditoria.Text = "HOJA DE RUTA DPV";
        else
            LblTipoAuditoria.Text = "HOJA DE RUTA";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        DeviceDisplay.Current.KeepScreenOn = true;
        _isNavegando = false;

        ConfigurarBarraNativaAndroid();
        ActualizarLabelsUsuarioYChasis();
        RegistrarFinFaseActual();

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

                if (!_datosCargados)
                {
                    _ = Task.Run(async () => await CargarPdfsDesdeNubeAsync());
                }
                else
                {
                    CargarListaEstandares();
                }
            });
        }
        catch (Exception)
        {
            ContenedorTarjeta.Opacity = 1;
            ContenedorTarjeta.TranslationY = 0;
            CargarListaEstandares();
        }
    }

    private async Task CargarPdfsDesdeNubeAsync()
    {
        try
        {
            var sp = new SharePointService();
            _listaPdfsDisponibles = await sp.ObtenerPdfsEnCarpetaAsync(_carpetaManualesPdf);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                GenerarListaPdfsPanel();
                _datosCargados = true;
                CargarListaEstandares();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error cargando PDFs: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _datosCargados = true;
                CargarListaEstandares();
            });
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

            Grid contenidoLayout = new Grid { Padding = new Thickness(15, 10), HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center };
            contenidoLayout.Children.Add(lblTexto);

            LinearGradientBrush gradienteBorde = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            gradienteBorde.GradientStops.Add(new GradientStop(Color.FromArgb("#243782"), 0.0f));
            gradienteBorde.GradientStops.Add(new GradientStop(Color.FromArgb("#43AAA0"), 1.0f));

            Border btnPdf = new Border
            {
                MinimumHeightRequest = 55,
                BackgroundColor = Color.FromArgb("#4F7F9E"),
                Stroke = gradienteBorde,
                StrokeThickness = 2,
                Margin = new Thickness(0, 0, 10, 10),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(27.5) },
                Content = contenidoLayout
            };

            TapGestureRecognizer tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) =>
            {
                if (_isNavegando) return;
                _isNavegando = true;

                try
                {
                    if (btnPdf != null) { await btnPdf.ScaleTo(0.9, 50); await btnPdf.ScaleTo(1.0, 50); }
                    await AbrirPdfDesdeSharePoint(archivoPdf);
                }
                finally
                {
                    _isNavegando = false;
                }
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
            await DisplayAlert(LocalizationService.Translate("ERR_TITULO"), LocalizationService.Translate("ERR_ABRIR_MANUAL") + ex.Message, LocalizationService.Translate("BTN_ACEPTAR"));
        }
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

    private void ActualizarLabelsUsuarioYChasis()
    {
        if (SesionGlobal.UsuarioActivo != null)
            LblUsuarioNombre.Text = $"{SesionGlobal.UsuarioActivo.NOMBRE} {SesionGlobal.UsuarioActivo.APELLIDOS}";
        else
            LblUsuarioNombre.Text = "Auditor Desconocido";

        LblChasisText.Text = !string.IsNullOrEmpty(SesionGlobal.ChasisActual)
            ? $"VIN: {SesionGlobal.ChasisActual}"
            : "VIN: PENDIENTE";
    }

    private void RegistrarFinFaseActual()
    {
        if (string.IsNullOrEmpty(_nombreFaseEnCurso)) return;

        var tiempoTranscurrido = DateTime.Now - SesionGlobal.HoraInicioFaseActual;
        double minutos = tiempoTranscurrido.TotalMinutes;

        if (tiempoTranscurrido.TotalSeconds < 5)
        {
            _nombreFaseEnCurso = string.Empty;
            return;
        }

        if (SesionGlobal.TiemposPorFase == null)
            SesionGlobal.TiemposPorFase = new Dictionary<string, double>();

        if (SesionGlobal.TiemposPorFase.ContainsKey(_nombreFaseEnCurso))
            SesionGlobal.TiemposPorFase[_nombreFaseEnCurso] += minutos;
        else
            SesionGlobal.TiemposPorFase.Add(_nombreFaseEnCurso, minutos);

        _nombreFaseEnCurso = string.Empty;
    }

    private void CargarListaEstandares()
    {
        bool esModo13Columnas = _modo.Contains("CORE_DPV", StringComparison.OrdinalIgnoreCase) ||
                                _modo.Contains("Formacion", StringComparison.OrdinalIgnoreCase);

        var listaActiva = esModo13Columnas ? SesionGlobal.Estandares : SesionGlobal.EstandaresDPV;

        if (listaActiva == null) return;

        int pistaActual = SesionGlobal.EsRodajeExterior ? 2 : 1;

        _itemsEstandarVisual.Clear();

        for (int i = 0; i < listaActiva.Count; i++)
        {
            var estandar = listaActiva[i];

            bool tienePasosValidos = estandar.ListaControles != null &&
                                     estandar.ListaControles.Count > 0 &&
                                     estandar.ListaControles.Any(p => p.Exterior == 0 || p.Exterior == pistaActual);

            bool estaDeshabilitado = false;

            if (!SesionGlobal.EsRodajeExterior && estandar.NombreEstandar != null && estandar.NombreEstandar.ToUpper().Contains("EXTERIOR"))
            {
                estaDeshabilitado = true;
            }
            else if (!tienePasosValidos)
            {
                if (estandar.NombreEstandar != null && estandar.NombreEstandar.ToUpper().Contains("EXTERIOR"))
                {
                    estaDeshabilitado = true;
                }
                else
                {
                    continue;
                }
            }

            bool completado = SesionGlobal.EstandaresCompletados != null && SesionGlobal.EstandaresCompletados.Contains(i);

            bool necesitaMapa = false;
            if (estandar.ListaControles != null)
            {
                necesitaMapa = estandar.ListaControles.Any(paso =>
                    !string.IsNullOrEmpty(paso.TipoPlantilla) && paso.TipoPlantilla.ToUpper().Contains("RODAJE")
                );
            }

            _itemsEstandarVisual.Add(new EstandarUIItem
            {
                Indice = i,
                Nombre = estandar.NombreEstandar?.ToUpper() ?? $"ESTÁNDAR {i + 1}",
                EsCompletado = completado,
                EsRodaje = necesitaMapa,
                EsDeshabilitado = estaDeshabilitado
            });
        }
    }

    private void OcultarTeclado()
    {
        EntChasisEstandar?.Unfocus();
#if ANDROID || IOS
        EntChasisEstandar?.HideSoftInputAsync(System.Threading.CancellationToken.None);
#endif
    }

    private void OnFondoTapped(object sender, TappedEventArgs e) => OcultarTeclado();

    private void OnChasisTextChanged(object sender, TextChangedEventArgs e)
    {
        string nuevoChasis = e.NewTextValue?.ToUpper() ?? "";
        SesionGlobal.ChasisActual = nuevoChasis;
        LblChasisText.Text = string.IsNullOrEmpty(nuevoChasis) ? "VIN: PENDIENTE" : $"VIN: {nuevoChasis}";
    }

    private async void OnEstandarTapped(object sender, TappedEventArgs e)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        try
        {
            OcultarTeclado();

            var item = e.Parameter as EstandarUIItem;
            if (item == null) { _isNavegando = false; return; }

            if (sender is Border b) { await b.ScaleTo(0.95, 50); await b.ScaleTo(1.0, 50); }

            if (item.EsDeshabilitado)
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_NO_APLICA"), LocalizationService.Translate("ALERT_RODAJE_DESACTIVADO"), LocalizationService.Translate("BTN_ACEPTAR"));
                _isNavegando = false;
                return;
            }

            if (item.EsCompletado)
            {
                bool confirmar = await DisplayAlert(LocalizationService.Translate("ALERT_FASE_COMPLETADA"), LocalizationService.Translate("ALERT_FASE_COMPLETADA_MSG"), LocalizationService.Translate("BTN_SI_REPETIR"), LocalizationService.Translate("BTN_CANCELAR"));
                if (!confirmar)
                {
                    _isNavegando = false;
                    return;
                }
            }

            SesionGlobal.IndiceEstandarActual = item.Indice;
            _nombreFaseEnCurso = item.Nombre;
            SesionGlobal.HoraInicioFaseActual = DateTime.Now;

            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
            if (item.EsRodaje)
                await Navigation.PushAsync(new RodajeExterior(_modelo, _motor, _modo));
            else
                await Navigation.PushAsync(new EstandarPage(_modelo, _motor, _modo));
        }
        catch (Exception)
        {
            _isNavegando = false;
        }
    }

    private async void OnFinalizarAuditoriaClicked(object sender, TappedEventArgs e)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        try
        {
            OcultarTeclado();
            if (sender is Border border) { await border.ScaleTo(0.95, 50); await border.ScaleTo(1.0, 50); }

            if (string.IsNullOrWhiteSpace(SesionGlobal.ChasisActual) || SesionGlobal.ChasisActual.Length < PlantContext.Current.ChassisMinLength)
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_FALTA_CHASIS"), LocalizationService.Translate("ALERT_FALTA_CHASIS_MSG"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
                _isNavegando = false;
                return;
            }

            RegistrarFinFaseActual();
            SesionGlobal.HoraFinInspeccion = DateTime.Now;
            TimeSpan duracionTotal = SesionGlobal.HoraFinInspeccion - SesionGlobal.HoraInicioInspeccion;
            SesionGlobal.TiempoTotalInspeccionReal = $"{(int)duracionTotal.TotalMinutes:D2}:{duracionTotal.Seconds:D2}";

            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
            await Navigation.PushAsync(new ResultsPage(_modelo, _motor, _modo));
        }
        catch (Exception)
        {
            _isNavegando = false;
        }
    }

    private async void OnVolverClicked(object sender, TappedEventArgs e)
    {
        if (_isNavegando) return;

        OcultarTeclado();
        if (sender is VisualElement btn) { await btn.ScaleTo(0.95, 50); await btn.ScaleTo(1.0, 50); }
        await SalirDeAuditoria();
    }

    protected override bool OnBackButtonPressed()
    {
        if (OverlayDocumentos != null && OverlayDocumentos.IsVisible) { OnCerrarDocumentosClicked(this, EventArgs.Empty); return true; }
        _ = SalirDeAuditoria();
        return true;
    }

    private async Task SalirDeAuditoria()
    {
        if (_isNavegando) return;
        _isNavegando = true;

        bool confirmarSalida = await DisplayAlert(LocalizationService.Translate("ALERT_CONFIRMAR_SALIDA"), LocalizationService.Translate("ALERT_SALIR_PROGRESO_MSG"), LocalizationService.Translate("BTN_SI_SALIR"), LocalizationService.Translate("BTN_CANCELAR"));
        if (confirmarSalida)
        {
            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
            SesionGlobal.ChasisActual = string.Empty;
            SesionGlobal.EstandaresCompletados?.Clear();
            SesionGlobal.TiemposPorFase?.Clear();
            await Navigation.PopAsync();
        }
        else
        {
            _isNavegando = false;
        }
    }

    private async void OnDocumentosClicked(object? sender, TappedEventArgs e)
    {
        OcultarTeclado();
        if (sender is VisualElement btn)
        {
            await btn.ScaleTo(0.9, 50);
            await btn.ScaleTo(1.0, 50);
        }
        if (OverlayDocumentos != null) { OverlayDocumentos.IsVisible = true; await OverlayDocumentos.FadeTo(1, 200); }
    }

    private async void OnCerrarDocumentosClicked(object sender, EventArgs e)
    {
        if (OverlayDocumentos != null) { await OverlayDocumentos.FadeTo(0, 200); OverlayDocumentos.IsVisible = false; }
    }

    private async void OnAyudaClicked(object sender, TappedEventArgs e)
    {
        OcultarTeclado();
        if (sender is VisualElement btn)
        {
            await btn.ScaleTo(0.8, 50);
            await btn.ScaleTo(1.0, 50);
        }

        await DisplayAlert(LocalizationService.Translate("TITLE_GUIA_RAPIDA"), LocalizationService.Translate("MSG_GUIA_RAPIDA"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
    }
}

public class EstandarUIItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private int _indice;
    public int Indice { get => _indice; set { _indice = value; OnPropertyChanged(); } }

    private string _nombre = string.Empty;
    public string Nombre { get => _nombre; set { _nombre = value; OnPropertyChanged(); } }

    private bool _esCompletado;
    public bool EsCompletado
    {
        get => _esCompletado;
        set
        {
            if (_esCompletado != value)
            {
                _esCompletado = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FondoPildora));
                OnPropertyChanged(nameof(ColorIcono));
                OnPropertyChanged(nameof(Icono));
            }
        }
    }

    private bool _esRodaje;
    public bool EsRodaje { get => _esRodaje; set { _esRodaje = value; OnPropertyChanged(); } }

    private bool _esDeshabilitado;
    public bool EsDeshabilitado
    {
        get => _esDeshabilitado;
        set
        {
            if (_esDeshabilitado != value)
            {
                _esDeshabilitado = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FondoPildora));
                OnPropertyChanged(nameof(ColorIcono));
                OnPropertyChanged(nameof(Icono));
                OnPropertyChanged(nameof(OpacidadBoton));
            }
        }
    }

    public Brush FondoPildora
    {
        get
        {
            if (EsDeshabilitado) return new SolidColorBrush(Color.FromArgb("#D3D3D3"));
            if (EsCompletado) return new SolidColorBrush(Color.FromArgb("40A85A"));
            return new SolidColorBrush(Color.FromArgb("#729EBB"));
        }
    }

    public Color ColorIcono
    {
        get
        {
            if (EsDeshabilitado) return Color.FromArgb("#A0A0A0");
            if (EsCompletado) return Color.FromArgb("#2E7D32");
            return Color.FromArgb("#0F2166");
        }
    }

    public string Icono
    {
        get
        {
            if (EsDeshabilitado) return "🚫";
            if (EsCompletado) return "✓";
            return "▶";
        }
    }

    public double OpacidadBoton => EsDeshabilitado ? 0.6 : 1.0;
}