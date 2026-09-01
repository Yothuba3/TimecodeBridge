; TimecodeBridge インストーラー (Inno Setup 6)
; ビルド: iscc /DAppVersion=1.2.0 installer\TimecodeBridge.iss
; 前提: dotnet publish 済みの publish\TimecodeBridge.exe が存在すること

#define AppName "TimecodeBridge"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; AppId は固定（変更するとアップグレードではなく別アプリ扱いになる）
AppId={{3CE414A7-0A00-42EC-927F-B5F08B5A7271}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Yothuba
AppPublisherURL=https://github.com/Yothuba3/TimecodeBridge
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
; 管理者/ユーザー単位を選択可能にする（現場PCで権限がない場合に備える）
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
; self-contained SingleFile なので実行ファイル1つで完結する
Source: "..\publish\{#AppName}.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppName}.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppName}.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
