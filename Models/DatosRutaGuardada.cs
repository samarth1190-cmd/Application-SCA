using Aplicacion_SCA.Services;
using Microsoft.Maui.Devices.Sensors;
using System.Collections.Generic;

namespace Aplicacion_SCA.Models
{
    public class DatosRutaGuardada
    {
        public string NombreFase { get; set; } = string.Empty;
        public double VelocidadMaxima { get; set; }
        public List<Location> Ruta { get; set; } = new List<Location>();
        public List<PuntoRuta> Defectos { get; set; } = new List<PuntoRuta>();
    }
}