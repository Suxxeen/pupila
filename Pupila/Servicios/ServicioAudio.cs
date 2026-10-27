using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Pupila;

/// Captura lo que suena por el altavoz (loopback de WASAPI) y calcula el espectro en bandas.
public static class ServicioAudio
{
    private const int Size = 2048;  // más resolución: se distinguen mejor los graves
    private const int SilenceMs = 300;  // sin datos este tiempo, el audio se considera mudo

    // Cada publicación es un arreglo nuevo: quien lo lee nunca ve valores a medias.
    private static volatile float[] _spectrum = new float[Constantes.SpectrumBands];
    private static volatile float _level;
    private static long _lastDataMs;  // Environment.TickCount64 del último bloque recibido

    /// Niveles de las bandas (0 a 1).
    public static float[] Spectrum => _spectrum;

    /// Volumen crudo del sistema (0 a 1).
    public static float Level => _level;

    /// True si el volumen supera el umbral: algo suena.
    public static bool Playing => _level > Constantes.AudioThreshold;

    /// Arranca la captura en un hilo de fondo.
    public static void Start()
    {
        var thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Pupila audio" };
        thread.Start();
    }

    private static void CaptureLoop()
    {
        while (true)
        {
            try
            {
                Capture();
            }
            catch (Exception)
            {
                // Sin audio disponible: se reintenta más abajo, sin mostrar nada.
            }
            Publish(0f, new float[Constantes.SpectrumBands]);
            Thread.Sleep(2000);  // reintenta en unos segundos
        }
    }

    /// Captura mientras el altavoz predeterminado no cambie. Vuelve si falla o si cambia el dispositivo.
    private static void Capture()
    {
        using var enumerator = new MMDeviceEnumerator();
        string device = DefaultDeviceId(enumerator);

        using var capture = new WasapiLoopbackCapture();  // altavoz predeterminado
        var analyzer = new Analyzer(capture.WaveFormat);
        bool stopped = false;
        capture.DataAvailable += (_, e) =>
        {
            Volatile.Write(ref _lastDataMs, Environment.TickCount64);
            analyzer.Feed(e.Buffer, e.BytesRecorded);
        };
        capture.RecordingStopped += (_, _) => Volatile.Write(ref stopped, true);
        capture.StartRecording();

        long nextDeviceCheck = Environment.TickCount64 + 1000;
        while (!Volatile.Read(ref stopped))
        {
            Thread.Sleep(100);
            long now = Environment.TickCount64;

            // Sin paquetes de audio (silencio): las bandas vuelven a cero.
            if (now - Volatile.Read(ref _lastDataMs) > SilenceMs && _level != 0f)
                Publish(0f, new float[Constantes.SpectrumBands]);

            // El altavoz predeterminado cambió: se reinicia la captura en el nuevo.
            if (now >= nextDeviceCheck)
            {
                nextDeviceCheck = now + 1000;
                if (DefaultDeviceId(enumerator) != device) break;
            }
        }

        try
        {
            capture.StopRecording();
        }
        catch (Exception)
        {
            // Ya estaba detenida: no pasa nada.
        }
    }

    private static string DefaultDeviceId(MMDeviceEnumerator enumerator)
    {
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        return device.ID;
    }

    private static void Publish(float level, float[] spectrum)
    {
        _spectrum = spectrum;
        _level = level;
    }

    /// Procesa el audio en bloques de 2048 muestras mono (cada muestra mezcla todos los canales).
    private sealed class Analyzer
    {
        // Ventana de Hann simétrica: suaviza los bordes de cada bloque antes de la FFT.
        private static readonly float[] Window = ComputeWindow();

        // Bordes de las bandas en escala logarítmica, de 2 hasta la mitad del bloque (Size / 2), en enteros.
        private static readonly int[] Edges = ComputeEdges();

        private readonly int _channels;
        private readonly int _bytesPerSample;
        private readonly bool _isFloat;
        private readonly float[] _mono = new float[Size];
        private int _filled;             // frames acumulados del bloque actual
        private double _absSum;          // suma de |muestra| del bloque (volumen crudo)
        private int _sampleCount;        // muestras contadas en el bloque
        private double _globalPeak = 1e-4;  // el volumen más fuerte reciente: lo que cuenta como 'fuerte'

        public Analyzer(WaveFormat format)
        {
            _channels = format.Channels;
            _bytesPerSample = format.BitsPerSample / 8;
            // WASAPI en modo compartido entrega float de 32 bits; 16 bits PCM por si acaso.
            _isFloat = format.BitsPerSample == 32;
            if (!_isFloat && format.BitsPerSample != 16)
                throw new NotSupportedException($"formato de audio no soportado: {format.BitsPerSample} bits");
        }

        public void Feed(byte[] buffer, int count)
        {
            int frameBytes = _channels * _bytesPerSample;
            for (int offset = 0; offset + frameBytes <= count; offset += frameBytes)
            {
                double sum = 0.0;
                for (int c = 0; c < _channels; c++)
                {
                    float s = ReadSample(buffer, offset + c * _bytesPerSample);
                    sum += s;
                    _absSum += Math.Abs(s);
                    _sampleCount++;
                }
                _mono[_filled++] = (float)(sum / _channels);  // mezcla a mono
                if (_filled == Size)
                {
                    Block();
                    _filled = 0;
                }
            }
        }

        private float ReadSample(byte[] buffer, int index)
        {
            return _isFloat
                ? BitConverter.ToSingle(buffer, index)
                : BitConverter.ToInt16(buffer, index) / 32768f;
        }

        private void Block()
        {
            float level = _sampleCount > 0 ? (float)(_absSum / _sampleCount) : 0f;
            _absSum = 0.0;
            _sampleCount = 0;

            // Silencio: no hace falta calcular bandas.
            if (level <= Constantes.AudioThreshold)
            {
                Publish(level, new float[Constantes.SpectrumBands]);
                return;
            }

            var fft = new Complex[Size];
            for (int i = 0; i < Size; i++)
            {
                fft[i].X = _mono[i] * Window[i];
                fft[i].Y = 0f;
            }
            FastFourierTransform.FFT(true, 11, fft);  // 2^11 = 2048

            // Magnitudes de la mitad positiva del espectro: 1025 valores (Size / 2 + 1).
            int bins = Size / 2 + 1;
            var magnitude = new double[bins];
            for (int k = 0; k < bins; k++)
            {
                double re = fft[k].X, im = fft[k].Y;
                magnitude[k] = Math.Sqrt(re * re + im * im);
            }

            // Promedio por banda. La última banda llega hasta el final del espectro.
            var bands = new double[Constantes.SpectrumBands];
            for (int b = 0; b < Constantes.SpectrumBands; b++)
            {
                int from = Edges[b];
                int to = b == Constantes.SpectrumBands - 1 ? bins : Edges[b + 1];
                double sum = 0.0;
                for (int k = from; k < to; k++) sum += magnitude[k];
                bands[b] = sum / (Edges[b + 1] - Edges[b]);
            }

            // Referencia lenta de volumen: el pico decae muy despacio.
            _globalPeak = Math.Max(_globalPeak * 0.9995, bands.Max());

            // Potencia visual: cada banda cuenta según qué tan fuerte suena frente al volumen de referencia.
            var power = new float[Constantes.SpectrumBands];
            for (int b = 0; b < Constantes.SpectrumBands; b++)
            {
                double v = Math.Clamp(bands[b] * 1.5 / _globalPeak, 0.0, 1.0);
                power[b] = (float)Math.Sqrt(v);
            }
            Publish(level, power);
        }

        private static float[] ComputeWindow()
        {
            var w = new float[Size];
            for (int i = 0; i < Size; i++)
                w[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (Size - 1)));
            return w;
        }

        private static int[] ComputeEdges()
        {
            int n = Constantes.SpectrumBands + 1;
            double a = Math.Log10(2.0), b = Math.Log10(Size / 2.0);
            var edges = new int[n];
            for (int i = 0; i < n; i++)
                edges[i] = (int)Math.Pow(10.0, i * (b - a) / (n - 1) + a);
            edges[0] = 2;
            edges[n - 1] = Size / 2;
            return edges;
        }
    }
}
