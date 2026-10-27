using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Pupila;

/// <summary>
/// Capa propia del saludo de arranque: el "Hola" de Windows. Tiene su propio desenfoque gaussiano
/// (BlurEffect de WPF) y su entrada "fuera de plano": llega grande y borroso, se encoge y se enfoca.
/// La pastilla no se toca: solo el saludo.
/// </summary>
public sealed class OjosEnfoque : FrameworkElement
{
    const double DESENFOQUE_MAX = 10;
    const double ESCALA_INICIAL = 1.6;
    const double TAMANO_HOLA = 12;  // puntos: "Bienvenido" tiene que caber dentro de la pastilla

    // Fuente de escritura a mano del saludo de Windows.
    static readonly Typeface FuenteHola = new(new FontFamily("Segoe Script"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // Resplandor blanco detrás de la palabra: fuerte en el centro y transparente en el borde.
    static readonly RadialGradientBrush HaloBienvenido = Frozen(new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromArgb(70, 255, 255, 255), 0.0),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0),
        },
    });

    static readonly Pen TrazoLetra = Frozen(new Pen(Brushes.White, 0.7));

    Geometry? _contorno;  // la palabra ya calculada, para no rehacerla en cada cuadro
    double _contornoSw = -1;
    double _contornoSh = -1;
    double _anchoTexto;
    double _altoTexto;

    double _sw;
    double _sh;
    double _offsetX;
    double _escalaExtra;  // al salir, la palabra crece un poco mientras se desenfoca
    readonly BlurEffect _blur = new();

    /// <summary>Actualiza la posición de la pastilla y el estado de la entrada. Lo llama la isla en cada cuadro.</summary>
    public void Actualizar(double sw, double sh, double offsetX)
    {
        _sw = sw;
        _sh = sh;
        _offsetX = offsetX + Constants.Pad;  // el cuerpo de la pastilla empieza después del margen

        // Entrada: 0 = fuera de plano y borrosa, 1 = en su lugar. Salida: la palabra se desenfoca y se aleja.
        double entrada = Mascot.Entrada;
        double salida = Mascot.SaludoSaliendo ? 1 - Mascot.EyesMix : 0;
        _blur.Radius = DESENFOQUE_MAX * Math.Max(1 - entrada, salida);
        _escalaExtra = 0.5 * salida;
        Effect = _blur.Radius > 0.05 ? _blur : null;
        Opacity = Mascot.EyesMix;  // se desvanece mientras entra el reloj
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!Mascot.Saludando) return;

        double entrada = Mascot.Entrada;
        double escala = ESCALA_INICIAL - (ESCALA_INICIAL - 1) * entrada + _escalaExtra;
        dc.PushTransform(new TranslateTransform(_offsetX, 0));
        dc.PushTransform(new ScaleTransform(escala, escala, _sw / 2, _sh / 2));
        DibujarHola(dc);
        dc.Pop();
        dc.Pop();
    }

    void DibujarHola(DrawingContext dc)
    {
        // El contorno de la palabra se calcula solo cuando cambia el tamaño de la pastilla, no en cada cuadro.
        if (_contorno is null || _contornoSw != _sw || _contornoSh != _sh)
        {
            // Centrado dentro del cuerpo de la pastilla, en horizontal y en vertical.
            var texto = new FormattedText("Bienvenido", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                FuenteHola, Pt(TAMANO_HOLA), Brushes.White, 1.0)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = _sw,
            };
            _anchoTexto = texto.Width;
            _altoTexto = texto.Height;
            _contorno = texto.BuildGeometry(new Point(0, (_sh - texto.Height) / 2));
            _contornoSw = _sw;
            _contornoSh = _sh;
        }

        double cx = _sw / 2;
        double cy = _sh / 2;
        double ahora = IslandState.Now;

        // Vida, como el anillo de carga: un resplandor que late detrás de la palabra...
        double latido = 1 + 0.08 * Math.Sin(ahora * 2 * Math.PI * 0.8);
        dc.DrawEllipse(HaloBienvenido, null, new Point(cx, cy), (_anchoTexto / 2 + 8) * latido, (_altoTexto / 2 + 4) * latido);

        // ...y brillitos que orbitan la palabra y parpadean.
        for (int k = 0; k < 3; k++)
        {
            double angulo = ahora * 1.6 + k * 2.094;
            dc.PushOpacity(0.5 + 0.5 * Math.Sin(ahora * 3 + k));
            dc.DrawEllipse(Brushes.White, null,
                new Point(cx + Math.Cos(angulo) * (_anchoTexto / 2 + 6), cy + Math.Sin(angulo) * (_altoTexto / 2 + 3)), 0.9, 0.9);
            dc.Pop();
        }

        // Se dibuja como contorno con un trazo fino para que la letra se vea más gruesa.
        dc.DrawGeometry(Brushes.White, TrazoLetra, _contorno);
    }

    static double Pt(double puntos) => puntos * 96.0 / 72.0;

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
