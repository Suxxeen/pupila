# Cómo colaborar en Pupila

¡Gracias por querer ayudar! Pupila es un proyecto pequeño y hecho en casa, así que cualquier ayuda cuenta: probarla en otras PC, reportar un problema o mandar un cambio.

## Antes de empezar

- Pupila es solo para **Windows 11 de 64 bits**. Los cambios para Windows 10 todavía no se aceptan.
- Los textos de la interfaz van **en español**.
- El código y los comentarios van **en español**, con frases cortas. Si algo no se entiende, se explica en el comentario.

## Compilar y probar

1. Instala el [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Compila:

   ```bash
   dotnet build -c Release
   ```

3. Ejecuta `bin/Release/net10.0-windows10.0.19041.0/win-x64/Pupila.exe`.

Solo hay una copia de Pupila corriendo a la vez. Si ya hay una abierta, la nueva se cierra sola.

## Reglas del proyecto

- **Animaciones a 60 cuadros por segundo.** Todo lo que se mueve pasa por `Core/CuadroFijo.cs`, para que se vea igual en monitores de 60, 144 o 240 Hz. No agregues animaciones que se salten ese reloj.
- **Sin registros en disco.** Pupila no escribe archivos de registro ni de diagnóstico. Si necesitas saber qué pasa, usa el depurador o `Debug.WriteLine` mientras pruebas, y quítalo antes de mandar el cambio.
- **Privacidad primero.** No se guardan ni se envían textos escritos, contenido de avisos ni ubicaciones. Si un cambio toca eso, dilo en la descripción del cambio.
- **Sin dependencias nuevas sin razón fuerte.** Cada paquete hace más pesado el ejecutable.
- **Mide la versión instalada.** El consumo de CPU y memoria de la versión de desarrollo no es el que tendrá quien instale Pupila. Compila el instalador antes de medir.

## Cómo mandar un cambio

1. Haz un *fork* o una rama.
2. Haz el cambio y compila en `Release`. Debe compilar sin errores.
3. Describe qué cambia y por qué, en español, en la descripción del *pull request*.
4. Si cambia algo que se ve, incluye una captura de pantalla o un video corto.

## Reportar un problema

Abre un *issue* y cuenta:

- Qué estabas haciendo cuando pasó.
- Qué esperabas que pasara.
- Versión de Windows (Configuración → Sistema → Acerca de).
- Si es posible, cómo repetirlo.

## Licencia

Al contribuir aceptas que tu aporte se publique bajo la [licencia MIT](LICENSE) del proyecto.
