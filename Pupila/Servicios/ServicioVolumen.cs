using NAudio.CoreAudioApi;

namespace Pupila;

/// <summary>
/// Vigila el volumen del sistema (teclas, botones de la isla o el icono de sonido) y lo manda a la isla
/// cuando cambia. Windows no siempre avisa de estos cambios, así que se revisa unas 8 veces por segundo:
/// es una lectura muy liviana. Si cambia el altavoz predeterminado (por ejemplo, unos audífonos), se engancha al nuevo.
/// </summary>
public static class ServicioVolumen
{
    private const int CuadroMs = 120;
    private const int RevisarDispositivoMs = 3000;

    public static void Start()
    {
        var hilo = new Thread(Bucle) { IsBackground = true, Name = "Pupila volumen" };
        hilo.Start();
    }

    private static void Bucle()
    {
        var enumerador = new MMDeviceEnumerator();
        MMDevice? dispositivo = null;  // se guarda aquí para que no lo libere el recolector de basura
        AudioEndpointVolume? volumen = null;
        string? actual = null;
        float? anterior = null;
        bool? mudoAnterior = null;
        long proximaRevision = 0;
        while (true)
        {
            try
            {
                long ahora = Environment.TickCount64;
                if (ahora >= proximaRevision || volumen == null)
                {
                    proximaRevision = ahora + RevisarDispositivoMs;
                    var nuevo = enumerador.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (nuevo.ID != actual || volumen == null)
                    {
                        dispositivo = nuevo;
                        volumen = dispositivo.AudioEndpointVolume;
                        actual = dispositivo.ID;
                        anterior = null;  // al cambiar de altavoz no se muestra la barra
                        mudoAnterior = null;
                    }
                }

                float nivel = volumen!.MasterVolumeLevelScalar;
                bool mudo = volumen.Mute;
                if (anterior is float previo && (Math.Abs(previo - nivel) > 0.005f || mudoAnterior != mudo))
                    BarraVolumenBrillo.Cambio(TipoBarra.Volumen, mudo ? 0 : nivel * 100.0);
                anterior = nivel;
                mudoAnterior = mudo;
            }
            catch (Exception)
            {
                volumen = null;
                actual = null;
            }
            Thread.Sleep(CuadroMs);
        }
    }
}
