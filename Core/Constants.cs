using System.Windows.Media;

namespace Pupila;

/// <summary>
/// Medidas y tiempos de la isla, copiados de nusa.py (líneas 39-50 y 150-151).
/// </summary>
public static class Constants
{
    public const int IdleW = 160;
    public const int IdleH = 32;
    public const int CanvasW = 480;  // lienzo justo: holgura para el rebote, sin área de dibujo de más
    public const int CanvasH = 150;
    public const int OpenW = 330;    // más alto para el control multimedia
    public const int OpenH = 124;
    public const int Flare = 12;     // curva de los hombros que se abren hacia el borde superior
    public const int Pad = Flare;    // holgura alrededor de la pastilla (igual que Flare)
    public const int Radius = 14;    // esquinas de abajo
    public const int AnimMs = 620;   // duración de la expansión, igual que la demo de Nusa
    public const int CloseDelayMs = 300;
    public const int MinW = 40;      // modo mínimo: una barrita centrada que no estorba
    public const int MinH = 4;
    public const int SpectrumBands = 10;         // columnas del espectro de audio
    public const double AudioThreshold = 0.002;  // volumen a partir del cual se considera que algo suena
}

/// <summary>
/// Colores de la isla (nusa.py líneas 70-73). Las brochas van congeladas para dibujar sin costo extra.
/// </summary>
public static class Palette
{
    public static readonly SolidColorBrush White = Congelado(255, 255, 255);
    public static readonly SolidColorBrush Gray = Congelado(150, 152, 175);
    public static readonly SolidColorBrush Green = Congelado(110, 240, 160);
    public static readonly SolidColorBrush Black = Congelado(0, 0, 0);

    private static SolidColorBrush Congelado(byte r, byte g, byte b)
    {
        var brocha = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brocha.Freeze();
        return brocha;
    }
}
