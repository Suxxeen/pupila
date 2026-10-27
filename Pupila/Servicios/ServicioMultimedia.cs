using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace Pupila;

/// Datos de la canción que suena. Position es la posición estimada en el momento de pedirla.
public record InfoMultimedia(string Title, string Artist, bool Playing, double Position, double End, ImageSource? Art);

/// Lee la música de Windows (SMTC) cada segundo, en segundo plano, y manda play/pausa, anterior, siguiente y saltos.
public static class ServicioMultimedia
{
    private static readonly object _gate = new();
    private static readonly Stopwatch _clock = Stopwatch.StartNew();

    // Estado compartido: el hilo de lectura escribe y la UI lee; todo se toca con _gate.
    private static bool _hasSession;   // hay sesión de música (si no, Current es null)
    private static string _title = "";
    private static string _artist = "";
    private static bool _playing;
    private static double _mediaPos;   // posición aceptada de la canción (segundos)
    private static double _readAt;     // reloj local en que se aceptó esa posición
    private static double _end;        // duración de la canción (segundos)
    private static ImageSource? _art;  // portada de la canción actual
    private static (string Title, string Artist)? _lastKey;  // canción de la última lectura
    private static GlobalSystemMediaTransportControlsSession? _session;

    private static double Now => _clock.Elapsed.TotalSeconds;

    /// Canción actual, o null si no hay sesión de música.
    public static InfoMultimedia? Current
    {
        get
        {
            lock (_gate)
            {
                if (!_hasSession) return null;
                return new InfoMultimedia(_title, _artist, _playing, PositionLocked(), _end, _art);
            }
        }
    }

    /// Posición estimada ahora mismo: avanza con el reloj mientras suena, sin pasar de la duración.
    public static double PositionNow()
    {
        lock (_gate) return PositionLocked();
    }

    /// Arranca el bucle de lectura en un Task de fondo (no bloquea la UI).
    public static void Start()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await LoopAsync();
            }
            catch (Exception)
            {
            }
        });
    }

    /// Salta a una parte de la canción (0 a 1). Actualiza la posición local al instante.
    public static void Seek(double fraction)
    {
        long ticks;
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate)
        {
            session = _session;
            if (!_hasSession || session == null || _end <= 0) return;
            fraction = Math.Clamp(fraction, 0.0, 1.0);
            _mediaPos = fraction * _end;
            _readAt = Now;
            ticks = (long)(fraction * _end * 1e7);  // TimeSpan de Windows: ticks de 100 ns
        }
        if (session is not { } target) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await target.TryChangePlaybackPositionAsync(ticks);
            }
            catch (Exception)
            {
            }
        });
    }

    /// Manda "previous", "playpause" o "next" a la sesión de música actual.
    public static void Command(string name)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate) session = _session;
        if (session is not { } target) return;
        _ = Task.Run(async () =>
        {
            try
            {
                if (name == "playpause") await target.TryTogglePlayPauseAsync();
                else if (name == "next") await target.TrySkipNextAsync();
                else await target.TrySkipPreviousAsync();
            }
            catch (Exception)
            {
            }
        });
    }

    private static DateTimeOffset _ultimaMarca;

    // Llamar con _gate tomado.
    private static double PositionLocked()
    {
        if (!_hasSession) return 0.0;
        double pos = _mediaPos + (_playing ? Now - _readAt : 0.0);
        return _end > 0 ? Math.Min(pos, _end) : pos;
    }

    private static async Task LoopAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        while (true)
        {
            try
            {
                await ReadOnceAsync(manager);
            }
            catch (Exception)
            {
            }
            await Task.Delay(1000);
        }
    }

    private static async Task ReadOnceAsync(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var session = manager.GetCurrentSession();
        if (session == null)
        {
            lock (_gate)
            {
                _hasSession = false;
                _session = null;
            }
            return;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        var timeline = session.GetTimelineProperties();
        bool playing = session.GetPlaybackInfo().PlaybackStatus ==
                       GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;  // 4 = reproduciendo
        string title = props.Title ?? "";
        string artist = props.Artist ?? "";
        var key = (title, artist);

        bool newSong;
        lock (_gate)
        {
            // Windows guarda la posición con la hora en que se tomó: se suma lo que pasó desde entonces.
            double pos = timeline.Position.TotalSeconds;
            if (playing) pos += SecondsSince(timeline.LastUpdatedTime);

            newSong = !_lastKey.HasValue || _lastKey.Value != key;
            _title = title;
            _artist = artist;
            _playing = playing;
            _session = session;
            _hasSession = true;

            // La app de música solo informa de una posición nueva cuando cambia su marca de tiempo.
            // Si la marca es la misma de antes, el dato es viejo: se sigue con el reloj propio y no se reinicia.
            bool reporteNuevo = timeline.LastUpdatedTime != _ultimaMarca;
            _ultimaMarca = timeline.LastUpdatedTime;

            // Solo se acepta si cambió la canción o si hubo un salto de verdad (búsqueda) en un dato nuevo.
            if (newSong || (reporteNuevo && Math.Abs(pos - PositionLocked()) > 2.0))
            {
                _mediaPos = pos;
                _readAt = Now;
            }
            _end = timeline.EndTime.TotalSeconds;
            if (newSong) _lastKey = key;
        }

        if (newSong)  // portada solo cuando cambia la canción
        {
            var art = await ReadArtAsync(props.Thumbnail);
            lock (_gate) _art = art;
        }
    }

    /// Segundos desde la hora que guarda Windows; 0 si el dato no es válido o tiene más de 30 s.
    private static double SecondsSince(DateTimeOffset stamp)
    {
        double elapsed = (DateTimeOffset.UtcNow - stamp).TotalSeconds;
        return elapsed >= 0.0 && elapsed <= 30.0 ? elapsed : 0.0;
    }

    /// Bytes de la portada, ya decodificados como imagen congelada; null si el reproductor no la expone.
    private static async Task<ImageSource?> ReadArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return null;
        try
        {
            using var stream = await thumb.OpenReadAsync();
            using var net = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await net.CopyToAsync(ms);
            ms.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;  // se decodifica ya; el stream puede cerrarse
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
