using System.Diagnostics;
using System.IO;
using System.Text;

namespace Pupila;

/// <summary>Un aviso de Windows que llegó: la app que lo mandó.</summary>
public record ToastInfo(string App, string Title, string Body);

/// <summary>
/// Sabe cuándo llega un aviso de Windows y cuántos hay sin ver.
/// Como un .exe normal no puede leer las notificaciones, pide ayuda a un proceso de PowerShell
/// (que sí tiene permiso) y lee sus líneas: NUEVA|id|app, CUENTA|n y ERROR|mensaje.
/// La primera lectura solo recuerda los que ya estaban, para no contar avisos viejos.
/// </summary>
public static class NotificationService
{
    private const string Guion = "avisos.ps1";

    // Avisos nuevos que siguen en el Centro de notificaciones y que no se han visto todavía.
    private static readonly HashSet<string> _pendientes = new();
    private static readonly object _cerrojo = new();
    private static Process? _ayudante;

    /// <summary>Se dispara en un hilo de fondo cuando llega un aviso nuevo.</summary>
    public static event Action<ToastInfo>? Nueva;

    /// <summary>Avisos nuevos pendientes. Si borras un aviso desde Windows, deja de contar.</summary>
    public static int Pendientes
    {
        get { lock (_cerrojo) return _pendientes.Count; }
    }

    /// <summary>Limpia el contador (al pasar el mouse por la isla).</summary>
    public static void Limpiar()
    {
        lock (_cerrojo) _pendientes.Clear();
    }

    /// <summary>Arranca el ayudante de PowerShell en segundo plano. Si falla, se registra y la isla sigue sin avisos.</summary>
    public static void Start()
    {
        _ = Task.Run(LeerAsync);
    }

    private static async Task LeerAsync()
    {
        try
        {
            string ruta = Path.Combine(Config.DataDir, Guion);
            File.WriteAllText(ruta, GuionContenido, new UTF8Encoding(false));

            // Se le pasa el id de Pupila: si Pupila se cierra a la fuerza, el ayudante se apaga solo.
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{ruta}\" {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            _ayudante = Process.Start(psi);
            if (_ayudante == null)
            {
                return;
            }
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { _ayudante?.Kill(true); } catch { /* ya terminó */ }
            };

            string? linea;
            while ((linea = await _ayudante.StandardOutput.ReadLineAsync()) != null)
                Procesar(linea);
        }
        catch (Exception)
        {
        }
    }

    private static void Procesar(string linea)
    {
        string[] partes = linea.Split('|');
        switch (partes[0])
        {
            case "NUEVA" when partes.Length >= 2:
                lock (_cerrojo) _pendientes.Add(partes[1]);
                Nueva?.Invoke(new ToastInfo(partes.Length > 2 ? partes[2] : "", "", ""));
                break;
            case "ACTUALES":
                {
                    // Solo siguen contando los nuevos que todavía están en el Centro de notificaciones.
                    var ids = partes.Length > 1 ? partes[1].Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                    lock (_cerrojo) _pendientes.IntersectWith(ids);
                    break;
                }
            case "ERROR":
                break;
        }
    }

    // Lo que corre dentro de PowerShell: revisa los avisos cada 2 segundos y escribe solo lo nuevo.
    private const string GuionContenido = """
        param([int]$PadrePid = 0)
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        [void][Windows.UI.Notifications.Management.UserNotificationListener, Windows.UI.Notifications.Management, ContentType = WindowsRuntime]
        [void][Windows.UI.Notifications.NotificationKinds, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $escucha = [Windows.UI.Notifications.Management.UserNotificationListener]::Current
        $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        $tipo = $asTask.MakeGenericMethod([System.Collections.Generic.IReadOnlyList[Windows.UI.Notifications.UserNotification]])
        $vistas = @{}
        $primera = $true
        while ($true) {
            # Si Pupila ya no está abierta, este ayudante se termina solo (no queda huérfano).
            if ($PadrePid -gt 0 -and -not (Get-Process -Id $PadrePid -ErrorAction SilentlyContinue)) { break }
            try {
                $op = $escucha.GetNotificationsAsync([Windows.UI.Notifications.NotificationKinds]::Toast)
                $tarea = $tipo.Invoke($null, @($op))
                [void]$tarea.Wait(5000)
                $lista = @($tarea.Result)
                foreach ($n in $lista) {
                    if (-not $vistas.ContainsKey($n.Id)) {
                        $vistas[$n.Id] = $true
                        if (-not $primera) {
                            $app = [string]$n.AppInfo.DisplayInfo.DisplayName -replace '\|', ' '
                            Write-Output ("NUEVA|" + $n.Id + "|" + $app)
                        }
                    }
                }
                $primera = $false
                Write-Output ("ACTUALES|" + (($lista | ForEach-Object { $_.Id }) -join ','))
                Write-Output ("CUENTA|" + $lista.Count)
            } catch {
                Write-Output ("ERROR|" + ([string]$_.Exception.Message -replace '\|', ' '))
            }
            Start-Sleep -Seconds 2
        }
        """;
}
