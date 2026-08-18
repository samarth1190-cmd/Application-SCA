using Aplicacion_SCA.Models;
using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using ClosedXML.Excel;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Aplicacion_SCA.Pages;

public partial class LoginPage : ContentPage
{
    private bool _isProcessingLogin = false;

    public LoginPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyTranslations();

        // Cuenta local de acceso (admin/admin) para pruebas sin conexión a SharePoint.
        SesionGlobal.ListaUsuarios ??= new List<Usuario>();
        if (!SesionGlobal.ListaUsuarios.Any(u => u.CV.Equals("admin", StringComparison.OrdinalIgnoreCase)))
        {
            SesionGlobal.ListaUsuarios.Add(new Usuario
            {
                CV = "admin",
                PSA = "admin",
                NOMBRE = "Admin",
                APELLIDOS = "Local",
                ROL = "Admin",
                TURNO = "C"
            });
        }

        // Usuario de pruebas para la planta MACK (eliminar antes de producción).
        if (!SesionGlobal.ListaUsuarios.Any(u => u.CV.Equals("sam", StringComparison.OrdinalIgnoreCase)))
        {
            SesionGlobal.ListaUsuarios.Add(new Usuario
            {
                CV = "sam",
                PSA = "admin",
                NOMBRE = "Sam",
                APELLIDOS = "MACK Test",
                ROL = "Auditor",
                TURNO = "A"
            });
        }

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
                    ContenedorTarjeta.FadeTo(1, 800, Easing.CubicOut),
                    ContenedorTarjeta.TranslateTo(0, 0, 800, Easing.CubicOut)
                );
            });
        }
        catch (Exception)
        {
            ContenedorTarjeta.Opacity = 1;
            ContenedorTarjeta.TranslationY = 0;
        }
    }

    private void ApplyTranslations()
    {
        LblAccesoAuditor.Text = LocalizationService.Translate("LOGIN_TITLE");
        TxtUsuario.Placeholder = LocalizationService.Translate("PLACEHOLDER_USER");
        TxtPassword.Placeholder = LocalizationService.Translate("PLACEHOLDER_PASS");
        LblTextoEntrar.Text = _isProcessingLogin ? LocalizationService.Translate("MSG_CONECTANDO") : LocalizationService.Translate("BTN_ENTRAR");
    }

    private void OcultarTeclado()
    {
        TxtUsuario?.Unfocus();
        TxtPassword?.Unfocus();
#if ANDROID || IOS
        TxtUsuario?.HideSoftInputAsync(System.Threading.CancellationToken.None);
        TxtPassword?.HideSoftInputAsync(System.Threading.CancellationToken.None);
#endif
    }

    private async void OnLoginClicked(object sender, TappedEventArgs e)
    {
        if (_isProcessingLogin) return;

        OcultarTeclado();

        if (sender is VisualElement btn)
        {
            await btn.ScaleTo(0.95, 50);
            await btn.ScaleTo(1.0, 50);
        }

        await ProcesarLogin();
    }

    private async void OnPasswordCompleted(object sender, EventArgs e)
    {
        if (_isProcessingLogin) return;

        OcultarTeclado();
        await ProcesarLogin();
    }

    private void OnFondoTapped(object sender, TappedEventArgs e)
    {
        OcultarTeclado();
    }

    private void OnVerPasswordTapped(object sender, EventArgs e)
    {
        TxtPassword.IsPassword = !TxtPassword.IsPassword;
        ImgOjo.Source = TxtPassword.IsPassword ? "eye_closed.png" : "eye_open.png";
    }

    private async Task ProcesarLogin()
    {
        _isProcessingLogin = true;
        ApplyTranslations();

        try
        {
            string usuario = TxtUsuario.Text?.Trim() ?? string.Empty;
            string password = TxtPassword.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(password))
            {
                await DisplayAlert(
                    LocalizationService.Translate("ALERT_ATENCION"), 
                    LocalizationService.Translate("ALERT_LOGIN_VACIO"), 
                    "OK"
                );
                return;
            }

            var usuarioEncontrado = SesionGlobal.ListaUsuarios?.FirstOrDefault(u =>
                u.CV.Equals(usuario, StringComparison.OrdinalIgnoreCase) &&
                u.PSA == password);

            if (usuarioEncontrado != null)
            {
                SesionGlobal.UsuarioActivo = usuarioEncontrado;

                LblTextoEntrar.Text = LocalizationService.Translate("MSG_CONECTANDO");

                await Task.Run(async () =>
                {
                    try
                    {
                        var sp = new SharePointService();
                        string token = await sp.ConseguirTokenSilenciosoAsync();

                        byte[] excelBytes = await sp.DescargarExcelConTokenAsync(PlantContext.ResolvePath("10_Notificaciones/Notificaciones.xlsx"), token);

                        if (excelBytes != null && excelBytes.Length > 0)
                        {
                            using (var stream = new MemoryStream(excelBytes))
                            using (var workbook = new XLWorkbook(stream))
                            {
                                var worksheet = workbook.Worksheet(1);
                                int ultimaFila = worksheet.LastRowUsed().RowNumber();

                                var listaNotificacionesTemp = new List<NotificacionApp>();

                                for (int fila = 2; fila <= ultimaFila; fila++)
                                {
                                    string mensajeLeido = worksheet.Cell(fila, 1).GetString();
                                    string imagenLeida = worksheet.Cell(fila, 2).GetString();

                                    if (!string.IsNullOrWhiteSpace(mensajeLeido))
                                    {
                                        listaNotificacionesTemp.Add(new NotificacionApp
                                        {
                                            Mensaje = mensajeLeido,
                                            ImagenMensaje = imagenLeida
                                        });
                                    }
                                }
                                SesionGlobal.Notificaciones = listaNotificacionesTemp;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error al leer la notificaciA3n en Login: {ex.Message}");
                        SesionGlobal.Notificaciones = new List<NotificacionApp>();
                    }
                });

                if (usuarioEncontrado.ROL.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                {
                    await DisplayAlert(
                        $"{LocalizationService.Translate("ALERT_BIENVENIDO")} {usuarioEncontrado.NOMBRE}",
                        SesionGlobal.ResumenBaseDatos,
                        LocalizationService.Translate("BTN_ENTRAR_SISTEMA")
                    );
                }

                await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);

                var nextPage = new AuditModePage();
                NavigationPage.SetHasNavigationBar(nextPage, false);

                await Navigation.PushAsync(nextPage);

                Navigation.RemovePage(this);
            }
            else
            {
                await DisplayAlert(
                    LocalizationService.Translate("ALERT_LOGIN_DENEGADO"), 
                    LocalizationService.Translate("ALERT_LOGIN_ERR"), 
                    LocalizationService.Translate("BTN_REINTENTAR")
                );
                TxtPassword.Text = string.Empty;
                TxtPassword.Focus();
            }
        }
        finally
        {
            _isProcessingLogin = false;
            ApplyTranslations();
        }
    }

    protected override bool OnBackButtonPressed()
    {
        Dispatcher.Dispatch(async () =>
        {
            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
            await Navigation.PopAsync();
        });
        return true;
    }

    private void LimpiarCampos()
    {
        TxtUsuario.Text = string.Empty;
        TxtPassword.Text = string.Empty;
        TxtPassword.IsPassword = true;
        ImgOjo.Source = "eye_closed.png";
        TxtUsuario.Focus();
    }
}
