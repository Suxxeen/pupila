# Pupila

Una isla dinámica para Windows: pasa el mouse por arriba de la pantalla y aparece tu música, el clima, la hora, las notificaciones y una mascota que duerme y saluda.

> **Estado:** versión 1.0.0-beta, en desarrollo. Solo para Windows 11. Ver [Limitaciones](#limitaciones).

## Qué hace

- **Reposo:** hora centrada, clima, batería y una mascota de ojos que parpadea y se duerme.
- **Reproductor:** muestra la canción que está sonando (de cualquier reproductor compatible con los controles multimedia de Windows), con portada difuminada, barra de progreso y controles. Incluye botones de volumen.
- **Volumen y brillo:** al cambiarlos aparece una barra compacta dentro de la isla.
- **Notificaciones:** una campanita con punto rojo avisa de los avisos de Windows que todavía no has visto.
- **Ligera:** no dibuja nada que no cambie, y se queda quieta cuando no pasa nada.

## Inspiración

Pupila está inspirada en [Nusa](https://rama2555.github.io/nusa/), de rama2555, en la idea de una isla dinámica para Windows. **No usa código, imágenes, sonidos ni nombres de Nusa.** Todo el código y el diseño son propios, y la mascota y la identidad visual se diseñaron desde cero.

## Cómo se hizo

Este proyecto se desarrolló con ayuda de IA (Claude, de Anthropic) bajo la dirección de su autor, que no es programador. Las decisiones de diseño y de producto son del autor; el código lo escribió y lo revisó el asistente, y se probó en la PC del autor.
## Requisitos

- Windows 11, de 64 bits. No se da soporte a Windows 10 todavía.
- Para compilar: [.NET 10 SDK](https://dotnet.microsoft.com/download).

## Compilar

```bash
dotnet build
```

Para generar un solo ejecutable autocontenido:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

El resultado queda en `bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/`.

## Privacidad

- **Teclado:** Pupila escucha las teclas de volumen, Mayús y Bloq Num para mostrar avisos en la isla. Para eso instala un gancho de teclado de bajo nivel, que en teoría podría ver todo lo que escribes. **Pupila no guarda, no registra ni envía lo que escribes**; el código solo mira esas tres teclas y descarta el resto.
- **Notificaciones:** para contar los avisos, Pupila abre un pequeño proceso de PowerShell (con `-ExecutionPolicy Bypass`, para poder ejecutar su propio script). Ese proceso solo lee el número de avisos y el nombre de la app; no lee el contenido. Se cierra solo cuando Pupila se cierra.
- **Clima:** primero lee la temperatura del botón de Widgets de la barra de tareas de Windows. Si ese botón no está, usa la ubicación de Windows (Wi-Fi o GPS), también con un proceso de PowerShell, y consulta [Open-Meteo](https://open-meteo.com/) con esa ubicación. La ubicación no se guarda.
- **Datos:** la configuración vive en `%APPDATA%\Pupila`. No se envía nada a ningún servidor propio.

## Limitaciones

- La app no está firmada digitalmente, así que Windows puede mostrar un aviso al ejecutarla.
- Solo se prueba en Windows 11. Si encuentras un problema, abre un issue y describe qué estabas haciendo cuando pasó.
- El clima depende del botón de Widgets de la barra de tareas o, si no está, de la ubicación de Windows. Si cambias el idioma de Windows o cambia el texto de ese botón, la lectura puede dejar de funcionar.
- Los avisos, la música y la lectura de la barra de tareas dependen de cómo Windows y cada app expongan esa información; no todas las apps aparecen.
- Las teclas de brillo de algunas laptops no llegan a las aplicaciones, así que la barra de brillo solo aparece cuando el valor cambia.

## Licencia

[MIT](LICENSE). Puedes usar, modificar y distribuir el código; solo conserva el aviso de copyright.

## Créditos

- Iconos del clima: [Phosphor Icons](https://phosphoricons.com/), licencia MIT (ver `Assets/icons/PHOSPHOR-LICENSE.txt`).
- Audio: [NAudio](https://github.com/naudio/NAudio), licencia MIT.
