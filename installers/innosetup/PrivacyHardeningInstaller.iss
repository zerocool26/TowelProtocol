[Setup]
AppName=PrivacyHardeningFramework
AppVersion=0.1.0
DefaultDirName={autopf}\PrivacyHardeningFramework
DefaultGroupName=PrivacyHardeningFramework
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\..\dist\installer
OutputBaseFilename=PrivacyHardeningInstaller

[Files]
; Adjust Source paths if your build output is in a different folder
Source: "{#SourceDir}\src\PrivacyHardeningUI\bin\Release\net8.0-windows10.0.22621.0\PrivacyHardeningUI.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\dist\PrivacyHardeningElevated\PrivacyHardeningElevated.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\policies\*"; DestDir: "{app}\policies"; Flags: recursesubdirs createallsubdirs
Source: "{#SourceDir}\scripts\*"; DestDir: "{app}\scripts"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PrivacyHardening UI"; Filename: "{app}\PrivacyHardeningUI.exe"
Name: "{group}\Uninstall PrivacyHardeningFramework"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\PrivacyHardeningUI.exe"; Description: "Launch PrivacyHardening UI"; Flags: nowait postinstall skipifsilent

[Code]
// Defines SourceDir variable dynamically at build time using Inno Setup Compiler's Preprocessor
#define SourceDir GetEnv("PWD")
