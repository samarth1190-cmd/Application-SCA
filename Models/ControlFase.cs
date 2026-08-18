namespace Aplicacion_SCA.Models
{
    public class ControlFase
    {
        public int NumeroFase { get; set; }
        // Valor original de la columna B ("Fase") del Excel. Es solo referencia
        // visual para el auditor (histórico de formación); no afecta a la lógica.
        public string Fase { get; set; } = string.Empty;
        public string AudioFormacion { get; set; } = string.Empty;
        public string TiempoFormacion { get; set; } = string.Empty;
        public string AudioAuditoria { get; set; } = string.Empty;
        public string TiempoAuditoria { get; set; } = string.Empty;
        public int MotorTermico { get; set; }
        public int MotorHibrido { get; set; }
        public int MotorElectrico { get; set; }
        public double Latitud { get; set; }
        public double Longitud { get; set; }
        public double Radio { get; set; }
        public int Exterior { get; set; }
        public string TipoPlantilla { get; set; } = string.Empty;

        public string TextoAMostrar
        {
            get
            {
                if (Services.SesionGlobal.ModoSeleccionado != null &&
                    Services.SesionGlobal.ModoSeleccionado.Contains("Formacion", System.StringComparison.OrdinalIgnoreCase))
                {
                    return !string.IsNullOrEmpty(AudioFormacion) ? AudioFormacion : "Sin texto de formación";
                }
                else
                {
                    return !string.IsNullOrEmpty(AudioAuditoria) ? AudioAuditoria : "Sin texto de auditoría";
                }
            }
        }
    }
}