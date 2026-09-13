#define AppName "PRomda FileShare"
#define AppVersion "1.2.4"
#define AppExe "PRomda.FileShare.exe"

[Setup]
AppId={{B4F6A20C-5A8D-4E8D-9C1E-7A0012345678}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=PRomda
DefaultDirName={localappdata}\PRomda\FileShare
DefaultGroupName=PRomda FileShare
OutputDir=output
OutputBaseFilename=PRomda-FileShare-Installer
WizardStyle=modern
WizardSizePercent=110
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
SetupIconFile=..\src\Assets\PRomda-FileShare-Icon.ico
UninstallDisplayIcon={app}\{#AppExe}
DisableProgramGroupPage=yes

[Dirs]
Name: "{app}"
Name: "{userdocs}\FPI"

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные значки:"; Flags: unchecked

[Icons]
Name: "{autodesktop}\PRomda FileShare"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; IconFilename: "{app}\{#AppExe}"
Name: "{group}\PRomda FileShare"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить PRomda FileShare"; Flags: nowait postinstall skipifsilent
