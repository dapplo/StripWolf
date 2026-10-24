; StripWolf installer for Windows (Inno Setup 6)
;
; Installs per user by default (no administrator rights needed, into %LOCALAPPDATA%\Programs\StripWolf), the user can
; choose an installation for all users in the dialog. The registry entries use HKA, which is HKCU or HKLM accordingly.
;
; Build (see .github/workflows/release.yml):
;   iscc /DAppVersion=1.2.3 /DPublishDir=<publish folder> /DOutputName=<setup file name> installer\windows\StripWolf.iss
;
; File types: StripWolf is the default application of the comic book archives (cbz, cbr, cb7, cbt), for PDF it is only
; added to "Open with", it doesn't take over the PDF reader. EPUB is not registered: the published (Native AOT) build
; has no EPUB support. Windows keeps the choice of the user (UserChoice) for every type, it can be changed in
; Settings > Apps > Default apps > StripWolf (see the Capabilities below).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\publish\windows"
#endif
#ifndef OutputName
  #define OutputName "StripWolf-Setup"
#endif

#define AppName "StripWolf"
#define AppExe "StripWolf.exe"
#define AppPublisher "Dapplo"
#define AppUrl "https://github.com/dapplo/StripWolf"
; Registered in Default apps, also used as the uninstall registry key
#define CapabilitiesKey "Software\Dapplo\StripWolf\Capabilities"

[Setup]
; Never change the AppId: it identifies the installation for updates and the uninstaller
AppId={{6F1B3A52-4D2E-4C8B-9B7A-2E5C1D9F7A31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Lets Explorer pick up the new file types right after the installation (and the uninstallation)
ChangesAssociations=yes
; The running StripWolf must be closed before its files can be replaced (see SingleInstance.cs)
AppMutex=Local\StripWolf_Mutex
CloseApplications=yes
SetupIconFile=..\..\src\StripWolf\StripWolf.Desktop\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\..\LICENSE
OutputDir=..\..\
OutputBaseFilename={#OutputName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; ProgIDs, one per type so Explorer shows a readable type name. The command passes the file to StripWolf, a running
; StripWolf gets it through its named pipe (SingleInstance / ActivationManager).
Root: HKA; Subkey: "Software\Classes\StripWolf.cbz"; ValueType: string; ValueName: ""; ValueData: "Comic Book Archive (CBZ)"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\StripWolf.cbz\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\StripWolf.cbz\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\StripWolf.cbr"; ValueType: string; ValueName: ""; ValueData: "Comic Book Archive (CBR)"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\StripWolf.cbr\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\StripWolf.cbr\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\StripWolf.cb7"; ValueType: string; ValueName: ""; ValueData: "Comic Book Archive (CB7)"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\StripWolf.cb7\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\StripWolf.cb7\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\StripWolf.cbt"; ValueType: string; ValueName: ""; ValueData: "Comic Book Archive (CBT)"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\StripWolf.cbt\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\StripWolf.cbt\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\StripWolf.pdf"; ValueType: string; ValueName: ""; ValueData: "PDF Document (StripWolf)"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\StripWolf.pdf\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\StripWolf.pdf\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

; The comic book archives: StripWolf is their default application, unless the user chose another one
Root: HKA; Subkey: "Software\Classes\.cbz"; ValueType: string; ValueName: ""; ValueData: "StripWolf.cbz"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.cbz"; ValueType: string; ValueName: "PerceivedType"; ValueData: "compressed"; Flags: createvalueifdoesntexist
Root: HKA; Subkey: "Software\Classes\.cbz\OpenWithProgids"; ValueType: string; ValueName: "StripWolf.cbz"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.cbr"; ValueType: string; ValueName: ""; ValueData: "StripWolf.cbr"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.cbr"; ValueType: string; ValueName: "PerceivedType"; ValueData: "compressed"; Flags: createvalueifdoesntexist
Root: HKA; Subkey: "Software\Classes\.cbr\OpenWithProgids"; ValueType: string; ValueName: "StripWolf.cbr"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.cb7"; ValueType: string; ValueName: ""; ValueData: "StripWolf.cb7"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.cb7"; ValueType: string; ValueName: "PerceivedType"; ValueData: "compressed"; Flags: createvalueifdoesntexist
Root: HKA; Subkey: "Software\Classes\.cb7\OpenWithProgids"; ValueType: string; ValueName: "StripWolf.cb7"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.cbt"; ValueType: string; ValueName: ""; ValueData: "StripWolf.cbt"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.cbt"; ValueType: string; ValueName: "PerceivedType"; ValueData: "compressed"; Flags: createvalueifdoesntexist
Root: HKA; Subkey: "Software\Classes\.cbt\OpenWithProgids"; ValueType: string; ValueName: "StripWolf.cbt"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
; PDF: only "Open with"
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "StripWolf.pdf"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty

; "Open with > Choose another app": shows StripWolf with its name, only for the supported types
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".cbz"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".cbr"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".cb7"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".cbt"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""

; Settings > Apps > Default apps: lets the user make StripWolf the default application for these types
Root: HKA; Subkey: "Software\Dapplo\StripWolf"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "{#CapabilitiesKey}"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "{#CapabilitiesKey}"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Comic book reader"
Root: HKA; Subkey: "{#CapabilitiesKey}"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "{#CapabilitiesKey}\FileAssociations"; ValueType: string; ValueName: ".cbz"; ValueData: "StripWolf.cbz"
Root: HKA; Subkey: "{#CapabilitiesKey}\FileAssociations"; ValueType: string; ValueName: ".cbr"; ValueData: "StripWolf.cbr"
Root: HKA; Subkey: "{#CapabilitiesKey}\FileAssociations"; ValueType: string; ValueName: ".cb7"; ValueData: "StripWolf.cb7"
Root: HKA; Subkey: "{#CapabilitiesKey}\FileAssociations"; ValueType: string; ValueName: ".cbt"; ValueData: "StripWolf.cbt"
Root: HKA; Subkey: "{#CapabilitiesKey}\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "StripWolf.pdf"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "{#CapabilitiesKey}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// Versions before the installer registered the file types themselves, on every start, under HKCU with the ProgIDs
// StripWolf_cbz etc. and also took over .pdf and .epub. Those registrations point to wherever the zip was extracted,
// they are removed here (only when they are still ours) so the entries of the installation are used.
procedure RemoveSelfRegistration(Extension: String);
var
  ProgId: String;
  Current: String;
begin
  ProgId := 'StripWolf_' + Extension;
  if RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Classes\.' + Extension, '', Current) and (Current = ProgId) then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Classes\.' + Extension, '');
  end;
  RegDeleteValue(HKEY_CURRENT_USER, 'Software\Classes\.' + Extension + '\OpenWithProgids', ProgId);
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\' + ProgId);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    RemoveSelfRegistration('cbz');
    RemoveSelfRegistration('cbr');
    RemoveSelfRegistration('cb7');
    RemoveSelfRegistration('cbt');
    RemoveSelfRegistration('pdf');
    RemoveSelfRegistration('epub');
  end;
end;
