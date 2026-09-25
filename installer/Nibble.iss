; =====================================================================
;  Nibble — installer
;
;  A per-user install, the way Chrome and Edge install themselves: no UAC prompt, files
;  under %LocalAppData%\Programs\Nibble, Start-menu and optional desktop shortcuts, the
;  browser entries written for you, and a proper uninstaller in Apps & features.
;
;  There is deliberately no "install for all users" choice: PrivilegesRequired=lowest with
;  no overrides means Windows never sees an elevation prompt, and {autopf} resolves to the
;  per-user Programs folder. Nibble keeps everything it owns under the user's own hive.
;
;  Build it with:  ISCC.exe installer\Nibble.iss
;  (or tools\package.ps1, which builds the exe first and then this)
;
;  Signing is left to you: Setup.exe and Nibble.exe both want your certificate before
;  they go on the internet. See the "Signing" section of the README.
; =====================================================================

#define AppName        "Nibble"
#define AppVersion     "1.0.0"
#define AppPublisher   "Dallen Larson"
#define AppUrl         "https://github.com/DallenLarson/nibble"
#define AppExeName     "Nibble.exe"

[Setup]
AppId={{8A1D2B6C-3E4F-4A55-9C1B-7D2E5F0A1B33}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\Nibble\Assets\nibble.ico
LicenseFile=..\LICENSE
InfoBeforeFile=..\docs\install-notes.txt
OutputDir=..\dist
OutputBaseFilename=Nibble-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
DisableWelcomePage=no
AppMutex=Nibble.Browser.Instance
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "browser";     Description: "Make Nibble available as a browser (you still choose the default in Windows Settings)"

[Files]
Source: "..\dist\{#AppExeName}";               DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\README.md";                   DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\CHANGELOG.md";                DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\LICENSE";                     DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\THIRD-PARTY-NOTICES.txt";     DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\Icons-LICENSE.txt";           DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\Silkscreen-OFL.txt";          DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\Monocraft-OFL.txt";           DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\nibble.png";                  DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\nibble-logo.png";             DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";                Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} (private)";      Filename: "{app}\{#AppExeName}"; Parameters: "--private"
Name: "{group}\Uninstall {#AppName}";      Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";          Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Parameters: "--register-browser"; \
  StatusMsg: "Adding Nibble to the browser list..."; Flags: runhidden waituntilterminated; Tasks: browser
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent
; only offered when the runtimes really are missing - see MissingDependencies below
Filename: "https://dotnet.microsoft.com/download/dotnet/8.0"; \
  Description: "Download the Microsoft .NET 8 Desktop Runtime (required to run Nibble)"; \
  Flags: shellexec nowait postinstall skipifsilent; Check: NeedsDotNetRuntime
Filename: "https://go.microsoft.com/fwlink/p/?LinkId=2124703"; \
  Description: "Download the Microsoft Edge WebView2 Runtime (required to run Nibble)"; \
  Flags: shellexec nowait postinstall skipifsilent; Check: NeedsWebView2Runtime

[UninstallRun]
; before the files go, so nothing in the registry points at a deleted folder
Filename: "{app}\{#AppExeName}"; Parameters: "--unregister-browser"; \
  RunOnceId: "UnregisterBrowser"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}\pages"

[Code]
const
  DotNet8DesktopUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0';
  WebView2Url       = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

var
  NeedsDotNet: Boolean;
  NeedsWebView2: Boolean;

// Any folder matching Pattern inside Root? Used because the registry is not a
// reliable witness: Visual Studio, winget and zip installs of .NET 8 write only
// part of the keys the official installer writes, so the folders are the truth.
function AnyFolder(const Root, Pattern: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(Root) then exit;
  if FindFirst(AddBackslash(Root) + Pattern, FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          Result := True;
      until Result or (not FindNext(FindRec));
    finally
      FindClose(FindRec);
    end;
  end;
end;

function SharedFxLists8(RootKey: Integer): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  // the shared framework key lists every installed 8.x desktop runtime
  if RegGetSubkeyNames(RootKey, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Pos('8.', Names[I]) = 1 then Result := True;
end;

function DotNet8DesktopInstalled(): Boolean;
begin
  Result :=
    SharedFxLists8(HKLM32) or
    SharedFxLists8(HKLM) or
    SharedFxLists8(HKCU) or
    AnyFolder(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App'), '8.*') or
    AnyFolder(ExpandConstant('{commonpf32}\dotnet\shared\Microsoft.WindowsDesktop.App'), '8.*');
end;

function WebView2Registered(RootKey: Integer): Boolean;
var
  Version: String;
begin
  // Edge Update registers the Evergreen runtime under this well-known client id
  Result := RegQueryStringValue(RootKey,
    'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
    and (Version <> '');
end;

function WebView2Installed(): Boolean;
begin
  Result :=
    WebView2Registered(HKLM32) or
    WebView2Registered(HKLM) or
    WebView2Registered(HKCU) or
    DirExists(ExpandConstant('{commonpf32}\Microsoft\EdgeWebView\Application')) or
    DirExists(ExpandConstant('{commonpf}\Microsoft\EdgeWebView\Application'));
end;

// the two [Run] entries that offer the download pages ask these
function NeedsDotNetRuntime(): Boolean;
begin
  Result := NeedsDotNet;
end;

function NeedsWebView2Runtime(): Boolean;
begin
  Result := NeedsWebView2;
end;

function InitializeSetup(): Boolean;
var
  Missing: String;
begin
  Result := True;
  NeedsDotNet := not DotNet8DesktopInstalled();
  NeedsWebView2 := not WebView2Installed();

  if not (NeedsDotNet or NeedsWebView2) then exit;

  Missing := '';
  if NeedsDotNet then
    Missing := Missing + '- Microsoft .NET 8 Desktop Runtime' + #13#10 +
      '    Download: ' + DotNet8DesktopUrl + #13#10;
  if NeedsWebView2 then
    Missing := Missing + '- Microsoft Edge WebView2 Runtime' + #13#10 +
      '    Download: ' + WebView2Url + #13#10;

  Log('Nibble setup: missing runtime(s): ' + #13#10 + Missing);

  // An unattended install has nobody to answer a dialog, so it must never stop
  // to ask one; the download pages are still offered on the last wizard page.
  if not WizardSilent then
  begin
    // Nibble says the same thing at first launch, so this is a heads-up rather than a wall.
    if MsgBox('Nibble needs two pieces Windows usually already has, and this PC is missing:' + #13#10#13#10 +
              Missing + #13#10 +
              'You can install Nibble anyway and fetch them afterwards - it will tell you what is missing.' + #13#10#13#10 +
              'Continue with the install?', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\Nibble');
    if not DirExists(DataDir) then exit;

    // A silent uninstall keeps the profile, always: nobody is there to answer, and a
    // person's history is not the installer's to throw away by default.
    if UninstallSilent then exit;

    if MsgBox('Also delete your Nibble profile?' + #13#10#13#10 +
              DataDir + #13#10#13#10 +
              'That removes your history, bookmarks, settings and cookies. Choose No to keep ' +
              'them, so reinstalling brings everything back.', mbConfirmation,
              MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DataDir, True, True, True);
  end;
end;
