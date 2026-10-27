namespace Pupila;

/// <summary>
/// Estado compartido de la isla: reloj, expansión y ocultado.
/// VentanaIsla actualiza Now, Dt y HideT en cada cuadro, y llama a Tick().
/// </summary>
public static class EstadoIsla
{
    private const double BounceOvershoot = 2.6;  // cuánto se pasa del destino al expandir: da el rebote

    /// <summary>Segundos monotónicos, puestos por la ventana en cada cuadro.</summary>
    public static double Now { get; set; }

    /// <summary>Delta de tiempo del cuadro, en segundos.</summary>
    public static double Dt { get; set; }

    /// <summary>0 = pastilla, 1 = abierta. Puede pasar de 1 al rebotar.</summary>
    public static double Progress { get; private set; }

    /// <summary>0 = isla completa, 1 = oculta como barrita. Lo anima la ventana.</summary>
    public static double HideT { get; set; }

    /// <summary>True cuando el usuario hizo clic en la barrita para verla.</summary>
    public static bool Revealed { get; set; }

    /// <summary>Modo mínimo: la isla se oculta y queda una barrita.</summary>
    public static bool Minimized { get; set; }

    /// <summary>
    /// True mientras corre la animación de arranque (línea de luz, saludo, transición al reloj).
    /// Durante ese tiempo la isla no responde a nada: ni se despliega, ni hay clics, ni avisos ni otras animaciones.
    /// </summary>
    public static bool ArranqueActivo => Mascota.EsperandoSaludo || Mascota.Saludando || Mascota.SaludoSaliendo;

    private static double _desde;
    private static double _hasta;
    private static double _inicio;
    private static bool _rebota;
    private static bool _corriendo;

    /// <summary>
    /// Anima Progress hasta end. Rebota al expandir; al volver, curva suave sin rebote.
    /// Si ya había una animación, la reemplaza desde el valor actual.
    /// </summary>
    public static void AnimateTo(double end)
    {
        _rebota = end > Progress;
        _desde = Progress;
        _hasta = end;
        _inicio = Now;
        _corriendo = true;
    }

    /// <summary>Avanza la animación con el reloj de la ventana. Llamar en cada cuadro.</summary>
    public static void Tick()
    {
        if (!_corriendo) return;
        double t = (Now - _inicio) / (Constantes.AnimMs / 1000.0);
        if (t >= 1.0)
        {
            Progress = _hasta;
            _corriendo = false;
            return;
        }
        double e = _rebota ? OutBack(t, BounceOvershoot) : OutCubic(t);
        Progress = _desde + (_hasta - _desde) * e;
    }

    /// <summary>Curva OutBack: sobrepasa el destino y regresa; overshoot indica cuánto se pasa.</summary>
    public static double OutBack(double t, double overshoot)
    {
        double u = t - 1;
        return u * u * ((overshoot + 1) * u + overshoot) + 1;
    }

    /// <summary>Curva OutCubic: arranca rápido y frena suave al llegar al destino.</summary>
    public static double OutCubic(double t)
    {
        double u = t - 1;
        return u * u * u + 1;
    }
}
