using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Pupila;

/// Control al estilo Samsung: portada de fondo, título, progreso y controles.
public static class Player
{
    // Colores del original (blanco, gris y negro), como brochas congeladas.
    private static readonly Brush WhiteBrush = Frozen(new SolidColorBrush(Color.FromRgb(255, 255, 255)));
    private static readonly Brush GrayBrush = Frozen(new SolidColorBrush(Color.FromRgb(150, 152, 175)));
    private static readonly Brush BlackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0, 0, 0)));
    private static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)));
    private static readonly Brush OverlayArtBrush = Frozen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)));
    private static readonly Brush OverlayPlainBrush = Frozen(new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)));

    // Trazo blanco de 2.2 px para los controles, y negro de 2 px para el triángulo de play. Uniones redondas.
    private static readonly Pen SoftPen = FrozenPen(WhiteBrush, 2.2);
    private static readonly Pen BlackPen = FrozenPen(BlackBrush, 2.0);

    // Qt mide las fuentes en puntos (1/72 in) y WPF en DIP (1/96 in): 11 pt = 11 * 96 / 72 DIP.
    private const double Pt = 96.0 / 72.0;
    private static readonly Typeface TitleFace = new(new FontFamily("Segoe UI Variable Display"),
        FontStyles.Normal, FontWeights.DemiBold, FontStretches.Normal);
    private static readonly Typeface TextFace = new(new FontFamily("Segoe UI Variable Text"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // Caché de la portada difuminada: solo se recalcula si cambia la canción o el tamaño.
    private static ImageSource? _artSrc;
    private static int _artW = -1;
    private static int _artH = -1;
    private static BitmapSource? _artSoft;

    /// Dibuja el reproductor. Coordenadas locales de la isla: x=0 es el borde izquierdo de la pastilla sin Pad.
    public static void Draw(DrawingContext dc, double sw, double sh, double alpha)
    {
        if (alpha <= 0.0) return;

        var info = MediaService.Current;
        string title = info == null ? "Nada sonando" : info.Title;
        string artist = info?.Artist ?? "";
        bool playing = info?.Playing ?? false;

        double left = 16;
        double right = sw - 16;
        double width = sw - 32;
        double cx = sw / 2;

        // Se aparece con la opacidad de alpha y de abajo hacia arriba: solo se pinta la franja de abajo.
        dc.PushOpacity(alpha);
        dc.PushTransform(new TranslateTransform(Constants.Pad, 0));
        dc.PushClip(new RectangleGeometry(new Rect(-Constants.Pad, sh * (1 - alpha), sw + 2 * Constants.Pad, sh * alpha)));
        try
        {
            // Fondo: la portada difuminada, o negro si no hay. Cubre también los hombros de la pastilla.
            DrawBackground(dc, info?.Art, sw, sh);

            // Fila superior: reloj, clima y batería.
            IdleContent.DrawIdleBar(dc, sw, 34);

            // Título y artista, con elipsis si no caben.
            string titleShown = Elide(title, TitleFace, 11 * Pt, width);
            DrawLine(dc, titleShown, TitleFace, 11 * Pt, WhiteBrush, left, 38, width, 16);
            DrawLine(dc, artist, TextFace, 8 * Pt, GrayBrush, left, 54, width, 12);

            // Barra de progreso: pista fina, parte recorrida en blanco y un círculo en la posición actual.
            double pos = MediaService.PositionNow();
            double end = info?.End ?? 0.0;
            double frac = end > 0 ? Math.Clamp(pos / end, 0.0, 1.0) : 0.0;
            const double barY = 78;
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(left, barY - 2, width, 4), 2, 2);
            if (width * frac > 0)
                dc.DrawRoundedRectangle(WhiteBrush, null, new Rect(left, barY - 2, width * frac, 4), 2, 2);
            dc.DrawEllipse(WhiteBrush, null, new Point(left + width * frac, barY), 4.5, 4.5);

            // Tiempos: posición a la izquierda y duración a la derecha.
            var posText = Text(Fmt(pos), TextFace, 7 * Pt, GrayBrush);
            dc.DrawText(posText, new Point(left, 86));
            var endText = Text(Fmt(end), TextFace, 7 * Pt, GrayBrush);
            dc.DrawText(endText, new Point(right - endText.Width, 86));

            // Controles: anterior, play o pausa y siguiente. Blancos, con esquinas redondeadas.
            const double y = 104;
            double x = cx - 50;  // anterior: barrita y triángulo que apunta a la izquierda
            dc.DrawRoundedRectangle(WhiteBrush, SoftPen, new Rect(x - 7, y - 5, 2.4, 10), 1.2, 1.2);
            Poly(dc, WhiteBrush, SoftPen, new Point(x + 5, y - 5), new Point(x - 3, y), new Point(x + 5, y + 5));
            x = cx + 50;  // siguiente: triángulo que apunta a la derecha y barrita
            Poly(dc, WhiteBrush, SoftPen, new Point(x - 5, y - 5), new Point(x + 3, y), new Point(x - 5, y + 5));
            dc.DrawRoundedRectangle(WhiteBrush, SoftPen, new Rect(x + 4.6, y - 5, 2.4, 10), 1.2, 1.2);

            // Volumen: menos y más a los lados de los controles.
            DrawVolumeSign(dc, cx - 92, y, mas: false);
            DrawVolumeSign(dc, cx + 92, y, mas: true);

            // Play o pausa: un disco blanco con el símbolo en negro.
            dc.DrawEllipse(WhiteBrush, null, new Point(cx, y), 15, 15);
            if (playing)
            {
                dc.DrawRoundedRectangle(BlackBrush, null, new Rect(cx - 5, y - 6, 3.4, 12), 1.2, 1.2);
                dc.DrawRoundedRectangle(BlackBrush, null, new Rect(cx + 1.6, y - 6, 3.4, 12), 1.2, 1.2);
            }
            else
            {
                Poly(dc, BlackBrush, BlackPen, new Point(cx - 3, y - 6), new Point(cx + 6, y), new Point(cx - 3, y + 6));
            }
        }
        finally
        {
            dc.Pop();  // clip
            dc.Pop();  // transform
            dc.Pop();  // opacidad
        }
    }

    /// Dibuja un signo de volumen: "−" o "+", en blanco.
    private static void DrawVolumeSign(DrawingContext dc, double x, double y, bool mas)
    {
        var pen = new Pen(WhiteBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(pen, new Point(x - 4.5, y), new Point(x + 4.5, y));
        if (mas) dc.DrawLine(pen, new Point(x, y - 4.5), new Point(x, y + 4.5));
    }

    /// Dirección del botón de volumen que hay en un punto del panel: -1 (menos), 1 (más) o 0 si no es uno.
    /// Sirve para repetir el paso mientras se mantiene presionado el botón.
    public static int DireccionVolumenEn(double x, double y)
    {
        double center = Constants.OpenW / 2.0;
        if (y < 88 || y > 120) return 0;
        if (Math.Abs(x - (center - 92)) <= 14) return -1;
        if (Math.Abs(x - (center + 92)) <= 14) return 1;
        return 0;
    }

    /// Clic en el reproductor, en coordenadas locales (sin Pad). True si tocó la barra o un botón.
    public static bool HandleClick(double x, double y)
    {
        double center = Constants.OpenW / 2.0;  // OPEN_W / 2 del original

        // El volumen funciona aunque no haya música: como una tecla del teclado, para que Windows lo muestre.
        int direccion = DireccionVolumenEn(x, y);
        if (direccion != 0)
        {
            Native.PulsarTeclaVolumen(direccion < 0 ? Native.VK_VOLUME_DOWN : Native.VK_VOLUME_UP);
            return true;
        }

        var info = MediaService.Current;
        if (info == null || string.IsNullOrEmpty(info.Title)) return false;  // sin sesión, los demás clics se ignoran

        if (y >= 70 && y <= 86)  // barra de progreso: saltar a esa parte
        {
            if (info.End > 0)
                MediaService.Seek(Math.Clamp((x - 16) / (Constants.OpenW - 32), 0.0, 1.0));
            return true;
        }

        if (y >= 88 && y <= 120)  // anterior, play o pausa, siguiente
        {
            if (Math.Abs(x - (center - 50)) <= 16)
            {
                MediaService.Command("previous");
                return true;
            }
            if (Math.Abs(x - center) <= 16)
            {
                MediaService.Command("playpause");
                return true;
            }
            if (Math.Abs(x - (center + 50)) <= 16)
            {
                MediaService.Command("next");
                return true;
            }
        }
        return false;
    }

    /// Portada escalada sin deformar, recortada al centro y difuminada. Se queda en caché por tamaño.
    private static void DrawBackground(DrawingContext dc, ImageSource? art, double sw, double sh)
    {
        double full = sw + 2 * Constants.Pad;
        var panel = new Rect(-Constants.Pad, 0, full, sh);
        if (art == null || art.Width <= 0 || art.Height <= 0)
        {
            dc.DrawRectangle(OverlayPlainBrush, null, panel);
            return;
        }

        int w = (int)full;
        int h = (int)sh;
        var soft = Soft(art, w, h);
        dc.DrawImage(soft, new Rect(-Constants.Pad, 0, w, h));
        dc.DrawRectangle(OverlayArtBrush, null, panel);
    }

    private static BitmapSource Soft(ImageSource art, int w, int h)
    {
        if (ReferenceEquals(art, _artSrc) && w == _artW && h == _artH && _artSoft != null) return _artSoft;

        // Se escala sin deformar (cubre el panel) y se recorta centrada: nunca se estira.
        // Luego se difumina con un desenfoque gaussiano real. Se dibuja con un margen para que
        // el desenfoque no deje bordes transparentes, y al final se recorta al tamaño del panel.
        const int margen = 40;
        double s = Math.Max(w / art.Width, h / art.Height);
        double dw = art.Width * s;
        double dh = art.Height * s;

        var visual = new DrawingVisual
        {
            Effect = new BlurEffect { Radius = 20, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        };
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(art, new Rect(margen + (w - dw) / 2, margen + (h - dh) / 2, dw, dh));
        }
        var grande = new RenderTargetBitmap(w + 2 * margen, h + 2 * margen, 96, 96, PixelFormats.Pbgra32);
        grande.Render(visual);
        var soft = new CroppedBitmap(grande, new Int32Rect(margen, margen, w, h));
        soft.Freeze();

        _artSrc = art;
        _artW = w;
        _artH = h;
        _artSoft = soft;
        return soft;
    }

    /// Una línea de texto centrada verticalmente en la caja (x, y, w, h), alineada a la izquierda.
    private static void DrawLine(DrawingContext dc, string text, Typeface face, double size, Brush brush,
        double x, double y, double w, double h)
    {
        var ft = Text(text, face, size, brush);
        dc.DrawText(ft, new Point(x, y + (h - ft.Height) / 2));
    }

    /// Polígono relleno con trazo, como drawPolygon de Qt con el lápiz actual.
    private static void Poly(DrawingContext dc, Brush brush, Pen? pen, params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], true, true);
            for (int i = 1; i < points.Length; i++)
                ctx.LineTo(points[i], true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, pen, geometry);
    }

    /// Recorta el texto con puntos suspensivos al final para que quepa en maxWidth.
    private static string Elide(string text, Typeface face, double size, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || Text(text, face, size, Brushes.Black).Width <= maxWidth) return text;
        const string ellipsis = "…";
        int lo = 0;
        int hi = text.Length;
        while (lo < hi)  // busca el prefijo más largo que, con los puntos, todavía cabe
        {
            int mid = (lo + hi + 1) / 2;
            if (Text(text[..mid] + ellipsis, face, size, Brushes.Black).Width <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        return text[..lo] + ellipsis;
    }

    private static FormattedText Text(string text, Typeface face, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, 1.0);

    /// Formato m:ss, como _fmt del original (segundos truncados).
    private static string Fmt(double seconds)
    {
        int total = (int)Math.Max(0.0, seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Pen FrozenPen(Brush brush, double width) => Frozen(new Pen(brush, width)
    {
        LineJoin = PenLineJoin.Round,
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    });
}
