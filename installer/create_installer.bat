@echo off
SET WIXDIR="C:\Program Files (x86)\WiX Toolset v3.8\bin"
SET OPTIONS=-nologo -wx -ext WixUtilExtension -ext WiXNetFxExtension -ext %WIXDIR%\WixDifxAppExtension.dll

REM Remove last installer created
del srepp_installer.exe > NUL 2>&1

REM ---------------------------------------------------------------------------
REM Collect Files
REM ---------------------------------------------------------------------------
rmdir bins /S /Q > NUL 2>&1
mkdir bins > NUL 2>&1

copy srepp.inf bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Copying srepp.inf failed
  exit /b %errorlevel%
)

copy "..\bins\Win7 Debug\Win32\srepp.sys" bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Copying srepp.sys failed
  exit /b %errorlevel%
)

copy "..\bins\Win7 Debug\Win32\srkcomm.dll" bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Copying srkcomm.dll failed
  exit /b %errorlevel%
)

copy ..\bins\Debug\srsvc.exe bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Copying srsvc.exe failed
  exit /b %errorlevel%
)

copy ..\bins\Debug\srui.exe bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Copying srui.exe failed
  exit /b %errorlevel%
)

echo *** Collected compiled files

REM ---------------------------------------------------------------------------
REM Get rid of any old gunk
REM ---------------------------------------------------------------------------
del *.msi installer.wix* *.wixpdb > NUL 2>&1


REM ---------------------------------------------------------------------------
REM "Sign" driver with fake cert
REM ---------------------------------------------------------------------------
REM Must run the following on test boxes
REM   CertMgr  /add SR_test.cer /s root
REM   CertMgr /add SR_test.cer /s trustedpublisher
REM Also need to set the _DFX_INSTALL_UNSIGNED_DRIVER environment variable to 1.

REM SR_test.cer was created with: MakeCert -r -pe -ss SRTestCert -n "CN=SR_Test" SR_test.cer

Signtool sign /q /ac SR_test.cer /s SRTestCert bins\srepp.sys >NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Signing the driver failed
  exit /b %errorlevel%
)

echo *** Signed driver with fake cert

REM ---------------------------------------------------------------------------
REM Create .cat
REM ---------------------------------------------------------------------------
inf2cat /driver:.\bins /os:Vista_X86,Vista_X64,Server2008_X86,Server2008_X64,7_X86,7_X64,Server2008R2_X64,Server8_X64,8_X86,8_X64,Server6_3_X64,6_3_X64,6_3_X86 >NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Creating the catalog failed
  exit /b %errorlevel%
)

Signtool sign /q /ac SR_test.cer /s SRTestCert bins\srepp.cat >NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Signing the catalog failed
  exit /b %errorlevel%
)

echo *** Created catalog file

REM ---------------------------------------------------------------------------
REM Collect Third-Party libraries
REM ---------------------------------------------------------------------------
copy ..\lib\win32\FluentNHibernate.1.4.0.0\lib\net35\FluentNHibernate.dll bins\. > NUL
if %errorlevel% neq 0 (
  echo "!!! ERROR Unable to copy FluentNHibernate.dll"
  exit /b %errorlevel%
)

copy ..\lib\win32\Iesi.Collections.3.2.0.4000\lib\Net35\Iesi.Collections.dll bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Unable to copy Iesi.Collections.dll
  exit /b %errorlevel%
)

copy ..\lib\win32\NHibernate.3.3.1.4000\lib\Net35\NHibernate.dll bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Unable to copy NHibernate.dll
  exit /b %errorlevel%
)

copy ..\lib\win32\sqlite-netFx40-static-binary-bundle-Win32-2010-1.0.94.0\System.Data.SQLite.dll bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Unable to copy System.Data.SQLite.dll
  exit /b %errorlevel%
)

copy ..\lib\win32\System.Web.Helpers\System.Web.Helpers.dll bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Unable to copy System.Web.Helpers.dll
  exit /b %errorlevel%
)

copy ..\lib\msvcr120d.dll bins\. > NUL
if %errorlevel% neq 0 (
  echo !!! ERROR Unable to copy msvcr120d.dll
  exit /b %errorlevel%
)

echo *** Collected third-party libraries

REM ---------------------------------------------------------------------------
REM Create msi
REM ---------------------------------------------------------------------------
%WIXDIR%\candle.exe %OPTIONS%  installer.wxs -arch x86
if %errorlevel% neq 0 (
  echo !!! ERROR candle failed on installer.wxs
  exit /b %errorlevel%
)

%WIXDIR%\light.exe %OPTIONS% installer.wixobj  %WIXDIR%\difxapp_x86.wixlib -o SummitRoute_x86.msi
if %errorlevel% neq 0 (
  echo !!! ERROR light failed on installer.wixobj
  exit /b %errorlevel%
)

echo *** Created SummitRoute_x86.msi

REM ---------------------------------------------------------------------------
REM Sign MSI
REM ---------------------------------------------------------------------------

rem %WIXDIR%\insignia -im SummitRoute_x86.msi -nologo
rem if %errorlevel% neq 0 (
rem  echo !!! ERROR insignia failed on SummitRoute_x86.msi
rem  exit /b %errorlevel%
rem )

osslsigncode\osslsigncode.exe sign -pkcs12 osslsigncode/test.pfx -pass password -in SummitRoute_x86.msi -out SummitRoute_x86_signed.msi >NUL
if %errorlevel% neq 0 (
  echo !!! ERROR osslsigncode failed on SummitRoute_x86.msi
  exit /b %errorlevel%
)

del SummitRoute_x86.msi > NUL 2>&1
move SummitRoute_x86_signed.msi SummitRoute_x86.msi >NUL

echo *** Signed SummitRoute_x86.msi

REM ---------------------------------------------------------------------------
REM Create boostrapper exe
REM ---------------------------------------------------------------------------

%WIXDIR%\candle.exe %OPTIONS%  bootstrapper.wxs -arch x86 -ext WixBalExtension >NUL
if %errorlevel% neq 0 (
  echo !!! ERROR candle failed on bootstrapper.wxs
  exit /b %errorlevel%
)

%WIXDIR%\light.exe %OPTIONS% bootstrapper.wixobj -ext WixBalExtension -o srepp_installer.exe
if %errorlevel% neq 0 (
  echo !!! ERROR light failed on bootstrapper.wixobj
  exit /b %errorlevel%
)

echo *** Created srepp_installer.exe

REM ---------------------------------------------------------------------------
REM Sign it
REM ---------------------------------------------------------------------------

%WIXDIR%\insignia -ib srepp_installer.exe -o engine.exe -nologo
if %errorlevel% neq 0 (
  echo !!! ERROR insignia failed on srepp_installer.exe
  exit /b %errorlevel%
)


osslsigncode\osslsigncode.exe sign -pkcs12 osslsigncode/test.pfx -pass password -addBlob -in engine.exe -out engine_signed.exe >NUL
if %errorlevel% neq 0 (
  echo !!! ERROR osslsigncode failed on engine.exe
  exit /b %errorlevel%
)

echo *** Signed engine.exe

%WIXDIR%\insignia -ab engine_signed.exe srepp_installer.exe -o srepp_installer.exe -nologo
if %errorlevel% neq 0 (
  echo !!! ERROR insignia failed on signed srepp_installer.exe
  exit /b %errorlevel%
)

osslsigncode\osslsigncode.exe sign -pkcs12 osslsigncode/test.pfx -pass password -addBlob -in srepp_installer.exe -out srepp_installer_signed.exe >NUL
if %errorlevel% neq 0 (
  echo !!! ERROR osslsigncode add blob failed on srepp_installer.exe
  exit /b %errorlevel%
)

echo *** Added config blob area in srepp_installer.exe

del engine.exe > NUL 2>&1
del engine_signed.exe > NUL 2>&1
del srepp_installer.wixpdb > NUL 2>&1
del SummitRoute_x86.wixpdb > NUL 2>&1
del SummitRoute_x86.msi > NUL 2>&1
del bootstrapper.wixobj > NUL 2>&1

del srepp_installer.exe > NUL 2>&1
move srepp_installer_signed.exe srepp_installer.exe >NUL

del installer.wix* > NUL 2>&1
echo *** SUCCESS: Installer created ***
