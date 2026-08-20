#ifndef AppVersion
  #error AppVersion must be provided with /DAppVersion=x.y.z
#endif
#ifndef SourceDir
  #error SourceDir must be provided with /DSourceDir=path
#endif
#ifndef OutputDir
  #error OutputDir must be provided with /DOutputDir=path
#endif
#ifndef Distribution
  #error Distribution must be provided with /DDistribution=Full or Lite
#endif
#ifndef IsLite
  #error IsLite must be provided with /DIsLite=0 or 1
#endif

[Setup]
AppId={{E3D9A690-A19C-4D11-873E-7D8D699F3D91}
AppName=Game Pause
AppVersion={#AppVersion}
AppPublisher=Kratosmax
AppPublisherURL=https://github.com/Kratosmax/gamepause
AppSupportURL=https://github.com/Kratosmax/gamepause/issues
DefaultDirName={autopf}\Game Pause
DefaultGroupName=Game Pause
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=GamePause-{#AppVersion}-{#Distribution}-Setup
SetupIconFile=..\src\GamePause.App\Assets\game-pause.ico
UninstallDisplayIcon={app}\GamePause.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Game Pause"; Filename: "{app}\GamePause.exe"
Name: "{autodesktop}\Game Pause"; Filename: "{app}\GamePause.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GamePause.exe"; Description: "启动 Game Pause"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""GamePause.AutoStart"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveGamePauseAutoStart"

#if IsLite
[Code]
function RegistryViewHasDesktopRuntime8(RootKey: Integer): Boolean;
var
  Versions: TArrayOfString;
  Index: Integer;
begin
  Result := False;
  if RegGetValueNames(
    RootKey,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    Versions) then
  begin
    for Index := 0 to GetArrayLength(Versions) - 1 do
    begin
      if Pos('8.', Versions[Index]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function HasDesktopRuntime8(): Boolean;
begin
  Result := RegistryViewHasDesktopRuntime8(HKLM64) or
    RegistryViewHasDesktopRuntime8(HKLM32);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := HasDesktopRuntime8();
  if Result then
    Exit;

  if MsgBox(
    '精简版需要 Microsoft .NET 8 Desktop Runtime x64。是否打开官方下载页面？',
    mbInformation,
    MB_YESNO) = IDYES then
  begin
    ShellExec(
      'open',
      'https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0/runtime?cid=getdotnetcore&runtime=windowsdesktop',
      '',
      '',
      SW_SHOWNORMAL,
      ewNoWait,
      ErrorCode);
  end;
  Result := False;
end;
#endif
