@echo OFF
cls

ECHO [INFO] Checking if FAKE was downloaded from Nuget
IF NOT EXIST %~dp0\src\packages\FAKE\tools\FAKE.exe (
	ECHO [INFO] Downloading FAKE from Nuget
	"src\.nuget\NuGet.exe" "Install" "FAKE" "-OutputDirectory" "src\packages" "-ExcludeVersion"
)


IF "%1"=="target" (
	SET _target=%2
	CALL :TARGET
	GOTO EXIT
)

ECHO [INFO] ------------------------------------------------------------------------
ECHO [INFO] Building the application
"src\packages\FAKE\tools\Fake.exe" setup.fsx
GOTO EXIT

:TARGET
ECHO [INFO] ------------------------------------------------------------------------
ECHO [INFO] Running the specified target from the setup script
"src\packages\FAKE\tools\Fake.exe" setup.fsx %_target% -st
EXIT /B 0

:EXIT
ECHO [INFO] ------------------------------------------------------------------------