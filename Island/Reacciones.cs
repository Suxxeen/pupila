namespace Pupila;

/// <summary>
/// Reacciones de la mascota: cuando algo cambia en la PC, pone una expresión unos segundos.
/// Revisa una vez por segundo y solo dispara en el momento del cambio, no mientras la situación siga igual.
/// </summary>
public static class Reacciones
{
    const double REVISAR_CADA_S = 1.0;
    const double CLIMA_SIN_DATOS_S = 90;  // si el clima no llega en 90 s, la mascota pone una X
    const int BATERIA_BAJA = 15;          // por debajo de este porcentaje, triste (sin cargador)
    const int NOTIFICACIONES_MUCHAS = 5;  // con más avisos pendientes, enojado

    static double _siguiente;
    static double _inicio = -1;
    static bool? _sonabaMusica;
    static bool _bateriaBajaAvisada;
    static bool _pendientesAltos;
    static bool _climaAvisado;
    static volatile bool _intentoTope;

    /// <summary>Se suscribe a los avisos de volumen y brillo. Llamar una vez al arrancar.</summary>
    public static void Start()
    {
        // Subir o bajar cuando ya estaba al tope: la mascota se enoja. Se anota aquí y se revisa en Revisar().
        Osd.Cambiado += llegoAlTope =>
        {
            if (!llegoAlTope && Osd.EnLimite) _intentoTope = true;
        };
    }

    /// <summary>Revisa las situaciones que disparan una expresión. Lo llama la isla en cada cuadro.</summary>
    public static void Revisar(double now)
    {
        if (now < _siguiente) return;
        _siguiente = now + REVISAR_CADA_S;
        if (_inicio < 0) _inicio = now;

        if (Mascot.Sleeping) return;  // dormida no reacciona

        // Música pausada: triste. Solo cuenta si había una sesión de música y se pausó.
        bool haySesion = MediaService.Current != null;
        bool suena = haySesion && MediaService.Current!.Playing;
        if (_sonabaMusica == true && haySesion && !suena) Mascot.Expresion("triste");
        if (haySesion) _sonabaMusica = suena;

        // Batería baja sin cargador: triste. (Conectar el cargador lo anima IdleContent, con los ojos.)
        var bateria = BatteryService.Read();
        if (bateria is { } b)
        {
            if (!b.Charging && b.Percent <= BATERIA_BAJA && !_bateriaBajaAvisada)
            {
                _bateriaBajaAvisada = true;
                Mascot.Expresion("triste");
            }
            if (b.Charging || b.Percent > BATERIA_BAJA + 5) _bateriaBajaAvisada = false;
        }

        // Muchas notificaciones sin revisar: enojado.
        bool muchas = NotificationService.Pendientes >= NOTIFICACIONES_MUCHAS;
        if (muchas && !_pendientesAltos) Mascot.Expresion("enojado");
        _pendientesAltos = muchas;

        // Intentar subir o bajar el volumen o el brillo estando al tope: enojado.
        if (_intentoTope)
        {
            _intentoTope = false;
            Mascot.Expresion("enojado");
        }

        // El clima no llega en mucho tiempo (sin internet, por ejemplo): X, una vez por sesión.
        if (!_climaAvisado && WeatherService.TempText == "" && now - _inicio > CLIMA_SIN_DATOS_S)
        {
            _climaAvisado = true;
            Mascot.Expresion("x");
        }
    }
}
