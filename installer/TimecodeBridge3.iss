; TimecodeBridge3 インストーラー (Inno Setup 6)
; ビルド: iscc /DAppVersion=3.0.0 installer\TimecodeBridge3.iss
; 前提: dotnet publish 済みの publish\ ディレクトリ(TimecodeBridge3.exe, web\, libltc.dll, libltc-COPYING.txt)と
;       リポジトリ直下の THIRD_PARTY_NOTICES.md が存在すること

#define AppName "TimecodeBridge3"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; AppId は v3 用に新規発行。v2(TimecodeBridge2)とは別アプリとして並存インストールできる。以後は固定
AppId={{7C42D7EA-AB62-4851-AADC-CCC196115E6E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Yothuba
AppPublisherURL=https://github.com/Yothuba3/TimecodeBridge
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\installer-output
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppName}.exe
DisableProgramGroupPage=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; ディレクトリ発行(web\ と libltc.dll を同梱するため SingleFile にしない)
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
; WebView2 Evergreen ブートストラッパー(未導入の PC にだけ実行)。CI で公式 URL から取得して installer\ に置く
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not IsWebView2Installed

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppName}.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppName}.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Microsoft Edge WebView2 ランタイムをインストールしています…"; Check: not IsWebView2Installed; Flags: waituntilterminated
Filename: "{app}\{#AppName}.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// WebView2 Evergreen ランタイムの有無。公式ドキュメントの検出手順どおり、EdgeUpdate の pv を見る
function IsWebView2Installed: Boolean;
var
  Version: string;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version);
  if Result then Result := (Version <> '') and (Version <> '0.0.0.0');
end;
