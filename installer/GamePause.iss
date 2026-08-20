#ifndef AppVersion
  #error AppVersion must be provided with /DAppVersion=x.y.z
#endif
#ifndef SourceDir
  #error SourceDir must be provided with /DSourceDir=path
#endif
#ifndef OutputDir
  #error OutputDir must be provided with /DOutputDir=path
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
OutputBaseFilename=GamePause-{#AppVersion}-Setup
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
