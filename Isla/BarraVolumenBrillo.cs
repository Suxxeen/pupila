using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WpfFlow = System.Windows.FlowDirection;

namespace Pupila;

/// <summary>Qué cambió: el volumen o el brillo.</summary>
public enum TipoBarra { Volumen, Brillo }

/// <summary>
/// Barra de volumen o brillo dentro de la isla.
/// Las llenan ServicioVolumen y ServicioBrillo; VentanaIsla la muestra un momento y la oculta.
/// </summary>
public static class BarraVolumenBrillo
{
    /// <summary>Ancho de la pastilla compacta del volumen o brillo (la altura es la de la isla en reposo).</summary>
    public const double Width = 210;

    private static readonly object _cerrojo = new();
    private static TipoBarra _tipo;
    private static double _valor;
    private static bool _visible;

    // Lo que se ve en pantalla: la barra se llena poco a poco hacia el valor real.
    private static double _mostrado;
    private static double _visibleDesde;

    private static bool _enExtremo;  // true si el valor está en 0% o 100%

    /// <summary>
    /// Se dispara en un hilo de fondo cuando cambia el volumen o el brillo.
    /// El parámetro es true solo cuando el valor acaba de llegar a 0% o 100% (para el latido de la isla).
    /// </summary>
    public static event Action<bool>? Cambiado;

    /// <summary>True mientras la barra debe verse en la isla.</summary>
    public static bool Visible
    {
        get { lock (_cerrojo) return _visible; }
    }

    /// <summary>Anota un cambio (valor de 0 a 100) y avisa a la isla.</summary>
    public static void Cambio(TipoBarra tipo, double valor)
    {
        bool llegoAlTope;
        lock (_cerrojo)
        {
            if (!_visible) _visibleDesde = EstadoIsla.Now;  // el icono rebota al aparecer
            _tipo = tipo;
            _valor = Math.Clamp(valor, 0, 100);
            _visible = true;
            bool extremo = _valor <= 0.5 || _valor >= 99.5;
            llegoAlTope = extremo && !_enExtremo;  // solo cuenta si acaba de llegar, no si ya estaba ahí
            _enExtremo = extremo;
        }
        Cambiado?.Invoke(llegoAlTope);
    }

    /// <summary>True si el valor actual está en 0% o 100% (la barra ya no se puede mover).</summary>
    public static bool EnLimite
    {
        get { lock (_cerrojo) return _enExtremo; }
    }

    /// <summary>Se presionó una tecla de volumen: si ya estaba en el tope, la isla igual reacciona.</summary>
    public static void TeclaVolumen()
    {
        bool tope;
        lock (_cerrojo)
        {
            tope = _enExtremo;
            if (!tope) return;  // si la barra se mueve, el cambio ya avisa
            if (!_visible) _visibleDesde = EstadoIsla.Now;
            _visible = true;
        }
        Cambiado?.Invoke(true);
    }

    /// <summary>Quita la barra de la isla.</summary>
    public static void Ocultar()
    {
        lock (_cerrojo) _visible = false;
    }

    /// <summary>Dibuja la barra. alpha va de 0 a 1 con la expansión, igual que PanelReproductor.Draw.</summary>
    public static void Draw(DrawingContext dc, double sw, double sh, double alpha)
    {
        TipoBarra tipo;
        double valor;
        double desde;
        lock (_cerrojo)
        {
            tipo = _tipo;
            valor = _valor;
            desde = _visibleDesde;
        }

        // La barra se llena suave hacia el valor real, en unos 0,3 segundos.
        _mostrado += (valor - _mostrado) * Math.Min(1.0, EstadoIsla.Dt * 10.0);
        // El icono aparece con un rebote corto.
        double edad = Math.Clamp((EstadoIsla.Now - desde) / 0.3, 0, 1);
        double pop = 0.55 + 0.45 * (edad * edad * (3 - 2 * edad));

        dc.PushTransform(new TranslateTransform(Constantes.Pad, 0));
        dc.PushClip(new RectangleGeometry(new Rect(-Constantes.Pad, sh * (1 - alpha), sw + 2 * Constantes.Pad, sh * alpha)));
        dc.PushOpacity(alpha);

        double cy = sh / 2;
        DrawIcon(dc, tipo, valor, 17, cy, pop);

        // Pista tenue y parte llena, delgadas.
        const double x0 = 34, barH = 4;
        double x1 = sw - 44;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(x0, cy - barH / 2, x1 - x0, barH), 2, 2);
        double ancho = (x1 - x0) * _mostrado / 100.0;
        if (ancho > 0)
            dc.DrawRoundedRectangle(WhiteBrush, null, new Rect(x0, cy - barH / 2, ancho, barH), 2, 2);

        // Porcentaje a la derecha.
        var texto = Texto($"{Math.Round(_mostrado)}%", Pt(10));
        dc.DrawText(texto, new Point(sw - 12 - texto.Width, cy - texto.Height / 2));

        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private static void DrawIcon(DrawingContext dc, TipoBarra tipo, double valor, double x, double cy, double pop)
    {
        dc.PushTransform(new TranslateTransform(x - 6, cy - 6));
        dc.PushTransform(new ScaleTransform(pop, pop, 6, 6));  // crece desde el centro del icono
        if (tipo == TipoBarra.Volumen)
        {
            dc.DrawGeometry(WhiteBrush, null, SpeakerGeometry);
            if (valor > 0) dc.DrawGeometry(null, WavePen, WaveGeometry);
        }
        else
        {
            dc.DrawEllipse(WhiteBrush, null, new Point(6, 6), 3.2, 3.2);
            dc.DrawGeometry(null, SunPen, SunRays);
        }
        dc.Pop();
        dc.Pop();
    }

    // --- colores y dibujos (congelados) ---

    private static readonly SolidColorBrush WhiteBrush = Frozen(new SolidColorBrush(Color.FromRgb(255, 255, 255)));
    private static readonly SolidColorBrush TrackBrush = Frozen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)));

    private static readonly Geometry SpeakerGeometry = Frozen(Geometry.Parse("M 1 4 L 4 4 L 8 1 L 8 11 L 4 8 L 1 8 Z"));
    private static readonly Geometry WaveGeometry = Frozen(Geometry.Parse("M 9.5 3.5 Q 11.5 6 9.5 8.5"));
    private static readonly Pen WavePen = FrozenPen(WhiteBrush, 1.4);
    private static readonly Geometry SunRays = CreateSunRays();
    private static readonly Pen SunPen = FrozenPen(WhiteBrush, 1.4);

    private static Geometry CreateSunRays()
    {
        var grupo = new GeometryGroup();
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            grupo.Children.Add(new LineGeometry(
                new Point(6 + 4.6 * Math.Cos(a), 6 + 4.6 * Math.Sin(a)),
                new Point(6 + 5.9 * Math.Cos(a), 6 + 5.9 * Math.Sin(a))));
        }
        grupo.Freeze();
        return grupo;
    }

    private static T Frozen<T>(T objeto) where T : Freezable
    {
        objeto.Freeze();
        return objeto;
    }

    private static Pen FrozenPen(Brush brush, double grosor)
    {
        var pen = new Pen(brush, grosor) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    private static double Pt(double puntos) => puntos * 96.0 / 72.0;

    private static FormattedText Texto(string texto, double tamano)
    {
        var letra = new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        return new FormattedText(texto, CultureInfo.CurrentUICulture, WpfFlow.LeftToRight, letra, tamano, WhiteBrush, 1.0);
    }
}
