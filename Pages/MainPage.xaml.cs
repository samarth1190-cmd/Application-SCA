using Aplicacion_SCA.Models;
using Aplicacion_SCA.Services;
using Aplicacion_SCA.Services.Plants;
using Microsoft.Maui.Controls;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace Aplicacion_SCA.Pages;

public partial class MainPage : ContentPage
{
    private bool _isNavigating = false;

    public MainPage()
    {
        InitializeComponent();
        ApplyTranslations();
        UpdateLanguageSelectionUI();

        // Discreet corner label so someone can check they're on the latest
        // build without it competing with the actual UI. Read from AppInfo
        // (which mirrors ApplicationDisplayVersion from the csproj) instead
        // of a hardcoded string, so it can never drift out of sync with the
        // build that's actually installed.
        LblVersion.Text = $"v{AppInfo.Current.VersionString}";
    }

    private void ApplyTranslations()
    {
        TituloTexto.Text = LocalizationService.Translate("MAIN_TITLE");
        if (!_isNavigating)
        {
            LblTextoEmpezar.Text = LocalizationService.Translate("BTN_EMPEZAR");
        }
        else
        {
            LblTextoEmpezar.Text = LocalizationService.Translate("MSG_CARGANDO_DATOS");
        }
    }

    private void UpdateLanguageSelectionUI()
    {
        ResetButtonHighlight(BtnLangES, LblLangES);
        ResetButtonHighlight(BtnLangEN, LblLangEN);
        ResetButtonHighlight(BtnLangFR, LblLangFR);
        ResetButtonHighlight(BtnLangDE, LblLangDE);

        switch (LocalizationService.CurrentLanguage)
        {
            case LocalizationService.Language.Spanish:
                HighlightButton(BtnLangES, LblLangES);
                break;
            case LocalizationService.Language.English:
                HighlightButton(BtnLangEN, LblLangEN);
                break;
            case LocalizationService.Language.French:
                HighlightButton(BtnLangFR, LblLangFR);
                break;
            case LocalizationService.Language.German:
                HighlightButton(BtnLangDE, LblLangDE);
                break;
        }
    }

    private void HighlightButton(Border btn, Label lbl)
    {
        btn.BackgroundColor = Color.FromArgb("#43AAA0");
        btn.Stroke = Color.FromArgb("#43AAA0");
        lbl.TextColor = Colors.White;
    }

    private void ResetButtonHighlight(Border btn, Label lbl)
    {
        btn.BackgroundColor = Colors.White;
        btn.Stroke = Color.FromArgb("#A2ADB9");
        lbl.TextColor = Color.FromArgb("#243782");
    }

    private async void OnLangESClicked(object sender, EventArgs e)
    {
        await AnimarBoton(sender as VisualElement);
        LocalizationService.CurrentLanguage = LocalizationService.Language.Spanish;
        ApplyTranslations();
        UpdateLanguageSelectionUI();
    }

    private async void OnLangENClicked(object sender, EventArgs e)
    {
        await AnimarBoton(sender as VisualElement);
        LocalizationService.CurrentLanguage = LocalizationService.Language.English;
        ApplyTranslations();
        UpdateLanguageSelectionUI();
    }

    private async void OnLangFRClicked(object sender, EventArgs e)
    {
        await AnimarBoton(sender as VisualElement);
        LocalizationService.CurrentLanguage = LocalizationService.Language.French;
        ApplyTranslations();
        UpdateLanguageSelectionUI();
    }

    private async void OnLangDEClicked(object sender, EventArgs e)
    {
        await AnimarBoton(sender as VisualElement);
        LocalizationService.CurrentLanguage = LocalizationService.Language.German;
        ApplyTranslations();
        UpdateLanguageSelectionUI();
    }

    private async Task AnimarBoton(VisualElement? elemento)
    {
        if (elemento == null) return;
        await elemento.ScaleTo(0.9, 50, Easing.Linear);
        await elemento.ScaleTo(1.0, 50, Easing.Linear);
    }

    private async Task<Stream> ObtenerArchivoTurboAsync(SharePointService sp, string rutaCompleta, string token)
    {
        try
        {
            byte[] archivoBytes = await sp.DescargarExcelConTokenAsync(rutaCompleta, token);
            return new MemoryStream(archivoBytes);
        }
        catch (Exception ex)
        {
            throw new Exception($"Fallo en {rutaCompleta}: {ex.Message}");
        }
    }

    private async void OnEmpezarClicked(object sender, EventArgs e)
    {
        if (_isNavigating) return;

        try
        {
            _isNavigating = true;

            await BtnEmpezar.ScaleTo(0.95, 80, Easing.CubicOut);
            await BtnEmpezar.ScaleTo(1.0, 80, Easing.CubicIn);

            LblTextoEmpezar.Text = LocalizationService.Translate("MSG_CARGANDO_DATOS");

            await Task.Run(async () =>
            {
                var excelService = new ExcelService();
                var spConexion = new SharePointService();

                string token = await spConexion.ConseguirTokenSilenciosoAsync();
                string rutaUsuarios = PlantContext.ResolvePath("01_Usuarios/Usuarios.xlsx");
                string rutaVehiculos = PlantContext.ResolvePath("02_Configuraciones/Vehiculos.xlsx");

                var tareaUsuarios = ObtenerArchivoTurboAsync(spConexion, rutaUsuarios, token);
                var tareaVehiculos = ObtenerArchivoTurboAsync(spConexion, rutaVehiculos, token);

                await Task.WhenAll(tareaUsuarios, tareaVehiculos);

                using (var streamUsuarios = tareaUsuarios.Result)
                {
                    SesionGlobal.ListaUsuarios = excelService.LeerExcelUsuarios(streamUsuarios);
                }

                using (var streamVehiculos = tareaVehiculos.Result)
                {
                    SesionGlobal.ListaVehiculos = excelService.LeerExcelVehiculos(streamVehiculos);
                    PlantContext.VehiclesLoadedForPlant = PlantContext.Current.Code;
                }

                SesionGlobal.Estandares?.Clear();
                SesionGlobal.EstandaresDPV?.Clear();
                SesionGlobal.ListaControlesJapon?.Clear();
                SesionGlobal.ListaCheckListJapon?.Clear();
                SesionGlobal.ListaParadasRRU?.Clear();
                SesionGlobal.IndiceEstandarActual = 0;

                int cantidadModelos = SesionGlobal.ListaVehiculos?.Select(v => v.Modelo).Distinct().Count() ?? 0;
                int cantidadMotores = SesionGlobal.ListaVehiculos?.Select(v => v.Motor).Distinct().Count() ?? 0;

                string activeUsersText = LocalizationService.CurrentLanguage switch
                {
                    LocalizationService.Language.English => "active users",
                    LocalizationService.Language.French => "utilisateurs actifs",
                    LocalizationService.Language.German => "aktive Benutzer",
                    _ => "Usuarios activos"
                };
                string downloadAuditText = LocalizationService.CurrentLanguage switch
                {
                    LocalizationService.Language.English => "(Specific audit data will be downloaded upon selection)",
                    LocalizationService.Language.French => "(Les données spécifiques de l'audit seront téléchargées lors de la sélection)",
                    LocalizationService.Language.German => "(Spezifische Auditdaten werden bei der Auswahl heruntergeladen)",
                    _ => "(Los datos específicos de la auditoría se descargarán al seleccionarla)"
                };
                string baseDbLoadedText = LocalizationService.CurrentLanguage switch
                {
                    LocalizationService.Language.English => "Main database loaded:",
                    LocalizationService.Language.French => "Base de données principale chargée :",
                    LocalizationService.Language.German => "Hauptdatenbank geladen:",
                    _ => "Base de datos principal cargada:"
                };

                SesionGlobal.ResumenBaseDatos =
                    $"{baseDbLoadedText}\n\n" +
                    $"- {SesionGlobal.ListaUsuarios?.Count ?? 0} {activeUsersText}\n" +
                    $"{downloadAuditText}";
            });

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                LblTextoEmpezar.Text = LocalizationService.Translate("BTN_EMPEZAR"); 
                await Navigation.PushAsync(new LoginPage());
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                LblTextoEmpezar.Text = LocalizationService.Translate("BTN_EMPEZAR");
                await DisplayAlert(
                    LocalizationService.Translate("ERR_CONEXION_TITULO"), 
                    LocalizationService.Translate("ERR_CONEXION_MSG") + ex.Message, 
                    LocalizationService.Translate("BTN_REINTENTAR")
                );
                _isNavigating = false;
            });
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isNavigating = false;
        ApplyTranslations();
        UpdateLanguageSelectionUI();

#if ANDROID
        var window = Platform.CurrentActivity?.Window;
        if (window != null)
        {
            window.SetStatusBarColor(Android.Graphics.Color.Transparent);
            window.SetFlags(Android.Views.WindowManagerFlags.LayoutNoLimits, Android.Views.WindowManagerFlags.LayoutNoLimits);

            var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
            if (controller != null)
            {
                controller.AppearanceLightStatusBars = true;
                controller.AppearanceLightNavigationBars = true;
            }
        }
#endif

        try
        {
            ContenedorPrincipal.Opacity = 0;
            ContenedorPrincipal.TranslationY = 60;

            await Task.WhenAll(
                ContenedorPrincipal.FadeTo(1, 1200, Easing.CubicOut),
                ContenedorPrincipal.TranslateTo(0, 0, 1200, Easing.CubicOut)
            );
        }
        catch (Exception)
        {
            ContenedorPrincipal.Opacity = 1;
            ContenedorPrincipal.TranslationY = 0;
        }
    }
}
