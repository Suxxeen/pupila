namespace Pupila;

/// <summary>
/// Movimiento con resorte: se mueve natural y no a saltos, sin importar los Hz.
/// </summary>
public static class Resorte
{
    /// <summary>Avanza un paso el resorte hacia target. pos y vel se actualizan por referencia.</summary>
    public static void Step(ref double pos, ref double vel, double target, double dt, double k = 45.0, double damp = 0.8)
    {
        double c = 2 * Math.Sqrt(k) * damp;  // amortiguación: 1.0 llega sin rebote; 0.8 con un leve rebote
        vel += (k * (target - pos) - c * vel) * dt;
        pos += vel * dt;
    }
}
