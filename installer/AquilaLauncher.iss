#define MyAppName "AQUILA Launcher"
#define MyAppExe "AquilaLauncher.exe"
#define MyAppVersion "1.0"

[Setup]
AppId={{7B2D9C6E-4F31-4A8E-9D55-A1C0E3B7F2A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=terms.rtf
OutputDir=Output
OutputBaseFilename=AquilaLauncher-Setup
SetupIconFile=aquila.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Messages]
LicenseAccepted=I &accept
LicenseNotAccepted=I &don't accept

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
type
  TScrollInfo = record
    cbSize: Cardinal;
    fMask: Cardinal;
    nMin: Integer;
    nMax: Integer;
    nPage: Cardinal;
    nPos: Integer;
    nTrackPos: Integer;
  end;

function GetScrollInfo(hWnd: HWND; nBar: Integer; var lpsi: TScrollInfo): Integer;
  external 'GetScrollInfo@user32.dll stdcall';
function SetTimer(hWnd: LongWord; nIDEvent: LongWord; uElapse: LongWord; lpTimerFunc: LongWord): LongWord;
  external 'SetTimer@user32.dll stdcall';
function KillTimer(hWnd: LongWord; uIDEvent: LongWord): Integer;
  external 'KillTimer@user32.dll stdcall';

var
  ScrollTimer: LongWord;
  ReachedBottom: Boolean;

function IsScrolledToBottom: Boolean;
var
  Info: TScrollInfo;
begin
  Result := True;
  Info.cbSize := SizeOf(Info);
  Info.fMask := $7;                                   { SIF_RANGE or SIF_PAGE or SIF_POS }
  if GetScrollInfo(WizardForm.LicenseMemo.Handle, 1, Info) <> 0 then   { 1 = SB_VERT }
    Result := (Info.nPos + Integer(Info.nPage)) >= Info.nMax;
end;

procedure ScrollTimerProc(H: LongWord; Msg: LongWord; IdEvent: LongWord; Time: LongWord);
begin
  if (not ReachedBottom) and IsScrolledToBottom then
  begin
    ReachedBottom := True;
    WizardForm.LicenseAcceptedRadio.Enabled := True;   { "I accept" becomes clickable }
  end;
end;

procedure InitializeWizard;
begin
  ReachedBottom := False;
  WizardForm.LicenseAcceptedRadio.Enabled := False;    { locked until the terms are scrolled to the end }
  WizardForm.LicenseNotAcceptedRadio.Checked := True;
  ScrollTimer := SetTimer(0, 0, 150, CreateCallback(@ScrollTimerProc));
end;

procedure DeinitializeSetup;
begin
  if ScrollTimer <> 0 then KillTimer(0, ScrollTimer);
end;