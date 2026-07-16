using Microsoft.Maui.Graphics;

namespace Aplicacion_SCA.Models
{
    public class ItemParadaRRU
    {
        public string NumeroStr { get; set; } = string.Empty;
        public string Punto { get; set; } = string.Empty;
        public string Mensaje { get; set; } = string.Empty;

        public string Imagen { get; set; } = string.Empty;

        public string RutaImagenCompleta => Imagen;

        public bool TieneImagen => !string.IsNullOrWhiteSpace(Imagen);
        public Color ColorBorde { get; set; } = Color.FromArgb("#EAECEF");
        public Color ColorIcono { get; set; } = Color.FromArgb("#EAECEF");
        public string InfoValidacion { get; set; } = "Pendiente de validación...";
        public Color ColorTextoInfo { get; set; } = Color.FromArgb("#90A4AE");
    }
}