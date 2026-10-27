# Cómo funciona Pupila

Esta guía es para quien quiere entender o cambiar el código. Para usar la app, ve al [README](../../README.md).

## La idea general

Pupila es una ventana de WPF sin bordes, transparente y siempre encima, centrada arriba de la pantalla principal. Solo recibe el mouse dentro de la forma de la pastilla; lo demás deja pasar el clic a la ventana de abajo.

El proyecto está en la carpeta `Pupila/` del repositorio. Dentro hay tres carpetas de código:

- `Nucleo/`: lo que usan todos. Ritmo de cuadros, estado compartido, constantes, resortes y las llamadas a Windows generales.
- `Servicios/`: lee datos de la PC (música, audio, brillo, clima, avisos...) en segundo plano.
- `Isla/`: la ventana, el dibujo y la mascota.

Los servicios no dibujan nada. Dejan su último valor listo y la isla lo lee en cada cuadro.

## Arranque (`App.xaml.cs`)

`App.xaml` no tiene ventana de inicio: todo arranca en `OnStartup`. La app no termina sola al cerrar ventanas (`ShutdownMode="OnExplicitShutdown"`); solo se cierra con "Salir".

En orden:

1. **Instancia única.** Si ya hay una copia abierta (mutex `Pupila-instancia-unica`), la nueva sale en silencio.
2. **Servicios.** Arrancan `ServicioClima`, `ServicioMultimedia`, `ServicioAudio`, `DetectorContenido`, `ServicioAvisos`, `ServicioVolumen`, `ServicioBrillo`, `ServicioBateria` y `ServicioTeclas`.
3. **Suscripciones.** `IndicadorBloqueo.Start()`.
4. **Bandeja.** Icono con "Modo mínimo" y "Salir". Al salir, `OnExit` lo quita.
5. **Isla oculta.** Se crea `VentanaIsla` con opacidad 0, como barrita (`EstadoIsla.Minimized`), y `Mascota.EsperandoSaludo` en true. Luego se muestra.
6. **Línea de luz.** `Introduccion.Reproducir` corre la animación. Al destello (1,9 s), la pastilla se expande, la isla aparece y sale del modo barrita.
7. **Saludo.** 0,6 s después del destello, `Mascota.EmpezarSaludo()`. A los 3 s, `Mascota.TerminarSaludo()` y entra el reloj.

El arranque con Windows lo agrega el instalador (`instalador/pupila.iss`, dentro de `Pupila/`), solo si se marca esa opción.

## Ritmo de 60 cuadros (`Nucleo/CuadroFijo.cs`)

WPF avisa en cada pasada de render (`CompositionTarget.Rendering`), que sigue el refresco del monitor: puede ser 60, 144 o 240 veces por segundo. `CuadroFijo.Toca(ahora)` decide si esa pasada dibuja un cuadro, así que la animación sale a 60 por segundo sin importar el monitor. Si la PC se atrasa, no intenta recuperar de golpe los cuadros perdidos.

Lo usan `VentanaIsla` (la isla) e `Introduccion` (la línea de luz).

Regla: las animaciones no cuentan cuadros. La isla usa `EstadoIsla.Now` (segundos) y `EstadoIsla.Dt` (tiempo del cuadro, con tope de 0,05 s). La línea de luz usa su propio cronómetro.

## Estado compartido (`Nucleo/EstadoIsla.cs`)

Clase estática. `VentanaIsla` actualiza `Now`, `Dt` y `HideT` en cada cuadro.

- `Progress`: qué tan expandida está (0 = pastilla, 1 = abierta; puede pasar de 1 al rebotar). Cambia con `AnimateTo(valor)` y `Tick()`, en 620 ms.
- `HideT`: qué tan escondida está (0 = isla completa, 1 = barrita). Lo anima `VentanaIsla`.
- `Minimized`: modo mínimo, desde la bandeja o al arrancar.
- `Revealed`: el usuario hizo clic en la barrita para verla. Se borra cada vez que la isla pasa de tapada a libre, o al revés.
- `ArranqueActivo`: true durante el saludo de arranque. Mientras, la isla no responde al mouse ni a los clics, no muestra la barra de volumen o brillo ni el aviso de Mayús o Bloq Num, y la mascota no reacciona a los avisos.

Los servicios protegen sus datos con `lock` o `volatile`.

## La isla (`Isla/`)

En cada cuadro, `VentanaIsla.AlCuadro` hace esto:

1. Toca el ritmo y avanza la expansión (`EstadoIsla.Tick`).
2. Actualiza la mascota, el aviso de bloqueo y el contenido en reposo.
3. Cada 0,5 s revisa si algo tapa la isla (pantalla completa o contenido debajo) y anima el ocultado.
4. Calcula el tamaño de la pastilla con resortes (`Resorte.Step`) y su posición. Si la mascota busca, la pastilla se corre a un lado.
5. Actualiza los ojos y el saludo, y pide dibujar.

El dibujo va así: primero la pastilla negra y, recortado a su forma, el contenido en reposo, la barra de volumen o brillo y el reproductor, que aparece al abrirse la isla.

En reposo, el lienzo se redibuja 15 veces por segundo. Con algo en movimiento, en cada cuadro. Los ojos van en su propia capa (`CapaOjos`), que se redibuja en cada cuadro.

Mouse: al entrar, la isla se expande (salvo que esté escondida como barrita) y el contador de avisos vuelve a cero. Al salir, se cierra 300 ms después si el mouse no está encima. Un clic en la barrita la muestra. Con el panel abierto, el clic va a `PanelReproductor.HandleClick`, y los botones de volumen repiten la tecla mientras se mantienen presionados.

| Archivo | Qué hace |
|---|---|
| `VentanaIsla.cs` y `.xaml` | La ventana y el cuadro. Forma, mouse, ocultado y clics. Reparte los eventos de volumen, brillo, avisos y teclas. |
| `ContenidoReposo.cs` | La fila de reposo: hora, clima, batería o espectro, campanita de avisos y animación de carga. Carga los iconos de `Recursos/iconos/`. |
| `PanelReproductor.cs` | El panel abierto: portada difuminada, título, progreso, controles y volumen. |
| `BarraVolumenBrillo.cs` | La barra compacta de volumen o brillo que sale de la isla un momento. |
| `IndicadorBloqueo.cs` | El aviso "Mayús activado", "Bloq Num desactivado", etc. |
| `Mascota.cs` | Los ojos: se duermen tras 60 s sin uso, parpadeo, mirada, escenas (buscar, pensar, articular) y saludo de arranque. |
| `CapaOjos.cs` | Dibuja los ojos en su propia capa. |
| `SaludoBienvenida.cs` | La palabra "Bienvenido" del arranque. |
| `Introduccion.cs` | La línea de luz del arranque, en una ventana de pantalla completa que no recibe clics. |

## Servicios (`Servicios/`)

Cada servicio arranca en `OnStartup` y guarda su último valor. `ServicioVolumen` y `ServicioBrillo` pasan sus cambios a `BarraVolumenBrillo` (en `Isla/`).

| Archivo | Fuente de datos |
|---|---|
| `ServicioMultimedia.cs` | Controles multimedia de Windows (SMTC), cada 1 s: título, artista, portada, si suena y la posición. Manda reproducir o pausar, anterior, siguiente y cambiar posición. |
| `ServicioAudio.cs` | Lo que suena por el altavoz predeterminado (captura de WASAPI, no el micrófono), en 10 bandas para el espectro. No se guarda ni se envía. |
| `ServicioVolumen.cs` | Volumen y silencio del altavoz predeterminado, cada 120 ms. Revisa si cambió el altavoz cada 3 s. |
| `ServicioBrillo.cs` | Brillo de la pantalla interna: evento `WmiMonitorBrightnessEvent` de WMI, con respaldo cada 10 s. |
| `ServicioTeclas.cs` | Gancho de teclado. Solo mira volumen, Mayús y Bloq Num; ignora todas las teclas inyectadas por programas, como las que manda Pupila. No guarda lo que escribes. |
| `ServicioAvisos.cs` | Proceso de PowerShell (`avisos.ps1`): cuántos avisos nuevos siguen en el Centro de notificaciones, su identificador y el nombre de la app. No lee el contenido. |
| `ServicioClima.cs` | Primero el botón de Widgets de la barra de tareas. Si no está, la ubicación de Windows (con PowerShell, `ubicacion.ps1`) y Open-Meteo. Al arrancar y cada 15 min. Es la única conexión a internet de la app: `api.open-meteo.com`, con latitud y longitud. No usa IP. |
| `ServicioBateria.cs` | Porcentaje y si está conectada al cargador. Se consulta cada 30 s y cuando Windows avisa que cambió la corriente; la isla lee el último valor guardado. |
| `DetectorContenido.cs` | Hilo propio que mira la ventana activa (solo lectura, con UI Automation). Solo ve el tipo y la posición de cada control, no su texto. |

Archivos: los datos van en `%APPDATA%\Pupila` (`Nucleo/Configuracion.cs`). Ahí solo se guardan `avisos.ps1` y `ubicacion.ps1`, que se ejecutan con `-ExecutionPolicy Bypass`. No hay configuración ni registros.

## Llamadas a Windows

No están en un solo archivo. Las generales (user32 y kernel32) están en `Nucleo/Nativo.cs`: tiempo sin uso, pantalla completa y teclas de volumen. Las demás están donde se usan:

- `Isla/VentanaIsla.cs`: estilos de la ventana (no sale en la barra de tareas ni roba el foco) y mensajes del mouse.
- `Isla/IndicadorBloqueo.cs`: `GetKeyState`, para Mayús y Bloq Num.
- `Servicios/ServicioTeclas.cs`: gancho de teclado (`SetWindowsHookEx`).
- `Servicios/ServicioBateria.cs`: `GetSystemPowerStatus`.
- `Servicios/DetectorContenido.cs`: ventana activa y escala de Windows, más UI Automation.
- `Servicios/ServicioClima.cs`: UI Automation para leer el botón de Widgets.
- `Servicios/ServicioBrillo.cs`: WMI.
- `Servicios/ServicioVolumen.cs` y `ServicioAudio.cs`: NAudio, sobre la API de audio de Windows.
- `Servicios/ServicioMultimedia.cs`: controles multimedia de Windows.

El clima, los avisos y la ubicación también usan PowerShell como proceso aparte.

## Dónde agregar algo nuevo

- **Un dato de la PC:** crea `Servicios/ServicioNuevo.cs` con un `Start()` que lea en un hilo de fondo y guarde el valor en propiedades estáticas (con `lock` o `volatile`). Arráncalo en `App.OnStartup` y léelo desde `ContenidoReposo.cs` o `PanelReproductor.cs`, dentro del cuadro.
- **Una llamada a Windows:** si la usan varios archivos, ponla en `Nucleo/Nativo.cs`. Si solo la usa un servicio, déjala en ese servicio.
- **Un elemento de la fila de reposo:** `ContenidoReposo.DrawIdleBar`.
- **Tamaños, tiempos o colores:** `Nucleo/Constantes.cs`, que contiene `Constantes` y `Paleta`. Algunos archivos tienen colores propios, como el rojo de la carga en `ContenidoReposo.cs`. Los tiempos de la mascota están al inicio de `Isla/Mascota.cs`. La rigidez (`k`) y el rebote (`damp`) de un resorte se pasan en cada llamada a `Resorte.Step`.
- **Una opción del menú de la bandeja:** `App.CrearBandeja`.

Las reacciones de la mascota (expresiones según lo que pasa en la PC) se quitaron de la versión 1.0.0-beta. Se verán en una versión posterior.
