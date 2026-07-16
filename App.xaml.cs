using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using System;
using System.Threading.Tasks;

namespace Aplicacion_SCA
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Preferences.Set("UltimoCrash", "Error Fatal: " + ex?.Message + "\n" + ex?.StackTrace);
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Preferences.Set("UltimoCrash", "Error Tarea Fondo: " + e.Exception?.Message + "\n" + e.Exception?.StackTrace);
                e.SetObserved(); 
            };
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var shell = new AppShell();
            var window = new Window(shell);

            // Esperar a Loaded: en Windows, Created se dispara antes de que la página
            // tenga XamlRoot y DisplayAlert lanza excepción (bucle de crash al arrancar).
            shell.Loaded += async (s, e) =>
            {
                string errorGuardado = Preferences.Get("UltimoCrash", "");
                if (string.IsNullOrEmpty(errorGuardado))
                    return;

                Preferences.Remove("UltimoCrash");
                try
                {
                    if (window.Page != null)
                    {
                        await window.Page.DisplayAlert("🚨 CRASH DETECTADO 🚨", errorGuardado, "Cerrar");
                    }
                }
                catch
                {
                    // Nunca dejar que el aviso de crash tumbe el arranque.
                }
            };

            return window;
        }
    }
}