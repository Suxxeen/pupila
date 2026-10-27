using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace Pupila;

/// <summary>
/// Ventana principal de la isla: sin bordes, transparente y siempre encima.
/// Cada cuadro actualiza el cuerpo, la expansión y el ocultado, y dibuja la pastilla negra.
/// Solo recibe el mouse dentro de la forma de la isla (WM_NCHITTEST).
/// </summary>
public partial class VentanaIsla : Window
{
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_NCHITTEST = 0x0084;
    private const int MA_NOACTIVATE = 3;
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const double OcultarRebote = 1.70158;  // intensidad del rebote de la curva OutBack al ocultar la isla

    private readonly Stopwatch _reloj = Stopwatch.StartNew();
    private readonly LienzoIsla _lienzo;
    private readonly SaludoBienvenida _ojos = new() { IsHitTestVisible = false };
    private readonly CapaOjos _capaOjos = new() { IsHitTestVisible = false };
    private int _cuadro;
    private readonly DispatcherTimer _cierre;
    private readonly DispatcherTimer _osdCierre;  // cuánto tiempo se queda la barra de volumen o brillo
    private readonly DispatcherTimer _teclaCheck;  // espera a leer el volumen después de una tecla
    private readonly DispatcherTimer _repeticion;  // repite la tecla de volumen mientras se mantiene el clic
    private int _direccionMantenida;  // -1 o 1 mientras se mantiene un botón de volumen

    private double _ultimoCuadro;
    private readonly CuadroFijo _cuadroFijo = new();
    private double _siguienteRevision;
    private bool _cubierta;

    // Cuerpo con resorte: tamaño de la pastilla en reposo.
    private double _ancho = Constantes.IdleW;
    private double _velAncho;
    private double _alto = Constantes.IdleH;
    private double _velAlto;

    // Pastilla compacta del volumen o brillo: 0 = isla normal, 1 = barra visible.
    private double _osdMix;
    private double _osdVel;

    // Latido de la isla completa cada vez que cambia el volumen o el brillo.
    private double _pulso;
    private double _pulsoVel;

    // Ocultado animado (HideT).
    private double _ocultoDesde;
    private double _ocultoHasta;
    private double _ocultoInicio;
    private bool _ocultoCorriendo;

    // Geometría del último cuadro: la usan el dibujo y la detección de clics.
    private StreamGeometry? _forma;
    private double _sw;
    private double _sh;
    private double _offsetX;
    private const double TravesiaBuscar = 60;  // píxeles que se corre la pastilla a cada lado al buscar

    public VentanaIsla()
    {
        InitializeComponent();

        Width = Constantes.CanvasW;
        Height = Constantes.CanvasH;
        // Centrada arriba en la pantalla primaria (unidades WPF).
        Left = Math.Floor(SystemParameters.PrimaryScreenWidth / 2) - Constantes.CanvasW / 2;
        Top = 0;

        _lienzo = new LienzoIsla(this);
        var capas = new System.Windows.Controls.Grid();
        capas.Children.Add(_lienzo);
        capas.Children.Add(_capaOjos);  // los ojos: su propia capa, por encima de la isla
        capas.Children.Add(_ojos);  // el saludo va encima, con su propio desenfoque
        Content = capas;

        _cierre = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Constantes.CloseDelayMs) };
        _cierre.Tick += (_, _) =>
        {
            _cierre.Stop();
            AlExpirarCierre();
        };

        // Un aviso nuevo solo mueve a la mascota; el aviso se ve en la campanita.
        ServicioAvisos.Nueva += _ => Dispatcher.BeginInvoke(new Action(() => Mascota.Notify()));

        _osdCierre = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _osdCierre.Tick += (_, _) =>
        {
            _osdCierre.Stop();
            CerrarOsd();
        };
        BarraVolumenBrillo.Cambiado += tope => Dispatcher.BeginInvoke(new Action(() => MostrarOsd(tope)));
        // Al presionar, se espera a que se lea el valor nuevo: solo rebota si sigue en el tope.
        _teclaCheck = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _teclaCheck.Tick += (_, _) =>
        {
            _teclaCheck.Stop();
            BarraVolumenBrillo.TeclaVolumen();
        };
        ServicioTeclas.TeclaVolumen += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            _teclaCheck.Stop();
            _teclaCheck.Start();
        }));

        _repeticion = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _repeticion.Tick += (_, _) =>
        {
            _repeticion.Interval = TimeSpan.FromMilliseconds(110);  // tras la pausa inicial, repite cada 110 ms
            if (_direccionMantenida == 0) return;
            Nativo.PulsarTeclaVolumen(_direccionMantenida < 0 ? Nativo.VK_VOLUME_DOWN : Nativo.VK_VOLUME_UP);
        };
        MouseLeftButtonUp += (_, _) => PararRepeticion();

        _ultimoCuadro = _reloj.Elapsed.TotalSeconds;

        MouseEnter += AlEntrar;
        MouseLeave += AlSalir;
        MouseLeftButtonDown += AlHacerClic;
        SourceInitialized += AlInicializarOrigen;
        CompositionTarget.Rendering += AlCuadro;
    }

    /// <summary>Modo mínimo desde el menú de la bandeja: la isla se oculta y queda una barrita.</summary>
    public void SetMinimized(bool on)
    {
        EstadoIsla.Minimized = on;
    }

    // --- cuadro -------------------------------------------------------------

    /// <summary>Corre al ritmo de la pantalla: cuerpo, expansión, ocultado y geometría.</summary>
    private void AlCuadro(object? sender, EventArgs e)
    {
        double ahora = _reloj.Elapsed.TotalSeconds;
        if (!_cuadroFijo.Toca(ahora)) return;  // las animaciones van a 60 cuadros por segundo, sin importar el monitor
        double dt = Math.Min(0.05, ahora - _ultimoCuadro);  // si la PC se traba, no saltar de golpe
        _ultimoCuadro = ahora;
        EstadoIsla.Now = ahora;
        EstadoIsla.Dt = dt;
        EstadoIsla.Tick();

        // Ojos, gestos, escenas, sueño y carga: los llevan sus módulos.
        Mascota.Update(ahora, dt, Nativo.IdleSeconds());
        Reacciones.Revisar(ahora);
        IndicadorBloqueo.Revisar(ahora);
        ContenidoReposo.Update(ahora, dt);


        // Tapado: se revisa cada 0.5 s.
        if (ahora >= _siguienteRevision)
        {
            _siguienteRevision = ahora + 0.5;
            RevisarTapado();
        }
        ActualizarOcultado(ahora);

        // La pastilla compacta del volumen o brillo sale de la isla con un rebote suave.
        Resorte.Step(ref _osdMix, ref _osdVel, BarraVolumenBrillo.Visible ? 1.0 : 0.0, dt, 110.0, 0.7);
        Resorte.Step(ref _pulso, ref _pulsoVel, 0.0, dt, 200.0, 0.2);  // rebote que se nota y vuelve a 0

        // Cuerpo con resorte. Con carga activa, la pastilla cambia de ancho según la animación de carga.
        Resorte.Step(ref _ancho, ref _velAncho, ContenidoReposo.ChargeActive ? ContenidoReposo.ChargeWidth : Constantes.IdleW, dt, 220.0, 1.0);
        Resorte.Step(ref _alto, ref _velAlto, Constantes.IdleH, dt, 220.0, 1.0);

        var (sw, sh) = TamanoForma();
        _sw = sw;
        _sh = sh;
        // Centrada en el lienzo. Mascota.TravelX (buscar) se suma como desplazamiento.
        // Se mueve dentro de la ventana, sin mover la ventana misma, que es lenta de reposicionar.
        _offsetX = Math.Floor((Constantes.CanvasW - (sw + 2 * Constantes.Pad)) / 2.0) + Mascota.TravelX * TravesiaBuscar;
        _forma = ConstruirForma(sw, sh);

        _ojos.Actualizar(_sw, _sh, _offsetX);
        _capaOjos.Actualizar(_sw, _sh, _offsetX);
        // En reposo la isla casi no cambia: se vuelve a dibujar 15 veces por segundo. Con movimiento, en cada cuadro.
        _cuadro++;
        // Los ojos tienen su propia capa, así que no cuentan aquí. Sí cuentan la música (espectro), los avisos,
        // las transiciones y el movimiento de buscar, que deben verse fluidos.
        bool enReposo = EstadoIsla.Progress < 0.01 && EstadoIsla.HideT < 0.01 && !BarraVolumenBrillo.Visible && !Mascota.Saludando
            && (Mascota.EyesMix < 0.01 || Mascota.EyesMix > 0.99) && !ContenidoReposo.ChargeActive && !Mascota.Moviendose
            && IndicadorBloqueo.Opacidad(ahora) < 0.001 && !ServicioAudio.Playing && ServicioAvisos.Pendientes == 0;
        if (!enReposo || _cuadro % 4 == 0) _lienzo.InvalidateVisual();
    }

    /// <summary>Revisa si hay una ventana tapando la isla. Si cambia, la isla vuelve a su modo automático.</summary>
    private void RevisarTapado()
    {
        bool pantallaCompleta = Nativo.IsFullscreenForeground();
        bool contenido = DetectorContenido.Covered;
        bool tapado = pantallaCompleta || contenido;
        if (tapado != _cubierta)
        {
            _cubierta = tapado;
            EstadoIsla.Revealed = false;
        }
    }

    /// <summary>Oculta la isla hasta la barrita, o la muestra, con la misma curva que la expansión.</summary>
    private void ActualizarOcultado(double ahora)
    {
        double objetivo = (EstadoIsla.Minimized || _cubierta) ? 1.0 : 0.0;
        if (objetivo != _ocultoHasta)
        {
            _ocultoDesde = EstadoIsla.HideT;
            _ocultoHasta = objetivo;
            _ocultoInicio = ahora;
            _ocultoCorriendo = true;
        }
        if (!_ocultoCorriendo) return;

        double u = (ahora - _ocultoInicio) / (Constantes.AnimMs / 1000.0);
        if (u >= 1.0)
        {
            EstadoIsla.HideT = _ocultoHasta;
            _ocultoCorriendo = false;
        }
        else
        {
            EstadoIsla.HideT = _ocultoDesde + (_ocultoHasta - _ocultoDesde) * EstadoIsla.OutBack(u, OcultarRebote);
        }
    }

    /// <summary>Tamaño de la forma: cuerpo, mezclado con el modo mínimo y con la expansión.</summary>
    private (int W, int H) TamanoForma()
    {
        double anchoIdle = Lerp(_ancho, Constantes.MinW, EstadoIsla.HideT);
        anchoIdle = Lerp(anchoIdle, BarraVolumenBrillo.Width, Math.Clamp(_osdMix, 0, 1));  // la barra de volumen o brillo la ensancha
        double altoIdle = Lerp(_alto, Constantes.MinH, EstadoIsla.HideT);
        double p = EstadoIsla.Progress;
        return ((int)Math.Round(Lerp(anchoIdle, Constantes.OpenW, p)),
                (int)Math.Round(Lerp(altoIdle, Constantes.OpenH, p)));
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Contorno de la isla con curvas circulares: siempre redondeado, nunca filoso al animarse.</summary>
    private static StreamGeometry ConstruirForma(double sw, double sh)
    {
        double x0 = Constantes.Pad;
        double x1 = Constantes.Pad + sw;
        double f = Math.Min(Constantes.Flare, sh / 2);  // hombros que salen del borde de la pantalla
        double r = Math.Min(sh / 2, 38);                // esquinas de abajo bien redondas
        const double k = 0.5523;                        // constante para aproximar un cuarto de círculo

        var geometria = new StreamGeometry();
        using (StreamGeometryContext c = geometria.Open())
        {
            c.BeginFigure(new Point(x0 - f, 0), true, true);
            c.BezierTo(new Point(x0 - f + f * k, 0), new Point(x0, f - f * k), new Point(x0, f), true, false);
            c.LineTo(new Point(x0, sh - r), true, false);
            c.BezierTo(new Point(x0, sh - r + r * k), new Point(x0 + r - r * k, sh), new Point(x0 + r, sh), true, false);
            c.LineTo(new Point(x1 - r, sh), true, false);
            c.BezierTo(new Point(x1 - r + r * k, sh), new Point(x1, sh - r + r * k), new Point(x1, sh - r), true, false);
            c.LineTo(new Point(x1, f), true, false);
            c.BezierTo(new Point(x1, f - f * k), new Point(x1 + f - f * k, 0), new Point(x1 + f, 0), true, false);
        }
        geometria.Freeze();
        return geometria;
    }

    /// <summary>Dibujo de la isla. Lo llama LienzoIsla.OnRender.</summary>
    internal void DibujarIsla(DrawingContext dc)
    {
        var forma = _forma;
        if (forma == null) return;
        double t = EstadoIsla.Progress;
        double sw = _sw;
        double sh = _sh;

        dc.PushTransform(new TranslateTransform(_offsetX, 0));

        // Latido: la isla completa crece y vuelve, desde su centro.
        var latido = Matrix.Identity;
        latido.ScaleAt(1 + _pulso * 0.06, 1 + _pulso * 0.06, sw / 2 + Constantes.Pad, sh / 2);
        dc.PushTransform(new MatrixTransform(latido));

        // (1) Relleno negro liso: sin degradado ni resplandor.
        dc.DrawGeometry(Paleta.Black, null, forma);

        // (2) Todo lo que sigue se recorta a la forma.
        dc.PushClip(forma);

        // (3) Contenido en reposo (hora, clima, batería, ojos...), con una escala muy ligera al rebotar.
        if (t < 0.99 && EstadoIsla.HideT < 0.5)
        {
            double over = Math.Max(0, t - 1) + Math.Max(0, -t);
            double escala = 1 + 0.25 * over;
            double cx = Constantes.Pad + sw / 2;
            double cy = sh / 2;
            var m = Matrix.Identity;
            m.ScaleAt(escala, escala, cx, cy);
            dc.PushTransform(new MatrixTransform(m));
            // El contenido parte del centro de la pastilla (sw/2 desde Pad), igual que el reproductor.
            dc.PushTransform(new TranslateTransform(Constantes.Pad, 0));
            // La hora y el clima se desvanecen mientras aparece la barra de volumen o brillo.
            dc.PushOpacity(1 - Math.Clamp(_osdMix, 0, 1));
            ContenidoReposo.Draw(dc, sw, sh, t);
            dc.Pop();
            dc.Pop();
        }

        // (3b) Barra de volumen o brillo compacta: sale de la isla en reposo.
        double osd = Math.Clamp(_osdMix, 0, 1);
        if (osd > 0.01 && t < 0.5)
        {
            dc.PushOpacity(osd);
            BarraVolumenBrillo.Draw(dc, sw, sh, 1.0);
            dc.Pop();
        }

        // (4) Control multimedia: aparece de abajo hacia arriba (PanelReproductor hace el recorte y la opacidad). Sin escala.
        double alpha = Math.Clamp((t - 0.25) / 0.6, 0.0, 1.0);
        // Con el reproductor abierto se queda el reproductor: la barra compacta solo sale con la isla en reposo.
        if (alpha > 0.0)
            PanelReproductor.Draw(dc, sw, sh, alpha);

        // (5) Se quitan el recorte de la forma, el latido y el desplazamiento.
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    // --- eventos del mouse -------------------------------------------------

    private void AlEntrar(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (EstadoIsla.ArranqueActivo) return;  // durante el arranque, el mouse no despliega la isla
        _cierre.Stop();
        ServicioAvisos.Limpiar();  // al pasar el mouse, la campanita se queda sin avisos pendientes
        if (!Mascota.Sleeping) Mascota.OnPointerEnter();
        if (EstadoIsla.HideT < 0.5 || EstadoIsla.Revealed) EstadoIsla.AnimateTo(1.0);
    }

    private void AlSalir(object sender, System.Windows.Input.MouseEventArgs e)
    {
        PararRepeticion();
        _cierre.Stop();
        _cierre.Start();
    }

    /// Deja de repetir la tecla de volumen (al soltar el botón o al salir de la isla).
    private void PararRepeticion()
    {
        _direccionMantenida = 0;
        _repeticion.Stop();
    }

    private void AlExpirarCierre()
    {
        if (EstaSobreIsla()) return;
        BarraVolumenBrillo.Ocultar();
        Mascota.OnPointerLeave();
        EstadoIsla.AnimateTo(0.0);
    }

    /// <summary>Cambió el volumen o el brillo: la isla se expande un momento con la barra. Si está tapada, no se muestra.</summary>
    private void MostrarOsd(bool llegoAlTope)
    {
        if (EstadoIsla.ArranqueActivo) return;  // durante el arranque, el volumen y el brillo no se muestran
        if (EstadoIsla.Minimized || _cubierta || EstadoIsla.HideT > 0.5) return;
        // El latido es solo de la barra compacta: con el reproductor abierto no rebota nada.
        if (llegoAlTope && EstadoIsla.Progress < 0.5) _pulsoVel += 14;
        _osdCierre.Stop();
        _osdCierre.Start();
    }

    /// <summary>Pasó el tiempo de la barra: la pastilla compacta se encoge de vuelta a la isla.</summary>
    private void CerrarOsd()
    {
        BarraVolumenBrillo.Ocultar();
    }

    private void AlHacerClic(object sender, MouseButtonEventArgs e)
    {
        if (EstadoIsla.ArranqueActivo) return;  // durante el arranque, los clics no hacen nada
        if (EstadoIsla.HideT > 0.5)  // clic en la barrita: la isla se muestra
        {
            EstadoIsla.Revealed = true;
            EstadoIsla.AnimateTo(1.0);
            return;
        }
        if (EstadoIsla.Progress > 0.5)
        {
            Point p = e.GetPosition(_lienzo);
            double x = p.X - _offsetX - Constantes.Pad;
            PanelReproductor.HandleClick(x, p.Y);

            // Si es un botón de volumen, sigue subiendo o bajando mientras se mantiene presionado.
            int direccion = PanelReproductor.DireccionVolumenEn(x, p.Y);
            if (direccion != 0)
            {
                _direccionMantenida = direccion;
                _repeticion.Interval = TimeSpan.FromMilliseconds(350);  // pausa antes de empezar a repetir
                _repeticion.Start();
            }
        }
    }

    /// <summary>True si el mouse está encima de la forma de la isla.</summary>
    private bool EstaSobreIsla()
    {
        var forma = _forma;
        if (forma == null) return false;
        Point p = Mouse.GetPosition(_lienzo);
        return forma.FillContains(new Point(p.X - _offsetX, p.Y));
    }

    // --- ventana de Windows --------------------------------------------------

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private void AlInicializarOrigen(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        // Herramienta (sin botón en la barra de tareas) y no activable: no le roba el foco a nadie.
        long estilo = ObtenerEstilo(hwnd);
        PonerEstilo(hwnd, estilo | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd).AddHook(FiltrarMensajes);
    }

    private static long ObtenerEstilo(IntPtr hwnd)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64()
            : GetWindowLong32(hwnd, GWL_EXSTYLE);
    }

    private static void PonerEstilo(IntPtr hwnd, long estilo)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(estilo));
        else SetWindowLong32(hwnd, GWL_EXSTYLE, (int)estilo);
    }

    private IntPtr FiltrarMensajes(IntPtr hwnd, int mensaje, IntPtr wParam, IntPtr lParam, ref bool manejado)
    {
        if (mensaje == WM_MOUSEACTIVATE)
        {
            manejado = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        if (mensaje == WM_NCHITTEST)
        {
            // lParam trae la posición del mouse en píxeles reales de la pantalla (x y y con signo).
            long l = lParam.ToInt64();
            int x = (short)(l & 0xFFFF);
            int y = (short)((l >> 16) & 0xFFFF);
            manejado = true;
            // Dentro de la isla recibe el mouse; fuera, el clic pasa a la ventana de abajo.
            return new IntPtr(DentroDeLaIsla(x, y) ? HTCLIENT : HTTRANSPARENT);
        }
        return IntPtr.Zero;
    }

    private bool DentroDeLaIsla(int xFisico, int yFisico)
    {
        var forma = _forma;
        if (forma == null) return false;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Point p = PointFromScreen(new Point(xFisico / dpi.DpiScaleX, yFisico / dpi.DpiScaleY));
        return forma.FillContains(new Point(p.X - _offsetX, p.Y));
    }
}

/// <summary>
/// Superficie de dibujo de la isla. Delega el dibujo y el mouse en VentanaIsla.
/// </summary>
internal sealed class LienzoIsla : FrameworkElement
{
    private readonly VentanaIsla _dueno;

    internal LienzoIsla(VentanaIsla dueno)
    {
        _dueno = dueno;
    }

    protected override void OnRender(DrawingContext dc)
    {
        _dueno.DibujarIsla(dc);
    }

    // El recorte real lo hace WM_NCHITTEST; aquí todo el lienzo responde a la prueba.
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return new PointHitTestResult(this, hitTestParameters.HitPoint);
    }
}
