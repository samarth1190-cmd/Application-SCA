using AndroidX.Core.View;

namespace Aplicacion_SCA.Platforms.Android
{
    // Alturas reales de la barra de estado y la barra/gesto de navegación del
    // sistema, en unidades independientes de densidad (DIP), para poder darle
    // a las cabeceras y pies de página el margen exacto que necesitan en modo
    // "edge-to-edge" (barras transparentes). Un valor fijo (p.ej. "24dp") no
    // vale para todos los dispositivos: varía con cámaras perforadas, barra de
    // gestos vs. botones, etc.
    public static class SafeAreaHelper
    {
        public static (double Top, double Bottom) ObtenerInsetsBarras(global::Android.Views.Window? window)
        {
            try
            {
                if (window?.DecorView == null) return (24, 24);

                var insets = ViewCompat.GetRootWindowInsets(window.DecorView);
                if (insets == null) return (24, 24);

                var systemBars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
                double density = global::Android.App.Application.Context.Resources?.DisplayMetrics?.Density ?? 1.0;
                if (density <= 0) density = 1.0;

                double top = systemBars.Top / density;
                double bottom = systemBars.Bottom / density;

                // Si la vista aún no está adjunta la primera vez, los insets pueden
                // llegar a 0 antes de tiempo: usar un mínimo razonable en su lugar.
                if (top <= 0) top = 24;
                if (bottom <= 0) bottom = 24;

                return (top, bottom);
            }
            catch
            {
                return (24, 24);
            }
        }
    }
}
