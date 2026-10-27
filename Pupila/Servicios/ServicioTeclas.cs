using System.Runtime.InteropServices;

namespace Pupila;

/// <summary>
/// Escucha las teclas de volumen del teclado sin quitárselas al sistema.
/// Así la isla reacciona en cuanto se presionan, aunque la barra ya esté en 0% o 100% y no cambie.
/// </summary>
public static class ServicioTeclas
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_VOLUME_MUTE = 0xAD;
    private const int VK_VOLUME_DOWN = 0xAE;
    private const int VK_VOLUME_UP = 0xAF;
    private const uint LLKHF_INJECTED = 0x10;
    private const int VK_CAPITAL = 0x14;  // Mayús (Caps Lock)
    private const int VK_NUMLOCK = 0x90;  // Bloq Num

    private delegate IntPtr ProcTeclado(int nCode, IntPtr wParam, IntPtr lParam);

    private static ProcTeclado? _proc;  // se guarda para que no lo recoja el recolector de basura
    private static IntPtr _gancho;

    /// <summary>Se dispara al presionar una tecla de volumen (en el hilo de la interfaz).</summary>
    public static event Action? TeclaVolumen;

    /// <summary>Se dispara al presionar Mayús o Bloq Num, con el código de la tecla (en el hilo del teclado).</summary>
    public static event Action<int>? TeclaBloqueo;

    public static void Start()
    {
        _proc = Callback;
        _gancho = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            // El primer campo de KBDLLHOOKSTRUCT es el código de la tecla; el campo flags (offset 8)
            // marca las teclas inyectadas. Se ignoran todas las teclas inyectadas por programas, incluidas las que manda Pupila (botones del panel).
            int tecla = Marshal.ReadInt32(lParam);
            uint flags = (uint)Marshal.ReadInt32(lParam, 8);
            bool inyectada = (flags & LLKHF_INJECTED) != 0;
            if (!inyectada && tecla is VK_VOLUME_UP or VK_VOLUME_DOWN or VK_VOLUME_MUTE)
                TeclaVolumen?.Invoke();
            if (tecla is VK_CAPITAL or VK_NUMLOCK)
                TeclaBloqueo?.Invoke(tecla);
        }
        return CallNextHookEx(_gancho, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, ProcTeclado lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
