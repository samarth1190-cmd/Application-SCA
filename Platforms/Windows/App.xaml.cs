using Microsoft.UI.Xaml;
using System;
using System.IO;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Aplicacion_SCA.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        private static readonly string _crashLog = Path.Combine(Path.GetTempPath(), "SCA_crash.log");

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // Hook unhandled WinUI exceptions before anything else
            this.UnhandledException += (s, e) =>
            {
                File.AppendAllText(_crashLog,
                    $"[{DateTime.Now}] WinUI UnhandledException:\n{e.Exception}\n\n");
                e.Handled = false; // let it crash so we capture it
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                File.AppendAllText(_crashLog,
                    $"[{DateTime.Now}] AppDomain UnhandledException:\n{e.ExceptionObject}\n\n");
            };

            try
            {
                this.InitializeComponent();
            }
            catch (Exception ex)
            {
                File.AppendAllText(_crashLog,
                    $"[{DateTime.Now}] CRASH in InitializeComponent:\n{ex}\n\n");
                throw;
            }
        }

        protected override MauiApp CreateMauiApp()
        {
            try
            {
                return MauiProgram.CreateMauiApp();
            }
            catch (Exception ex)
            {
                File.AppendAllText(_crashLog,
                    $"[{DateTime.Now}] CRASH in CreateMauiApp:\n{ex}\n\n");
                throw;
            }
        }
    }

}
