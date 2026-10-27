using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace Pupila;

/// <summary>
/// Temperatura y condición del clima. Primero se lee el botón de Widgets de la barra de tareas de Windows
/// ("Widgets 29°C Parc. soleado"). Si ese botón no está (widget oculto o desactivado), se usa la ubicación
/// de Windows (Wi-Fi o GPS) y Open-Meteo. Se revisa al arrancar y cada 15 minutos.
/// </summary>
public static class ServicioClima
{
    const int INTERVAL_S = 900;  // cada 15 min: el clima cambia despacio

    // Pide la ubicación a Windows en un proceso de PowerShell (la app de escritorio no puede hacerlo sola).
    const string ScriptUbicacion = """
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
        [Windows.Devices.Geolocation.Geolocator, Windows.Devices.Geolocation, ContentType=WindowsRuntime] | Out-Null
        $g = New-Object Windows.Devices.Geolocation.Geolocator
        $task = $asTask.MakeGenericMethod([Windows.Devices.Geolocation.Geoposition]).Invoke($null, @($g.GetGeopositionAsync()))
        $task.Wait(15000) | Out-Null
        $p = $task.Result.Coordinate.Point.Position
        $inv = [Globalization.CultureInfo]::InvariantCulture
        Write-Output ("{0}|{1}" -f $p.Latitude.ToString($inv), $p.Longitude.ToString($inv))
        """;

    static readonly Regex Temperatura = new(@"(-?\d+)\s*°", RegexOptions.Compiled);
    static readonly Regex Lluvia = new(@"lluv|llov|chubas|rain|drizzle|shower|tormenta|storm", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly HttpClient Http = CreateClient();

    static bool _started;
    static volatile string _tempText = "";
    static volatile string _iconName = "cloud";

    /// <summary>Temperatura para mostrar, p. ej. "24°"; "" si todavía no hay dato.</summary>
    public static string TempText => _tempText;

    /// <summary>Icono del clima: "cloud" (sin lluvia) o "cloud-rain" (lluvia).</summary>
    public static string IconName => _iconName;

    /// <summary>Arranca la lectura en segundo plano: al arrancar y cada 15 min. Llamar una sola vez.</summary>
    public static void Start()
    {
        if (_started) return;
        _started = true;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await LeerAsync();
                await Task.Delay(TimeSpan.FromSeconds(INTERVAL_S));
            }
        });
    }

    static async Task LeerAsync()
    {
        try
        {
            // El botón a veces tarda en aparecer al arrancar: se intenta tres veces antes de usar la ubicación.
            for (int intento = 0; intento < 3; intento++)
            {
                if (LeerWidget()) return;
                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            var ubicacion = await UbicacionWindowsAsync();
            if (ubicacion is null)
            {
                return;
            }

            var (temp, code) = await FetchWeatherAsync(ubicacion.Value.Lat, ubicacion.Value.Lon);
            _tempText = temp;
            _iconName = WeatherIconName(code);
        }
        catch (Exception)
        {
            // Sin internet o error inesperado: se queda con la última temperatura.
        }
    }

    /// <summary>Lee el botón de Widgets. Devuelve false si no está o no trae temperatura.</summary>
    static bool LeerWidget()
    {
        string? nombre;
        try
        {
            nombre = NombreDelBotonDeWidgets();
        }
        catch (Exception)
        {
            // La barra de tareas a veces no responde a la lectura en ese momento: se usa la ubicación.
            return false;
        }
        if (nombre is null) return false;

        var temp = Temperatura.Match(nombre);
        if (!temp.Success) return false;

        _tempText = $"{temp.Groups[1].Value}°";
        _iconName = Lluvia.IsMatch(nombre) ? "cloud-rain" : "cloud";
        return true;
    }

    /// <summary>Nombre accesible del botón de Widgets, p. ej. "Widgets 29°C Parc. soleado"; null si no existe.</summary>
    static string? NombreDelBotonDeWidgets()
    {
        var barra = AutomationElement.RootElement.FindFirst(TreeScope.Children,
            new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
        if (barra is null) return null;

        var botones = barra.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement boton in botones)
        {
            string nombre = boton.Current.Name;
            if (nombre.StartsWith("Widgets", StringComparison.OrdinalIgnoreCase) && nombre.Contains('°'))
                return nombre;
        }
        return null;
    }

    /// <summary>Ubicación de Windows (Wi-Fi o GPS) leída con un proceso de PowerShell. Null si Windows no la da.</summary>
    static async Task<(double Lat, double Lon)?> UbicacionWindowsAsync()
    {
        try
        {
            string script = Path.Combine(Configuracion.DataDir, "ubicacion.ps1");
            File.WriteAllText(script, ScriptUbicacion);
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proceso = Process.Start(psi);
            if (proceso is null) return null;

            var salida = proceso.StandardOutput.ReadToEndAsync();
            if (!proceso.WaitForExit(25000))
            {
                proceso.Kill();
                return null;
            }

            var partes = (await salida).Trim().Split('|');
            if (partes.Length != 2) return null;
            return (double.Parse(partes[0], CultureInfo.InvariantCulture), double.Parse(partes[1], CultureInfo.InvariantCulture));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Temperatura en °C (redondeada) y código del clima con Open-Meteo, sin clave.</summary>
    static async Task<(string Temp, int Code)> FetchWeatherAsync(double lat, double lon)
    {
        string url = "https://api.open-meteo.com/v1/forecast"
            + "?latitude=" + lat.ToString(CultureInfo.InvariantCulture)
            + "&longitude=" + lon.ToString(CultureInfo.InvariantCulture)
            + "&current=temperature_2m,weather_code";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var current = doc.RootElement.GetProperty("current");
        double temperature = current.GetProperty("temperature_2m").GetDouble();
        int code = current.GetProperty("weather_code").GetInt32();
        return ($"{(int)Math.Round(temperature)}°", code);
    }

    /// <summary>Nube normal o con lluvia, según el código de Open-Meteo: llovizna 51-57, lluvia 61-67, chubascos 80-82.</summary>
    static string WeatherIconName(int code)
    {
        bool llueve = code is >= 51 and <= 67 or >= 80 and <= 82;
        return llueve ? "cloud-rain" : "cloud";
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Pupila/1.0");
        return client;
    }
}
