using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Pupila;

/// <summary>
/// Animación de arranque: una línea blanca sale de las esquinas de arriba, corre por el borde superior
/// hacia el centro, hace un destello y avisa para que la pastilla aparezca con los ojos.
/// Dura unos 2,6 s y corre en una capa transparente sin bloquear el mouse.
/// </summary>
public sealed class Intro : Window
{
    const double DURACION_LINEA = 1.9;   // tiempo que tarda la línea en llegar al centro
    const double DURACION_TOTAL = 2.6;   // incluye el apagado y el destello
    const double RASTRO = 320;           // largo de la línea de luz, en píxeles
    const double GROSOR = 2.5;

    readonly Stopwatch _reloj = Stopwatch.StartNew();
    readonly Action _alDestello;
    readonly Lienzo _lienzo = new();
    bool _destelloDado;
    readonly CuadroFijo _cuadroFijo = new();

    Intro(Action alDestello)
    {
        _alDestello = alDestello;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        IsHitTestVisible = false;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Content = _lienzo;
        CompositionTarget.Rendering += Avanzar;
    }

    /// <summary>Muestra la animación una vez y llama a alDestello en el momento del destello.</summary>
    public static void Reproducir(Action alDestello)
    {
        new Intro(alDestello).Show();
    }

    void Avanzar(object? sender, EventArgs e)
    {
        double t = _reloj.Elapsed.TotalSeconds;
        if (!_cuadroFijo.Toca(t)) return;  // la línea de luz también va a 60 cuadros por segundo
        _lienzo.T = t;
        _lienzo.InvalidateVisual();

        if (!_destelloDado && t >= DURACION_LINEA)
        {
            _destelloDado = true;
            _alDestello();
        }
        if (t >= DURACION_TOTAL)
        {
            CompositionTarget.Rendering -= Avanzar;
            Close();
        }
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>Lo que se dibuja: las dos líneas blancas que avanzan y el destello.</summary>
    sealed class Lienzo : FrameworkElement
    {
        public double T;

        protected override void OnRender(DrawingContext dc)
        {
            double ancho = ActualWidth;
            double cx = ancho / 2;

            if (T < DURACION_LINEA + 0.3)
            {
                // Movimiento suave: acelera al principio y frena con calma al llegar al centro.
                double p = Math.Clamp(T / DURACION_LINEA, 0, 1);
                double avance = p < 0.5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2;
                double d = avance * cx;
                double apagar = Math.Clamp((T - DURACION_LINEA) / 0.3, 0, 1);
                dc.PushOpacity(1 - apagar);
                DibujarLinea(dc, 0, d, true);
                DibujarLinea(dc, ancho, d, false);
                dc.Pop();
            }

        }

        // Una línea blanca sólida que nace en la esquina (x0) y llega hasta d píxeles hacia el centro.
        // La cola se desvanece en un degradado; sin brillo extra, para que sea blanco puro.
        static void DibujarLinea(DrawingContext dc, double x0, double d, bool izquierda)
        {
            double dir = izquierda ? 1 : -1;
            Point cabeza = new(x0 + dir * d, 0);
            Point cola = new(x0 + dir * Math.Max(0, d - RASTRO), 0);
            if (Math.Abs(cabeza.X - cola.X) < 0.5) return;

            var degradado = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Colors.White, cola, cabeza)
            {
                MappingMode = BrushMappingMode.Absolute,
            };
            var pluma = new Pen(degradado, GROSOR) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pluma, cola, cabeza);
        }
    }
}
