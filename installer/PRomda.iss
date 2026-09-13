#define AppName "PRomda FileShare"
#define AppVersion "1.2.0"
#define AppExe "PRomda.FileShare.exe"
#define GitHubDownloadUrl "https://github.com/Yuka2241/PRomda-FileShare/releases/latest/download/PRomda.FileShare.exe"

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
UninstallDisplayIcon={app}\{#AppExe}
DisableProgramGroupPage=yes

[Dirs]
Name: "{app}"
Name: "{userdocs}\FPI"

[Icons]
Name: "{autodesktop}\PRomda FileShare"; Filename: "{app}\{#AppExe}"
Name: "{group}\PRomda FileShare"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить PRomda FileShare"; Flags: nowait postinstall skipifsilent

[Code]
const
  AppDownloadUrl = '{#GitHubDownloadUrl}';

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    WizardForm.StatusLabel.Caption := 'Загрузка PRomda FileShare из GitHub...';
    WizardForm.ProgressGauge.Style := npbstMarquee;
    try
      DownloadTemporaryFile(AppDownloadUrl, '{#AppExe}', '', nil);
      if not FileExists(ExpandConstant('{tmp}\{#AppExe}')) then
      begin
        MsgBox('GitHub не вернул файл приложения.' + #13#10 + #13#10 +
          'Проверь GitHub Release.', mbError, MB_OK);
        Result := False;
      end;
    except
      MsgBox('Не удалось скачать PRomda FileShare из GitHub.' + #13#10 + #13#10 +
        GetExceptionMessage, mbError, MB_OK);
      Result := False;
    finally
      WizardForm.ProgressGauge.Style := npbstNormal;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    ForceDirectories(ExpandConstant('{app}'));
    if not FileCopy(ExpandConstant('{tmp}\{#AppExe}'), ExpandConstant('{app}\{#AppExe}'), False) then
      RaiseException('Не удалось скопировать PRomda FileShare в папку установки.');
  end;
end;
