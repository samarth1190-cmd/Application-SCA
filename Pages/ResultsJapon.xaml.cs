using Aplicacion_SCA.Models;
using Aplicacion_SCA.Services;
using System.Text.RegularExpressions;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using System.Text.Json;
using Microsoft.Maui.Graphics;

namespace Aplicacion_SCA.Pages;

public partial class ResultsJapon : ContentPage
{
    private string _chasis;
    private string _modelo;
    private string _motor;
    private List<ItemCheckList> _puntosTT;
    private List<ItemCheckList> _puntosControl;
    private List<ItemCheckList> _puntosVisto;
    private List<ItemCheckList> _puntosOtros;

    private bool _modoEdicion = false;

    public ResultsJapon(string chasis, string modelo, string motor, List<ItemCheckList> puntosTT, List<ItemCheckList> puntosControl, List<ItemCheckList> puntosVisto, List<ItemCheckList> puntosOtros)
    {
        InitializeComponent();

        if (EntryChasisEditar != null)
        {
            EntryChasisEditar.MaxLength = 8;
            EntryChasisEditar.TextChanged += OnChasisTextChanged;
        }

        _chasis = string.IsNullOrWhiteSpace(chasis) ? "Sin registrar" : chasis;
        _modelo = modelo;
        _motor = motor;
        _puntosTT = puntosTT;
        _puntosControl = puntosControl;
        _puntosVisto = puntosVisto;
        _puntosOtros = puntosOtros;

        if (SesionGlobal.ListaVehiculos != null && SesionGlobal.ListaVehiculos.Any())
        {
            var modelosUnicos = SesionGlobal.ListaVehiculos
                .Where(v => !string.IsNullOrWhiteSpace(v.Modelo))
                .Select(v => v.Modelo.Trim())
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            var motoresUnicos = SesionGlobal.ListaVehiculos
                .Where(v => !string.IsNullOrWhiteSpace(v.Motor))
                .Select(v => v.Motor.Trim())
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            PickerModeloEditar.ItemsSource = modelosUnicos;
            PickerMotorEditar.ItemsSource = motoresUnicos;
        }

        CargarDatosInforme();
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
    }

    protected override bool OnBackButtonPressed()
    {
        if (OverlayEdicionFase != null && OverlayEdicionFase.IsVisible)
        {
            OnCerrarOverlayEdicionFase(this, EventArgs.Empty);
            return true;
        }

        DisplayAlert(LocalizationService.Translate("ALERT_BLOQUEADO"), LocalizationService.Translate("ALERT_BLOQUEADO_MSG"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
        return true;
    }

    private void CargarDatosInforme()
    {
        LblFecha.Text = SesionGlobal.HoraFinInspeccion.ToString("dd/MM/yyyy");
        LblHoraInicio.Text = SesionGlobal.HoraInicioInspeccion.ToString("HH:mm:ss");
        LblHora.Text = SesionGlobal.HoraFinInspeccion.ToString("HH:mm:ss");
        LblTiempoTotal.Text = string.IsNullOrEmpty(SesionGlobal.TiempoTotalInspeccionReal) ? "00:00" : SesionGlobal.TiempoTotalInspeccionReal;

        LblModo.Text = "Control Japón";

        ActualizarVistaLecturaVehiculo();

        var usuarioActual = SesionGlobal.UsuarioActivo;
        if (usuarioActual != null)
        {
            LblAuditor.Text = $"{usuarioActual.NOMBRE} {usuarioActual.APELLIDOS}";
            LblTurno.Text = string.IsNullOrEmpty(usuarioActual.TURNO) ? "Sin turno definido" : usuarioActual.TURNO;
        }
        else
        {
            LblAuditor.Text = "Operario No Identificado";
            LblTurno.Text = "Sin turno definido";
        }

        MarcarVacios(_puntosTT);
        MarcarVacios(_puntosControl);
        MarcarVacios(_puntosVisto);
        MarcarVacios(_puntosOtros);

        ActualizarListasVisuales();
    }

    private void ActualizarListasVisuales()
    {
        BindableLayout.SetItemsSource(ListaTT, null);
        BindableLayout.SetItemsSource(ListaControl, null);
        BindableLayout.SetItemsSource(ListaVisto, null);
        BindableLayout.SetItemsSource(ListaOtros, null);

        BindableLayout.SetItemsSource(ListaTT, _puntosTT);
        BindableLayout.SetItemsSource(ListaControl, _puntosControl);
        BindableLayout.SetItemsSource(ListaVisto, _puntosVisto);

        if (_puntosOtros != null && _puntosOtros.Count > 0)
        {
            ContenedorOtros.IsVisible = true;
            BindableLayout.SetItemsSource(ListaOtros, _puntosOtros);
        }
        else
        {
            ContenedorOtros.IsVisible = false;
        }
    }

    private void ActualizarVistaLecturaVehiculo()
    {
        LblChasis.Text = _chasis;
        LblVehiculo.Text = $"{_modelo} - {_motor}";

        if (string.IsNullOrWhiteSpace(_chasis) || _chasis.Equals("Sin registrar", StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(_chasis, @"^[A-Z]{2}[0-9]{6}$"))
        {
            LblChasis.TextColor = Colors.Red;
        }
        else
        {
            LblChasis.TextColor = Color.FromArgb("#43AAA0");
        }
    }

    private void MarcarVacios(List<ItemCheckList> lista)
    {
        if (lista == null) return;
        foreach (var item in lista)
        {
            if (string.IsNullOrWhiteSpace(item.Estado))
            {
                item.Estado = "N/A";
            }
        }
    }


    private void OnChasisTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry)
        {
            if (string.IsNullOrEmpty(e.NewTextValue)) return;

            string textoFormateado = e.NewTextValue.ToUpper();

            if (textoFormateado.Length > 8)
            {
                textoFormateado = textoFormateado.Substring(0, 8);
            }

            if (Regex.IsMatch(textoFormateado, @"^[A-Z]{2}[0-9]{6}$"))
            {
                entry.TextColor = Color.FromArgb("#43AAA0");
            }
            else
            {
                entry.TextColor = Colors.Red;
            }

            if (entry.Text != textoFormateado)
            {
                entry.Text = textoFormateado;
            }
        }
    }

    private async void OnModificarEnLineaClicked(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement icon)
        {
            await icon.ScaleTo(0.8, 50);
            await icon.ScaleTo(1.0, 50);
        }

        if (!_modoEdicion)
        {
            _modoEdicion = true;

            EntryChasisEditar.Text = _chasis.Equals("Sin registrar", StringComparison.OrdinalIgnoreCase) ? "" : _chasis;

            if (PickerModeloEditar.ItemsSource is IEnumerable<string> modelos)
                PickerModeloEditar.SelectedItem = modelos.FirstOrDefault(m => m.Trim().Equals(_modelo?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (PickerMotorEditar.ItemsSource is IEnumerable<string> motores)
                PickerMotorEditar.SelectedItem = motores.FirstOrDefault(m => m.Trim().Equals(_motor?.Trim(), StringComparison.OrdinalIgnoreCase));

            VistaLecturaVehiculo.IsVisible = false;
            VistaEdicionVehiculo.IsVisible = true;

            IconoEditarCentral.Text = "✓";
            IconoEditarCentral.TextColor = Colors.White;
            BordeEditarCentral.BackgroundColor = Color.FromArgb("#28a745");
        }
        else
        {
            string nuevoVin = EntryChasisEditar.Text?.ToUpper().Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(nuevoVin) && !Regex.IsMatch(nuevoVin, @"^[A-Z]{2}[0-9]{6}$"))
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_FORMATO_INVALIDO"), LocalizationService.Translate("ALERT_CHASIS_FORMATO_SIMPLE_MSG"), LocalizationService.Translate("BTN_ACEPTAR"));
                return;
            }

            _chasis = string.IsNullOrWhiteSpace(nuevoVin) ? "Sin registrar" : nuevoVin;
            SesionGlobal.ChasisActual = _chasis.Equals("Sin registrar") ? "" : _chasis;
            _modelo = PickerModeloEditar.SelectedItem?.ToString() ?? _modelo;
            _motor = PickerMotorEditar.SelectedItem?.ToString() ?? _motor;

            ActualizarVistaLecturaVehiculo();

            _modoEdicion = false;
            VistaEdicionVehiculo.IsVisible = false;
            VistaLecturaVehiculo.IsVisible = true;

            IconoEditarCentral.Text = "✎";
            IconoEditarCentral.TextColor = Colors.White;
            BordeEditarCentral.BackgroundColor = Color.FromArgb("#4F7F9E");
        }
    }


    private async void OnEditarFaseClicked(object sender, TappedEventArgs e)
    {
        OcultarTeclado();
        if (sender is VisualElement btn)
        {
            await btn.ScaleTo(0.9, 50);
            await btn.ScaleTo(1.0, 50);
        }

        BindableLayout.SetItemsSource(SeccionPopUpTT, _puntosTT);
        BindableLayout.SetItemsSource(SeccionPopUpControl, _puntosControl);
        BindableLayout.SetItemsSource(SeccionPopUpVisto, _puntosVisto);

        if (_puntosOtros != null && _puntosOtros.Count > 0)
        {
            ContenedorPopUpOtros.IsVisible = true;
            BindableLayout.SetItemsSource(SeccionPopUpOtros, _puntosOtros);
        }

        if (OverlayEdicionFase != null)
        {
            OverlayEdicionFase.Opacity = 0;
            OverlayEdicionFase.IsVisible = true;
            await OverlayEdicionFase.FadeTo(1, 200);
        }
    }

    private async void OnCerrarOverlayEdicionFase(object sender, EventArgs e)
    {
        OcultarTeclado();
        if (OverlayEdicionFase != null)
        {
            await OverlayEdicionFase.FadeTo(0, 200);
            OverlayEdicionFase.IsVisible = false;
        }

        ActualizarListasVisuales();
    }

    private void OnOkClicked(object sender, EventArgs e)
    {
        OcultarTeclado();
        if (sender is Button btnOk && btnOk.BindingContext is ItemCheckList item)
        {
            item.Estado = "OK";

            btnOk.BackgroundColor = Color.FromArgb("#28a745");
            btnOk.Opacity = 1.0;

            if (btnOk.Parent is HorizontalStackLayout contenedorBotones && contenedorBotones.Children.Count > 1)
            {
                if (contenedorBotones.Children[1] is Button btnNo)
                {
                    btnNo.BackgroundColor = Color.FromArgb("#A2ADB9");
                    btnNo.Opacity = 0.4;
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

            btnNo.BackgroundColor = Color.FromArgb("#dc3545");
            btnNo.Opacity = 1.0;

            if (btnNo.Parent is HorizontalStackLayout contenedorBotones && contenedorBotones.Children.Count > 0)
            {
                if (contenedorBotones.Children[0] is Button btnOk)
                {
                    btnOk.BackgroundColor = Color.FromArgb("#A2ADB9");
                    btnOk.Opacity = 0.4;
                }
            }
        }
    }

    private async void OnConfirmarClicked(object sender, TappedEventArgs e)
    {
        try
        {
            if (sender is VisualElement btn) { await btn.ScaleTo(0.95, 50); await btn.ScaleTo(1.0, 50); }

            if (_modoEdicion)
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_EDICION_PENDIENTE"), LocalizationService.Translate("ALERT_EDICION_PENDIENTE_MSG"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
                return;
            }

            if (string.IsNullOrWhiteSpace(_chasis) || _chasis.Equals("Sin registrar", StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(_chasis, @"^[A-Z]{2}[0-9]{6}$"))
            {
                await DisplayAlert(LocalizationService.Translate("ALERT_ATENCION"), LocalizationService.Translate("ALERT_VIN_VALIDO_MSG"), LocalizationService.Translate("BTN_ENTENDIDO_OK"));
                return;
            }

            bool confirmar = await DisplayAlert(LocalizationService.Translate("ALERT_FINALIZAR"), LocalizationService.Translate("ALERT_CONFIRMAR_ENVIO_SIMPLE_MSG"), LocalizationService.Translate("BTN_SI_ENVIAR_CAPS"), LocalizationService.Translate("BTN_REVISAR"));

            if (confirmar)
            {
                string payloadJson = GenerarJsonParaListaSharePoint();
                await EnviarASharePointAsync(payloadJson);

                AutoGuardadoService.BorrarBackup();

                SesionGlobal.ChasisActual = string.Empty;
                SesionGlobal.VelocidadMaximaRodaje = 0;
                SesionGlobal.RutaTrazadaGPS?.Clear();

                await DisplayAlert(LocalizationService.Translate("ALERT_EXITO"), LocalizationService.Translate("ALERT_JAPON_GUARDADA_MSG"), "OK");

                var animaciones = new List<Task>();
                if (ContenedorTarjeta != null)
                    animaciones.Add(ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn));

                var btnGuardarFisico = this.FindByName<VisualElement>("BtnGuardar");
                if (btnGuardarFisico != null)
                    animaciones.Add(btnGuardarFisico.FadeTo(0, 300, Easing.CubicIn));

                if (animaciones.Any())
                    await Task.WhenAll(animaciones);

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
                            if (paginaABorrar != null)
                            {
                                Navigation.RemovePage(paginaABorrar);
                            }
                        }

                        await Navigation.PopAsync();
                    }
                    else
                    {
                        await Navigation.PopToRootAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Crash evitado en navegación: {ex.Message}");
            var navPage = new NavigationPage(new AuditModePage()) { BackgroundColor = Color.FromArgb("#ECEFF1") };
            Application.Current.MainPage = navPage;
        }
    }

    private string GenerarJsonParaListaSharePoint()
    {
        string FormatearListaCompleta(List<ItemCheckList>? lista)
        {
            if (lista == null || !lista.Any()) return "---";

            return string.Join("\n", lista.Select(x =>
            {
                string estadoReal = x.Estado == "NO OK" ? "NOK" : x.Estado;
                string textoFinal = $"{x.Check}";

                if (!string.IsNullOrWhiteSpace(x.ValorIntroducido))
                {
                    textoFinal += $" [{x.ValorIntroducido}]";
                }

                textoFinal += ": ";

                if (!string.IsNullOrWhiteSpace(x.Observacion))
                {
                    textoFinal += $"{x.Observacion}, ";
                }

                textoFinal += $"{estadoReal}";

                return textoFinal;
            }));
        }

        string FormatearEstadoSimple(List<ItemCheckList>? lista)
        {
            if (lista == null || !lista.Any()) return "---";

            return string.Join("\n", lista.Select(x =>
            {
                string estadoReal = x.Estado == "NO OK" ? "NOK" : x.Estado;
                string textoFinal = "";

                if (!string.IsNullOrWhiteSpace(x.ValorIntroducido))
                {
                    textoFinal += $"[{x.ValorIntroducido}] ";
                }

                if (!string.IsNullOrWhiteSpace(x.Observacion))
                {
                    textoFinal += $"{x.Observacion}, ";
                }

                textoFinal += $"{estadoReal}";

                return textoFinal.Trim();
            }));
        }

        var controlAspecto = _puntosControl?.Where(c => c.Check != null && c.Check.Contains("Aspecto", StringComparison.OrdinalIgnoreCase)).ToList();
        var controlNicho = _puntosControl?.Where(c => c.Check != null && c.Check.Contains("Nicho", StringComparison.OrdinalIgnoreCase)).ToList();
        var controlBajoCaja = _puntosControl?.Where(c => c.Check != null && (c.Check.Contains("Bajo", StringComparison.OrdinalIgnoreCase) || c.Check.Contains("Caja", StringComparison.OrdinalIgnoreCase))).ToList();
        var controlPistas = _puntosControl?.Where(c => c.Check != null && c.Check.Contains("Pista", StringComparison.OrdinalIgnoreCase)).ToList();

        var datosAuditoria = new
        {
            fields = new
            {
                Title = _chasis,
                Fecha = LblFecha.Text,
                Auditor = LblAuditor.Text,
                Turno = LblTurno.Text,
                Modelo = _modelo,
                Motor = _motor,
                Hora_Inicio = LblHoraInicio.Text,
                Hora_Final = LblHora.Text,
                Tiempo_Total = LblTiempoTotal.Text,

                Resultados_TT = FormatearListaCompleta(_puntosTT),

                Aspecto = FormatearEstadoSimple(controlAspecto),
                Nicho = FormatearEstadoSimple(controlNicho),
                Bajo_Caja = FormatearEstadoSimple(controlBajoCaja),
                Pistas = FormatearEstadoSimple(controlPistas),

                Resultados_Visto = FormatearListaCompleta(_puntosVisto),
                Resultados_Especificos = FormatearListaCompleta(_puntosOtros)
            }
        };

        return JsonSerializer.Serialize(datosAuditoria, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private async Task EnviarASharePointAsync(string payloadJson)
    {
        try
        {
            var spConexion = new SharePointService();
            string listId = "";

            bool exito = await spConexion.InsertarEnListaAsync(listId, payloadJson);
            if (!exito) throw new Exception("No se pudo insertar en SharePoint");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error SharePoint Japón: {ex.Message}");
            await DisplayAlert(LocalizationService.Translate("ERR_SHAREPOINT_TITULO"), ex.Message, "OK");
        }
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

    private void OnEntryCompleted(object sender, EventArgs e)
    {
        if (sender is Entry entry)
        {
            entry.Unfocus();
        }
        OcultarTeclado();
    }
}