using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Aplicacion_SCA.Pages;

// Pantalla intermedia del flujo C-DPV: elige la planta (Vigo, MACK, ...) antes de
// la selección de modelo/motor. El resto de modos no pasan por aquí y siempre
// trabajan con Vigo (AuditModePage resetea PlantContext al aparecer).
public partial class PlantSelectionPage : ContentPage
{
    private readonly string _modoOperativo;
    private bool _isNavegando = false;

    public PlantSelectionPage(string modo = "CORE_DPV")
    {
        InitializeComponent();
        _modoOperativo = modo;
        GenerarBotonesPlantas();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isNavegando = false;

        LblVolver.Text = LocalizationService.Translate("BTN_VOLVER");
        LblTitulo.Text = LocalizationService.Translate("PLANT_SELECT_TITLE");
        LblSubtitulo.Text = LocalizationService.Translate("PLANT_SELECT_SUBTITLE");
        LblEstado.IsVisible = false;

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
                    ContenedorTarjeta.FadeTo(1, 700, Easing.CubicOut),
                    ContenedorTarjeta.TranslateTo(0, 0, 700, Easing.CubicOut)
                );
            });
        }
        catch
        {
            ContenedorTarjeta.Opacity = 1;
            ContenedorTarjeta.TranslationY = 0;
        }
    }

    private void GenerarBotonesPlantas()
    {
        ContenedorPlantas.Children.Clear();

        foreach (var planta in PlantRegistry.All)
        {
            var lbl = new Label
            {
                Text = planta.DisplayName.ToUpper(),
                TextColor = Colors.White,
                FontSize = 16,
                CharacterSpacing = 1,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };

            var btn = new Border
            {
                HeightRequest = 70,
                WidthRequest = 250,
                StrokeThickness = 0,
                HorizontalOptions = LayoutOptions.Center,
                Background = new SolidColorBrush(Color.FromArgb(planta.Code == "VIGO" ? "#243782" : "#43AAA0")),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(35) },
                Shadow = new Shadow
                {
                    Brush = new SolidColorBrush(Colors.Black),
                    Offset = new Point(0, 7),
                    Opacity = 0.2f,
                    Radius = 10
                },
                Content = lbl
            };

            var plantaCapturada = planta;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) => await OnPlantaSeleccionada(btn, plantaCapturada);
            btn.GestureRecognizers.Add(tap);

            ContenedorPlantas.Children.Add(btn);
        }
    }

    private async Task OnPlantaSeleccionada(VisualElement boton, PlantDefinition planta)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        try
        {
            await boton.ScaleTo(0.95, 50);
            await boton.ScaleTo(1.0, 50);

            PlantContext.Set(planta);

            // La lista de vehículos se carga en el arranque con los datos de Vigo;
            // si la planta elegida es otra (o se cambió antes), se recarga de su carpeta.
            if (PlantContext.VehiclesLoadedForPlant != planta.Code)
            {
                LblEstado.Text = LocalizationService.Translate("MSG_CARGANDO_VEHICULOS");
                LblEstado.IsVisible = true;

                bool ok = await CargarVehiculosDePlantaAsync();

                LblEstado.IsVisible = false;

                if (!ok)
                {
                    PlantContext.Reset();
                    await DisplayAlert(
                        LocalizationService.Translate("ERR_CONEXION_TITULO"),
                        LocalizationService.Translate("ERR_VEHICULOS_PLANTA"),
                        "OK");
                    return;
                }
            }

            SesionGlobal.ModoSeleccionado = _modoOperativo;
            await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
            await Navigation.PushAsync(new SelectionPage(_modoOperativo));
        }
        finally
        {
            _isNavegando = false;
        }
    }

    private async Task<bool> CargarVehiculosDePlantaAsync()
    {
        try
        {
            var sp = new SharePointService();
            var excelService = new ExcelService();
            string token = await sp.ConseguirTokenSilenciosoAsync();

            byte[] bytes = await sp.DescargarExcelConTokenAsync(
                PlantContext.ResolvePath("02_Configuraciones/Vehiculos.xlsx"), token);

            using var stream = new MemoryStream(bytes);
            var vehiculos = excelService.LeerExcelVehiculos(stream);

            if (vehiculos == null || vehiculos.Count == 0) return false;

            SesionGlobal.ListaVehiculos = vehiculos;
            PlantContext.VehiclesLoadedForPlant = PlantContext.Current.Code;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando vehículos de planta: {ex.Message}");
            return false;
        }
    }

    private async void OnVolverClicked(object? sender, TappedEventArgs e)
    {
        if (_isNavegando) return;
        _isNavegando = true;

        if (sender is VisualElement btn)
        {
            await btn.ScaleTo(0.9, 50);
            await btn.ScaleTo(1.0, 50);
        }

        await ContenedorTarjeta.FadeTo(0, 300, Easing.CubicIn);
        await Navigation.PopAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        OnVolverClicked(null, new TappedEventArgs(null));
        return true;
    }
}
