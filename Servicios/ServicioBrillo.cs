using System.Management;

namespace Pupila;

/// <summary>
/// Se entera de los cambios de brillo de la pantalla interna (laptops) y los manda a la isla.
/// Windows avisa cada cambio con el evento WmiMonitorBrightnessEvent, así que no hay que preguntar.
/// Como respaldo, cada 10 s se revisa el valor por si algún equipo no emite el aviso.
/// Si la PC no expone el brillo (monitor externo o escritorio), se rinde sin ruido.
/// </summary>
public static class ServicioBrillo
{
    private const int RespaldoMs = 10000;

    private static readonly object _cerrojo = new();
    private static int? _anterior;

    public static void Start()
    {
        var hilo = new Thread(Bucle) { IsBackground = true, Name = "Pupila brillo" };
        hilo.Start();
    }

    private static void Bucle()
    {
        try
        {
            // Valor inicial: se guarda sin avisar a la isla.
            _anterior = LeerBrillo();

            var vigilante = new ManagementEventWatcher(
                new ManagementScope(@"root\WMI"),
                new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            vigilante.EventArrived += (_, e) =>
            {
                if (e.NewEvent["Brightness"] is { } valor)
                    Anunciar(Convert.ToInt32(valor));
            };
            vigilante.Start();

            // Respaldo lento: si el aviso no llega en algún equipo, el cambio se ve igual.
            while (true)
            {
                Thread.Sleep(RespaldoMs);
                if (LeerBrillo() is int brillo) Anunciar(brillo);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Si el brillo cambió, lo manda a la isla. Lo pueden llamar el aviso de Windows y el respaldo.</summary>
    private static void Anunciar(int brillo)
    {
        lock (_cerrojo)
        {
            if (_anterior is int previo && brillo != previo)
                BarraVolumenBrillo.Cambio(TipoBarra.Brillo, brillo);
            _anterior = brillo;
        }
    }

    /// <summary>Brillo de la pantalla interna en este momento, o null si no hay.</summary>
    private static int? LeerBrillo()
    {
        using var consulta = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
        foreach (ManagementObject pantalla in consulta.Get())
        {
            return Convert.ToInt32(pantalla["CurrentBrightness"]);  // la pantalla interna es la primera
        }
        return null;
    }
}
