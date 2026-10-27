using System.Threading;
using WinForms = System.Windows.Forms;

namespace Pupila;

/// <summary>
/// Arranque: instancia única, servicios en segundo plano, icono de bandeja y la isla.
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _instancia;  // mantiene vivo el mutex mientras la app está abierta
    private WinForms.NotifyIcon? _bandeja;
    private VentanaIsla? _isla;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // Si ya hay una copia abierta, esta sale en silencio.
        _instancia = new Mutex(true, "Pupila-instancia-unica", out bool creada);
        if (!creada)
        {
            Shutdown();
            return;
        }

        ServicioClima.Start();
        ServicioMultimedia.Start();
        ServicioAudio.Start();
        DetectorContenido.Start();
        ServicioAvisos.Start();
        ServicioVolumen.Start();
        ServicioBateria.Start();
        ServicioBrillo.Start();
        ServicioTeclas.Start();
        IndicadorBloqueo.Start();

        CrearBandeja();

        _isla = new VentanaIsla();
        _isla.Opacity = 0;  // oculta hasta el destello
        EstadoIsla.Minimized = true;  // arranca como barrita; al aparecer se expande con la animación normal
        Mascota.EsperandoSaludo = true;  // la pastilla sale vacía; el reloj no aparece antes del saludo
        _isla.Show();
        Introduccion.Reproducir(() =>
        {
            // Primero la pastilla se expande con su animación normal.
            EstadoIsla.AnimateTo(0.0);
            if (_isla != null)
            {
                _isla.Opacity = 1;
                EstadoIsla.Minimized = false;  // la barrita se expande hasta la pastilla
            }

            // Después, los ojos salen fuera de plano y borrosos, se enfocan mientras saludan; luego entra el reloj.
            Despues(0.6, Mascota.EmpezarSaludo);
            Despues(3.0, Mascota.TerminarSaludo);  // después de que el saludo termina de regresar
        });
    }

    /// <summary>Ejecuta una acción después de unos segundos, en el hilo de la interfaz.</summary>
    private static void Despues(double segundos, Action accion)
    {
        var temporizador = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(segundos) };
        temporizador.Tick += (_, _) =>
        {
            temporizador.Stop();
            accion();
        };
        temporizador.Start();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_bandeja != null)
        {
            _bandeja.Visible = false;  // que el icono no quede fantasma en la bandeja
            _bandeja.Dispose();
        }
        base.OnExit(e);
    }

    private void CrearBandeja()
    {
        var menu = new WinForms.ContextMenuStrip();

        var minimo = new WinForms.ToolStripMenuItem("Modo mínimo") { CheckOnClick = true };
        minimo.CheckedChanged += (_, _) => _isla?.SetMinimized(minimo.Checked);
        menu.Items.Add(minimo);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Shutdown());

        _bandeja = new WinForms.NotifyIcon
        {
            Icon = IconoDelPrograma(),
            Text = "Pupila",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    /// <summary>El mismo icono del ejecutable (el de Pupila); si no se puede leer, el genérico de Windows.</summary>
    private static System.Drawing.Icon IconoDelPrograma()
    {
        try
        {
            return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? System.Drawing.SystemIcons.Application;
        }
        catch (Exception)
        {
            return System.Drawing.SystemIcons.Application;
        }
    }
}
