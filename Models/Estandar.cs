using System;
using System.Collections.Generic;
using System.Text;

namespace Aplicacion_SCA.Models
{
    public class Estandar
    {
        public string NombreEstandar { get; set; } = string.Empty;
        public bool EsRodajeExterior { get; set; } = false;
        public List<ControlFase> ListaControles { get; set; } = new List<ControlFase>();
        public int TotalControles => ListaControles?.Count ?? 0;
    }
}
