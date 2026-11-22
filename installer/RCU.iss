#define AppExe "RedControlUtility.exe"
#ifndef SourceDir
  #define SourceDir "..\RedControlUtility\bin\x64\Release\net8.0-windows10.0.19041.0\publish"
#endif
#if Pos(":", SourceDir) > 0
  #define PublishPath AddBackslash(SourceDir)
#else
  #define PublishPath AddBackslash(AddBackslash(SourcePath) + SourceDir)
#endif
#define PublishedExe PublishPath + AppExe
#if !FileExists(PublishedExe)
  #error Publish the Release x64 application before compiling this installer.
#endif
#define AppName GetStringFileInfo(PublishedExe, "ProductName")
#define AppVersion GetStringFileInfo(PublishedExe, "ProductVersion")
#if AppName == "" || AppVersion == ""
  #error The published executable is missing ProductName or ProductVersion.
#endif
#define CultureFolders "|en|en-us|pt|pt-br|es|es-es|zh|zh-cn|zh-hans|zh-hans-cn|"
#define AsciiLetters "abcdefghijklmnopqrstuvwxyz"

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
VersionInfoVersion={#GetVersionNumbersString(PublishedExe)}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "simplifiedchinese"; MessagesFile: "languages\ChineseSimplified.isl"

[CustomMessages]
english.StartupTask=Start with Windows
brazilianportuguese.StartupTask=Iniciar com o Windows
spanish.StartupTask=Iniciar con Windows
simplifiedchinese.StartupTask=随 Windows 启动

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishPath}*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion
Source: "{#PublishPath}locales\en-US.json"; DestDir: "{app}\locales"; Flags: ignoreversion
Source: "{#PublishPath}locales\pt-BR.json"; DestDir: "{app}\locales"; Flags: ignoreversion
Source: "{#PublishPath}locales\es-ES.json"; DestDir: "{app}\locales"; Flags: ignoreversion
Source: "{#PublishPath}locales\zh-CN.json"; DestDir: "{app}\locales"; Flags: ignoreversion
#define FindHandle
#define FindResult
#sub IncludePublishedFolder
  #define FolderName FindGetFileName(FindHandle)
  #define LowerName LowerCase(FolderName)
  #define Prefix Copy(LowerName, 1, Pos("-", LowerName + "-") - 1)
  #define IsCulture (Len(Prefix) == 2 || Len(Prefix) == 3) && Pos(Copy(Prefix, 1, 1), AsciiLetters) && Pos(Copy(Prefix, 2, 1), AsciiLetters) && (Len(Prefix) == 2 || Pos(Copy(Prefix, 3, 1), AsciiLetters))
  #if FolderName != "." && FolderName != ".." && LowerName != "locales" && DirExists(PublishPath + FolderName)
    #if !IsCulture || Pos("|" + LowerName + "|", CultureFolders)
Source: "{#PublishPath}{#FolderName}\*"; DestDir: "{app}\{#FolderName}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist
    #endif
  #endif
#endsub
#for {FindHandle = FindResult = FindFirst(PublishPath + "*", 16); FindResult; FindResult = FindNext(FindHandle)} IncludePublishedFolder
#if FindHandle
  #call FindClose(FindHandle)
#endif

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"; Check: IsExistingInstall

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

[Code]
function IsExistingInstall: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\{#AppExe}'));
end;

function NeedsVcRedist: Boolean;
var
  Installed: Cardinal;
begin
  Result := not (RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and (Installed = 1));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if not NeedsVcRedist then Exit;
  try
    DownloadTemporaryFile('https://aka.ms/vs/17/release/vc_redist.x64.exe', 'vc_redist.x64.exe', '', nil);
    Exec(ExpandConstant('{tmp}\vc_redist.x64.exe'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  except
  end;
end;
