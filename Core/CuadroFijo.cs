namespace Pupila;

/// <summary>
/// Reparte los cuadros de animación a 60 por segundo fijos, sin importar la frecuencia del monitor
/// (60, 144, 240 Hz...). El monitor avisa en cada refresco; este decide cuáles refrescos se usan.
/// </summary>
public sealed class CuadroFijo
{
    public const double CuadrosPorSegundo = 60;
    const double Paso = 1.0 / CuadrosPorSegundo;

    double _proximo;

    /// <summary>True si toca dibujar un cuadro en este instante. Llamarlo en cada refresco del monitor.</summary>
    public bool Toca(double ahora)
    {
        if (ahora < _proximo) return false;

        _proximo += Paso;
        // Si se atrasó demasiado (la PC se trabó), no intenta recuperar los cuadros perdidos de golpe.
        if (_proximo < ahora - Paso) _proximo = ahora + Paso;
        return true;
    }
}
