namespace Aplicacion_SCA.Services.Plants
{
    // Planta activa de la sesión. Por defecto (y para todos los modos salvo
    // C-DPV) es Vigo; PlantSelectionPage la cambia al entrar en C-DPV.
    public static class PlantContext
    {
        public static PlantDefinition Current { get; private set; } = PlantRegistry.Vigo;

        // Planta para la que se cargó SesionGlobal.ListaVehiculos (se llena en el
        // arranque con los vehículos de Vigo; PlantSelectionPage la recarga si cambia).
        public static string? VehiclesLoadedForPlant { get; set; }

        public static void Set(PlantDefinition plant) => Current = plant;

        public static void SetByCode(string? code) => Current = PlantRegistry.ByCode(code);

        public static void Reset() => Current = PlantRegistry.Vigo;

        // Convierte una ruta relativa común ("03_Documentos_pdf/x.pdf") en la ruta
        // completa de SharePoint de la planta activa.
        public static string ResolvePath(string relativePath) =>
            $"{Current.SharePointRoot}/{relativePath.TrimStart('/')}";
    }
}
