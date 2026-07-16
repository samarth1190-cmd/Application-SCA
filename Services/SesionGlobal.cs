using Aplicacion_SCA.Models;
using System;
using System.Collections.Generic;

namespace Aplicacion_SCA.Services;

public class Usuario
{
    public string CV { get; set; } = string.Empty;
    public string PSA { get; set; } = string.Empty;
    public string NOMBRE { get; set; } = string.Empty;
    public string APELLIDOS { get; set; } = string.Empty;
    public string ROL { get; set; } = string.Empty;

    private string _turno = string.Empty;
    public string TURNO
    {
        get => _turno;
        set => _turno = TransformarTurno(value);
    }

    private string TransformarTurno(string letra)
    {
        if (string.IsNullOrEmpty(letra)) return "Sin turno definido";

        return letra.ToUpper().Trim() switch
        {
            "A" => "Turno A",
            "B" => "Turno B",
            "C" => "Turno Central",
            "N" => "Turno Noche",
            "W" => "Turno Fin de Semana",
            _ => $"Turno {letra}"
        };
    }
}

public static class SesionGlobal
{
    public static DateTime HoraInicioFaseActual { get; set; }
    public static Dictionary<string, double> TiemposPorFase { get; set; } = new Dictionary<string, double>();
    public static Usuario? UsuarioActivo { get; set; }
    public static string? ChasisActual { get; set; }
    public static bool HaySesionActiva => UsuarioActivo != null;
    public static string MotorSeleccionado { get; set; } = string.Empty;
    public static string ModeloSeleccionado { get; set; } = string.Empty;
    public static List<Usuario> ListaUsuarios { get; set; } = new List<Usuario>();
    public static List<Estandar> Estandares { get; set; } = new List<Estandar>();
    public static List<Estandar> EstandaresDPV { get; set; } = new List<Estandar>();
    public static bool EsRodajeExterior { get; set; } = false;
    public static List<Vehiculo> ListaVehiculos { get; set; } = new List<Vehiculo>();
    public static List<ControlFaseJapon> ListaControlesJapon { get; set; } = new List<ControlFaseJapon>();
    public static List<ItemCheckList> ListaCheckListJapon { get; set; } = new List<ItemCheckList>();
    public static List<ItemParadaRRU> ListaParadasRRU { get; set; } = new List<ItemParadaRRU>();
    public static List<int> EstandaresCompletados { get; set; } = new List<int>();
    public static int IndiceEstandarActual { get; set; } = 0;
    public static string ModoSeleccionado { get; set; } = "Auditoria";
    public static string ResumenBaseDatos { get; set; } = string.Empty;
    public static DateTime HoraInicioInspeccion { get; set; }
    public static DateTime HoraFinInspeccion { get; set; }
    public static string TiempoTotalInspeccionReal { get; set; } = string.Empty;
    public static double VelocidadMaximaRodaje { get; set; } = 0;
    public static List<Microsoft.Maui.Devices.Sensors.Location> RutaTrazadaGPS { get; set; } = new List<Microsoft.Maui.Devices.Sensors.Location>();
    public static List<Microsoft.Maui.Devices.Sensors.Location> ParadasRRU { get; set; } = new List<Microsoft.Maui.Devices.Sensors.Location>();

    public static List<PuntoRuta> PuntosRuta { get; set; } = new List<PuntoRuta>();

    public static List<Aplicacion_SCA.Models.DatosRutaGuardada> HistorialRutasGPS { get; set; } = new();

    public static List<NotificacionApp> Notificaciones { get; set; } = new List<NotificacionApp>();
    public static bool NotificacionInicialVista { get; set; } = false;

    public static void CerrarSesion()
    {
        UsuarioActivo = null;
        ChasisActual = null;
        ModoSeleccionado = string.Empty;
        IndiceEstandarActual = 0;
        MotorSeleccionado = string.Empty;
        ModeloSeleccionado = string.Empty;
        VelocidadMaximaRodaje = 0;
        RutaTrazadaGPS?.Clear();
        ParadasRRU?.Clear();
        EstandaresCompletados?.Clear();
        HistorialRutasGPS?.Clear();

        PuntosRuta?.Clear();

        Notificaciones?.Clear();
        NotificacionInicialVista = false;
    }
}

public class PuntoRuta
{
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double Velocidad { get; set; }
    public string Hora { get; set; } = string.Empty;
    public string InfoPin => $"{Hora} | {Math.Round(Velocidad)} km/h";
}