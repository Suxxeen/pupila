using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Pupila;

/// Mira (UI Automation) si hay una pestaña o una página web debajo de la isla. Si la hay, la isla se vuelve barrita.
public static class DetectorContenido
{
    // Tamaño de la isla en reposo, en píxeles lógicos (antes de aplicar la escala de Windows).
    private const double IdleW = 160;
    private const double IdleH = 32;

    // Tipos de control que cuentan como contenido visible.
    private static readonly string[] ContentTypes =
    {
        "TextControl", "ImageControl", "ButtonControl", "HyperlinkControl", "EditControl", "ListItemControl",
    };

    private static volatile bool _covered;
    private static bool _ultimaLectura;  // lo último que leyó la revisión de contenido
    private static int _racha;           // cuántas lecturas seguidas dieron ese mismo resultado

    /// True si hay contenido debajo de la isla.
    public static bool Covered => _covered;

    /// Arranca el hilo de revisión. El escaneo de UI Automation nunca corre en el hilo de la UI.
    public static void Start()
    {
        var thread = new Thread(Loop) { IsBackground = true, Name = "Pupila contenido" };
        thread.Start();
    }

    private static void Loop()
    {
        var clock = Stopwatch.StartNew();
        IntPtr lastFg = IntPtr.Zero;
        double lastScan = double.NegativeInfinity;

        while (true)
        {
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                IntPtr fg = GetForegroundWindow();
                if (fg != lastFg)  // cambió la ventana activa: revisar de inmediato
                {
                    lastFg = fg;
                    lastScan = double.NegativeInfinity;
                    _racha = 0;
                }

                double scale = Scale();
                if (!ForegroundBajoIsla(scale))
                {
                    _covered = false;  // nada debajo de la isla: no hace falta revisar
                }
                else if (now - lastScan > 1.0)  // misma ventana: revisar cada segundo
                {
                    lastScan = now;
                    // Histéresis: solo cambia si la lectura se repite 2 veces seguidas; evita el parpadeo.
                    bool lectura = ContenidoBajoIsla(scale);
                    _racha = lectura == _ultimaLectura ? _racha + 1 : 1;
                    _ultimaLectura = lectura;
                    if (_racha >= 2) _covered = lectura;
                }
            }
            catch (Exception)
            {
            }
            Thread.Sleep(100);
        }
    }

    /// Escala de Windows del monitor principal: 96 ppp equivale a 1.0 (100 %).
    private static double Scale() => GetDpiForSystem() / 96.0;

    /// True si la ventana activa queda debajo de la isla, sea del programa que sea.
    private static bool ForegroundBajoIsla(double scale)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        if (IsDesktopOrTaskbar(hwnd)) return false;  // escritorio y barra de tareas

        GetWindowRect(hwnd, out NativeRect rect);
        double center = GetSystemMetrics(0) / 2.0;  // píxeles reales
        double half = IdleW * scale / 2;
        return rect.Top < IdleH * scale && rect.Bottom > 0 && rect.Left < center + half && rect.Right > center - half;
    }

    private static bool IsDesktopOrTaskbar(IntPtr hwnd)
    {
        var name = new StringBuilder(256);
        GetClassName(hwnd, name, 256);
        string cls = name.ToString();
        return cls is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }

    /// Pregunta a Windows (UI Automation) si debajo de la isla hay pestañas o una página con contenido.
    private static bool ContenidoBajoIsla(double scale)
    {
        try
        {
            // El rectángulo exacto que ocupa la isla, en píxeles reales.
            double center = GetSystemMetrics(0) / 2.0;
            double left = center - IdleW / 2 * scale;
            double right = center + IdleW / 2 * scale;
            double bottom = IdleH * scale;

            bool TocaIsla(System.Windows.Rect r) =>
                r.Top < bottom && r.Bottom > 0 && r.Left < right && r.Right > left;

            // Hay texto, imagen o botón de la página bajo la isla (no solo paneles vacíos).
            bool HasVisibleContent(AutomationElement control, int depth)
            {
                if (depth > 10) return false;
                foreach (var child in Children(control))
                {
                    var info = child.Current;
                    var r = info.BoundingRectangle;
                    if (r.Bottom <= 0 || r.Top >= bottom) continue;
                    if (ContentTypes.Contains(TypeName(info)) && TocaIsla(r)) return true;
                    if (HasVisibleContent(child, depth + 1)) return true;
                }
                return false;
            }

            // Busca una pestaña o página cuyo rectángulo toque la isla; solo baja por lo que está en su franja.
            bool Scan(AutomationElement control, int depth)
            {
                if (depth > 14) return false;  // Chrome y Edge anidan mucho sus pestañas; hace falta bajar más niveles
                foreach (var child in Children(control))
                {
                    var info = child.Current;
                    var r = info.BoundingRectangle;
                    if (r.Bottom <= 0 || r.Top >= bottom) continue;  // fuera de la franja de la isla
                    string type = TypeName(info);
                    if (type == "TabItemControl" && TocaIsla(r)) return true;  // pestaña real
                    // Una página solo cuenta si tiene contenido visible bajo la isla; un panel vacío no.
                    if (type == "DocumentControl" && TocaIsla(r) && HasVisibleContent(child, 0)) return true;
                    if (Scan(child, depth + 1)) return true;
                }
                return false;
            }

            var window = AutomationElement.FromHandle(GetForegroundWindow());
            return Scan(window, 0);
        }
        catch (Exception)
        {
            return false;  // cualquier fallo de Windows cuenta como "sin contenido"
        }
    }

    private static IEnumerable<AutomationElement> Children(AutomationElement parent)
    {
        var walker = TreeWalker.ControlViewWalker;
        for (var child = walker.GetFirstChild(parent); child != null; child = walker.GetNextSibling(child))
            yield return child;
    }

    /// Nombre del tipo de control al estilo de uiautomation: "ControlType.Text" -> "TextControl".
    private static string TypeName(AutomationElement.AutomationElementInformation info) =>
        info.ControlType.ProgrammaticName.Replace("ControlType.", "") + "Control";

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
