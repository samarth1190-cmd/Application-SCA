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
using Microsoft.Maui.Storage; 

namespace Aplicacion_SCA.Pages;

public partial class SelectionPage : ContentPage
{
    string modeloSeleccionado = "";
    string motorSeleccionado = "";
    private string _modoOperativo;

    private bool _isNavegando = false;

    private readonly Color azulOscuro = Color.FromArgb("#0F2166");
    private readonly Color grisSuave = Color.FromArgb("#A2ADB9");
    private readonly Color textoGris = Color.FromArgb("#90959E");
    private readonly Color azulFondoSeleccion = Color.FromArgb("#B1C8D8");

    public SelectionPage(string modo = "SCA_Formacion")
    {
        InitializeComponent();
        _modoOperativo = modo;

        ApplyTranslations();
        ConfigurarTituloCabecera();
        ConfigurarOpcionesPorModo();
    }

    private void ApplyTranslations()
    {
        LblVolver.Text = LocalizationService.Translate("BTN_VOLVER");
        LblIncluirPrueba.Text = LocalizationService.Translate("SWITCH_RODAJE");
        LblTextoComenzar.Text = LocalizationService.Translate("BTN_COMENZAR");
    }

    private void ConfigurarTituloCabecera()
    {
        string modoTraducido = "";
        if (_modoOperativo.Contains("Japon", StringComparison.OrdinalIgnoreCase))
            modoTraducido = LocalizationService.Translate("BTN_JAPONES");
        else if (_modoOperativo.Contains("Formacion", StringComparison.OrdinalIgnoreCase))
            modoTraducido = LocalizationService.Translate("BTN_FORMACION");
        else if (_modoOperativo.Contains("SCA", StringComparison.OrdinalIgnoreCase) || _modoOperativo.Contains("CORE_DPV", StringComparison.OrdinalIgnoreCase))
            modoTraducido = LocalizationService.Translate("AUDITORIA_CDPV_TITLE");
        else if (_modoOperativo.Equals("RRU", StringComparison.OrdinalIgnoreCase))
            modoTraducido = LocalizationService.Translate("AUDITORIA_RRU_TITLE");
        else
            modoTraducido = _modoOperativo.ToUpper();

        LblTipoAuditoria.Text = $"{LocalizationService.Translate("SELECTION_TITLE")}: {modoTraducido}";
    }

    private void ConfigurarOpcionesPorModo()
    {
        if (_modoOperativo.Contains("Japon", StringComparison.OrdinalIgnoreCase) ||
            _modoOperativo.Equals("RRU", StringComparison.OrdinalIgnoreCase))
        {
            SwitchRodajeExterior.IsToggled = false;
            ContenedorOpcionRodaje.IsVisible = false;

            if (LblTituloModelo != null) LblTituloModelo.Text = LocalizationService.Translate("HEADER_MODELO").Replace("2. ", "1. ");
            if (LblTituloMotor != null) LblTituloMotor.Text = LocalizationService.Translate("HEADER_MOTOR").Replace("3. ", "2. ");
        }
        else
        {
            ContenedorOpcionRodaje.IsVisible = true;
            if (LblTituloModelo != null) LblTituloModelo.Text = LocalizationService.Translate("HEADER_MODELO");
            if (LblTituloMotor != null) LblTituloMotor.Text = LocalizationService.Translate("HEADER_MOTOR");
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _isNavegando = false;
        ApplyTranslations();
        ConfigurarTituloCabecera();
        ConfigurarOpcionesPorModo();

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

        if (SesionGlobal.UsuarioActivo != null)
        {
            LblHola.Text = $"{SesionGlobal.UsuarioActivo.NOMBRE} {SesionGlobal.UsuarioActivo.APELLIDOS}";
            LblTurno.Text = SesionGlobal.UsuarioActivo.TURNO;
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
            LblHola.Text = anonAuditor;
            LblTurno.Text = LocalizationService.Translate("MSG_SIN_TURNO");
        }

        modeloSeleccionado = "";
        motorSeleccionado = "";
        SesionGlobal.ModeloSeleccionado = string.Empty;
        SesionGlobal.MotorSeleccionado = string.Empty;

        Task.Run(() =>
        {
            var modelosUnicos = SesionGlobal.ListaVehiculos
                .Where(v => !string.IsNullOrWhiteSpace(v.Modelo))
                .Select(v => v.Modelo.Trim())
                .Distinct()
                .ToList();

            var motoresUnicos = SesionGlobal.ListaVehiculos
                .Where(v => !string.IsNullOrWhiteSpace(v.Motor))
                .Select(v => v.Motor.Trim())
                .Distinct()
                .ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                BindableLayout.SetItemsSource(ContenedorModelos, null);
                BindableLayout.SetItemsSource(ContenedorMotores, null);

                BindableLayout.SetItemsSource(ContenedorModelos, modelosUnicos);
                BindableLayout.SetItemsSource(ContenedorMotores, motoresUnicos);
            });
        });

        if (AutoGuardadoService.ExisteCopiaDeSeguridad())
        {
            string nombreUsuarioGuardado = Preferences.Get("Progreso_UsuarioActivo", "");
            string nombreUsuarioActual = SesionGlobal.UsuarioActivo?.NOMBRE ?? "";

            if (!string.IsNullOrEmpty(nombreUsuarioGuardado) && nombreUsuarioGuardado != nombreUsuarioActual)
            {
                AutoGuardadoService.BorrarBackup();
            }
            else
            {
                bool recuperar = await DisplayAlert(
                    LocalizationService.Translate("ALERT_INTERRUMPIDO"),
                    LocalizationService.Translate("ALERT_INTERRUMPIDO_MSG"),
                    LocalizationService.Translate("BTN_RECUPERAR_PROGRESO"),
                    LocalizationService.Translate("BTN_EMPEZAR_CERO")
                );

                if (recuperar)
                {
                    _isNavegando = true;
                    if (LblTextoComenzar != null) LblTextoComenzar.Text = LocalizationService.Translate("MSG_RECUPERANDO");

                    AutoGuardadoService.RecuperarProgreso();

                    string recupModo = SesionGlobal.ModoSeleccionado ?? _modoOperativo;
                    string recupModelo = SesionGlobal.ModeloSeleccionado ?? "";
                    string recupMotor = SesionGlobal.MotorSeleccionado ?? "";

                    if (recupModo.Contains("Japon", StringComparison.OrdinalIgnoreCase))
                    {
                        await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
                        await Navigation.PushAsync(new ControlJapon("", recupModelo, recupMotor));
                    }
                    else if (recupModo.Equals("RRU", StringComparison.OrdinalIgnoreCase))
                    {
                        bool descarga = await DescargarExcelRRUAsync();
                        if (descarga)
                        {
                            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
                            await Navigation.PushAsync(new RRUPage(recupModelo, recupMotor, ""));
                        }
                    }
                    else
                    {
                        bool descarga = await DescargarExcelEstandarAsync(recupModo);
                        if (descarga)
                        {
                            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
                            await Navigation.PushAsync(new MenuEstandarPage(recupModelo, recupMotor, recupModo));
                        }
                    }

                    if (LblTextoComenzar != null) LblTextoComenzar.Text = LocalizationService.Translate("BTN_COMENZAR");
                    _isNavegando = false;
                }
                else
                {
                    AutoGuardadoService.BorrarBackup();
                    LimpiarSesionGlobal();
                }
            }
        }
    }

    private void LimpiarSesionGlobal()
    {
        SesionGlobal.ChasisActual = string.Empty;
        SesionGlobal.IndiceEstandarActual = 0;
        if (SesionGlobal.EstandaresCompletados != null) SesionGlobal.EstandaresCompletados.Clear();
        if (SesionGlobal.HistorialRutasGPS != null) SesionGlobal.HistorialRutasGPS.Clear();
        if (SesionGlobal.RutaTrazadaGPS != null) SesionGlobal.RutaTrazadaGPS.Clear();
        if (SesionGlobal.TiemposPorFase != null) SesionGlobal.TiemposPorFase.Clear();
        if (SesionGlobal.ListaControlesJapon != null) SesionGlobal.ListaControlesJapon.Clear();
        if (SesionGlobal.ListaCheckListJapon != null) SesionGlobal.ListaCheckListJapon.Clear();
        if (SesionGlobal.ListaParadasRRU != null) SesionGlobal.ListaParadasRRU.Clear();
        if (SesionGlobal.ParadasRRU != null) SesionGlobal.ParadasRRU.Clear();
    }

    private void OcultarTeclado() { }

    private void OnFondoTapped(object? sender, TappedEventArgs e)
    {
        OcultarTeclado();
    }

    private void OnModeloClicked(object? sender, EventArgs e)
    {
        OcultarTeclado();
        var btn = sender as Button;
        if (btn == null) return;
        modeloSeleccionado = btn.Text;

        foreach (var child in ContenedorModelos.Children)
        {
            if (child is Button b)
            {
                b.BackgroundColor = Colors.White; b.TextColor = textoGris; b.BorderColor = grisSuave;
            }
        }
        btn.BackgroundColor = azulFondoSeleccion; btn.TextColor = azulOscuro; btn.BorderColor = azulOscuro;
    }

    private void OnMotorClicked(object? sender, EventArgs e)
    {
        OcultarTeclado();
        var btn = sender as Button;
        if (btn == null) return;
        motorSeleccionado = btn.Text;

        foreach (var child in ContenedorMotores.Children)
        {
            if (child is Button b)
            {
                b.BackgroundColor = Colors.White; b.TextColor = textoGris; b.BorderColor = grisSuave;
            }
        }
        btn.BackgroundColor = azulFondoSeleccion; btn.TextColor = azulOscuro; btn.BorderColor = azulOscuro;
    }

    private async void OnIniciarAuditoria(object? sender, TappedEventArgs e)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        try
        {
            SesionGlobal.HoraInicioInspeccion = DateTime.Now;
            OcultarTeclado();

            var border = sender as Border;
            if (border != null) { await border.ScaleTo(0.95, 50); await border.ScaleTo(1.0, 50); }

            if (string.IsNullOrEmpty(modeloSeleccionado) || string.IsNullOrEmpty(motorSeleccionado))
            {
                await DisplayAlert(
                    LocalizationService.Translate("ALERT_FALTAN_DATOS"), 
                    LocalizationService.Translate("ALERT_FALTAN_DATOS_MSG"), 
                    "OK"
                );
                return;
            }

            LblTextoComenzar.Text = LocalizationService.Translate("MSG_DESCARGANDO_DATOS");

            SesionGlobal.ChasisActual = string.Empty;
            SesionGlobal.MotorSeleccionado = motorSeleccionado;
            SesionGlobal.ModeloSeleccionado = modeloSeleccionado;
            SesionGlobal.ModoSeleccionado = _modoOperativo;
            SesionGlobal.EsRodajeExterior = SwitchRodajeExterior.IsToggled;

            AutoGuardadoService.BorrarBackup();

            if (_modoOperativo.Contains("Japon", StringComparison.OrdinalIgnoreCase))
            {
                await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
                await Navigation.PushAsync(new ControlJapon("", modeloSeleccionado, motorSeleccionado));
            }
            else if (_modoOperativo.Equals("RRU", StringComparison.OrdinalIgnoreCase))
            {
                bool descargaExitosa = await DescargarExcelRRUAsync();

                if (!descargaExitosa) return;

                await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
                await Navigation.PushAsync(new RRUPage(modeloSeleccionado, motorSeleccionado, ""));
            }
            else
            {
                bool descargaExitosa = await DescargarExcelEstandarAsync(_modoOperativo);

                if (!descargaExitosa) return;

                SesionGlobal.IndiceEstandarActual = 0;

                if (SesionGlobal.EstandaresCompletados == null)
                    SesionGlobal.EstandaresCompletados = new System.Collections.Generic.List<int>();
                else
                    SesionGlobal.EstandaresCompletados.Clear();

                await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
                await Navigation.PushAsync(new MenuEstandarPage(modeloSeleccionado, motorSeleccionado, _modoOperativo));
            }
        }
        finally
        {
            LblTextoComenzar.Text = LocalizationService.Translate("BTN_COMENZAR");
            _isNavegando = false;
        }
    }

    private async Task<bool> DescargarExcelEstandarAsync(string modo)
    {
        try
        {
            var sp = new SharePointService();
            var excelService = new ExcelService();
            string token = await sp.ConseguirTokenSilenciosoAsync();

            string rutaExcel = string.Empty;

            bool esModo13Columnas = modo.Contains("CORE_DPV", StringComparison.OrdinalIgnoreCase) ||
                                    modo.Contains("C-DPV", StringComparison.OrdinalIgnoreCase) ||
                                    modo.Contains("C_DPV", StringComparison.OrdinalIgnoreCase) ||
                                    modo.Contains("Formacion", StringComparison.OrdinalIgnoreCase) ||
                                    modo.Contains("SCA", StringComparison.OrdinalIgnoreCase);

            if (esModo13Columnas)
                rutaExcel = PlantContext.ResolvePath("05_CDPV_Formacion/CDPV.xlsx");
            else if (modo.Equals("DPV", StringComparison.OrdinalIgnoreCase))
                rutaExcel = PlantContext.ResolvePath("06_DPV/DPV.xlsx");
            else
                rutaExcel = PlantContext.ResolvePath($"08_Otros/{modo}.xlsx");

            byte[] excelBytes = await sp.DescargarExcelConTokenAsync(rutaExcel, token);

            await Task.Run(() =>
            {
                using var stream = new MemoryStream(excelBytes);

                if (esModo13Columnas)
                {
                    SesionGlobal.Estandares = excelService.LeerExcelAuditoria(stream);
                }
                else
                {
                    SesionGlobal.EstandaresDPV = excelService.LeerExcelDPV(stream);
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error descargando Excel de fases: {ex.Message}");
            await DisplayAlert(
                LocalizationService.Translate("ERR_CONEXION_TITULO"), 
                LocalizationService.Translate("ERR_FASES_SHAREPOINT"), 
                "OK"
            );
            return false;
        }
    }

    private async Task<bool> DescargarExcelRRUAsync()
    {
        try
        {
            var sp = new SharePointService();
            var excelService = new ExcelService();
            string token = await sp.ConseguirTokenSilenciosoAsync();

            string rutaRRU = PlantContext.ResolvePath("07_RRU/RRU.xlsx");

            byte[] excelBytes = await sp.DescargarExcelConTokenAsync(rutaRRU, token);

            await Task.Run(() =>
            {
                using var stream = new MemoryStream(excelBytes);
                SesionGlobal.ListaParadasRRU = excelService.LeerExcelRRU(stream);
            });

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error descargando Excel de RRU: {ex.Message}");
            await DisplayAlert(
                LocalizationService.Translate("ERR_CONEXION_TITULO"), 
                LocalizationService.Translate("ERR_RRU_SHAREPOINT"), 
                "OK"
            );
            return false;
        }
    }

    private async void OnVolverClicked(object? sender, TappedEventArgs e)
    {
        if (_isNavegando) return;

        var visualElement = sender as VisualElement;
        if (visualElement != null) { await visualElement.ScaleTo(0.9, 50); await visualElement.ScaleTo(1.0, 50); }
        ConfirmarSalidaYLimpiar();
    }

    private async void ConfirmarSalidaYLimpiar()
    {
        string modoNombre = LblTipoAuditoria.Text;
        bool respuesta = await DisplayAlert(
            LocalizationService.Translate("CONFIRMATION"), 
            LocalizationService.TranslateFormat("CONFIRM_EXIT_SELECTION", modoNombre), 
            LocalizationService.Translate("BTN_SI_SALIR"), 
            LocalizationService.Translate("BTN_CANCELAR")
        );

        if (respuesta)
        {
            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);

            LimpiarSesionGlobal();
            modeloSeleccionado = "";
            motorSeleccionado = "";
            SesionGlobal.ModoSeleccionado = string.Empty;

            await Navigation.PopAsync();
        }
    }

    protected override bool OnBackButtonPressed()
    {
        ConfirmarSalidaYLimpiar();
        return true;
    }
}
