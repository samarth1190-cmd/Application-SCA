using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Aplicacion_SCA;

[Activity(Theme = "@style/Maui.SplashTheme",
          MainLauncher = true,
          // La app es de un solo propósito (auditoría dedicada en tablet); sin esto,
          // Samsung/Android ofrece modo ventana libre/redimensionable con una barra de
          // título gris (minimizar, dividir pantalla, cerrar) que rompe el diseño a
          // pantalla completa y hace que todo el layout se vea mal calibrado.
          ResizeableActivity = false,
          ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}