# Cambios

## 1.0.3-beta

- La campanita de avisos ahora se ve siempre y muestra el número de avisos que no has visto.
- Al llegar un aviso, la pastilla se ensancha lo justo y muestra el nombre de la app y el título del aviso (en un chat, el contacto); después se oculta y queda la campanita con el número de avisos. Tocar el aviso lo cierra. Nunca muestra el texto del mensaje.
- Pasar el mouse por la isla ya no borra el contador.
- Si la isla está escondida como barrita porque hay contenido debajo, al pasar el mouse muestra solo el reloj (sin ojos ni saludo) y se esconde de nuevo a los 15 s. Durante los primeros 2,5 s no abre el multimedia; después, sí, hasta que se vuelva a esconder.
- El ayudante de PowerShell de avisos lee el identificador, el nombre de la app y el título del aviso; el texto del mensaje no se lee. Si se cierra, Pupila lo vuelve a abrir.

## 1.0.2-beta

- Baja el consumo de memoria: Pupila pasa de unos 156 MB a unos 77 MB de memoria privada en reposo. El ejecutable ya no va comprimido, así que ocupa más espacio en el disco (unos 190 MB).
- El proceso que lee los avisos de Windows (un ayudante de PowerShell) usa unos 71 MB; esta versión no lo cambió.

## 1.0.1-beta

- Corrige que la isla no se veía sobre otras aplicaciones: Windows la bajaba debajo de las ventanas. Ahora se vuelve a poner encima cada medio segundo, sin activarla.

## 1.0.0-beta

Primera versión para probar. Solo Windows 11 de 64 bits.

- Isla con reloj, clima, batería y mascota de ojos.
- Saludo de arranque con línea de luz y "Bienvenido".
- Reproductor con portada difuminada, barra de progreso, controles y botones de volumen.
- Espectro de audio mientras suena música.
- Barra de volumen y brillo, con la misma isla.
- Aviso de Mayús y Bloq Num.
- Campanita de avisos de Windows.
- Animación al conectar o desconectar el cargador.
- Modo mínimo desde la bandeja.
- Animaciones fijas a 60 cuadros por segundo.
- Instalador de Inno Setup, sin permisos de administrador.
- Sin registros de diagnóstico en disco.
- Se quitaron las referencias a otros proyectos, el código sin uso y el panel de pruebas, y las carpetas y clases pasaron a español.
