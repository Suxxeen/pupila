using System.Runtime.InteropServices;

namespace Pupila;

/// <summary>
/// Aviso corto en la isla cuando se activa o desactiva Mayús (Caps Lock) o Bloq Num.
/// El hook de teclado avisa de la tecla; aquí se espera un poco a que Windows cambie el estado y se compara.
/// </summary>
public static class IndicadorBloqueo
{
    const int VK_CAPITAL = 0x14;
    const int VK_NUMLOCK = 0x90;
    const double MOSTRAR_S = 1.2;
    const int ESPERA_MS = 60;  // Windows cambia el estado de la tecla un poco después de presionarla

    static volatile int _vkPendiente;     // 0 = nada que revisar
    static volatile int _desdeTicks;      // cuándo llegó el último aviso del teclado
    static readonly Dictionary<int, bool> Estado = new();
    static string _texto = "";
    static double _inicio;
    static double _hasta = -1;

    /// <summary>Texto que se muestra ahora mismo, p. ej. "Mayús activado".</summary>
    public static string Texto => _texto;

    /// <summary>Se suscribe al teclado y anota el estado inicial de las dos teclas. Llamar una vez al arrancar.</summary>
    public static void Start()
    {
        Estado[VK_CAPITAL] = Activo(VK_CAPITAL);
        Estado[VK_NUMLOCK] = Activo(VK_NUMLOCK);
        TeclasService.TeclaBloqueo += vk =>
        {
            _desdeTicks = Environment.TickCount;
            _vkPendiente = vk;
        };
    }

    /// <summary>Revisa si la tecla cambió de estado y, si sí, prepara el aviso. Lo llama la isla en cada cuadro.</summary>
    public static void Revisar(double now)
    {
        int vk = _vkPendiente;
        if (vk == 0 || Environment.TickCount - _desdeTicks < ESPERA_MS) return;
        _vkPendiente = 0;

        bool ahora = Activo(vk);
        bool antes = Estado.GetValueOrDefault(vk);
        Estado[vk] = ahora;
        if (ahora == antes) return;

        string nombre = vk == VK_CAPITAL ? "Mayús" : "Bloq Num";
        Mostrar($"{nombre} {(ahora ? "activado" : "desactivado")}", now);
    }

    /// <summary>Muestra un aviso con este texto durante un momento.</summary>
    public static void Mostrar(string texto, double now)
    {
        if (IslandState.ArranqueActivo) return;  // durante el arranque, el aviso de Mayús no reemplaza el saludo
        // Si ya había un aviso en pantalla, el nuevo reemplaza el texto sin desvanecerse:
        // así no aparece el reloj entre uno y otro al pulsar varias veces seguidas.
        bool yaVisible = now < _hasta;
        _texto = texto;
        _inicio = yaVisible ? now - 0.15 : now;
        _hasta = now + MOSTRAR_S;
    }

    /// <summary>Opacidad del aviso en este momento: 0 si no hay aviso, sube y baja suave.</summary>
    public static double Opacidad(double now)
    {
        if (now >= _hasta) return 0;
        double entra = Math.Clamp((now - _inicio) / 0.15, 0, 1);
        double sale = Math.Clamp((_hasta - now) / 0.3, 0, 1);
        return Math.Min(entra, sale);
    }

    static bool Activo(int vk) => (GetKeyState(vk) & 1) != 0;

    [DllImport("user32.dll")]
    static extern short GetKeyState(int nVirtKey);
}
