#ifndef AppName
  #define AppName "Red Control Utility"
#endif
#ifndef AppExe
  #define AppExe "RedControlUtility.exe"
#endif
#ifndef AppVersion
  #define AppVersion "0.2.2"
#endif
#ifndef SourceDir
  #if DirExists(SourcePath + "..\publish")
    #define SourceDir "..\publish"
  #else
    ; Visual Studio publish folder
    #define SourceDir "..\RedControlUtility\bin\x64\Release\net8.0-windows10.0.19041.0\publish"
  #endif
#endif

[Setup]
AppId={{7D3C2B61-5E0A-4F7B-9B1D-2A6C4E8F1A93}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Khotyz
AppPublisherURL=https://github.com/Khotyz/RCU
AppSupportURL=https://github.com/Khotyz/RCU/issues
AppUpdatesURL=https://github.com/Khotyz/RCU/releases
DefaultDirName=C:\RCU
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=RCU-Setup-{#AppVersion}
SetupIconFile=..\RedControlUtility\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Iniciar com o Windows / Start with Windows"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RedControlUtility"; \
    ValueData: """{app}\{#AppExe}"" --minimized"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillRCU"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
