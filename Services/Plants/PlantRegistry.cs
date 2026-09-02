using System.Collections.Generic;
using System.Linq;

namespace Aplicacion_SCA.Services.Plants
{
    public static class PlantRegistry
    {
        // Los valores de VIGO son EXACTAMENTE los que estaban repartidos por el
        // código antes del refactor multi-planta (ver docs/03_VIGO_COUPLING.md).
        public static readonly PlantDefinition Vigo = new()
        {
            Code = "VIGO",
            DisplayName = "Stellantis Vigo",
            SharePointRoot = "02_Datos_App_SCA",
            ResultsListId = "",                                   // pendiente: id real de producción
            ChassisPattern = @"^[A-Z]{2}[0-9]{6}$",
            ChassisMaxLength = 8,
            ChassisMinLength = 6,
            MotorKeywords = new MotorKeywordSet
            {
                Termico = new[] { "termic" },
                Hibrido = new[] { "hibrid" },
                Electrico = new[] { "electr" }
            },
            VoskModelAsset = "model_es.zip",
            VoskModelFolder = "model_es",
            VoiceCommands = new List<(string, string[])>
            {
                ("siguiente",   new[] { "sigue", "siguiente", "continuar" }),
                ("atras",       new[] { "atras", "anterior" }),
                ("pausa",       new[] { "pausa", "parar" }),
                ("repite",      new[] { "repetir" }),
                ("mas_detalle", new[] { "detalle", "explica", "explicame", "informacion" })
            },
            TtsWaitPhrase = "Espero para continuar.",
            SimulatedLatitude = 42.2037,
            SimulatedLongitude = -8.7428
        };

        public static readonly PlantDefinition Mack = new()
        {
            Code = "MACK",
            DisplayName = "Stellantis MACK",
            SharePointRoot = "02_Datos_App_MACK",
            ResultsListId = "",                                   // pendiente: lista propia de MACK
            // VIN ISO de 17 caracteres (sin I, O, Q).
            ChassisPattern = @"^[A-HJ-NPR-Z0-9]{17}$",
            ChassisMaxLength = 17,
            ChassisMinLength = 17,
            MotorKeywords = new MotorKeywordSet
            {
                Termico = new[] { "gas" },                        // MACK: "GAS"
                Hibrido = new[] { "hybrid", "hibrid" },           // MACK: "Hybrid"
                Electrico = new[] { "electr" }
            },
            VoskModelAsset = "model_en.zip",
            VoskModelFolder = "model_en",
            VoiceCommands = new List<(string, string[])>
            {
                ("siguiente",   new[] { "next", "continue" }),
                ("atras",       new[] { "back", "previous" }),
                ("pausa",       new[] { "pause", "stop" }),
                ("repite",      new[] { "repeat" }),
                ("mas_detalle", new[] { "detail", "explain", "information" })
            },
            TtsWaitPhrase = "Waiting to continue.",
            // Detroit (Mack) — solo para la simulación GPS en Windows.
            SimulatedLatitude = 42.3663,
            SimulatedLongitude = -82.9944
        };

        public static readonly IReadOnlyList<PlantDefinition> All = new[] { Vigo, Mack };

        public static PlantDefinition ByCode(string? code) =>
            All.FirstOrDefault(p => p.Code.Equals(code, System.StringComparison.OrdinalIgnoreCase)) ?? Vigo;
    }
}
