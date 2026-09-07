!include "MUI2.nsh"
!include "LogicLib.nsh"

!define MyAppName "Midnight"
!define MyAppVersion "1.0.0"
!define MyAppPublisher "diffrent522"
!define MyAppURL "https://github.com/diffrent522/Midnight"
!define MyAppExeName "Midnight.exe"

Name "${MyAppName}"
OutFile "Midnight\Installer\Midnight_Setup.exe"
InstallDir "$LOCALAPPDATA\${MyAppName}"
InstallDirRegKey HKCU "Software\${MyAppName}" "InstallDir"

RequestExecutionLevel user

Icon "Midnight\Resources\app.ico"
UninstallIcon "Midnight\Resources\app.ico"

VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName" "${MyAppName}"
VIAddVersionKey "CompanyName" "${MyAppPublisher}"
VIAddVersionKey "FileDescription" "${MyAppName}"
VIAddVersionKey "FileVersion" "${MyAppVersion}"
VIAddVersionKey "ProductVersion" "${MyAppVersion}"

SetCompressor /SOLID lzma

!define MUI_ABORTWARNING
!define MUI_ICON "Midnight\Resources\app.ico"
!define MUI_UNICON "Midnight\Resources\app.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Var PreviousInstall
Var PreviousUninstaller
Var UninstallPrevious

Function CheckPreviousInstallation

    StrCpy $PreviousInstall 0
    StrCpy $PreviousUninstaller ""

    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Midnight" "UninstallString"

    ${If} $0 != ""
        StrCpy $PreviousInstall 1
        StrCpy $PreviousUninstaller $0
        Return
    ${EndIf}

    ReadRegStr $0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Midnight" "UninstallString"

    ${If} $0 != ""
        StrCpy $PreviousInstall 1
        StrCpy $PreviousUninstaller $0
        Return
    ${EndIf}

FunctionEnd

Function IsDotNet8DesktopInstalled

    StrCpy $0 0

    IfFileExists "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\8.*\*" Found
    IfFileExists "$PROGRAMFILES\dotnet\shared\Microsoft.WindowsDesktop.App\8.*\*" Found

    ReadRegStr $1 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App" "8.0.0"

    ${If} $1 != ""
        Goto Found
    ${EndIf}

    ReadRegStr $1 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App" "8.0"

    ${If} $1 != ""
        Goto Found
    ${EndIf}

    Return

Found:

    StrCpy $0 1

FunctionEnd

Function .onInit

    Call IsDotNet8DesktopInstalled
    Pop $1

    ${If} $1 == 0

        MessageBox MB_YESNO|MB_ICONQUESTION "The .NET 8 Desktop Runtime appears to be missing.$\r$\n$\r$\nMidnight requires it to run.$\r$\n$\r$\nDo you want to download it now?" IDYES DownloadDotNet

        Goto CheckPrevious

DownloadDotNet:

        ExecShell "open" "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"

    ${EndIf}

CheckPrevious:

    Call CheckPreviousInstallation

    ${If} $PreviousInstall == 1

        MessageBox MB_YESNO|MB_ICONQUESTION "An older version of Midnight is already installed.$\r$\n$\r$\nDo you want to uninstall the previous version before installing Midnight 1.0.0?" IDYES UninstallPrevious

        StrCpy $UninstallPrevious 0

        Goto Done

UninstallPrevious:

        StrCpy $UninstallPrevious 1

    ${EndIf}

Done:

FunctionEnd

Section "Midnight"

    ${If} $UninstallPrevious == 1

        ${If} $PreviousUninstaller != ""

            ExecWait '$PreviousUninstaller /SILENT'

        ${EndIf}

    ${EndIf}

    SetOutPath "$INSTDIR"

    File /r "Midnight\bin\Release\net8.0-windows\*.*"

    WriteRegStr HKCU "Software\${MyAppName}" "InstallDir" "$INSTDIR"

    CreateDirectory "$SMPROGRAMS\${MyAppName}"

    CreateShortCut "$SMPROGRAMS\${MyAppName}\${MyAppName}.lnk" "$INSTDIR\${MyAppExeName}" "" "$INSTDIR\${MyAppExeName}" 0

    CreateShortCut "$DESKTOP\${MyAppName}.lnk" "$INSTDIR\${MyAppExeName}" "" "$INSTDIR\${MyAppExeName}" 0

    WriteUninstaller "$INSTDIR\Uninstall.exe"

    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${MyAppName}" "DisplayName" "${MyAppName}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${MyAppName}" "DisplayVersion" "${MyAppVersion}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${MyAppName}" "Publisher" "${MyAppPublisher}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${MyAppName}" "URLInfoAbout" "${MyAppURL}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${MyAppName}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${MyAppName}" "UninstallString" "$INSTDIR\Uninstall.exe"

SectionEnd

Function .onInstSuccess

    Exec "$INSTDIR\${MyAppExeName}"

FunctionEnd

Section "Uninstall"

    Delete "$DESKTOP\${MyAppName}.lnk"

    Delete "$SMPROGRAMS\${MyAppName}\${MyAppName}.lnk"

    RMDir "$SMPROGRAMS\${MyAppName}"

    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${MyAppName}"

    DeleteRegKey HKCU "Software\${MyAppName}"

    RMDir /r "$INSTDIR"

SectionEnd