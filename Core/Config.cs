using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pupila;

/// <summary>
/// Rutas y configuración del usuario. Todo vive en %APPDATA%\Pupila.
/// </summary>
public static class Config
{
    public static readonly string DataDir = CrearCarpetaDatos();
    public static readonly string ConfigPath = Path.Combine(DataDir, "config.json");

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

    /// <summary>Latitud y longitud para el clima, desde config.json (sección clima). Null si no existen.</summary>
    public static (double Lat, double Lon)? Coords()
    {
        try
        {
            JsonObject? clima = JsonNode.Parse(File.ReadAllText(ConfigPath))?["clima"] as JsonObject;
            if (clima == null) return null;
            double lat = clima["lat"]!.GetValue<double>();
            double lon = clima["lon"]!.GetValue<double>();
            return (lat, lon);
        }
        catch (Exception)
        {
            // Igual que load_coords: archivo ausente, mal formado o sin la sección significa "no hay zona guardada".
            return null;
        }
    }

    /// <summary>Guarda la zona en config.json sin borrar el resto de secciones.</summary>
    public static void SaveCoords(double lat, double lon)
    {
        JsonObject raiz;
        try
        {
            raiz = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject ?? new JsonObject();
        }
        catch (Exception)
        {
            raiz = new JsonObject();
        }

        raiz["clima"] = new JsonObject
        {
            ["lat"] = JsonValue.Create(lat),
            ["lon"] = JsonValue.Create(lon),
        };

        var opciones = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,  // ensure_ascii=False: acentos sin escapar
        };
        try
        {
            File.WriteAllText(ConfigPath, raiz.ToJsonString(opciones), new UTF8Encoding(false));
        }
        catch (IOException)
        {
        }
    }
}
