using System;
using System.Collections.Generic;
using System.Linq;
using Aplicacion_SCA.Models;
using Aplicacion_SCA.Services.Plants;
using Microsoft.Maui.Storage;

namespace Aplicacion_SCA.Services
{
    // Filtro de pasos y clave de progreso compartidos entre EstandarPage (que
    // aplica el filtro para saber qué hablar/mostrar) y MenuEstandarPage (que
    // necesita el MISMO total para calcular un "% completado" real). Antes
    // cada página tenía su propia copia del filtro motor/pista/plantilla, y
    // ya estaban desincronizadas entre sí (MenuEstandarPage solo reconocía
    // "CORE_DPV"/"Formacion" como modo de 13 columnas, EstandarPage reconoce
    // también "C-DPV"/"C_DPV"/"SCA") — con esto solo hay un sitio que puede
    // desactualizarse.
    public static class AuditProgressHelper
    {
        public static string NormalizarTexto(string? texto)
        {
            if (string.IsNullOrEmpty(texto)) return "";
            return texto.Replace("á", "a").Replace("é", "e").Replace("í", "i")
                        .Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n");
        }

        public static bool EsModo13Columnas(string modo) =>
            modo.Contains("CORE_DPV", StringComparison.OrdinalIgnoreCase) ||
            modo.Contains("C-DPV", StringComparison.OrdinalIgnoreCase) ||
            modo.Contains("C_DPV", StringComparison.OrdinalIgnoreCase) ||
            modo.Contains("Formacion", StringComparison.OrdinalIgnoreCase) ||
            modo.Contains("SCA", StringComparison.OrdinalIgnoreCase);

        // Los mismos pasos que de verdad se hablarán/mostrarán en EstandarPage
        // para el motor y la pista (interior/exterior) elegidos: por motor
        // (o paso común a todos), por pista, y excluyendo plantillas RODAJE
        // (esas las gestiona RodajeExterior, no EstandarPage).
        public static List<ControlFase> FiltrarPasosReales(List<ControlFase>? todosLosPasos, bool esFormacion)
        {
            if (todosLosPasos == null) return new List<ControlFase>();

            string motorElegido = NormalizarTexto(SesionGlobal.MotorSeleccionado?.ToLower().Trim() ?? "");
            var kw = PlantContext.Current.MotorKeywords;

            return todosLosPasos.Where(p =>
            {
                string texto = (esFormacion ? p.AudioFormacion : p.AudioAuditoria) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(texto)) return false;

                bool pasoComun = (p.MotorTermico == 0 && p.MotorHibrido == 0 && p.MotorElectrico == 0);
                bool esParaEsteMotor = pasoComun ||
                                       (kw.Termico.Any(k => motorElegido.Contains(k)) && p.MotorTermico == 1) ||
                                       (kw.Hibrido.Any(k => motorElegido.Contains(k)) && p.MotorHibrido == 1) ||
                                       (kw.Electrico.Any(k => motorElegido.Contains(k)) && p.MotorElectrico == 1);

                bool esParaEstaPista = !SesionGlobal.EsRodajeExterior
                                       ? (p.Exterior == 0 || p.Exterior == 1)
                                       : (p.Exterior == 0 || p.Exterior == 2);

                bool esTipoEstandar = string.IsNullOrEmpty(p.TipoPlantilla) ||
                                      !p.TipoPlantilla.ToUpper().Contains("RODAJE");

                return esParaEsteMotor && esParaEstaPista && esTipoEstandar;
            }).ToList();
        }

        // Misma clave que usa EstandarPage para guardar/recuperar en qué paso
        // se quedó el auditor (por VIN + índice de fase) — centralizada aquí
        // para que MenuEstandarPage pueda leer exactamente el mismo valor al
        // calcular el progreso, sin duplicar el formato del string.
        public static string ClaveGuardadoPaso(string chasis, int indiceEstandar) =>
            $"PasoGuardado_{chasis}_{indiceEstandar}";

        public static int ObtenerPasoGuardado(string chasis, int indiceEstandar) =>
            Preferences.Get(ClaveGuardadoPaso(chasis, indiceEstandar), 0);
    }
}
