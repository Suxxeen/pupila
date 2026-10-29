; Pupila — instalador para Windows (Inno Setup 6)
; Compilar con ISCC.exe sobre este archivo. Genera release\pupila-setup-<versión>.exe

#define MyAppName "Pupila"
#define MyAppVersion "1.0.3-beta"
#define MyAppVersionNumero "1.0.3.0"
#define MyAppPublisher "Suxxeen"
#define MyAppExeName "Pupila.exe"

[Setup]
; El AppId identifica el programa para actualizaciones y desinstalación. No cambiarlo nunca.
AppId={{5C1E8A62-3B7D-4F29-9E0A-D4B6C2F1A837}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersionNumero}
VersionInfoProductName={#MyAppName}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Se instala a nombre del usuario, sin pedir administrador. Puede elegir "para todos" si quiere.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Si el programa está abierto, el instalador lo cierra antes de reemplazarlo.
CloseApplications=yes
OutputDir=..\release
OutputBaseFilename=pupila-setup-{#MyAppVersion}
SetupIconFile=pupila.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "autostart"; Description: "Iniciar {#MyAppName} al encender Windows"; GroupDescription: "Opciones:"; Flags: unchecked

[Files]
; El ejecutable (autocontenido) y sus iconos, que van en Recursos\iconos junto al .exe.
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\Recursos\*"; DestDir: "{app}\Recursos"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Inicio automático: solo si lo marcó. Al desinstalar, Windows quita esta entrada.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[UninstallDelete]
; Al desinstalar, borra también la carpeta de datos (los dos scripts de PowerShell que crea Pupila).
Type: filesandordirs; Name: "{userappdata}\{#MyAppName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent

