using System.Runtime.InteropServices;
using System.Text;

namespace Pupila;

/// <summary>
/// Llamadas a Windows (user32, kernel32): inactividad del usuario y si la ventana activa ocupa toda la pantalla.
/// </summary>
public static class Nativo
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public const byte VK_VOLUME_DOWN = 0xAE;
    public const byte VK_VOLUME_UP = 0xAF;

    /// <summary>Pulsa una tecla de volumen: así Windows muestra su propio indicador de volumen.</summary>
    public static void PulsarTeclaVolumen(byte tecla)
    {
        keybd_event(tecla, 0, 0, UIntPtr.Zero);
        keybd_event(tecla, 0, 2, UIntPtr.Zero);  // 2 = soltar la tecla
    }


    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>True si la ventana es de Pupila misma (p. ej. la animación de arranque): no cuenta como pantalla completa.</summary>
    private static bool EsNuestra(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Segundos desde el último movimiento de mouse o tecla en todo Windows.</summary>
    public static double IdleSeconds()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return 0.0;
            uint ticks = GetTickCount();
            return unchecked(ticks - info.dwTime) / 1000.0;  // resta sin signo: GetTickCount vuelve a cero cada ~49 días y la resta sigue bien
        }
        catch (Exception)
        {
            return 0.0;  // si Windows no responde, la isla no se rompe
        }
    }

    /// <summary>True si la ventana activa ocupa toda la pantalla (video, juego, navegador en pantalla completa).</summary>
    public static bool IsFullscreenForeground()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || EsEscritorio(hwnd) || EsNuestra(hwnd)) return false;
            GetWindowRect(hwnd, out RECT rect);
            int anchoPantalla = GetSystemMetrics(SM_CXSCREEN);  // píxeles reales, sin escala de Windows
            int altoPantalla = GetSystemMetrics(SM_CYSCREEN);
            // Cubre también la parte de abajo: una ventana maximizada no llega a la barra de tareas.
            return rect.Top <= 0 && rect.Bottom >= altoPantalla - 1 && (rect.Right - rect.Left) >= anchoPantalla - 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Escritorio y barra de tareas no cuentan como pantalla completa.</summary>
    private static bool EsEscritorio(IntPtr hwnd)
    {
        var nombre = new StringBuilder(256);
        GetClassName(hwnd, nombre, 256);
        string clase = nombre.ToString();
        return clase is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }
}
