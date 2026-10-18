#define AppVersion "0.3.0-preview.1"
#ifndef PublishDir
  #define PublishDir "..\artifacts\ui-preview\0.3.0-preview.1"
#endif

[Setup]
AppId={{A516715C-9FE6-4FE6-BD8B-B07BC474986D}
AppName=GlassDesk UI Preview
AppVersion={#AppVersion}
VersionInfoVersion=0.3.0.1
AppPublisher=GlassDesk
DefaultDirName={localappdata}\Programs\GlassDesk UI Preview
DefaultGroupName=GlassDesk UI Preview
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22621
DisableProgramGroupPage=yes
OutputDir=..\artifacts\release\0.3.0-preview.1
OutputBaseFilename=GlassDesk-0.3.0-preview.1-win-x64-ui-preview-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\GlassDesk.exe
CloseApplications=no
RestartApplications=no
SetupLogging=yes
LicenseFile=..\licenses\installer-third-party-terms.txt

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建预览版桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "preview-data,preview-data\*,qa-*.log,qa-*.txt,AcceptanceReport.md"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\GlassDesk UI Preview"; Filename: "{app}\GlassDesk.exe"; Parameters: "--ui-preview"
Name: "{autodesktop}\GlassDesk UI Preview"; Filename: "{app}\GlassDesk.exe"; Parameters: "--ui-preview"; Tasks: desktopicon

[Run]
Filename: "{app}\GlassDesk.exe"; Parameters: "--ui-preview"; Description: "启动 GlassDesk UI Preview"; Flags: nowait postinstall skipifsilent

[Code]
function PreviewMutex(): String;
var
  DataPath: String;
begin
  DataPath := UpperCase(RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}\preview-data'))));
  Result := 'Local\GlassDesk.UiPreview.' + Copy(LowerCase(GetSHA256OfString(Utf8Encode(DataPath))), 1, 16);
end;

function StopPreview(): Boolean;
var
  ExitCode, Attempt: Integer;
  ProgramPath: String;
begin
  Result := True;
  ProgramPath := ExpandConstant('{app}\GlassDesk.exe');
  if FileExists(ProgramPath) then begin
    if not Exec(ProgramPath, '--ui-preview --quit', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
      Result := False;
      Exit;
    end;
    for Attempt := 1 to 100 do begin
      if not CheckForMutexes(PreviewMutex()) then Exit;
      Sleep(100);
    end;
    Result := not CheckForMutexes(PreviewMutex());
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingVersion: String;
begin
  Result := '';
  if SameText(RemoveBackslash(ExpandConstant('{app}')), ExpandConstant('{localappdata}\Programs\GlassDesk')) then begin
    Result := '请为预览版选择独立目录，不能覆盖正式版。';
    Exit;
  end;
  if GetVersionNumbersString(ExpandConstant('{app}\GlassDesk.exe'), ExistingVersion) then
    if Pos('0.2.', ExistingVersion) = 1 then begin
      Result := '此目录包含正式0.2版，请为预览版选择独立目录。';
      Exit;
    end;
  if not StopPreview() then Result := '预览版尚未退出，请退出此目录的预览程序后重试。';
end;

function InitializeUninstall(): Boolean;
begin
  Result := StopPreview();
  if not Result then MsgBox('请先退出此目录的预览程序，再卸载。', mbError, MB_OK);
end;
{ No uninstall deletion of preview-data, formal settings, startup values or formal IPC. }
