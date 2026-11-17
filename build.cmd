@echo off
setlocal
cd /d "%~dp0"
set "LAB_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%LAB_CSC%" set "LAB_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%LAB_CSC%" (
  echo .NET Framework compiler was not found. Windows with .NET Framework 4.8 is required.
  exit /b 1
)
if not exist bin mkdir bin
"%LAB_CSC%" /nologo /target:winexe /out:bin\GProXLab.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll src\UiKit.cs src\Core.cs src\Hid.cs src\MainForm.cs src\Program.cs
if errorlevel 1 exit /b 1
echo Built bin\GProXLab.exe
exit /b 0
