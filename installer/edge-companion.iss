#ifndef AppVersion
  #error AppVersion must be supplied to ISCC
#endif
#ifndef PublishDir
  #error PublishDir must be supplied to ISCC
#endif
#ifndef OutputDir
  #error OutputDir must be supplied to ISCC
#endif

#define AppGuid "8276ACD5-14B5-4874-8F87-FE235E36B156"

[Setup]
AppId={{{#AppGuid}}
AppName=Edge Companion
AppVersion={#AppVersion}
AppPublisher=XENEON EDGE Widgets
AppPublisherURL=https://github.com/robheite/XeneonEdgeWidgets
AppSupportURL=https://github.com/robheite/XeneonEdgeWidgets/issues
DefaultDirName={localappdata}\Programs\XeneonEdgeWidgets\EdgeCompanion
DefaultGroupName=XENEON EDGE Widgets
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=EdgeCompanion-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\EdgeCompanion.Host.exe
CloseApplications=force
CloseApplicationsFilter=EdgeCompanion.Host.exe
RestartApplications=no
ChangesAssociations=yes
DisableProgramGroupPage=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Edge Companion"; Filename: "{app}\EdgeCompanion.Host.exe"; Parameters: "--start"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Classes\edgecompanion"; ValueType: string; ValueName: ""; ValueData: "URL:Edge Companion Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\edgecompanion"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\edgecompanion\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\EdgeCompanion.Host.exe,0"
Root: HKCU; Subkey: "Software\Classes\edgecompanion\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\EdgeCompanion.Host.exe"" --start"
Root: HKCU; Subkey: "Software\XeneonEdgeWidgets\EdgeCompanion"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\EdgeCompanion.Host.exe"; Parameters: "--start"; Description: "Start Edge Companion"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\EdgeCompanion.Host.exe"; Parameters: "--stop"; Flags: runhidden waituntilterminated; RunOnceId: "StopEdgeCompanion"

[Code]
function NumericVersion(const Version: String): String;
var
  SuffixAt: Integer;
begin
  SuffixAt := Pos('-', Version);
  if SuffixAt = 0 then
    SuffixAt := Pos('+', Version);
  if SuffixAt > 0 then
    Result := Copy(Version, 1, SuffixAt - 1)
  else
    Result := Version;
end;

function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
  InstalledPackedVersion: Int64;
  NewPackedVersion: Int64;
begin
  Result := True;
  if RegQueryStringValue(
       HKCU,
       'Software\XeneonEdgeWidgets\EdgeCompanion',
       'Version',
       InstalledVersion) and
     StrToVersion(NumericVersion(InstalledVersion), InstalledPackedVersion) and
     StrToVersion(NumericVersion('{#AppVersion}'), NewPackedVersion) and
     (ComparePackedVersion(NewPackedVersion, InstalledPackedVersion) < 0) then
  begin
    MsgBox(
      'A newer Edge Companion version is already installed. Uninstall it before installing this older version.',
      mbError,
      MB_OK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(
      HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'EdgeCompanion');
end;
