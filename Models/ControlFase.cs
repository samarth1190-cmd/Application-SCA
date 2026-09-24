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

        // Nombre de la sección a la que pertenece este paso (viene de la última fila
        // "cabecera de sección" -fondo azul en la columna B del Excel- vista antes de
        // esta fila). Las propias filas de cabecera de sección nunca llegan a ser un
        // ControlFase (ExcelService las detecta y las salta), así que este campo
        // siempre refleja la sección real del paso, nunca la del paso en sí mismo.
        public string Seccion { get; set; } = string.Empty;

        // AudioFormacion como "Requisito de la prueba": solo tiene sentido en el
        // flujo de auditoría (no en modo Formación, donde AudioFormacion ya es el
        // texto principal). "0" en el Excel significa explícitamente "sin requisito".
        public bool TieneRequisitoTest =>
            !string.IsNullOrWhiteSpace(AudioFormacion) && AudioFormacion.Trim() != "0";

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