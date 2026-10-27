# Pupila

Una isla dinámica para Windows 11. Pasa el mouse por arriba de la pantalla y aparecen la hora, el clima, la música que estás escuchando, tus avisos y una mascota de ojos que duerme, saluda y busca.

> **Estado:** 1.0.0-beta, en desarrollo. Solo Windows 11 de 64 bits. Ver [Limitaciones](#limitaciones).

## De dónde viene

Pupila está inspirada en [Nusa](https://rama2555.github.io/nusa/), de rama2555, en la idea de una isla dinámica para Windows. Es un proyecto aparte:

- **Pensada para Latinoamérica:** la interfaz y los textos están en español, y el clima se toma de la región y la ubicación que Windows ya tiene.
- **Funciones propias:** indicador de Mayús y Bloq Num, saludo de arranque, animación de carga con corazones, barra de volumen y brillo, y lectura del clima desde el widget de Windows. La lista completa está abajo.
- **Código y diseño propios:** no usa código, imágenes, sonidos ni nombres de Nusa. La mascota y la identidad visual se diseñaron desde cero.

Este proyecto se hizo con ayuda de IA (Claude, de Anthropic) bajo la dirección de su autor, que no es programador. Las decisiones de diseño y de producto son del autor; el código lo escribió y lo revisó el asistente, y se probó en la PC del autor.

## Funciones

- **Reposo:** hora centrada, clima, batería y una mascota de ojos que parpadea, se duerme y saluda.
- **Saludo de arranque:** una línea de luz recorre el borde superior, la pastilla se expande y aparece "Bienvenido" antes de mostrar el reloj.
- **Reproductor:** muestra la canción que está sonando en cualquier app compatible con los controles multimedia de Windows, con portada difuminada, barra de progreso, controles y botones de volumen.
- **Espectro:** mientras suena música, la isla muestra barras que siguen el audio.
- **Volumen y brillo:** al cambiarlos aparece una barra compacta dentro de la isla.
- **Mayús y Bloq Num:** al activarlas o desactivarlas, la isla muestra un aviso breve.
- **Notificaciones:** una campanita con punto rojo avisa de los avisos de Windows que todavía no has visto.
- **Carga:** al conectar o desconectar el cargador, la isla lo muestra con una animación.
- **Modo mínimo:** desde el icono de la bandeja, la isla se queda como una barrita discreta.
- **Ligera:** no dibuja nada que no cambie, y se queda quieta cuando no pasa nada.

## Instalar

1. Descarga `pupila-setup-1.0.0-beta.exe`.
2. Ejecútalo. No pide permisos de administrador: se instala en tu carpeta de usuario.
3. Si quieres que arranque con Windows, marca la opción en el instalador.

### La advertencia de Windows

Pupila todavía no está firmada digitalmente. Una firma de código cuesta dinero, y todavía no hay una opción gratuita que funcione para este proyecto. Por eso Windows puede mostrar **"Windows protegió tu PC"**. Para abrirla:

1. Haz clic en **Más información**.
2. Haz clic en **Ejecutar de todas formas**.

Si no quieres confiar en el ejecutable, puedes compilarlo tú mismo con las instrucciones de abajo.

## Privacidad

- **Teclado:** Pupila escucha las teclas de volumen, Mayús y Bloq Num para mostrar avisos en la isla. Para eso instala un gancho de teclado de bajo nivel, que en teoría podría ver todo lo que escribes. **Pupila no guarda, no registra ni envía lo que escribes**; el código solo mira esas tres teclas y descarta el resto.
- **Notificaciones:** para contar los avisos, Pupila abre un pequeño proceso de PowerShell (con `-ExecutionPolicy Bypass`, para poder ejecutar su propio script). Ese proceso solo lee el número de avisos y el nombre de la app; no lee el contenido. Se cierra solo cuando Pupila se cierra.
- **Clima:** primero lee la temperatura del botón de Widgets de la barra de tareas de Windows. Si ese botón no está, usa la ubicación de Windows (Wi-Fi o GPS), también con un proceso de PowerShell, y consulta [Open-Meteo](https://open-meteo.com/) con esa ubicación. La ubicación no se guarda.
- **Datos:** la configuración vive en `%APPDATA%\Pupila`. Pupila no guarda registros de diagnóstico y no envía nada a ningún servidor propio.

## Compilar

Necesitas el [.NET 10 SDK](https://dotnet.microsoft.com/download).

Para compilar y probar:

```bash
dotnet build -c Release
```

Para generar un solo ejecutable autocontenido:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Para crear el instalador, instala [Inno Setup 6](https://jrsoftware.org/isinfo.php), genera primero el ejecutable con el comando anterior y luego compila `instalador/pupila.iss`. El instalador queda en `release/`.

Más detalles para quien quiera colaborar: [CONTRIBUTING.md](CONTRIBUTING.md) y [docs/COMO-FUNCIONA.md](docs/COMO-FUNCIONA.md).

## Limitaciones

- La app no está firmada digitalmente, así que Windows muestra la advertencia de arriba.
- Solo se prueba en Windows 11. No hay soporte para Windows 10 todavía.
- El clima depende del botón de Widgets de la barra de tareas o, si no está, de la ubicación de Windows. Si cambias el idioma de Windows o cambia el texto de ese botón, la lectura puede dejar de funcionar.
- Los avisos, la música y la lectura de la barra de tareas dependen de cómo Windows y cada app expongan esa información; no todas las apps aparecen.
- Las teclas de brillo de algunas laptops no llegan a las aplicaciones, así que la barra de brillo solo aparece cuando el valor cambia.

## Licencia

[MIT](LICENSE). Puedes usar, modificar y distribuir el código; solo conserva el aviso de copyright.

## Créditos

- Iconos del clima: [Phosphor Icons](https://phosphoricons.com/), licencia MIT (ver `Assets/icons/PHOSPHOR-LICENSE.txt`).
- Audio: [NAudio](https://github.com/naudio/NAudio), licencia MIT.
- Inspiración: [Nusa](https://rama2555.github.io/nusa/), de rama2555.
