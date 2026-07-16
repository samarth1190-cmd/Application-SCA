using System;
using System.Collections.Generic;
using System.Text;
using Aplicacion_SCA.Models;
using System.Text.Json;
using System.IO;
using Microsoft.Maui.Storage;
using System.Threading.Tasks;

namespace Aplicacion_SCA.Services
{
    public class EstadoAuditoriaBackup
    {
        // Planta activa cuando se hizo la copia (backups antiguos: null → Vigo).
        public string? PlantCode { get; set; }
        public Usuario? UsuarioActivo { get; set; }
        public string? ChasisActual { get; set; }
        public string MotorSeleccionado { get; set; } = string.Empty;
        public string ModeloSeleccionado { get; set; } = string.Empty;
        public string ModoSeleccionado { get; set; } = string.Empty;

        public bool EsRodajeExterior { get; set; }

        public int IndiceEstandarActual { get; set; }
        public List<int> EstandaresCompletados { get; set; } = new List<int>();
        public DateTime HoraInicioInspeccion { get; set; }
        public Dictionary<string, double> TiemposPorFase { get; set; } = new Dictionary<string, double>();

        public double VelocidadMaximaRodaje { get; set; }
        public List<PuntoRuta> PuntosRuta { get; set; } = new List<PuntoRuta>();

        public List<ControlFaseJapon> ListaControlesJapon { get; set; } = new List<ControlFaseJapon>();
        public List<ItemCheckList> ListaCheckListJapon { get; set; } = new List<ItemCheckList>();

        public List<ItemParadaRRU> ListaParadasRRU { get; set; } = new List<ItemParadaRRU>();
        public List<Microsoft.Maui.Devices.Sensors.Location> ParadasRRU { get; set; } = new List<Microsoft.Maui.Devices.Sensors.Location>();
    }

    public static class AutoGuardadoService
    {
        private static readonly string rutaArchivo = Path.Combine(FileSystem.AppDataDirectory, "auditoria_encurso_backup.json");

        public static async Task GuardarProgresoAsync()
        {
            try
            {
                var backup = new EstadoAuditoriaBackup
                {
                    PlantCode = Plants.PlantContext.Current.Code,
                    UsuarioActivo = SesionGlobal.UsuarioActivo,
                    ChasisActual = SesionGlobal.ChasisActual,
                    MotorSeleccionado = SesionGlobal.MotorSeleccionado,
                    ModeloSeleccionado = SesionGlobal.ModeloSeleccionado,
                    ModoSeleccionado = SesionGlobal.ModoSeleccionado,

                    EsRodajeExterior = SesionGlobal.EsRodajeExterior,

                    IndiceEstandarActual = SesionGlobal.IndiceEstandarActual,
                    EstandaresCompletados = SesionGlobal.EstandaresCompletados ?? new List<int>(),
                    HoraInicioInspeccion = SesionGlobal.HoraInicioInspeccion,
                    TiemposPorFase = SesionGlobal.TiemposPorFase ?? new Dictionary<string, double>(),
                    VelocidadMaximaRodaje = SesionGlobal.VelocidadMaximaRodaje,
                    PuntosRuta = SesionGlobal.PuntosRuta ?? new List<PuntoRuta>(),

                    ListaControlesJapon = SesionGlobal.ListaControlesJapon ?? new List<ControlFaseJapon>(),
                    ListaCheckListJapon = SesionGlobal.ListaCheckListJapon ?? new List<ItemCheckList>(),
                    ListaParadasRRU = SesionGlobal.ListaParadasRRU ?? new List<ItemParadaRRU>(),
                    ParadasRRU = SesionGlobal.ParadasRRU ?? new List<Microsoft.Maui.Devices.Sensors.Location>()
                };

                string json = JsonSerializer.Serialize(backup);
                await File.WriteAllTextAsync(rutaArchivo, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en autoguardado: " + ex.Message);
            }
        }

        public static bool ExisteCopiaDeSeguridad()
        {
            return File.Exists(rutaArchivo);
        }

        public static string ObtenerPSABackup()
        {
            try
            {
                if (!ExisteCopiaDeSeguridad()) return "";
                string json = File.ReadAllText(rutaArchivo);
                var backup = JsonSerializer.Deserialize<EstadoAuditoriaBackup>(json);

                return backup?.UsuarioActivo?.PSA ?? "";
            }
            catch
            {
                return "";
            }
        }

        public static void RecuperarProgreso()
        {
            try
            {
                if (!ExisteCopiaDeSeguridad()) return;

                string json = File.ReadAllText(rutaArchivo);
                var backup = JsonSerializer.Deserialize<EstadoAuditoriaBackup>(json);

                if (backup != null)
                {
                    Plants.PlantContext.SetByCode(backup.PlantCode);
                    SesionGlobal.UsuarioActivo = backup.UsuarioActivo;
                    SesionGlobal.ChasisActual = backup.ChasisActual;
                    SesionGlobal.MotorSeleccionado = backup.MotorSeleccionado;
                    SesionGlobal.ModeloSeleccionado = backup.ModeloSeleccionado;
                    SesionGlobal.ModoSeleccionado = backup.ModoSeleccionado;

                    SesionGlobal.EsRodajeExterior = backup.EsRodajeExterior;

                    SesionGlobal.IndiceEstandarActual = backup.IndiceEstandarActual;
                    SesionGlobal.EstandaresCompletados = backup.EstandaresCompletados;
                    SesionGlobal.HoraInicioInspeccion = backup.HoraInicioInspeccion;
                    SesionGlobal.TiemposPorFase = backup.TiemposPorFase;
                    SesionGlobal.VelocidadMaximaRodaje = backup.VelocidadMaximaRodaje;
                    SesionGlobal.PuntosRuta = backup.PuntosRuta;

                    SesionGlobal.ListaControlesJapon = backup.ListaControlesJapon;
                    SesionGlobal.ListaCheckListJapon = backup.ListaCheckListJapon;
                    SesionGlobal.ListaParadasRRU = backup.ListaParadasRRU;
                    SesionGlobal.ParadasRRU = backup.ParadasRRU;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al recuperar backup: " + ex.Message);
            }
        }

        public static void BorrarBackup()
        {
            if (ExisteCopiaDeSeguridad())
            {
                File.Delete(rutaArchivo);
            }
        }
    }
}