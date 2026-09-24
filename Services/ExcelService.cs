using Aplicacion_SCA.Models;
using Aplicacion_SCA.Pages;
using ClosedXML.Excel;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Aplicacion_SCA.Services
{
    public class ExcelService
    {
        // Devuelve el sufijo de columna según el idioma activo ("_EN", "_FR", "_DE"; vacío para español).
        private static string SufijoIdioma()
        {
            return LocalizationService.CurrentLanguage switch
            {
                LocalizationService.Language.English => "_EN",
                LocalizationService.Language.French => "_FR",
                LocalizationService.Language.German => "_DE",
                _ => string.Empty
            };
        }

        // Mapea nombre de cabecera (fila 1) -> índice de columna. Insensible a mayúsculas; tolera huecos.
        private static Dictionary<string, int> ConstruirMapaCabeceras(IXLWorksheet ws)
        {
            var mapa = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= 60; col++)
            {
                string h = ws.Cell(1, col).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(h) && !mapa.ContainsKey(h))
                    mapa[h] = col;
            }
            return mapa;
        }

        // Lee la columna localizada "baseCol+sufijo"; si no existe o está vacía, usa el valor en español.
        private static string TextoLocalizado(IXLWorksheet ws, int fila, Dictionary<string, int> cabeceras, string baseCol, string sufijo, string valorEspanol)
        {
            if (!string.IsNullOrEmpty(sufijo) && cabeceras.TryGetValue(baseCol + sufijo, out int colIdx))
            {
                string v = ws.Cell(fila, colIdx).GetString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return valorEspanol;
        }

        // Cabecera de sección = la celda de la columna B ("Fase") tiene un relleno de
        // color (verificado contra el Excel real de MACK: las ~40 filas de sección
        // llevan Theme/Accent1, el resto de filas no llevan ningún relleno -Indexed
        // 64, "automático"-). No se basa en el texto ni en ninguna columna extra, así
        // que sigue funcionando aunque cambien los nombres de sección de un Excel a
        // otro - la única señal es el propio formato de la celda.
        private static bool EsFilaDeSeccion(IXLWorksheet ws, int fila)
        {
            var fill = ws.Cell(fila, 2).Style.Fill.BackgroundColor;
            return fill.ColorType != XLColorType.Indexed || fill.Indexed != 64;
        }

        public List<Estandar> LeerExcelAuditoria(Stream archivoStream)
        {
            var listaEstandares = new List<Estandar>();
            var mapaPorNombreEs = new Dictionary<string, Estandar>();
            var seccionPorEstandar = new Dictionary<string, string>();

            using (var workbook = new XLWorkbook(archivoStream))
            {
                var worksheet = workbook.Worksheet(1);
                var cabeceras = ConstruirMapaCabeceras(worksheet);
                string sufijo = SufijoIdioma();
                int fila = 2;

                while (!string.IsNullOrWhiteSpace(worksheet.Cell(fila, 1).GetString()))
                {
                    // El nombre en español agrupa las fases (clave estable); se muestra el nombre localizado.
                    string nombreEstandarEs = worksheet.Cell(fila, 1).GetString().Trim();
                    string nombreEstandarLocal = TextoLocalizado(worksheet, fila, cabeceras, "Estandar", sufijo, nombreEstandarEs);

                    if (!mapaPorNombreEs.TryGetValue(nombreEstandarEs, out var estandarActual))
                    {
                        estandarActual = new Estandar
                        {
                            NombreEstandar = nombreEstandarLocal,
                            ListaControles = new List<ControlFase>()
                        };
                        mapaPorNombreEs[nombreEstandarEs] = estandarActual;
                        listaEstandares.Add(estandarActual);
                    }

                    // Cabecera de sección: guarda el nombre para las filas siguientes de
                    // este mismo estándar y pasa a la fila siguiente sin crear ningún
                    // ControlFase - nunca cuenta como paso ejecutable ni se habla.
                    if (EsFilaDeSeccion(worksheet, fila))
                    {
                        seccionPorEstandar[nombreEstandarEs] = worksheet.Cell(fila, 2).GetString().Trim();
                        fila++;
                        continue;
                    }

                    int.TryParse(worksheet.Cell(fila, 7).GetString().Trim(), out int valTermico);
                    int.TryParse(worksheet.Cell(fila, 8).GetString().Trim(), out int valHibrido);
                    int.TryParse(worksheet.Cell(fila, 9).GetString().Trim(), out int valElectrico);

                    string latStr = worksheet.Cell(fila, 10).GetString().Trim();
                    string lonStr = worksheet.Cell(fila, 11).GetString().Trim();
                    string radStr = worksheet.Cell(fila, 12).GetString().Trim();

                    double.TryParse(latStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valLatitud);
                    double.TryParse(lonStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valLongitud);
                    double.TryParse(radStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valRadio);

                    var celdaExterior = worksheet.Cell(fila, 13);
                    int valExterior = 0;
                    if (celdaExterior.Value.IsNumber)
                        valExterior = (int)celdaExterior.Value.GetNumber();
                    else
                        int.TryParse(celdaExterior.GetString().Trim(), out valExterior);

                    var nuevoControl = new ControlFase
                    {
                        NumeroFase = estandarActual.ListaControles.Count + 1,
                        Fase = worksheet.Cell(fila, 2).GetString().Trim(),
                        Seccion = seccionPorEstandar.GetValueOrDefault(nombreEstandarEs, string.Empty),

                        AudioFormacion = TextoLocalizado(worksheet, fila, cabeceras, "AudioFormacion", sufijo, worksheet.Cell(fila, 3).GetString()),
                        TiempoFormacion = worksheet.Cell(fila, 4).GetString(),
                        AudioAuditoria = TextoLocalizado(worksheet, fila, cabeceras, "AudioAuditoria", sufijo, worksheet.Cell(fila, 5).GetString()),
                        TiempoAuditoria = worksheet.Cell(fila, 6).GetString(),

                        MotorTermico = valTermico,
                        MotorHibrido = valHibrido,
                        MotorElectrico = valElectrico,

                        Latitud = valLatitud,
                        Longitud = valLongitud,
                        Radio = valRadio,
                        Exterior = valExterior,
                        TipoPlantilla = worksheet.Cell(fila, 14).GetString().Trim().ToUpper()
                    };


                    estandarActual.ListaControles.Add(nuevoControl);
                    fila++;
                }
            }

            return listaEstandares;
        }
        public List<Estandar> LeerExcelDPV(Stream archivoStream)
        {
            var listaEstandares = new List<Estandar>();

            using (var workbook = new XLWorkbook(archivoStream))
            {
                var worksheet = workbook.Worksheet(1);
                int fila = 2;

                while (!string.IsNullOrWhiteSpace(worksheet.Cell(fila, 1).GetString()))
                {
                    string nombreEstandar = worksheet.Cell(fila, 1).GetString().Trim();
                    var estandarActual = listaEstandares.FirstOrDefault(e => e.NombreEstandar == nombreEstandar);

                    if (estandarActual == null)
                    {
                        estandarActual = new Estandar
                        {
                            NombreEstandar = nombreEstandar,
                            ListaControles = new List<ControlFase>()
                        };
                        listaEstandares.Add(estandarActual);
                    }

                    int.TryParse(worksheet.Cell(fila, 5).GetString().Trim(), out int valTermico);
                    int.TryParse(worksheet.Cell(fila, 6).GetString().Trim(), out int valHibrido);
                    int.TryParse(worksheet.Cell(fila, 7).GetString().Trim(), out int valElectrico);

                    string latStr = worksheet.Cell(fila, 8).GetString().Trim();
                    string lonStr = worksheet.Cell(fila, 9).GetString().Trim();
                    string radStr = worksheet.Cell(fila, 10).GetString().Trim();

                    double.TryParse(latStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valLatitud);
                    double.TryParse(lonStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valLongitud);
                    double.TryParse(radStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valRadio);

                    var celdaExterior = worksheet.Cell(fila, 11);
                    int valExterior = 0;
                    if (celdaExterior.Value.IsNumber)
                        valExterior = (int)celdaExterior.Value.GetNumber();
                    else
                        int.TryParse(celdaExterior.GetString().Trim(), out valExterior);

                    var nuevoControl = new ControlFase
                    {
                        NumeroFase = estandarActual.ListaControles.Count + 1,

                        AudioFormacion = "",
                        TiempoFormacion = "0",

                        AudioAuditoria = worksheet.Cell(fila, 3).GetString(),
                        TiempoAuditoria = worksheet.Cell(fila, 4).GetString(),

                        MotorTermico = valTermico,
                        MotorHibrido = valHibrido,
                        MotorElectrico = valElectrico,

                        Latitud = valLatitud,
                        Longitud = valLongitud,
                        Radio = valRadio,
                        Exterior = valExterior,

                        TipoPlantilla = worksheet.Cell(fila, 12).GetString().Trim().ToUpper()
                    };

                    estandarActual.ListaControles.Add(nuevoControl);
                    fila++;
                }
            }

            return listaEstandares;
        }
        public List<Usuario> LeerExcelUsuarios(Stream archivoStream)
        {
            var listaUsuarios = new List<Usuario>();

            using (var workbook = new XLWorkbook(archivoStream))
            {
                var worksheet = workbook.Worksheet(1);
                int fila = 2;

                while (!string.IsNullOrWhiteSpace(worksheet.Cell(fila, 1).GetString()))
                {
                    var nuevoUsuario = new Usuario
                    {
                        CV = worksheet.Cell(fila, 1).GetString().Trim(),
                        PSA = worksheet.Cell(fila, 2).GetString().Trim(),
                        NOMBRE = worksheet.Cell(fila, 3).GetString().Trim(),
                        APELLIDOS = worksheet.Cell(fila, 4).GetString().Trim(),
                        ROL = worksheet.Cell(fila, 5).GetString().Trim(),
                        TURNO = worksheet.Cell(fila, 6).GetString().Trim()
                    };

                    listaUsuarios.Add(nuevoUsuario);
                    fila++;
                }
            }
            return listaUsuarios;
        }

        public List<Vehiculo> LeerExcelVehiculos(Stream archivoStream)
        {
            var listaVehiculos = new List<Vehiculo>();

            using (var workbook = new XLWorkbook(archivoStream))
            {
                var worksheet = workbook.Worksheet(1);
                int fila = 2;

                while (!string.IsNullOrWhiteSpace(worksheet.Cell(fila, 1).GetString()))
                {
                    var nuevoVehiculo = new Vehiculo
                    {
                        Modelo = worksheet.Cell(fila, 1).GetString().Trim(),
                        Motor = worksheet.Cell(fila, 2).GetString().Trim()
                    };

                    listaVehiculos.Add(nuevoVehiculo);
                    fila++;
                }
            }
            return listaVehiculos;
        }

        public List<ControlFaseJapon> LeerExcelAudioJapon(Stream archivoStream)
        {
            var listaControles = new List<ControlFaseJapon>();

            using (var workbook = new XLWorkbook(archivoStream))
            {
                var worksheet = workbook.Worksheet(1);
                int fila = 2;

                while (!string.IsNullOrWhiteSpace(worksheet.Cell(fila, 2).GetString()))
                {
                    var nuevoControl = new ControlFaseJapon
                    {
                        Tipo = worksheet.Cell(fila, 1).GetString().Trim(),
                        Controles = worksheet.Cell(fila, 2).GetString().Trim(),
                        Audio = worksheet.Cell(fila, 3).GetString().Trim(),
                        Tiempo = worksheet.Cell(fila, 4).GetString().Trim(),
                        Imagen = worksheet.Cell(fila, 5).GetString().Trim(),
                        ModeloVehiculo = worksheet.Cell(fila, 6).GetString().Trim()
                    };

                    listaControles.Add(nuevoControl);
                    fila++;
                }
            }
            return listaControles;
        }

        public List<ItemCheckList> LeerExcelCheckListJapon(Stream archivoStream)
        {
            var listaCheckList = new List<ItemCheckList>();

            using (var workbook = new XLWorkbook(archivoStream))
            {
                var worksheet = workbook.Worksheet(1);
                int fila = 2;

                while (!string.IsNullOrWhiteSpace(worksheet.Cell(fila, 2).GetString()))
                {
                    var nuevoItem = new ItemCheckList
                    {
                        FaseCheck = worksheet.Cell(fila, 1).GetString().Trim(),
                        Check = worksheet.Cell(fila, 2).GetString().Trim(),
                        ModeloCheck = worksheet.Cell(fila, 3).GetString().Trim()
                    };

                    listaCheckList.Add(nuevoItem);
                    fila++;
                }
            }
            return listaCheckList;
        }

        public List<ItemParadaRRU> LeerExcelRRU(Stream archivoStream)
        {
            var listaParadas = new List<ItemParadaRRU>();

            using (var workbook = new XLWorkbook(archivoStream))
            {
                var worksheet = workbook.Worksheet(1);
                int fila = 2;
                int contador = 1;

                while (!string.IsNullOrWhiteSpace(worksheet.Cell(fila, 1).GetString()))
                {
                    var nuevaParada = new ItemParadaRRU
                    {
                        NumeroStr = contador.ToString(),
                        Punto = worksheet.Cell(fila, 1).GetString().Trim(),
                        Mensaje = worksheet.Cell(fila, 2).GetString().Trim(),
                        Imagen = worksheet.Cell(fila, 3).GetString().Trim()
                    };

                    listaParadas.Add(nuevaParada);

                    contador++;
                    fila++;
                }
            }
            return listaParadas;
        }
    }
}