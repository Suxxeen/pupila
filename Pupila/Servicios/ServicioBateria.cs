using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pupila;

/// <summary>
/// Estado de la batería (GetSystemPowerStatus): porcentaje y si el equipo está conectado a la corriente.
/// Windows se consulta cada 30 segundos y también cuando cambia la corriente. La isla lee el último valor
/// guardado, así que no pregunta a Windows en cada cuadro.
/// </summary>
public static class ServicioBateria
{
    const int CadaMs = 30000;

    static readonly object _cerrojo = new();
    static (int Percent, bool Charging)? _ultima;
    static System.Threading.Timer? _temporizador;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    /// <summary>Arranca la consulta periódica. Llamar una vez al arrancar.</summary>
    public static void Start()
    {
        Actualizar(null);
        _temporizador = new System.Threading.Timer(Actualizar, null, CadaMs, CadaMs);
        // Windows avisa cuando cambia la corriente (StatusChange) y al despertar (Resume).
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode is PowerModes.StatusChange or PowerModes.Resume) Actualizar(null);
        };
    }

    /// <summary>(porcentaje, cargando) o null si el equipo no tiene batería (escritorio).</summary>
    public static (int Percent, bool Charging)? Leer()
    {
        lock (_cerrojo) return _ultima;
    }

    static void Actualizar(object? _)
    {
        var lectura = PreguntarAWindows();
        lock (_cerrojo) _ultima = lectura;
    }

    static (int Percent, bool Charging)? PreguntarAWindows()
    {
        try
        {
            if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS estado)) return null;
            // Bit 128 = sin batería; 255 = porcentaje desconocido.
            if ((estado.BatteryFlag & 128) != 0 || estado.BatteryLifePercent == 255) return null;
            return ((int)estado.BatteryLifePercent, estado.ACLineStatus == 1);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
