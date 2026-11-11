; Stable per-user identity. Never change AppId when releasing an upgrade.
#ifndef AppVersion
  #define AppVersion "1.5.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\windows-x64"
#endif
#ifndef PackageDir
  #define PackageDir "..\artifacts\packages"
#endif

[Setup]
AppId={{8CEEC88B-634A-4BAE-A5C2-104385962A36}
AppName=AuthLocal for Windows
AppVersion={#AppVersion}
AppPublisher=AuthLocal Contributors
AppPublisherURL=https://github.com/tobeQAQ/AuthLocal
AppSupportURL=https://github.com/tobeQAQ/AuthLocal/issues
AppUpdatesURL=https://github.com/tobeQAQ/AuthLocal/releases
DefaultDirName={localappdata}\Programs\AuthLocal
DefaultGroupName=AuthLocal
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UsePreviousAppDir=yes
UsePreviousTasks=yes
DisableProgramGroupPage=yes
UninstallDisplayName=AuthLocal for Windows
UninstallDisplayIcon={app}\AuthLocal.exe
SetupIconFile=..\src\LocalAuth.App\app.ico
LicenseFile=..\LICENSE
OutputDir={#PackageDir}
OutputBaseFilename=AuthLocal-{#AppVersion}-windows-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupArchitecture=x64
AppMutex=Local\AuthLocal.App
SetupMutex=Local\AuthLocal.Setup
CloseApplications=no
RestartApplications=no
UninstallLogMode=append

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AuthLocal"; Filename: "{app}\AuthLocal.exe"; AppUserModelID: "AuthLocal.Windows"
Name: "{autodesktop}\AuthLocal"; Filename: "{app}\AuthLocal.exe"; Tasks: desktopicon; AppUserModelID: "AuthLocal.Windows"

[Registry]
; Only installation metadata goes into the registry. Account data stays in LocalAuth.
Root: HKCU; Subkey: "Software\AuthLocal"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\AuthLocal"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"

[Run]
Filename: "{app}\AuthLocal.exe"; Description: "{cm:LaunchProgram,AuthLocal}"; Flags: nowait postinstall skipifsilent

; Deliberately no [UninstallDelete] of %LOCALAPPDATA%\LocalAuth.
; Upgrades and uninstall retain accounts, preferences and warning acknowledgement.
