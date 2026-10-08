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

[UninstallDelete]
; Written by the setup program when the person chose a folder for their applications (see [Code]).
Type: files; Name: "{app}\install-defaults.json"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent
; An update started from inside the program passes /RESTARTAPP=1, so the program comes back by itself when the silent install is done.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: RestartRequested

[Code]
// ---- Where the applications are saved ---------------------------------------------------------------------------------------------------
// A wizard page asks for the folder (default: JobApplications on the Desktop). The choice is recorded in {app}\install-defaults.json, which
// the program reads only until the person has a settings file of their own, and it can be changed any time in Settings (with an option to
// move what is already saved). The page is skipped on updates and re-installs (the person has chosen already) and on silent installs, which
// can pass /DATAROOT="D:\Jobs" instead. Nothing is written unless the folder differs from the default, so an all-users install does not
// hand the installing person's Desktop to everyone.
var
  DataPage: TInputDirWizardPage;

function DefaultDataRoot: string;
begin
  Result := ExpandConstant('{userdesktop}\JobApplications');
end;

function SettingsFile: string;
begin
  Result := ExpandConstant('{userappdata}\JobTracker\settings.json');
end;

function ParamDataRoot: string;
begin
  Result := Trim(ExpandConstant('{param:DATAROOT|}'));
end;

function JsonEscape(const S: string): string;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

function LooksLikeFullPath(const Path: string): Boolean;
begin
  Result := False;
  if Length(Path) >= 3 then
  begin
    if (Path[2] = ':') and (Path[3] = '\') then
      Result := True
    else if Copy(Path, 1, 2) = '\\' then
      Result := True;
  end;
end;

procedure InitializeWizard;
begin
  DataPage := CreateInputDirPage(wpSelectDir,
    'Where should your applications be saved?',
    'Choose the folder for your job applications',
    'Job Tracker keeps one folder per company here, with the job description and resume of every application. ' +
    'The default is on your Desktop. You can change this later in Settings, and move what you have already saved.',
    False, '');
  DataPage.Add('');
  DataPage.Values[0] := DefaultDataRoot;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (DataPage <> nil) and (PageID = DataPage.ID) then
    Result := (ParamDataRoot <> '') or FileExists(SettingsFile);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (DataPage <> nil) and (CurPageID = DataPage.ID) then
  begin
    if not LooksLikeFullPath(Trim(DataPage.Values[0])) then
    begin
      MsgBox('Please choose a full folder path, for example C:\Users\You\Documents\JobApplications.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Root: string;
  Lines: TArrayOfString;
begin
  if CurStep = ssPostInstall then
  begin
    Root := ParamDataRoot;
    if (Root = '') and (DataPage <> nil) and (not ShouldSkipPage(DataPage.ID)) then
      Root := Trim(DataPage.Values[0]);

    if (Root <> '') and (CompareText(Root, DefaultDataRoot) <> 0) then
    begin
      SetArrayLength(Lines, 1);
      Lines[0] := '{"DataRoot": "' + JsonEscape(Root) + '"}';
      SaveStringsToUTF8File(ExpandConstant('{app}\install-defaults.json'), Lines, False);
    end;
  end;
end;

// ---- Updates ----------------------------------------------------------------------------------------------------------------------------
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
