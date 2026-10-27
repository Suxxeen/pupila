using System.Runtime.InteropServices;

namespace Pupila;

/// <summary>
/// Estado de la batería (GetSystemPowerStatus): porcentaje y si el equipo está conectado a la corriente.
/// </summary>
public static class ServicioBateria
{
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

    /// <summary>(porcentaje, cargando) o null si el equipo no tiene batería (escritorio).</summary>
    public static (int Percent, bool Charging)? Read()
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
