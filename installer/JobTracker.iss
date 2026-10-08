; Inno Setup script for Job Tracker. Built by tools\publish.ps1 -Installer (or by the release workflow), which passes:
;   /DAppVersion=1.0.0   the version shown in the installer and in Windows' list of apps
;   /DSourceDir=...      the folder produced by dotnet publish (JobTracker.exe)
;   /DOutputDir=...      where the setup program is written

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\JobTracker-1.0.0-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Job Tracker"
#define AppExe "JobTracker.exe"
#define RepoUrl "https://github.com/srinadhmanchikalapudi/job-tracker"

[Setup]
; The AppId identifies the program to Windows: it must never change, so that a new version upgrades the old one instead of installing beside it.
; InstallationInfo.UninstallKey in the code is derived from it.
AppId={{B7D4E2A1-5C93-4F68-8A1D-2E9F6C0B3D74}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=srinadhmanchikalapudi
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}

; Installs for the current user only (no administrator prompt) into %LOCALAPPDATA%\Programs; the wizard offers "all users" for those who want it.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

; A new version replaces the old one, closing the app if it is running.
CloseApplications=yes
RestartApplications=no

LicenseFile=..\LICENSE
SetupIconFile=..\src\JobTracker.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
OutputDir={#OutputDir}
OutputBaseFilename=JobTracker-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent
; An update started from inside the program passes /RESTARTAPP=1, so the program comes back by itself when the silent install is done.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: RestartRequested

[Code]
function RestartRequested: Boolean;
begin
  Result := ExpandConstant('{param:RESTARTAPP|0}') = '1';
end;

// Uninstalling removes the program only. The saved applications (the JobApplications folder) are never touched: they are the user's
// own files. Only the program's small settings file can be removed, and only if the user says so (never asked for a silent uninstall).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: string;
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    SettingsDir := ExpandConstant('{userappdata}\JobTracker');
    if DirExists(SettingsDir) then
      if MsgBox('Also delete Job Tracker''s settings?' + #13#10#13#10 +
                'Your saved applications (the JobApplications folder) are not touched either way. Settings are stored in ' + SettingsDir + '.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(SettingsDir, True, True, True);
  end;
end;
