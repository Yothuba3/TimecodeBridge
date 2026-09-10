@echo off
rem libltc を MSVC で libltc.dll としてビルドする(CI windows-latest 想定。未検証)
rem usage: build-windows.cmd <出力ディレクトリ>   ※ Developer Command Prompt(vcvars64)上で実行
setlocal
set VER=1.3.2
set OUT=%~1
if "%OUT%"=="" (echo 出力ディレクトリを指定 & exit /b 1)
set HERE=%~dp0
set WORK=%HERE%work
if not exist "%WORK%" mkdir "%WORK%"
if not exist "%OUT%" mkdir "%OUT%"
cd /d "%WORK%"
if not exist libltc-%VER%.tar.gz curl -fsSL -o libltc-%VER%.tar.gz https://github.com/x42/libltc/releases/download/v%VER%/libltc-%VER%.tar.gz
rem sha256 は VERSION に記載の値と一致すること
for /f "tokens=1" %%h in ('certutil -hashfile libltc-%VER%.tar.gz SHA256 ^| findstr /v "hash CertUtil"') do set GOT=%%h
for /f "tokens=2" %%h in ('findstr /b "sha256:" "%HERE%VERSION"') do set WANT=%%h
if /i not "%GOT%"=="%WANT%" (echo sha256 mismatch: %GOT% & exit /b 1)
tar xzf libltc-%VER%.tar.gz
cd libltc-%VER%
rem autotools 不要: ソース4ファイルで足りる(config.h は #ifdef HAVE_CONFIG_H 内のみなので定義しない)。ltc.h に dllexport が無いため .def で明示する
cl /nologo /O2 /LD /I src /I . src\ltc.c src\decoder.c src\encoder.c src\timecode.c /Fe:"%OUT%\libltc.dll" /link /DEF:"%HERE%libltc.def"
copy /y COPYING "%OUT%\libltc-COPYING.txt" >nul
echo ok: %OUT%\libltc.dll
