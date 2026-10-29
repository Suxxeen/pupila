using System.Windows;
using System.Windows.Media;

namespace Pupila;

/// <summary>
/// Los ojos de la mascota en su propia capa. Como se mueven en cada cuadro, solo esta capa se vuelve a dibujar;
/// el fondo de la isla (la forma, el reloj, el clima y la batería) no se rehace mientras los ojos se mueven.
/// Se redibujan en cada cuadro, a 60 por segundo, para que el movimiento se vea fluido.
/// </summary>
public sealed class CapaOjos : FrameworkElement
{
    double _sw;
    double _sh;
    double _offsetX;
    bool _eraVisible;

    /// <summary>Actualiza la posición de la pastilla. Lo llama la isla en cada cuadro.</summary>
    public void Actualizar(double sw, double sh, double offsetX)
    {
        _sw = sw;
        _sh = sh;
        _offsetX = offsetX + Constantes.Pad;  // el cuerpo de la pastilla empieza después del margen

        bool visible = !(Mascota.EsperandoSaludo || ContenidoReposo.CargaActiva || Mascota.Saludando) && Mascota.EyesMix > 0.01
            && EstadoIsla.HideT < 0.99;  // escondida como barrita, los ojos no se dibujan

        // Se redibujan en cada cuadro. Al apagarse, se redibuja una vez para borrarlos.
        if (visible || _eraVisible)
            InvalidateVisual();
        _eraVisible = visible;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // No se dibujan mientras espera el saludo, durante el saludo ni con la animación de carga.
        if (Mascota.EsperandoSaludo || ContenidoReposo.CargaActiva || Mascota.Saludando) return;
        double mix = Mascota.EyesMix;
        if (mix <= 0.01) return;

        // Los ojos solo se ven con la pastilla ya cerrada: al expandirse, se desvanecen.
        double rest = Math.Clamp(1 - EstadoIsla.Progress * 20, 0, 1) * (1 - AlertaAviso.Mix);  // el aviso de la pastilla tapa los ojos
        // Oculta como barrita (HideT), los ojos se desvanecen con la pastilla; no quedan flotando sobre el fondo.
        double pastilla = Math.Clamp(1 - EstadoIsla.HideT, 0, 1);
        dc.PushTransform(new TranslateTransform(_offsetX, 0));
        dc.PushOpacity(rest * mix * pastilla);
        Mascota.DrawEyes(dc, _sw, _sh);
        dc.Pop();
        dc.Pop();
    }
}
