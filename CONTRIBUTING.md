# Cómo colaborar en Pupila

¡Gracias por querer ayudar! Pupila es un proyecto pequeño y hecho en casa, así que cualquier ayuda cuenta: probarla en otras PC, reportar un problema o mandar un cambio.

Pupila es solo para **Windows 11 de 64 bits**. Los cambios para Windows 10 todavía no se aceptan.

## Compilar

Necesitas el [.NET 10 SDK](https://dotnet.microsoft.com/download).

1. Entra a la carpeta `Pupila` (dentro del repositorio) y compila:

   ```bash
   dotnet build -c Release
   ```

2. El ejecutable queda en `Pupila\bin\Release\net10.0-windows10.0.19041.0\Pupila.exe`. Ejecútalo desde ahí.

Solo puede correr una copia de Pupila a la vez. Si ya hay una abierta, la nueva se cierra sola.

## Publicar

Para generar un solo ejecutable autocontenido, en la carpeta `Pupila\publish\`:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Para el instalador, instala [Inno Setup 6](https://jrsoftware.org/isinfo.php), publica primero con el comando de arriba y luego compila `Pupila\instalador\pupila.iss`. El instalador queda en `Pupila\release\`.

## Reglas del proyecto

- **Animaciones a 60 cuadros por segundo.** Las animaciones salen del reloj de `Nucleo\CuadroFijo.cs`, para que se vean igual en monitores de 60, 144 o 240 Hz. No agregues animaciones que se salten ese reloj.
- **Sin registros en disco.** Pupila no escribe archivos de registro ni de diagnóstico. Lo único que escribe en disco son los dos scripts de PowerShell que crea en `%APPDATA%\Pupila`. Si necesitas saber qué pasa, usa el depurador o `Debug.WriteLine` mientras pruebas, y quítalo antes de mandar el cambio.
- **Privacidad primero.** No se guardan textos escritos, contenido de avisos ni ubicaciones. La única conexión a internet es a Open-Meteo, para el clima, y solo manda latitud y longitud. Lo que se lee y lo que se envía está en la sección «Privacidad» de [README.md](README.md). Si un cambio toca eso, dilo en la descripción del cambio.
- **Todo en español.** Las carpetas, los archivos, las clases, los comentarios y los textos van en español.
- **Sin dependencias nuevas sin razón fuerte.** Cada paquete hace más pesado el ejecutable.
- **Mide la versión instalada.** El consumo de CPU y memoria de la versión de desarrollo no es el que tendrá quien instale Pupila. Compila el instalador (ver Publicar) y mide esa versión.

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
