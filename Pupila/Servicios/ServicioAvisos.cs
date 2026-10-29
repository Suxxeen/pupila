using System.Diagnostics;
using System.IO;
using System.Text;

namespace Pupila;

/// <summary>Un aviso de Windows que llegó: la app que lo mandó y su título (en un chat, el nombre del contacto). Nunca el mensaje.</summary>
public record ToastInfo(string Id, string App, string Titulo);

/// <summary>
/// Sabe cuándo llega un aviso de Windows y cuántos no se han mostrado. Como un .exe normal no puede leer
/// las notificaciones, pide ayuda a un proceso de PowerShell (que sí tiene permiso) y lee sus líneas:
/// NUEVA|id|app|titulo, ACTUALES|ids, CUENTA|n y ERROR|mensaje.
/// Del aviso solo se usa su identificador, el nombre de la app y su título (en Base64, para que un "|" no rompa la línea).
/// El texto del mensaje no se lee.
/// La primera lectura solo recuerda los que ya estaban, para no mostrar avisos viejos.
/// </summary>
public static class ServicioAvisos
{
    private const string Guion = "avisos.ps1";

    // Ids de los avisos que llegaron y todavía no se han mostrado: son el contador de la campanita.
    private static readonly HashSet<string> _sinVer = new();
    private static readonly object _cerrojo = new();
    private static Process? _ayudante;

    /// <summary>Se dispara en un hilo de fondo cuando llega un aviso nuevo.</summary>
    public static event Action<ToastInfo>? Nueva;

    /// <summary>Avisos que llegaron y todavía no se han mostrado. Es el número de la campanita.</summary>
    public static int Pendientes
    {
        get { lock (_cerrojo) return _sinVer.Count; }
    }

    /// <summary>El aviso ya se mostró en la pastilla: el contador vuelve a cero.</summary>
    public static void MarcarVistos()
    {
        lock (_cerrojo) _sinVer.Clear();
    }

    /// <summary>Arranca el ayudante de PowerShell en segundo plano. Si se cierra, Pupila lo vuelve a abrir.</summary>
    public static void Start()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { _ayudante?.Kill(true); } catch { /* ya terminó */ }
        };
        _ = Task.Run(LeerAsync);
    }

    private static async Task LeerAsync()
    {
        string ruta;
        try
        {
            ruta = Path.Combine(Configuracion.DataDir, Guion);
            // Con BOM: PowerShell 5.1 lee los .ps1 sin BOM como ANSI y los acentos de los comentarios se dañan.
            File.WriteAllText(ruta, GuionContenido, new UTF8Encoding(true));
        }
        catch (Exception)
        {
            return;  // sin el guion no hay avisos; la isla sigue igual
        }

        int espera = 2;
        while (true)
        {
            var inicio = DateTime.UtcNow;
            await CorrerAyudanteAsync(ruta);
            // Si el ayudante se cerró, se vuelve a abrir. Si se cerró enseguida, cada reintento espera más, hasta un minuto.
            espera = DateTime.UtcNow - inicio > TimeSpan.FromMinutes(1) ? 2 : Math.Min(espera * 2, 60);
            await Task.Delay(TimeSpan.FromSeconds(espera));
        }
    }

    private static async Task CorrerAyudanteAsync(string ruta)
    {
        try
        {
            // Se le pasa el id de Pupila: si Pupila se cierra a la fuerza, el ayudante se apaga solo.
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{ruta}\" {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            _ayudante = Process.Start(psi);
            if (_ayudante == null) return;

            string? linea;
            while ((linea = await _ayudante.StandardOutput.ReadLineAsync()) != null)
                Procesar(linea);
        }
        catch (Exception)
        {
            // Si el ayudante falla, la isla sigue sin avisos hasta el siguiente intento.
        }
    }

    private static void Procesar(string linea)
    {
        string[] partes = linea.Split('|');
        switch (partes[0])
        {
            case "NUEVA" when partes.Length >= 4:
                {
                    var aviso = new ToastInfo(partes[1], partes[2], Decodificar(partes[3]));
                    lock (_cerrojo)
                    {
                        if (!_sinVer.Add(aviso.Id)) return;
                    }
                    Nueva?.Invoke(aviso);
                    break;
                }
            case "ACTUALES":
                {
                    // Solo siguen contando los que todavía están en el Centro de notificaciones.
                    var ids = partes.Length > 1 ? partes[1].Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                    lock (_cerrojo) _sinVer.IntersectWith(ids);
                    break;
                }
            case "ERROR":
                break;
        }
    }

    private static string Decodificar(string base64)
    {
        try
        {
            string texto = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return texto.Length <= 100 ? texto : texto[..100] + "…";
        }
        catch (FormatException)
        {
            return "";
        }
    }

    // Lo que corre dentro de PowerShell: revisa los avisos cada 2 segundos y escribe solo lo nuevo.
    // De cada aviso toma el identificador, el nombre de la app y el primer texto (el título). El cuerpo no se toca.
    private const string GuionContenido = """
        param([int]$PadrePid = 0)
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        [void][Windows.UI.Notifications.Management.UserNotificationListener, Windows.UI.Notifications.Management, ContentType = WindowsRuntime]
        [void][Windows.UI.Notifications.NotificationKinds, Windows.UI.Notifications, ContentType = WindowsRuntime]
        [void][Windows.UI.Notifications.KnownNotificationBindings, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $escucha = [Windows.UI.Notifications.Management.UserNotificationListener]::Current
        $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        $tipo = $asTask.MakeGenericMethod([System.Collections.Generic.IReadOnlyList[Windows.UI.Notifications.UserNotification]])
        # Solo el primer texto del aviso (el título). El resto, que es el mensaje, no se lee.
        function Titulo($aviso) {
            $vinculo = $aviso.Notification.Visual.GetBinding([Windows.UI.Notifications.KnownNotificationBindings]::ToastGeneric)
            if ($null -eq $vinculo) { return '' }
            $primero = @($vinculo.GetTextElements() | Select-Object -First 1)
            if ($primero.Count -eq 0) { return '' }
            return [string]$primero[0].Text
        }
        function Base64([string]$texto) { [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($texto)) }
        $vistas = @{}
        $primera = $true
        while ($true) {
            # Si Pupila ya no está abierta, este ayudante se termina solo (no queda huérfano).
            if ($PadrePid -gt 0 -and -not (Get-Process -Id $PadrePid -ErrorAction SilentlyContinue)) { break }
            try {
                $op = $escucha.GetNotificationsAsync([Windows.UI.Notifications.NotificationKinds]::Toast)
                $tarea = $tipo.Invoke($null, @($op))
                # Si Windows tarda más de 5 segundos, se avisa como error en vez de quedarse colgado.
                if (-not $tarea.Wait(5000)) { throw 'La consulta tardó demasiado' }
                $lista = @($tarea.Result)
                $presentes = @{}
                foreach ($n in $lista) {
                    $id = [string]$n.Id
                    $presentes[$id] = $true
                    if ($vistas.ContainsKey($id)) { continue }
                    $vistas[$id] = $true
                    if ($primera) { continue }
                    try {
                        $app = ([string]$n.AppInfo.DisplayInfo.DisplayName) -replace '[\|\r\n]', ' '
                        Write-Output ('NUEVA|' + $id + '|' + $app + '|' + (Base64 (Titulo $n)))
                    } catch {
                        # Un aviso que no se puede leer no detiene a los demás.
                    }
                }
                # Se olvidan los ids que ya no están en el Centro, para que $vistas no crezca sin límite.
                foreach ($id in @($vistas.Keys)) { if (-not $presentes.ContainsKey($id)) { $vistas.Remove($id) } }
                $primera = $false
                Write-Output ('ACTUALES|' + (@($presentes.Keys) -join ','))
                Write-Output ('CUENTA|' + $lista.Count)
            } catch {
                Write-Output ('ERROR|' + ([string]$_.Exception.Message -replace '[\|\r\n]', ' '))
            }
            Start-Sleep -Seconds 2
        }
        """;
}
