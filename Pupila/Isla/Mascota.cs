using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Pupila;

/// <summary>
/// Mascota de ojos de la isla: sueño, parpadeo, micro-movimientos, mirada, escenas de vida,
/// saludo de arranque, expresiones y su dibujo.
/// El origen de dc es la esquina superior izquierda de la pastilla.
/// </summary>
public static class Mascota
{
    // Tiempos y medidas de la mascota.
    const double SLEEP_AFTER_S = 60;      // sin usar la PC por este tiempo, la isla se duerme
    const double EYES_AFTER_IDLE_S = 15;  // sin mover el mouse este tiempo, salen los ojos y la hora se desvanece
    const double BLINK_EVERY_S = 4.2;
    const double BLINK_S = 0.15;
    const double EYE_SPACING = 11;        // distancia del centro a cada ojo
    const double EYE_WIDTH = 7;           // cápsulas: ancho y alto de cada ojo (caben en la pastilla de 32 px)
    const double EYE_HEIGHT = 14;
    const double GAZE_RANGE_PX = 46;      // cuánto se desplazan los ojos a cada lado dentro de la pastilla
    const double SCENE_GAP_MIN_S = 7.0;   // pausas al azar entre escenas: la vida no es constante
    const double SCENE_GAP_MAX_S = 20.0;
    const double SPRING_K = 45.0;         // rigidez de la mirada: relajada, no apurada
    const double SPRING_DAMP = 0.8;

    // Escenas de vida con su peso para el sorteo (nombre, peso).
    static readonly (string Name, double Weight)[] SCENES = { ("buscar", 4), ("pensar", 3), ("articular", 2) };

    // Duración de cada escena en segundos: se sortea entre mínimo y máximo al empezar.
    static (double Min, double Max) SceneDuration(string name) => name switch
    {
        "buscar" => (3.0, 5.0),
        "pensar" => (2.5, 4.0),
        _ => (1.5, 2.5),  // articular
    };

    // Brillo blanco suave alrededor de cada cápsula: fuerte en el centro y transparente en el borde.
    static readonly RadialGradientBrush EyeGlow = Frozen(new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromArgb(50, 255, 255, 255), 0.0),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0),
        },
    });

    // Trazo congelado para los ojos cerrados (arco).
    static readonly Pen ArcEyePen = Frozen(new Pen(Paleta.White, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    // Letras "z" de dormido. Sus tamaños van en puntos y se convierten a DIP (1 pt = 96/72 DIP).
    static readonly FormattedText[] ZzzText = BuildZzzText();

    // Estado de la mascota.
    static bool _started;
    static bool _sleeping;
    static bool _notifying;
    static double _gaze, _gazeV, _gazeTarget, _nextGaze;
    static double _lookY, _lookV, _lookTargetY;
    static string? _scene;
    static double _sceneUntil, _nextScene;
    static double _micro, _nextMicro;
    static double _lid, _lidV;
    static double _happyUntil, _eyesHoverUntil, _blinkUntil, _nextBlink;
    static double _notifyingUntil;
    static double _lean, _leanV;
    static double _eyesMix;
    static double _travelX, _travelV, _travelTarget;
    static bool _saludando, _saliendo;
    static double _salidaPalabra, _tiempoSalida;
    static double _arranqueHasta;

    /// <summary>True mientras el saludo se desvanece: la palabra se desenfoca y da paso al reloj.</summary>
    public static bool SaludoSaliendo => _saliendo;

    /// <summary>
    /// Mezcla del reloj: 1 = oculto, 0 = a la vista. Al salir del saludo, el reloj aparece
    /// 0,4 s más despacio que la palabra (0,8 s contra 0,4 s).
    /// </summary>
    public static double MixReloj => _saliendo ? 1 - Math.Clamp((_tiempoSalida - 0.55) / 0.8, 0, 1) : _eyesMix;

    /// <summary>True al arrancar, mientras la pastilla se expande vacía y todavía no empieza el saludo.</summary>
    public static bool EsperandoSaludo { get; set; }
    static double _entrada;
    static readonly Random Rng = new();

    /// <summary>True cuando la mascota duerme (sin uso prolongado).</summary>
    public static bool Sleeping => _sleeping;

    /// <summary>Mezcla de ojos: 0 = hora a la vista, 1 = ojos a la vista. La usa ContenidoReposo para desvanecer la hora.</summary>
    public static double EyesMix => _eyesMix;

    /// <summary>True mientras la mascota se mueve (buscar y el desplazamiento de la pastilla): la isla se redibuja a toda velocidad.</summary>
    public static bool Moviendose => _scene == "buscar" || Math.Abs(_travelX) > 0.01;

    /// <summary>Posición de la pastilla en el borde: -1 a la izquierda, 0 al centro, 1 a la derecha. Solo se mueve al buscar.</summary>
    public static double TravelX => _travelX;

    // --- lógica (un cuadro) ----------------------------------------------------

    /// <summary>Avanza la mascota un cuadro: sueño, parpadeo, mirada y escenas.</summary>
    public static void Update(double now, double dt, double idleSeconds)
    {
        dt = Math.Min(dt, 0.05);  // si la PC se traba, no saltar de golpe

        if (!_started)  // primer cuadro: arranca los temporizadores
        {
            _started = true;
            _nextBlink = now + BLINK_EVERY_S;
            _nextScene = now + SceneGap();
        }

        // Sueño: se duerme por inactividad, y al volver te saluda.
        if (!_sleeping && idleSeconds > SLEEP_AFTER_S)
        {
            _sleeping = true;
        }
        else if (_sleeping && idleSeconds < 1.0)
        {
            _sleeping = false;
            _happyUntil = now + 1.2;  // te saluda al volver
        }

        // Dormida: se corta la escena que estuviera corriendo.
        if (_sleeping)
        {
            _scene = null;
        }

        _notifying = now < _notifyingUntil;

        if (now >= _nextBlink)
        {
            _blinkUntil = now + BLINK_S;
            _nextBlink = now + BLINK_EVERY_S;
        }

        UpdateScene(now);

        if (_sleeping)
        {
            _gazeTarget = 0;
            _lookTargetY = 0;
        }
        else if (_scene == "buscar")
        {
            // Recorre la pastilla de lado a lado, como buscando algo.
            if (now >= _nextGaze)
            {
                _gazeTarget = Uniform(-0.7, 0.7);  // barrido amplio pero sin exagerar
                _lookTargetY = Uniform(-0.8, 0.8);
                _nextGaze = now + Uniform(0.7, 1.2);
            }
        }
        else if (_scene == "pensar")
        {
            // Mira hacia arriba a un lado, sin moverse mucho.
            _gazeTarget = -0.5;
            _lookTargetY = -0.9;
        }
        else if (now >= _nextGaze)
        {
            // Mirada normal: pequeños vaivenes cerca del centro.
            _gazeTarget = Uniform(-0.35, 0.35);
            _lookTargetY = Pick(-0.4, 0.0, 0.4);
            _nextGaze = now + Uniform(2.5, 5.0);
        }

        // Buscar: la pastilla sigue la mirada con el mismo resorte que los ojos, así va al mismo ritmo.
        _travelTarget = _scene == "buscar" && !_sleeping && MostrandoOjos() ? _gazeTarget : 0;  // fuera de buscar, vuelve al centro
        Resorte.Step(ref _travelX, ref _travelV, _travelTarget, dt, SPRING_K, SPRING_DAMP);

        if (now >= _nextMicro)
        {
            _micro = _sleeping ? 0 : Uniform(-0.25, 0.25);  // dormido: los ojos no se mueven
            _nextMicro = now + Uniform(0.3, 0.9);
        }

        // Durante el saludo de arranque, los ojos miran al centro y no se mueven.
        if (Saludando)
        {
            _gazeTarget = 0;
            _lookTargetY = 0;
            _micro = 0;
        }

        // Párpados: parpadeo rápido y, según la escena, una media cerrada.
        double restLid;
        if (_sleeping) restLid = 0.7;
        else if (_scene == "pensar") restLid = 0.3;
        else if (_scene == "articular") restLid = 0.3 + 0.15 * Math.Sin(now * 3.0);
        else restLid = 0.05 + 0.05 * Math.Sin(now * 0.35);
        double lidTarget = now < _blinkUntil ? 1.0 : restLid;
        Resorte.Step(ref _lid, ref _lidV, lidTarget, dt, 300.0, SPRING_DAMP);

        // Inclinación al saludar: se mueve despacio y vuelve sola cuando termina.
        double leanTarget = now < _happyUntil ? 1.0 : 0.0;
        Resorte.Step(ref _lean, ref _leanV, leanTarget, dt, 60.0, SPRING_DAMP);

        // Ojos a la vista tras 15 s sin mouse (o dormido), o al pasar el mouse; la hora se desvanece.
        // Mientras suena algo, la pastilla no vuelve a la mascota; solo se duerme si realmente duerme.
        // Al terminar el saludo de arranque, los ojos se van aunque la PC esté inactiva: el reloj vuelve.
        bool eyesTarget = !_saliendo && (_sleeping
            || (idleSeconds > EYES_AFTER_IDLE_S && !ServicioAudio.Playing && now >= _arranqueHasta)
            || now < _eyesHoverUntil);
        _eyesMix += ((eyesTarget ? 1.0 : 0.0) - _eyesMix) * Math.Min(1.0, dt * 4);

        Resorte.Step(ref _gaze, ref _gazeV, _gazeTarget + _micro, dt, SPRING_K, SPRING_DAMP);
        Resorte.Step(ref _lookY, ref _lookV, _lookTargetY, dt, SPRING_K, SPRING_DAMP);

        // Saludo de arranque: la entrada avanza en 1,2 s; al desvanecerse, termina cuando los ojos ya no se ven.
        if (_saludando && _entrada < 1) _entrada = Math.Min(1, _entrada + dt / 0.4);
        // Salida del saludo: la palabra tarda 0,45 s en desenfocarse y el reloj entra al mismo ritmo.
        if (_saliendo)
        {
            _tiempoSalida += dt;
            _salidaPalabra = Math.Min(1, _tiempoSalida / 0.35);
            _eyesMix = 1 - _salidaPalabra;  // lineal: la palabra se desenfoca y se aleja todo el tiempo
        }
        else
        {
            _salidaPalabra = 0;
            _tiempoSalida = 0;
        }
        // La palabra sale en 0,35 s, el reloj empieza 0,2 s después y tarda 0,8 s.
        if (_saliendo && _tiempoSalida >= 0.35 + 0.2 + 0.8) _saliendo = false;
    }

    static void UpdateScene(double now)
    {
        // Cada tanto, al azar, empieza una escena de vida; luego descansa un rato.
        if (_scene != null && now >= _sceneUntil)
        {
            _scene = null;
            _nextScene = now + SceneGap();
        }

        if (_scene == null && now >= _nextScene)
        {
            // Las escenas solo empiezan con los ojos a la vista: sin ojos, no se mueve nada.
            if (Saludando || !MostrandoOjos() || _notifying)
            {
                _nextScene = now + 2.0;  // no interrumpir mientras duerme o notifica
                return;
            }

            _scene = WeightedScene();
            var duration = SceneDuration(_scene);
            _sceneUntil = now + Uniform(duration.Min, duration.Max);
            if (_scene == "articular") _blinkUntil = now + BLINK_S;  // parpadeo al empezar
        }
    }

    // Solo se mueve la pastilla cuando la isla está cerrada y los ojos se ven. Expandida o con el reloj, quieta.
    static bool MostrandoOjos() => !_sleeping && _eyesMix > 0.5 && EstadoIsla.Progress < 0.05;

    // --- eventos -----------------------------------------------------

    /// <summary>Al pasar el mouse: saludo corto y ojos durante 2 s (si no duerme).</summary>
    public static void OnPointerEnter()
    {
        if (_sleeping)
        {
            Despertar();  // con el mouse encima, despierta con saludo en vez de mostrar el reloj de golpe
            return;
        }
        double now = EstadoIsla.Now;
        _happyUntil = now + 0.9;      // saludo al pasar el mouse
        _eyesHoverUntil = now + 2.0;  // los ojos aparecen al pasar el mouse
    }

    /// <summary>Al cerrarse la isla: los ojos del saludo se apagan de golpe, sin ojos a medio camino.</summary>
    public static void OnPointerLeave()
    {
        _eyesHoverUntil = 0;
        _eyesMix = 0;
    }

    /// <summary>Empieza el saludo de arranque: los ojos aparecen fuera de plano, se enfocan y saludan.</summary>
    public static void EmpezarSaludo()
    {
        double now = EstadoIsla.Now;
        EsperandoSaludo = false;
        _scene = null;  // el saludo empieza con los ojos quietos y al centro: sin buscar ni otras escenas
        _saludando = true;
        _saliendo = false;
        _entrada = 0;
        _eyesMix = 1;
        _happyUntil = now + 2.0;
        _eyesHoverUntil = now + 2.5;
    }

    /// <summary>Termina el saludo: los ojos se desvanecen (siguen con arco) mientras entra el reloj.</summary>
    public static void TerminarSaludo()
    {
        _saludando = false;
        _saliendo = true;  // el arco se queda mientras los ojos se desvanecen; el saludo ya terminó solo
        _eyesHoverUntil = 0;
        // Temporizador exclusivo del arranque: empieza cuando llega el reloj (0,55 s después de terminar el saludo) y dura 30 s.
        _arranqueHasta = EstadoIsla.Now + 0.55 + 30;
    }

    /// <summary>True mientras hay un saludo de arranque en pantalla (incluido su desvanecido).</summary>
    public static bool Saludando => _saludando || _saliendo;

    /// <summary>Avance de la entrada del saludo: 0 = fuera de plano y borrosa, 1 = en su lugar y enfocada.</summary>
    public static double Entrada => _entrada;

    /// <summary>Despierta con saludo: los ojos se quedan un momento y te saluda antes de volver el reloj.</summary>
    public static void Despertar()
    {
        double now = EstadoIsla.Now;
        _sleeping = false;
        _happyUntil = now + 1.2;
        _eyesHoverUntil = now + 2.5;
    }

    /// <summary>Pulso rápido y fuerte mientras hay una notificación pendiente.</summary>
    public static void Notify(double seconds = 8)
    {
        if (EstadoIsla.ArranqueActivo) return;
        _notifyingUntil = EstadoIsla.Now + seconds;
    }

    // --- dibujo ----------------------------------------------------------------

    /// <summary>
    /// Dibuja los ojos y la mirada dentro de la pastilla de ancho sw y alto sh.
    /// La opacidad la fija quien llama. Incluye el zzz de dormido.
    /// </summary>
    public static void DrawEyes(DrawingContext dc, double sw, double sh)
    {
        if (Saludando) return;  // durante el saludo de arranque, los ojos los dibuja SaludoBienvenida
        DibujarOjos(dc, sw, sh);
    }

    static void DibujarOjos(DrawingContext dc, double sw, double sh)
    {
        double now = EstadoIsla.Now;
        double cx = sw / 2;  // cuando salen, los ojos ocupan el centro de la pastilla
        // Balanceo suave en reposo: los ojos flotan arriba y abajo y se mueven un poco de lado.
        double floatY = _sleeping ? 0 : Math.Sin(now * 1.6) * 1.2;
        double swayX = _sleeping ? 0 : Math.Sin(now * 0.9) * 0.4;
        double cy = sh / 2 + floatY + _lookY * 3 - _lean * 1.2;

        for (int side = -1; side <= 1; side += 2)
        {
            double ex = cx + side * EYE_SPACING + _gaze * GAZE_RANGE_PX + swayX + _lean * 2.5;
            if (_sleeping)
            {
                DrawArcEye(dc, ex, cy, 4);
            }
            else if (now < _happyUntil || _saludando || _saliendo)  // durante todo el saludo, nada de ojos normales
            {
                DrawArcEye(dc, ex, cy, -5);
            }
            else if (now < _blinkUntil)
            {
                dc.DrawRoundedRectangle(Paleta.White, null, new Rect(ex - 4.5, cy - 0.8, 9, 1.6), 0.8, 0.8);
            }
            else
            {
                DrawOvalEye(dc, ex, cy);
            }
        }

        if (_sleeping) DrawZzz(dc, cx, cy, now);
    }

    static void DrawOvalEye(DrawingContext dc, double ex, double cy)
    {
        // Cápsula blanca vertical. Respira: crece y encoge despacio.
        double alto = EYE_HEIGHT * (1 + 0.03 * Math.Sin(EstadoIsla.Now * 1.6));
        double top = cy - alto / 2;

        // El párpado tapa la cápsula desde arriba recortando el dibujo (el brillo también).
        double lidY = top + _lid * alto;
        dc.PushClip(new RectangleGeometry(new Rect(ex - 20, lidY, 40, alto + 8)));
        DrawGlow(dc, ex, cy, alto);

        dc.DrawRoundedRectangle(Paleta.White, null, new Rect(ex - EYE_WIDTH / 2, top, EYE_WIDTH, alto), EYE_WIDTH / 2, EYE_WIDTH / 2);
        dc.Pop();
    }

    // Brillo suave alrededor de la cápsula: un degradado que se desvanece hacia afuera, sin bordes.
    static void DrawGlow(DrawingContext dc, double ex, double cy, double alto)
    {
        dc.DrawEllipse(EyeGlow, null, new Point(ex, cy), EYE_WIDTH / 2 + 5, alto / 2 + 5);
    }

    static void DrawArcEye(DrawingContext dc, double ex, double cy, double bend)
    {
        // Ojo cerrado: bend > 0 curva hacia abajo (dormido), bend < 0 hacia arriba (feliz).
        dc.DrawGeometry(null, ArcEyePen, Quad(new Point(ex - 5, cy), new Point(ex, cy + bend), new Point(ex + 5, cy), false));
    }

    static void DrawZzz(DrawingContext dc, double cx, double cy, double now)
    {
        // Tres letras que suben y se desvanecen en bucle.
        for (int i = 0; i < 3; i++)
        {
            double phase = Mod(now / 2.4 - i * 0.33, 1.0);
            double alpha = Math.Sin(phase * Math.PI) * 0.4;  // más tenue
            double x = cx + 12 + i * 7 + phase * 4;
            double y = cy - 4 - phase * 14 - i * 2;
            var text = ZzzText[i];
            // Centrada en un recuadro de 12x12.
            dc.PushOpacity(Math.Max(0.0, alpha));
            dc.DrawText(text, new Point(x + (12 - text.Width) / 2, y - 10 + (12 - text.Height) / 2));
            dc.Pop();
        }
    }

    // --- utilidades ------------------------------------------------------------

    static double SceneGap() => Uniform(SCENE_GAP_MIN_S, SCENE_GAP_MAX_S);

    static double Uniform(double low, double high) => low + (high - low) * Rng.NextDouble();

    static double Pick(params double[] values) => values[Rng.Next(values.Length)];

    static double Mod(double value, double modulus) => ((value % modulus) + modulus) % modulus;

    static string WeightedScene()
    {
        double total = 0;
        foreach (var scene in SCENES) total += scene.Weight;
        double roll = Rng.NextDouble() * total;
        foreach (var scene in SCENES)
        {
            roll -= scene.Weight;
            if (roll < 0) return scene.Name;
        }
        return SCENES[^1].Name;
    }

    static PathGeometry Quad(Point start, Point control, Point end, bool closed)
    {
        var figure = new PathFigure { StartPoint = start, IsClosed = closed };
        figure.Segments.Add(new QuadraticBezierSegment(control, end, true));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }

    static FormattedText[] BuildZzzText()
    {
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var texts = new FormattedText[3];
        for (int i = 0; i < 3; i++)
        {
            texts[i] = new FormattedText(i % 2 == 0 ? "z" : "Z", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, Pt(5 + 2 * i), Paleta.White, 1.0);
        }
        return texts;
    }

    static double Pt(double points) => points * 96.0 / 72.0;

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
