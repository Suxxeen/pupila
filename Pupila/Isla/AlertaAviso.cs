using System.Windows;
using System.Windows.Media;

namespace Pupila;

/// <summary>
/// Aviso que sale en la pastilla al llegar un aviso de Windows: la pastilla se ensancha lo justo, muestra
/// el nombre de la app y el título del aviso (en un chat, el contacto) un momento, y después vuelve a ser la campanita
/// con su número. Nunca muestra el texto del mensaje.
/// </summary>
public static class AlertaAviso
{
    public const double AltoH = 44;        // alto de la pastilla con el aviso
    private const double AnchoMin = 160;   // nunca más angosta que la pastilla en reposo
    private const double AnchoMax = 300;   // el nombre de la app se corta antes de pasar de aquí
    private const double TextoMax = 240;   // ancho máximo del texto dentro de la pastilla
    private const double DuracionS = 4.5;  // cuánto tiempo se queda el aviso en la pastilla
    private const double Velocidad = 8;    // qué tan rápido se abre y se cierra, por segundo
    private const double Pt = 96.0 / 72.0;

    private static readonly Typeface PequenoFace = new(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface AppFace = new(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static ToastInfo? _aviso;
    private static double _hasta = -100;  // hasta cuándo se muestra el aviso (en segundos de la isla)
    private static double _mix;

    // Textos en caché: solo se rehacen cuando cambia el nombre de la app.
    private static FormattedText? _pequeno;
    private static string _appClave = "";
    private static FormattedText? _appTexto;

    /// <summary>0 = pastilla normal, 1 = pastilla con el aviso. Se anima sola.</summary>
    public static double Mix => _mix;

    /// <summary>Ancho de la pastilla con el aviso: lo que ocupa el texto, dentro de los límites.</summary>
    public static double Ancho
    {
        get
        {
            if (_appTexto is null || _pequeno is null) return AnchoMin;
            double texto = Math.Max(_appTexto.Width, _pequeno.Width);
            return Math.Clamp(16 + texto + 44, AnchoMin, AnchoMax);  // 44: la campanita y su margen
        }
    }

    /// <summary>Muestra un aviso en la pastilla durante unos segundos.</summary>
    public static void Mostrar(ToastInfo aviso, double ahora)
    {
        _aviso = aviso;
        PrepararTextos();
        _hasta = ahora + DuracionS;
    }

    /// <summary>Cierra el aviso ya (al tocarlo, por ejemplo).</summary>
    public static void Cerrar() => _hasta = -100;

    /// <summary>Se llama en cada cuadro. Si no se puede mostrar ahora, el aviso se cierra y no vuelve solo.</summary>
    public static void Update(double ahora, double dt, bool permitido)
    {
        if (!permitido) _hasta = -100;
        double objetivo = ahora < _hasta ? 1.0 : 0.0;
        _mix += (objetivo - _mix) * Math.Min(1.0, dt * Velocidad);
    }

    /// <summary>Dibuja el aviso: la app arriba, el título abajo y la campanita a la derecha.</summary>
    public static void Draw(DrawingContext dc, double sw, double sh, double opacidad)
    {
        if (_aviso is null || opacidad <= 0.001) return;
        PrepararTextos();
        dc.PushOpacity(opacidad);
        dc.DrawText(_pequeno!, new Point(16, sh / 2 - 20));
        dc.DrawText(_appTexto!, new Point(16, sh / 2 - 3));
        ContenidoReposo.DibujarCampanita(dc, sw - 26, sh / 2, ServicioAvisos.Pendientes);
        dc.Pop();
    }

    private static void PrepararTextos()
    {
        var aviso = _aviso;
        if (aviso is null) return;
        // Arriba, la app; abajo, el título del aviso (en un chat, el contacto). Sin título: «Nuevo aviso».
        string app = aviso.App.Length > 0 ? aviso.App : "Windows";
        string titulo = aviso.Titulo.Length > 0 ? aviso.Titulo : "Nuevo aviso";
        if (_pequeno is null || _appTexto is null || _appClave != app + "\n" + titulo)
        {
            _appClave = app + "\n" + titulo;
            _pequeno = Texto(app, PequenoFace, 8.5 * Pt, Paleta.Gray);
            _appTexto = Texto(titulo, AppFace, 11 * Pt, Paleta.White);
        }
    }

    private static FormattedText Texto(string texto, Typeface cara, double tamano, Brush color)
    {
        var ft = ContenidoReposo.Formatted(texto, cara, tamano, color);
        // Una sola línea: lo que no cabe se corta con puntos suspensivos, nunca se parte en dos líneas.
        double altoLinea = ft.Height;
        ft.MaxTextWidth = TextoMax;
        ft.MaxTextHeight = altoLinea;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        return ft;
    }
}
