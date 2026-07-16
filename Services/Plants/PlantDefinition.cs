using System.Collections.Generic;

namespace Aplicacion_SCA.Services.Plants
{
    // Palabras clave (en minúsculas, sin tildes) que identifican cada tipo de motor
    // dentro del nombre de motor elegido en Vehiculos.xlsx.
    public class MotorKeywordSet
    {
        public string[] Termico { get; init; } = System.Array.Empty<string>();
        public string[] Hibrido { get; init; } = System.Array.Empty<string>();
        public string[] Electrico { get; init; } = System.Array.Empty<string>();
    }

    // Todo lo que distingue a una planta. Añadir una planta nueva = añadir una
    // entrada en PlantRegistry + crear su carpeta de SharePoint con la misma
    // estructura interna que la de Vigo.
    public class PlantDefinition
    {
        public string Code { get; init; } = string.Empty;          // "VIGO", "MAC"
        public string DisplayName { get; init; } = string.Empty;

        // Carpeta raíz en la biblioteca de documentos de SharePoint.
        // Las subcarpetas (01_Usuarios, 05_CDPV_Formacion, ...) son idénticas en todas las plantas.
        public string SharePointRoot { get; init; } = string.Empty;

        // Lista de SharePoint donde se insertan los resultados (una por planta).
        public string ResultsListId { get; init; } = string.Empty;

        // Formato del chasis/VIN.
        public string ChassisPattern { get; init; } = string.Empty;
        public int ChassisMaxLength { get; init; }
        public int ChassisMinLength { get; init; }

        public MotorKeywordSet MotorKeywords { get; init; } = new();

        // Reconocimiento de voz (Vosk).
        public string VoskModelAsset { get; init; } = string.Empty;   // zip en Resources/Raw
        public string VoskModelFolder { get; init; } = string.Empty;  // carpeta de extracción en caché

        // Comandos de voz: (comando interno, palabras que lo activan). El orden importa.
        public IReadOnlyList<(string Comando, string[] Palabras)> VoiceCommands { get; init; }
            = System.Array.Empty<(string, string[])>();

        // Frase TTS "espero para continuar" durante la validación manual en rodaje.
        public string TtsWaitPhrase { get; init; } = string.Empty;

        // GPS simulado en Windows (desarrollo).
        public double SimulatedLatitude { get; init; }
        public double SimulatedLongitude { get; init; }
    }
}
