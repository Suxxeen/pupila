# Cómo funciona Pupila

Esta guía es para quien quiere entender o cambiar el código. Para usar la app, ve al [README](../README.md).

## La idea general

Pupila es una ventana de WPF, sin bordes, transparente y siempre encima, pegada arriba de la pantalla. Cada cuadro de la animación:

1. Lee el estado de la PC (música, volumen, brillo, avisos, batería...) desde los servicios.
2. Actualiza el estado compartido de la isla (qué tan expandida está, si está oculta, etc.).
3. Dibuja la pastilla negra y lo que tiene adentro.

Casi todo el trabajo pesado se hace en hilos de fondo. La ventana solo dibuja y responde al mouse.

## Arranque (`App.xaml.cs`)

En orden:

1. Revisa que no haya otra copia abierta (un *mutex* con nombre).
2. Arranca los servicios de datos (clima, música, audio, contenido, avisos, volumen, brillo, teclas).
3. Crea el icono de la bandeja.
4. Crea la isla, la muestra oculta y reproduce la animación de arranque (`Island/Intro.cs`).
5. Cuando termina el destello, la isla aparece, la mascota saluda y luego entra el reloj.

## Ritmo de 60 cuadros (`Core/CuadroFijo.cs`)

Windows avisa a la app en cada refresco del monitor, que puede ser de 60, 144 o 240 veces por segundo. `CuadroFijo.Toca(ahora)` decide si ese refresco se usa para dibujar un cuadro nuevo, de forma que el resultado sea siempre de 60 por segundo.

Por eso toda animación lee el tiempo de `IslandState.Now` y no cuenta refrescos.

## Estado compartido (`Core/IslandState.cs`)

Valores que todos los módulos de la isla leen y escriben:

- `Progress`: qué tan expandida está la isla (0 = pastilla, 1 = panel abierto).
- `HideT`: qué tan escondida está como barrita (por tapar una ventana o por el modo mínimo).
- `Revealed`: si el usuario la mostró a la fuerza.
- `ArranqueActivo`: si la animación de arranque todavía corre. Mientras es `true`, la isla ignora el mouse, los clics y los avisos.

## La isla (`Island/`)

| Archivo | Qué hace |
|---|---|
| `IslandWindow.cs` | La ventana. Dibuja el cuerpo, el resorte de expansión, el ocultado, el área que responde al mouse y los clics. |
| `IdleContent.cs` | Lo que se ve en reposo: hora, clima, batería, espectro, campanita y la animación de carga. |
| `Player.cs` | El panel del reproductor: portada, barra de progreso, controles y volumen. |
| `Osd.cs` | La barra compacta de volumen y brillo que sale de la isla. |
| `IndicadorBloqueo.cs` | El aviso de Mayús y Bloq Num. |
| `Mascot.cs` | Estado de la mascota: ojos, parpadeo, sueño, saludo, búsqueda y gestos. |
| `CapaOjos.cs` | Dibuja los ojos en su propia capa, para no rehacer el fondo cada cuadro. |
| `OjosEnfoque.cs` | El "Bienvenido" del saludo de arranque, con su propio desenfoque. |
| `Reacciones.cs` | Pone una expresión cuando algo cambia en la PC (volumen, brillo, etc.). |
| `Intro.cs` | La línea de luz de la animación de arranque. |

## Servicios (`Services/`)

Cada servicio corre en su hilo y deja su último valor listo para la isla:

| Archivo | Fuente de datos |
|---|---|
| `MediaService.cs` | Controles multimedia de Windows (SMTC): canción, portada y posición. |
| `AudioService.cs` | Captura de lo que suena en el altavoz (WASAPI) para el espectro. |
| `VolumeService.cs` | Volumen y silencio del altavoz predeterminado. |
| `BrightnessService.cs` | Brillo de la pantalla, por evento de Windows (con revisión de respaldo). |
| `TeclasService.cs` | Gancho de teclado: solo mira las teclas de volumen, Mayús y Bloq Num. |
| `NotificationService.cs` | Cuenta los avisos con un proceso de PowerShell en segundo plano. |
| `WeatherService.cs` | Clima: lee el botón de Widgets; si no está, usa la ubicación de Windows y Open-Meteo. |
| `BatteryService.cs` | Nivel de batería y si está conectada al cargador. |
| `ContentDetector.cs` | Detecta si hay una pestaña o una página debajo de la isla (UI Automation), para esconderla. |

## Llamadas a Windows (`Core/Native.cs`)

Las funciones de `user32.dll` y `kernel32.dll` que necesita Pupila: tiempo sin actividad, ventana activa, pantalla completa, y teclas de volumen. Están en un solo archivo para que se vean juntas.

## Dónde agregar algo

- **Un dato nuevo de la PC:** crea un servicio en `Services/` que deje su valor en una propiedad estática, y léelo desde `IdleContent.cs` o `Player.cs`.
- **Una expresión nueva de la mascota:** agrégala en `Mascot.cs` y úsala desde `Reacciones.cs`.
- **Un ajuste de movimiento:** busca el resorte (`Core/Spring.cs`) o el valor en `Core/Constants.cs`.
