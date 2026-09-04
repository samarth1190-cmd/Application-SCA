using Aplicacion_SCA.Models;
using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using System.Threading.Tasks;
using System;
using System.IO;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using System.Collections.Generic;
using Microsoft.Maui.Controls.Shapes;
using System.Linq;
using Microsoft.Maui.Graphics;

namespace Aplicacion_SCA.Pages;

public partial class AuditModePage : ContentPage
{
    private List<string> _listaPdfsDisponibles = new List<string>();
    private static string _carpetaManualesPdf => PlantContext.ResolvePath("03_Documentos_pdf");
    private static string _carpetaOtrosAuditorias => PlantContext.ResolvePath("08_Otros");
    private static string _carpetaImagenesNotificaciones => PlantContext.ResolvePath("10_Notificaciones/Imagenes");

    private bool _isNavegando = false;
    private bool _notificacionCargadaEnUI = false;

    public AuditModePage()
    {
        InitializeComponent();

        // Discreet corner label so someone can check they're on the latest
        // build without it competing with the actual UI. Read from AppInfo
        // (which mirrors ApplicationDisplayVersion from the csproj) instead
        // of a hardcoded string, so it can never drift out of sync with the
        // build that's actually installed.
        LblVersion.Text = $"v{AppInfo.Current.VersionString}";
    }

    private void ApplyTranslations()
    {
        LblBienvenidoCentro.Text = LocalizationService.Translate("MSG_BIENVENIDA_CENTRO");
        LblCerrarSesion.Text = LocalizationService.Translate("CONFIRM_LOGOUT_TITLE");
        LblModoAuditoria.Text = LocalizationService.Translate("HEADER_MODO_AUDITORIA");
        LblControlJapon.Text = LocalizationService.Translate("BTN_JAPONES");
        LblFormacionSca.Text = LocalizationService.Translate("BTN_FORMACION");
        LblOverlayDocumentos.Text = LocalizationService.Translate("OVERLAY_DOCUMENTOS");
        LblCerrarDoc.Text = LocalizationService.Translate("BTN_CERRAR");
        LblOverlayAvisos.Text = LocalizationService.Translate("OVERLAY_AVISOS");
        LblEntendido.Text = LocalizationService.Translate("BTN_ENTENDIDO");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // En el menú principal la planta activa siempre vuelve a ser Vigo:
        // solo el flujo C-DPV (PlantSelectionPage) puede cambiarla.
        PlantContext.Reset();

        _isNavegando = false;
        ApplyTranslations();

#if ANDROID
        var window = Platform.CurrentActivity?.Window;
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

            var (insetSuperior, insetInferior) = Platforms.Android.SafeAreaHelper.ObtenerInsetsBarras(window);
            ContenedorCabecera.Padding = new Thickness(20, 10 + insetSuperior, 20, 0);
            ContenidoTarjeta.Padding = new Thickness(15, 15, 15, 20 + insetInferior);
        }
#endif

        if (SesionGlobal.UsuarioActivo != null)
        {
            LblHola.Text = $"{LocalizationService.Translate("MSG_HOLA")}{SesionGlobal.UsuarioActivo.NOMBRE} {SesionGlobal.UsuarioActivo.APELLIDOS}";
            string turnoLabel = LocalizationService.CurrentLanguage switch
            {
                LocalizationService.Language.English => "Shift",
                LocalizationService.Language.French => "Équipe",
                LocalizationService.Language.German => "Schicht",
                _ => "Turno"
            };
            LblTurno.Text = $"{turnoLabel}: {SesionGlobal.UsuarioActivo.TURNO}";
        }
        else
        {
            string anonAuditor = LocalizationService.CurrentLanguage switch
            {
                LocalizationService.Language.English => "Unknown Auditor",
                LocalizationService.Language.French => "Auditeur Inconnu",
                LocalizationService.Language.German => "Unbekannter Prüfer",
                _ => "Auditor Desconocido"
            };
            LblHola.Text = $"{LocalizationService.Translate("MSG_HOLA")}{anonAuditor}";
            LblTurno.Text = LocalizationService.Translate("MSG_SIN_TURNO");
        }

        try
        {
            ContenedorTarjeta.Opacity = 0;
            ContenedorTarjeta.TranslationY = 60;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(50);
                await Task.WhenAll(
                    ContenedorTarjeta.FadeTo(1, 800, Easing.CubicOut),
                    ContenedorTarjeta.TranslateTo(0, 0, 800, Easing.CubicOut)
                );
            });
        }
        catch { }

        if (!SesionGlobal.NotificacionInicialVista &&
            SesionGlobal.Notificaciones != null &&
            SesionGlobal.Notificaciones.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(800);
                MainThread.BeginInvokeOnMainThread(() => MostrarNotificacion());
            });
            SesionGlobal.NotificacionInicialVista = true;
        }

        _ = Task.Run(async () =>
        {
            await CargarNotificacionAsync();
            await VerificarDisponibilidadRRUAsync();
            await CargarListaPdfsDesdeSharePointAsync();
            await CargarBotonesDinamicosDesdeNubeAsync();
        });
    }

    private async Task CargarNotificacionAsync()
    {
        if (_notificacionCargadaEnUI) return;
        var avisos = SesionGlobal.Notificaciones ?? new List<NotificacionApp>();

        if (avisos.Count == 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ContenedorNotificaciones.Children.Clear();
                ContenedorNotificaciones.Children.Add(new Label
                {
                    Text = LocalizationService.Translate("MSG_NO_NOVEDADES"),
                    TextColor = Color.FromArgb("#444444"),
                    HorizontalTextAlignment = TextAlignment.Center
                });
            });
            _notificacionCargadaEnUI = true;
            return;
        }

        var listaVistas = new List<IView>();
        var sp = new SharePointService();
        string token = "";

        if (avisos.Any(a => !string.IsNullOrWhiteSpace(a.ImagenMensaje) && !a.ImagenMensaje.Contains(FileSystem.CacheDirectory)))
        {
            token = await sp.ConseguirTokenSilenciosoAsync();
        }

        foreach (var aviso in avisos)
        {
            Border recuadro = new Border
            {
                BackgroundColor = Color.FromArgb("#F8F9FA"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#E0E0E0"),
                Margin = new Thickness(0, 0, 0, 10),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) }
            };

            VerticalStackLayout contenidoRecuadro = new VerticalStackLayout { Padding = 15, Spacing = 10 };

            Label lblMensaje = new Label
            {
                Text = aviso.Mensaje,
                TextColor = Color.FromArgb("#333333"),
                FontSize = 14,
                LineHeight = 1.2
            };

            Image imgAdjunta = new Image
            {
                IsVisible = false,
                HeightRequest = 150,
                Aspect = Aspect.AspectFit
            };

            contenidoRecuadro.Children.Add(lblMensaje);
            contenidoRecuadro.Children.Add(imgAdjunta);
            recuadro.Content = contenidoRecuadro;

            if (!string.IsNullOrWhiteSpace(aviso.ImagenMensaje))
            {
                if (!aviso.ImagenMensaje.Contains(FileSystem.CacheDirectory))
                {
                    try
                    {
                        string nombreImagen = aviso.ImagenMensaje.Trim();
                        string rutaLocal = System.IO.Path.Combine(FileSystem.CacheDirectory, nombreImagen);

                        if (!File.Exists(rutaLocal))
                        {
                            byte[] imgBytes = await sp.DescargarExcelConTokenAsync($"{_carpetaImagenesNotificaciones}/{nombreImagen}", token);
                            if (imgBytes != null && imgBytes.Length > 0)
                            {
                                File.WriteAllBytes(rutaLocal, imgBytes);
                            }
                        }

                        if (File.Exists(rutaLocal))
                        {
                            aviso.ImagenMensaje = rutaLocal;
                        }
                    }
                    catch { /* Fallo silencioso de red */ }
                }

                if (File.Exists(aviso.ImagenMensaje))
                {
                    imgAdjunta.Source = ImageSource.FromFile(aviso.ImagenMensaje);
                    imgAdjunta.IsVisible = true;
                }
            }

            listaVistas.Add(recuadro);
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ContenedorNotificaciones.Children.Clear();
            foreach (var vista in listaVistas)
            {
                ContenedorNotificaciones.Children.Add(vista);
            }
        });

        _notificacionCargadaEnUI = true;
    }

    private void MostrarNotificacion()
    {
        if (OverlayNotificacion != null)
        {
            var cajaPopup = OverlayNotificacion.FindByName<VisualElement>("CajaPopupNotificacion");

            if (cajaPopup != null)
            {
                cajaPopup.TranslationY = -300;
                cajaPopup.Opacity = 0;
            }

            OverlayNotificacion.Opacity = 0;
            OverlayNotificacion.IsVisible = true;

            OverlayNotificacion.FadeTo(1, 200, Easing.Linear);

            if (cajaPopup != null)
            {
                Task.WhenAll(
                    cajaPopup.FadeTo(1, 400, Easing.Linear),
                    cajaPopup.TranslateTo(0, 0, 600, Easing.BounceOut)
                );
            }
        }
    }

    private async void OnCerrarNotificacionClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn)
        {
            await btn.ScaleTo(0.95, 50);
            await btn.ScaleTo(1.0, 50);
        }

        if (OverlayNotificacion != null)
        {
            var cajaPopup = OverlayNotificacion.FindByName<VisualElement>("CajaPopupNotificacion");

            if (cajaPopup != null)
            {
                _ = Task.WhenAll(
                    cajaPopup.FadeTo(0, 200, Easing.Linear),
                    cajaPopup.TranslateTo(0, 100, 200, Easing.CubicIn)
                );
            }

            await OverlayNotificacion.FadeTo(0, 250, Easing.Linear);
            OverlayNotificacion.IsVisible = false;
        }
    }

    private async void OnAbrirNotificacionesClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement btn)
        {
            await btn.ScaleTo(0.85, 50, Easing.Linear);
            await btn.ScaleTo(1.0, 50, Easing.Linear);
        }
        MostrarNotificacion();
    }

    private async Task VerificarDisponibilidadRRUAsync()
    {
        if (BotonRRU == null) return;
        try
        {
            var sp = new SharePointService();
            string token = await sp.ConseguirTokenSilenciosoAsync();
            string rutaRRU = PlantContext.ResolvePath("07_RRU/RRU.xlsx");
            await sp.DescargarExcelConTokenAsync(rutaRRU, token);

            MainThread.BeginInvokeOnMainThread(() => BotonRRU.IsVisible = true);
        }
        catch (Exception)
        {
            MainThread.BeginInvokeOnMainThread(() => BotonRRU.IsVisible = false);
        }
    }

    private async Task CargarBotonesDinamicosDesdeNubeAsync()
    {
        if (ContenedorBotonesDinamicos == null) return;

        try
        {
            var sp = new SharePointService();
            List<string> archivosExtra = await sp.ObtenerExcelsEnCarpetaAsync(_carpetaOtrosAuditorias);

            if (archivosExtra == null || archivosExtra.Count == 0) return;

            var botonesVirtuales = new List<IView>();

            foreach (string nombreArchivoConExtension in archivosExtra)
            {
                if (nombreArchivoConExtension.StartsWith("~$")) continue;

                string nombreBotonLimpio = nombreArchivoConExtension.Replace(".xlsx", "", StringComparison.OrdinalIgnoreCase);

                Label lblTexto = new Label
                {
                    Text = nombreBotonLimpio.ToUpper().Replace("_", " "),
                    TextColor = Colors.White,
                    FontSize = 16,
                    CharacterSpacing = 1,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };

                RoundRectangle formaRedondeada = new RoundRectangle { CornerRadius = new CornerRadius(45) };

                Shadow sombraBoton = new Shadow
                {
                    Brush = new SolidColorBrush(Colors.Black),
                    Offset = new Point(0, 7),
                    Opacity = 0.2f,
                    Radius = 10
                };

                Border btnDinamico = new Border
                {
                    HeightRequest = 70,
                    WidthRequest = 250,
                    StrokeThickness = 0,
                    Margin = new Thickness(0, 5, 0, 5),
                    HorizontalOptions = LayoutOptions.Center,
                    Background = new SolidColorBrush(Color.FromArgb("#43AAA0")),
                    StrokeShape = formaRedondeada,
                    Shadow = sombraBoton,
                    Content = lblTexto
                };

                TapGestureRecognizer tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += async (sender, args) =>
                {
                    if (_isNavegando) return;
                    _isNavegando = true;

                    await AnimarBoton(btnDinamico);

                    SesionGlobal.ModoSeleccionado = nombreBotonLimpio;
                    await Navigation.PushAsync(new SelectionPage(nombreBotonLimpio));
                };
                btnDinamico.GestureRecognizers.Add(tapGesture);

                botonesVirtuales.Add(btnDinamico);
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ContenedorBotonesDinamicos.Children.Clear();
                foreach (var btn in botonesVirtuales)
                {
                    ContenedorBotonesDinamicos.Children.Add(btn);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al cargar auditorias extra: {ex.Message}");
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
            ContenedorListaPdfs.Children.Add(new Label
            {
                Text = LocalizationService.Translate("MSG_CARGANDO_MANUALES"),
                TextColor = Colors.LightGray,
                HorizontalOptions = LayoutOptions.Center
            });
            return;
        }

        foreach (var archivoPdf in _listaPdfsDisponibles)
        {
            string nombreLimpio = "📖 " + archivoPdf.Replace(".pdf", "").Replace("_", " ").ToUpper();

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
                if (_isNavegando) return;
                _isNavegando = true;

                await AnimarBoton(btnPdf);
                await AbrirPdfDesdeSharePointAsync(archivoPdf);

                _isNavegando = false;
            };
            btnPdf.GestureRecognizers.Add(tapGesture);

            ContenedorListaPdfs.Children.Add(btnPdf);
        }
    }

    private async Task AbrirPdfDesdeSharePointAsync(string nombreArchivo)
    {
        try
        {
            var sp = new SharePointService();
            string token = await sp.ConseguirTokenSilenciosoAsync();
            string rutaPdfCompleta = $"{_carpetaManualesPdf}/{nombreArchivo}";

            byte[] pdfBytes = await sp.DescargarExcelConTokenAsync(rutaPdfCompleta, token);

            string rutaLocal = System.IO.Path.Combine(FileSystem.CacheDirectory, nombreArchivo);
            File.WriteAllBytes(rutaLocal, pdfBytes);

            await Launcher.Default.OpenAsync(new OpenFileRequest(LocalizationService.Translate("TITLE_VER_MANUAL"), new ReadOnlyFile(rutaLocal)));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                LocalizationService.Translate("ALERT_ATENCION"), 
                LocalizationService.Translate("ERR_DESCARGA_MANUAL") + ex.Message, 
                "OK"
            );
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (OverlayDocumentos != null && OverlayDocumentos.IsVisible)
        {
            OverlayDocumentos.IsVisible = false;
            return true;
        }

        if (OverlayNotificacion != null && OverlayNotificacion.IsVisible)
        {
            OverlayNotificacion.IsVisible = false;
            return true;
        }

        ConfirmarCierreDeSesion();
        return true;
    }

    private async void ConfirmarCierreDeSesion()
    {
        bool respuesta = await DisplayAlert(
            LocalizationService.Translate("CONFIRM_LOGOUT_TITLE"), 
            LocalizationService.Translate("CONFIRM_LOGOUT_MSG"), 
            LocalizationService.Translate("BTN_SI_SALIR"), 
            LocalizationService.Translate("BTN_CANCELAR")
        );

        if (respuesta)
        {
            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
            SesionGlobal.CerrarSesion();

            var paginaLogin = new LoginPage();
            NavigationPage.SetHasNavigationBar(paginaLogin, false);

            await Navigation.PushAsync(paginaLogin);
            Navigation.RemovePage(this);
        }
    }

    private async Task AnimarBoton(VisualElement? elemento)
    {
        if (elemento == null) return;
        await elemento.ScaleTo(0.96, 50, Easing.Linear);
        await elemento.ScaleTo(1.0, 50, Easing.Linear);
    }

    private async void OnCerrarSesionClicked(object? sender, TappedEventArgs e)
    {
        await AnimarBoton(sender as VisualElement);
        ConfirmarCierreDeSesion();
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

    private async void OnJaponesaClicked(object? sender, TappedEventArgs e)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        await AnimarBoton(sender as VisualElement);
        SesionGlobal.ModoSeleccionado = "Japon";
        await Navigation.PushAsync(new SelectionPage("Japon"));
    }

    private async void OnScaClicked(object? sender, TappedEventArgs e)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        await AnimarBoton(sender as VisualElement);
        SesionGlobal.ModoSeleccionado = "CORE_DPV";
        // C-DPV pasa primero por la selección de planta (Vigo / MACK).
        await Navigation.PushAsync(new PlantSelectionPage("CORE_DPV"));
    }

    private async void OnDpvClicked(object? sender, TappedEventArgs e)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        await AnimarBoton(sender as VisualElement);
        SesionGlobal.ModoSeleccionado = "DPV";
        await Navigation.PushAsync(new SelectionPage("DPV"));
    }

    private async void OnFormacionClicked(object? sender, TappedEventArgs e)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        await AnimarBoton(sender as VisualElement);
        SesionGlobal.ModoSeleccionado = "Formacion";
        await Navigation.PushAsync(new SelectionPage("SCA_Formacion"));
    }

    private async void OnRRUClicked(object? sender, TappedEventArgs e)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        await AnimarBoton(sender as VisualElement);
        SesionGlobal.ModoSeleccionado = "RRU";
        await Navigation.PushAsync(new SelectionPage("RRU"));
    }
}
