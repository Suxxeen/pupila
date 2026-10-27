using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Pupila;

/// <summary>
/// Contenido de la isla en reposo: animación de carga, hora con clima y batería o espectro,
/// y la decisión entre carga, aviso de Mayús o Bloq Num, y reloj.
/// El origen de dc es la esquina superior izquierda de la pastilla; quien llama hace el clip y la escala.
/// </summary>
public static class ContenidoReposo
{
    const int SPECTRUM_BANDS = 10;         // columnas del espectro de audio
    const double CHARGE_SHOW_S = 3.0;      // cuánto se muestra la animación al desconectar
    const double CHARGE_CONNECT_S = 4.6;   // al conectar: ojos que se llenan, luego texto con el porcentaje

    // Colores de la isla. Paleta no trae rojo, así que va aquí.
    static readonly Color GreenColor = ((SolidColorBrush)Paleta.Green).Color;  // (110, 240, 160)
    static readonly Color RedColor = Color.FromRgb(255, 120, 120);
    static readonly SolidColorBrush RedBrush = Frozen(new SolidColorBrush(RedColor));

    // Pastilla de batería y anillo de carga.
    static readonly Pen BatteryTrackPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))), 2));
    static readonly Pen GreenRingPen = Frozen(new Pen(Paleta.Green, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly Pen RedRingPen = Frozen(new Pen(RedBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly Pen ChargeTrackPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))), 2.4));
    static readonly Pen EyeOutlinePen = Frozen(new Pen(Paleta.White, 1.2));
    static readonly Geometry HeartGeometry = Frozen(Geometry.Parse(
        "M5,9 L0.6,4.6 C-0.9,3.1 -0.4,0.2 2.2,0.2 C3.5,0.2 4.6,1.1 5,2 C5.4,1.1 6.5,0.2 7.8,0.2 C10.4,0.2 10.9,3.1 9.4,4.6 Z"));
    static readonly SolidColorBrush GreenFill = Frozen(new SolidColorBrush(GreenColor));
    static readonly ChargeTint GreenTint = new(GreenColor);
    static readonly ChargeTint RedTint = new(RedColor);

    // Tipografías: Segoe UI Variable Display 16 DemiBold (reloj) y Segoe UI Variable Text 9 (clima).
    static readonly Typeface ClockFace = new(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.DemiBold, FontStretches.Normal);
    static readonly Typeface SmallFace = new(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // Caché de textos y de iconos: se rehacen solo cuando cambia el contenido.
    static string _clockKey = "";
    static FormattedText? _clockText;
    static string _tempKey = "";
    static FormattedText? _tempText;
    static readonly Dictionary<string, FormaIcono?> Iconos = new();

    // Estado de la fila en reposo: mezcla batería/espectro, animación de carga y último audio detectado.
    static double _lastAudio;
    static int _pendientesAnteriores;     // para detectar avisos nuevos
    static double _sonadoEn = -100;       // cuándo sonó la campanita por última vez
    static double _llegadaEn = -100;      // cuándo llegó el último aviso (inicia la transición del clima)
    static double _specMix;  // 0 = batería, 1 = espectro
    static readonly double[] _spectrumShown = new double[SPECTRUM_BANDS];
    static bool? _charging;  // estado de carga anterior, para detectar cuándo conectas o desconectas
    static double _chargeUntil;
    static double _chargeFill, _chargeFillV;
    static double _chargeStart;
    static double _chargeEndedAt;
    static bool _chargeConnected;  // true si la animación actual es la de conectar (ojos y texto)

    /// <summary>True mientras dura la animación de carga (al conectar o desconectar el cargador).</summary>
    public static bool ChargeActive => EstadoIsla.Now < _chargeUntil;

    /// <summary>Ancho de la pastilla durante la carga: estrecha con los ojos, un poco más ancha con el texto.</summary>
    public static double ChargeWidth => _chargeConnected && EstadoIsla.Now - _chargeStart >= 1.6 ? 200 : 96;

    /// <summary>Actualiza el espectro, la detección del cargador y las marcas de carga.</summary>
    public static void Update(double now, double dt)
    {
        dt = Math.Min(dt, 0.05);  // si la PC se traba, no saltar de golpe

        // Si suena algo en el sistema, el espectro reemplaza a la batería; la pastilla no cambia de tamaño.
        // El espectro solo aparece con un reproductor de música sonando, no con cualquier sonido de la PC.
        if (ServicioMultimedia.Current?.Playing == true) _lastAudio = now;
        bool audioPlaying = now - _lastAudio < 0.6;
        _specMix += ((audioPlaying ? 1.0 : 0.0) - _specMix) * Math.Min(1.0, dt * 16);  // cambio casi instantáneo

        // La campanita suena cuando llega un aviso y, mientras haya avisos sin ver, cada 6 segundos.
        int pendientes = ServicioAvisos.Pendientes;
        if (pendientes > _pendientesAnteriores)
        {
            _llegadaEn = now;  // llegó un aviso: transición del clima a la campanita
            _sonadoEn = now;
        }
        else if (pendientes > 0 && now - _sonadoEn >= 6)
            _sonadoEn = now;  // mientras haya avisos sin ver, se mueve cada 6 s
        _pendientesAnteriores = pendientes;

        // La animación de carga termina y el reloj aparece después de la pastilla.
        if (_chargeUntil > 0 && now >= _chargeUntil && _chargeEndedAt < _chargeUntil)
            _chargeEndedAt = _chargeUntil;

        var status = ServicioBateria.Read();
        if (status is not null)
        {
            var battery = status.Value;
            if (!EstadoIsla.ArranqueActivo && _charging is bool previous && battery.Charging != previous)  // se conectó o se desconectó
            {
                _chargeConnected = battery.Charging;
                _chargeUntil = now + (battery.Charging ? CHARGE_CONNECT_S : CHARGE_SHOW_S);
                _chargeFill = 0;
                _chargeFillV = 0;
                _chargeStart = now;
            }
            _charging = battery.Charging;
            // Nivel de la barra de carga, con resorte.
            Resorte.Step(ref _chargeFill, ref _chargeFillV, battery.Percent, dt, 50.0, 0.85);
        }

        // Espectro: sube rápido con el sonido y baja despacio, para que se vea fluido.
        var spectrum = ServicioAudio.Spectrum;
        for (int i = 0; i < SPECTRUM_BANDS; i++)
        {
            double target = spectrum[i];
            double rate = target > _spectrumShown[i] ? 40.0 : 30.0;  // reacción rápida y caída muy rápida
            _spectrumShown[i] += (target - _spectrumShown[i]) * Math.Min(1.0, dt * rate);
        }
    }

    /// <summary>
    /// Contenido en reposo: carga, aviso de bloqueo o reloj + clima + batería/espectro, con sus opacidades.
    /// t es el progreso de la expansión (0 = pastilla, 1 = abierta). No hace clip ni escala.
    /// </summary>
    public static void Draw(DrawingContext dc, double sw, double sh, double t)
    {
        if (t >= 0.99) return;  // casi abierta: el contenido en reposo ya no se ve
        if (Mascota.EsperandoSaludo) return;  // al arrancar, la pastilla sale vacía hasta que empieza el saludo

        double now = EstadoIsla.Now;
        double baseOpacity = Math.Max(0, 1 - t * 2.5);
        var status = ServicioBateria.Read();
        bool chargeNow = now < _chargeUntil && status is not null;  // la carga aparece al instante
        // El reloj aparece 0.2 s después de que termina la carga.
        double ramp = chargeNow ? 1.0 : Math.Clamp((now - _chargeEndedAt - 0.2) / 0.2, 0, 1);

        if (chargeNow)
        {
            dc.PushOpacity(1.0);
            DrawCharge(dc, sw, sh, status.Value);
            dc.Pop();
        }
        else if (IndicadorBloqueo.Opacidad(now) > 0.001)
        {
            // Aviso de Mayús o Bloq Num: reemplaza el reloj un momento.
            dc.PushOpacity(IndicadorBloqueo.Opacidad(now));
            var texto = Formatted(IndicadorBloqueo.Texto, SmallFace, Pt(10), Paleta.White);
            dc.DrawText(texto, new Point(sw / 2 - texto.Width / 2, sh / 2 - texto.Height / 2));
            dc.Pop();
        }
        else
        {
            double mixReloj = now < _chargeUntil ? 0 : Mascota.MixReloj;  // el reloj aparece al ritmo de su propia transición
            dc.PushOpacity(Math.Clamp(baseOpacity * (1 - mixReloj) * ramp, 0, 1));
            DrawIdleBar(dc, sw, sh);
            dc.Pop();
        }

        // Los ojos ya no se dibujan aquí: tienen su propia capa (CapaOjos), que la GPU compone aparte.
    }

    /// <summary>True si la carga está tapando el reloj y los ojos en este momento.</summary>
    public static bool CargaActiva => EstadoIsla.Now < _chargeUntil;

    /// <summary>
    /// Fila superior en reposo: hora centrada, clima a la izquierda y batería o espectro a la derecha.
    /// sh define el centro vertical.
    /// </summary>
    public static void DrawIdleBar(DrawingContext dc, double sw, double sh)
    {
        string text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        var clock = ClockFormatted(text);
        double width = clock.Width;
        double cx = sw / 2;
        double bx = Math.Min(cx + width / 2 + 12, sw - 20);  // la batería nunca sale de la pastilla

        // Un píxel arriba: las cifras se ven bajas si se centran por la caja completa de la fuente.
        dc.DrawText(clock, new Point(cx - width / 2, -1 + sh / 2 - clock.Height / 2));

        // Transición al llegar un aviso: el clima se va, la campanita grande aparece y luego vuelve el clima.
        double t = EstadoIsla.Now - _llegadaEn;
        double climaOp = Math.Clamp(1 - Ramp(t, 0.0, 0.25) + Ramp(t, 1.85, 2.15), 0, 1);

        string temp = ServicioClima.TempText;
        if (temp.Length > 0 && climaOp > 0.001)
        {
            double leftEdge = cx - width / 2 - 8;
            var tempText = TempFormatted(temp);
            dc.PushOpacity(climaOp);
            dc.DrawText(tempText, new Point(leftEdge - tempText.Width, sh / 2 - tempText.Height / 2));
            DrawWeatherIcon(dc, leftEdge - tempText.Width - 4 - 14, sh / 2 - 7, ServicioClima.IconName);
            dc.Pop();
        }

        // La campanita grande ocupa el lugar del clima: aparece, suena y se encoge.
        double grande = Ramp(t, 0.15, 0.4) - Ramp(t, 1.5, 1.85);
        if (grande > 0.001 && t > 0)
        {
            double escala = 0.6 + Smooth(Ramp(t, 0.15, 0.5)) - 0.6 * Ramp(t, 1.5, 1.85);
            double angulo = 18 * Math.Sin(t * 2 * Math.PI * 3) * (1 - Ramp(t, 0.45, 1.5));
            DrawBell(dc, cx - width / 2 - 30, sh / 2, escala, angulo, Math.Clamp(grande, 0, 1));
        }

        var status = ServicioBateria.Read();
        // La batería se desvanece cuando suena algo; el espectro aparece en su lugar.
        if (status is not null && _specMix < 1.0)
        {
            dc.PushOpacity(1 - _specMix);
            DrawBattery(dc, bx, sh / 2 - 8, status.Value.Percent, status.Value.Charging);
            dc.Pop();
        }
        if (_specMix > 0.0)
        {
            dc.PushOpacity(_specMix);
            DrawSpectrum(dc, bx, sh);
            dc.Pop();
        }

        // Campanita pequeña junto a la batería: vuelve cuando termina la transición.
        double pequena = Math.Clamp(1 - Ramp(t, 0.15, 0.3) + Ramp(t, 1.85, 2.15), 0, 1);
        if (ServicioAvisos.Pendientes > 0 && pequena > 0.001)
            DrawBell(dc, bx + 22, sh / 2, 1.0, AnguloTimbre(), pequena);
    }

    static double Ramp(double t, double a, double b) => Math.Clamp((t - a) / (b - a), 0, 1);

    static double Smooth(double u) => u * u * (3 - 2 * u);

    /// <summary>Vaivén corto de la campanita, de 1 segundo, después de cada toque.</summary>
    static double AnguloTimbre()
    {
        double s = EstadoIsla.Now - _sonadoEn;
        return s < 1.0 ? 16 * Math.Sin(s * 2 * Math.PI * 4) * (1 - s) * (1 - s) : 0;
    }

    // Campanita en una caja de 12x12, dibujada con un trazo de SVG.
    static readonly Geometry BellGeometry = CreateBell();
    static readonly SolidColorBrush RedDot = CreateRed();

    static Geometry CreateBell()
    {
        var g = Geometry.Parse("M 6 1 C 3.8 1 2.8 2.8 2.8 5 L 2.8 8 L 1.2 9.6 L 10.8 9.6 L 9.2 8 L 9.2 5 C 9.2 2.8 8.2 1 6 1 Z M 4.6 10.6 Q 6 12.2 7.4 10.6 Z");
        g.Freeze();
        return g;
    }

    static SolidColorBrush CreateRed()
    {
        var b = new SolidColorBrush(Color.FromRgb(255, 70, 70));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Campanita blanca con un punto rojo. centroX/centroY es el centro de la caja de 12x12;
    /// escala, ángulo y opacidad la animan.
    /// </summary>
    static void DrawBell(DrawingContext dc, double centroX, double centroY, double escala, double angulo, double opacidad)
    {
        dc.PushOpacity(opacidad);
        dc.PushTransform(new TranslateTransform(centroX, centroY));
        dc.PushTransform(new ScaleTransform(escala, escala));
        dc.PushTransform(new RotateTransform(angulo));
        dc.PushTransform(new TranslateTransform(-6, -6));  // gira alrededor del centro de la campanita
        dc.DrawGeometry(Paleta.White, null, BellGeometry);
        dc.DrawEllipse(RedDot, null, new Point(11, 1), 2.6, 2.6);
        dc.Pop();
        dc.Pop();
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    // --- dibujo de la carga ----------------------------------------------------

    static void DrawCharge(DrawingContext dc, double sw, double sh, (int Percent, bool Charging) status)
    {
        if (_chargeConnected)
        {
            DrawChargeEyes(dc, sw, sh, status.Percent, EstadoIsla.Now - _chargeStart);
            return;
        }

        double now = EstadoIsla.Now;
        double cx = sw / 2;
        double cy = sh / 2;
        var tint = RedTint;  // esta animación es solo la de desconectar: siempre roja, no depende de la lectura de la batería
        var center = new Point(cx, cy);

        // Entrada: el anillo crece con un poco de rebote durante los primeros 0.5 s.
        double grow = Math.Min(1.0, (now - _chargeStart) / 0.5);
        double scale = 0.6 + 0.4 * (1 - Math.Pow(1 - grow, 3)) + 0.06 * Math.Sin(grow * Math.PI);
        double radius = 10 * scale;

        // Halo suave detrás del anillo.
        dc.DrawEllipse(tint.Halo, null, center, radius + 8, radius + 8);

        // Ondas que salen del rayo.
        for (int k = 0; k < 2; k++)
        {
            double waveT = Mod(now * 0.8 + k / 2.0, 1.0);
            var waveColor = Color.FromArgb((byte)(150 * (1 - waveT)), tint.Tint.R, tint.Tint.G, tint.Tint.B);
            var wavePen = Frozen(new Pen(Frozen(new SolidColorBrush(waveColor)), 1.0));
            double r = radius * (0.9 + 1.1 * waveT);
            dc.DrawEllipse(null, wavePen, center, r, r);
        }

        // Anillo: pista tenue, arco con brillo y arco nítido que se cierra según la carga.
        double sweep = Math.Clamp(_chargeFill, 0, 100) * 3.6;
        dc.DrawEllipse(null, ChargeTrackPen, center, radius, radius);
        DrawRingArc(dc, cx, cy, radius, sweep, tint.Glow);
        DrawRingArc(dc, cx, cy, radius, sweep, tint.Sharp);

        // Partículas que orbitan el anillo.
        for (int k = 0; k < 3; k++)
        {
            double angle = now * 1.6 + k * 2.094;
            dc.PushOpacity(0.5 + 0.5 * Math.Sin(now * 3 + k));
            dc.DrawEllipse(Paleta.White, null,
                new Point(cx + Math.Cos(angle) * (radius + 3), cy + Math.Sin(angle) * (radius + 3)), 0.9, 0.9);
            dc.Pop();
        }

        // Rayo en el centro, que respira.
        double pulse = 0.9 + 0.1 * Math.Sin(now * 2 * Math.PI * 1.6);
        dc.DrawGeometry(Paleta.White, null, Polygon(
            new Point(cx + 2.2 * pulse, cy - 5.5 * pulse),
            new Point(cx - 2.8 * pulse, cy + 0.6),
            new Point(cx - 0.2, cy + 0.6),
            new Point(cx - 2.2 * pulse, cy + 5.5 * pulse),
            new Point(cx + 2.8 * pulse, cy - 0.6),
            new Point(cx + 0.2, cy - 0.6)));
    }

    // Al conectar: dos ojos en forma de corazón se llenan de verde de abajo hacia arriba con brillitos.
    // Luego se corren a la izquierda, la pastilla se ensancha y aparece "Carga iniciada" con el porcentaje,
    // que sube desde 0. Todo queda junto en la misma pastilla y al final se desvanece.
    // e = segundos desde que se conectó el cargador.
    static void DrawChargeEyes(DrawingContext dc, double sw, double sh, int percent, double e)
    {
        double cx = sw / 2;
        double cy = sh / 2;
        double salida = Ramp(e, 4.0, 4.5);  // al final, todo se desvanece
        double aparece = Ramp(e, 0, 0.4);
        double escala = 0.5 + 0.5 * (1 - Math.Pow(1 - aparece, 3)) + 0.08 * Math.Sin(aparece * Math.PI);  // rebote suave
        double nivel = Math.Clamp(e / 1.6, 0, 1);
        double llenado = percent / 100.0 * (1 - Math.Pow(1 - nivel, 3));  // sube rápido y frena al llegar
        // Primero los corazones se llenan; al terminar se encogen y se van, y luego entra el texto.
        double t = Ramp(e, 1.6, 2.0);
        double encoge = 1 - t * t * (3 - 2 * t);  // suave al empezar y al terminar

        // Pestañeo al aparecer: los ojos se cierran un momento y se abren, como si la PC despertara.
        double pestaneo = Math.Clamp((e - 0.9) / 0.15, 0, 1);
        double abierto = 1 - 0.85 * Math.Sin(pestaneo * Math.PI);

        dc.PushOpacity(1 - salida);
        if (encoge > 0)
        {
            for (int side = -1; side <= 1; side += 2)
                DibujarCorazon(dc, cx + side * 11, cy, escala * 1.15 * encoge, llenado, side, abierto);
        }

        // Texto solo, centrado: "Cargando" y el porcentaje en verde, que sube de 0 hasta el valor actual.
        double texto = Ramp(e, 2.0, 2.5);
        if (texto > 0)
        {
            int mostrado = (int)Math.Round(percent * Ramp(e, 2.1, 3.0));
            var etiqueta = Formatted("Cargando", SmallFace, Pt(9), Paleta.White);  // mismo tamaño que el clima
            var numero = Formatted($"{mostrado}%", ClockFace, Pt(13), GreenFill);  // más chico que el reloj: la pastilla mide 32 px
            double ancho = etiqueta.Width + 6 + numero.Width;
            double x0 = (sw - ancho) / 2;
            double linea = cy + 5.5 + (1 - texto) * 6;  // sube un poco al entrar; el número manda la posición
            // La palabra se centra a la altura de las cifras del número (0.35 = la mitad de la altura de mayúsculas).
            double centroNumero = linea - 0.35 * Pt(13);
            dc.PushOpacity(texto);
            dc.DrawText(etiqueta, new Point(x0, centroNumero + 0.35 * Pt(9) - etiqueta.Baseline));
            dc.DrawText(numero, new Point(x0 + etiqueta.Width + 6, linea - numero.Baseline));
            dc.Pop();
        }
        dc.Pop();
    }

    // Un corazón de carga: contorno blanco, relleno verde que sube y brillitos. (x, y) es su centro.
    // abierto: 1 con el ojo abierto, menos de 1 al pestañear (se aplasta en vertical).
    static void DibujarCorazon(DrawingContext dc, double x, double y, double escala, double llenado, int side, double abierto)
    {
        double now = EstadoIsla.Now;
        // Los dos ojos flotan igual, juntos (sin girar): así se ven como ojos, no como péndulos.
        double rebote = Math.Sin(now * 2 * Math.PI * 1.8) * 1.2;

        dc.PushTransform(new TranslateTransform(x, y + rebote));
        dc.PushTransform(new ScaleTransform(escala, escala * abierto));
        dc.PushTransform(new TranslateTransform(-5, -4.6));  // el centro del corazón queda en (x, y)

        dc.DrawEllipse(GreenTint.Halo, null, new Point(5, 4.6), 9, 8);  // resplandor detrás
        double alto = llenado * 9.2;
        dc.PushClip(HeartGeometry);
        dc.DrawRectangle(GreenFill, null, new Rect(-1, 9.2 - alto, 12, alto));
        for (int k = 0; k < 2; k++)  // brillitos que suben dentro del líquido
        {
            double fase = Mod(now * 0.9 + k * 0.5 + side * 0.25, 1);
            dc.PushOpacity(Math.Sin(fase * Math.PI));
            dc.DrawEllipse(Paleta.White, null, new Point(5 + Math.Sin(now * 3 + k) * 2.2, 9 - fase * alto), 0.7, 0.7);
            dc.Pop();
        }
        dc.Pop();
        dc.DrawGeometry(null, EyeOutlinePen, HeartGeometry);

        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    // --- dibujo de la fila en reposo -------------------------------------------

    static void DrawSpectrum(DrawingContext dc, double x0, double sh)
    {
        // Rangos de bandas: graves, medios-graves, medios y agudos.
        double bass = (_spectrumShown[0] + _spectrumShown[1] + _spectrumShown[2]) / 3;
        double mid = (_spectrumShown[3] + _spectrumShown[4] + _spectrumShown[5]) / 3;
        double treble = (_spectrumShown[6] + _spectrumShown[7] + _spectrumShown[8] + _spectrumShown[9]) / 4;
        double[] values = { bass, 0.6 * bass + 0.4 * mid, 0.4 * mid + 0.6 * treble, treble };
        for (int i = 0; i < values.Length; i++)
        {
            double height = 3 + 17 * Math.Min(1.0, values[i]);  // más alto para las notas fuertes
            double x = x0 + i * 4.4;  // 4 trazos de 2.8 px con separación 4.4
            dc.DrawRoundedRectangle(Paleta.White, null, new Rect(x, sh / 2 - height / 2, 2.8, height), 1.4, 1.4);
        }
    }

    static void DrawBattery(DrawingContext dc, double x, double y, int percent, bool charging)
    {
        // Indicador circular: un anillo que se cierra según la carga; rayo en el centro al cargar.
        double cx = x + 1.5 + 6.5;
        double cy = y + 1.5 + 6.5;
        dc.DrawEllipse(null, BatteryTrackPen, new Point(cx, cy), 6.5, 6.5);
        // Empieza arriba y gira a la derecha.
        DrawRingArc(dc, cx, cy, 6.5, percent * 3.6, percent > 20 ? GreenRingPen : RedRingPen);
        if (charging)
        {
            dc.DrawGeometry(Paleta.White, null, Polygon(
                new Point(x + 8.8, y + 4),
                new Point(x + 6.5, y + 8.5),
                new Point(x + 8, y + 8.5),
                new Point(x + 7.2, y + 12),
                new Point(x + 9.5, y + 7.5),
                new Point(x + 8, y + 7.5)));
        }
    }

    /// <summary>Arco que empieza arriba y gira a la derecha (sentido horario), con sweepDeg grados.</summary>
    static void DrawRingArc(DrawingContext dc, double cx, double cy, double r, double sweepDeg, Pen pen)
    {
        if (sweepDeg <= 0) return;
        if (sweepDeg >= 359.99)
        {
            dc.DrawEllipse(null, pen, new Point(cx, cy), r, r);
            return;
        }
        double a = sweepDeg * Math.PI / 180;
        var figure = new PathFigure
        {
            StartPoint = new Point(cx, cy - r),  // arriba: el arco empieza en el punto más alto
            IsClosed = false,
        };
        var end = new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    // --- clima -----------------------------------------------------------------

    static void DrawWeatherIcon(DrawingContext dc, double x, double y, string name)
    {
        var icon = LoadIcon(name);
        if (icon is null) return;  // si el archivo no está, no se dibuja

        const double size = 14;
        double sx = size / icon.ViewBox.Width;
        double sy = size / icon.ViewBox.Height;
        // Del viewBox de Phosphor (256) a 14 px, en la posición (x, y), pintado en blanco.
        var matrix = new Matrix(sx, 0, 0, sy, x - icon.ViewBox.X * sx, y - icon.ViewBox.Y * sy);
        dc.PushTransform(new MatrixTransform(matrix));
        foreach (var path in icon.Paths) dc.DrawGeometry(Paleta.White, null, path);
        dc.Pop();
    }

    static FormaIcono? LoadIcon(string name)
    {
        if (Iconos.TryGetValue(name, out var cached)) return cached;

        FormaIcono? shape = null;
        try
        {
            string file = Path.Combine(AppContext.BaseDirectory, "Recursos", "iconos", name + "-fill.svg");
            if (File.Exists(file))
            {
                var svg = XDocument.Load(file).Root!;
                var box = (svg.Attribute("viewBox")?.Value ?? "0 0 256 256")
                    .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
                    .ToArray();
                shape = new FormaIcono { ViewBox = new Rect(box[0], box[1], box[2], box[3]) };
                string rootRule = svg.Attribute("fill-rule")?.Value ?? "";
                foreach (var element in svg.Descendants().Where(e => e.Name.LocalName == "path"))
                {
                    string? data = element.Attribute("d")?.Value;
                    if (data is null) continue;
                    var geometry = Geometry.Parse(data);
                    if (geometry is PathGeometry path)
                    {
                        string rule = element.Attribute("fill-rule")?.Value ?? rootRule;
                        path.FillRule = rule == "evenodd" ? FillRule.EvenOdd : FillRule.Nonzero;
                    }
                    geometry.Freeze();
                    shape.Paths.Add(geometry);
                }
            }
        }
        catch (Exception)
        {
            shape = null;  // icono ilegible: se trata como si no existiera
        }

        Iconos[name] = shape;
        return shape;
    }

    // --- textos en caché -------------------------------------------------------

    static FormattedText ClockFormatted(string text)
    {
        if (_clockText is null || _clockKey != text)
        {
            _clockKey = text;
            _clockText = Formatted(text, ClockFace, Pt(16), Paleta.White);
        }
        return _clockText;
    }

    static FormattedText TempFormatted(string text)
    {
        if (_tempText is null || _tempKey != text)
        {
            _tempKey = text;
            _tempText = Formatted(text, SmallFace, Pt(9), Paleta.Gray);
        }
        return _tempText;
    }

    static FormattedText Formatted(string text, Typeface face, double size, Brush brush)
    {
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, 1.0);
    }

    // --- utilidades ------------------------------------------------------------

    static double Pt(double points) => points * 96.0 / 72.0;

    static double Mod(double value, double modulus) => ((value % modulus) + modulus) % modulus;

    static Geometry Polygon(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true };
        figure.Segments.Add(new PolyLineSegment(points.Skip(1), true));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>Par de pinceles de un color de carga: resplandor, arco grueso y arco nítido.</summary>
    sealed class ChargeTint
    {
        public readonly Color Tint;
        public readonly RadialGradientBrush Halo;
        public readonly Pen Glow;
        public readonly Pen Sharp;

        public ChargeTint(Color color)
        {
            Tint = color;
            Halo = Frozen(new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(90, color.R, color.G, color.B), 0.0),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0),
                },
            });
            Glow = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B))), 5.0)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            });
            Sharp = Frozen(new Pen(Frozen(new SolidColorBrush(color)), 2.4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            });
        }
    }

    /// <summary>Icono Phosphor ya convertido en geometrías, con su viewBox.</summary>
    sealed class FormaIcono
    {
        public readonly List<Geometry> Paths = new();
        public Rect ViewBox;
    }
}
