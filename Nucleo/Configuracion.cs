using System.IO;

namespace Pupila;

/// <summary>
/// Carpeta de datos de Pupila. Todo vive en %APPDATA%\Pupila.
/// </summary>
public static class Configuracion
{
    public static readonly string DataDir = CrearCarpetaDatos();

    private static string CrearCarpetaDatos()
    {
        // El programa puede vivir en una carpeta protegida: los datos van en %APPDATA%.
        string baseDir = Environment.GetEnvironmentVariable("APPDATA") ?? AppContext.BaseDirectory;
        string dir = Path.Combine(baseDir, "Pupila");
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return dir;
    }
}
