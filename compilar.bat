@echo off
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo ERRO: compilador C# do .NET Framework 4 nao encontrado.
  pause
  exit /b 1
)
taskkill /im GamepadBatteryOverlay.exe /f >nul 2>&1
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /unsafe /out:GamepadBatteryOverlay.exe /win32icon:assets\icone.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (
  echo.
  echo ERRO na compilacao.
  pause
  exit /b 1
)
echo OK: GamepadBatteryOverlay.exe gerado.
