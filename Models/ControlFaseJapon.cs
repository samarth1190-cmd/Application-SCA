using System;
using System.Collections.Generic;
using System.Text;

namespace Aplicacion_SCA.Models
{
    public class ControlFaseJapon
    {
        public string Tipo { get; set; } = string.Empty;
        public string Controles { get; set; } = string.Empty;
        public string Audio { get; set; } = string.Empty;
        public string Tiempo { get; set; } = string.Empty;
        public string Imagen { get; set; } = string.Empty;
        public string ModeloVehiculo { get; set; } = string.Empty;
        public string RutaImagenCompleta
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Imagen))
                    return string.Empty;

                string nombreArchivo = Imagen.Trim().ToLower();

#if WINDOWS
                return $@"C:\Users\ta32124\Desktop\Aplicacion_SCA\Japon\{Imagen.Trim()}";
#else
                return nombreArchivo;
#endif
            }
        }
    }
}