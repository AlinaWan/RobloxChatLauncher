#define AppVersion "1.0.0" ; Do not manually update this version; it is auto-updated by release workflow
#define Root ".."

[Setup]
; In Inno Setup you must use double curly braces at the start of the GUID to escape the character
AppId={{B0BACAFE-D326-4A7B-B6BA-1437C0DEBABE}
AppName=Roblox Chat Launcher
AppVersion={#AppVersion}
AppVerName=Roblox Chat Launcher
DefaultDirName={pf}\AlinaWan\RobloxChatLauncher
DefaultGroupName=Roblox Chat Launcher
OutputDir=.
OutputBaseFilename=RobloxChatLauncherInstaller
Compression=lzma
SolidCompression=yes
LicenseFile={#Root}\LICENSE
SetupIconFile={#Root}\assets\brand\rcl_icon-variable.ico
UninstallDisplayIcon={app}\RobloxChatLauncher.exe
CloseApplications=yes

[Files]
Source: "{#Root}\LICENSE"; DestDir: "{app}"
Source: "{#Root}\PRIVACY"; DestDir: "{app}"
Source: "{#Root}\TERMS"; DestDir: "{app}"
; Copy everything from the publish folder including resource folders
Source: "{#Root}\client\bin\Release\net10.0-windows\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{autoprograms}\Roblox Chat Launcher"; Filename: "{app}\RobloxChatLauncher.exe"; IconFileName: "{app}\RobloxChatLauncher.exe"; Parameters: "--launch-homepage"
Name: "{autodesktop}\Roblox Chat Launcher"; Filename: "{app}\RobloxChatLauncher.exe"; IconFileName: "{app}\RobloxChatLauncher.exe"; Parameters: "--launch-homepage"

[Run]
; Silently run the app to register it as the Roblox launcher
Filename: "{app}\RobloxChatLauncher.exe"; Flags: nowait runhidden; Check: not IsForceRunFlagPresent

; Runs with --force-run arg if /FORCERUN IS passed to the installer
Filename: "{app}\RobloxChatLauncher.exe"; Parameters: "--force-run"; Flags: nowait postinstall; Check: IsForceRunFlagPresent

[Code]
// ---------------------------------------------------------------
// Helper function to check for the presence of the /FORCERUN flag
// ----------------------------------------------------------------
function IsForceRunFlagPresent(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), '/FORCERUN') = 0 then
    begin
      Result := True;
      Break;
    end;
  end;
end;

// -------------------------------------------------------------------
// Helper function to check for the presence of the /CLEANINSTALL flag
// -------------------------------------------------------------------
function IsCleanInstallFlagPresent(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), '/CLEANINSTALL') = 0 then
    begin
      Result := True;
      Break;
    end;
  end;
end;

// --------------------------------------------------------------------
// Helper function to check for the presence of the /CLEARAPPDATA flag
// --------------------------------------------------------------------
function IsClearAppDataFlagPresent(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), '/CLEARAPPDATA') = 0 then
    begin
      Result := True;
      Break;
    end;
  end;
end;

// --------------------------------------------------------------------
// Helper function to check for the presence of the /FORCEPURGE flag
// --------------------------------------------------------------------
function IsForcePurgeFlagPresent(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), '/FORCEPURGE') = 0 then
    begin
      Result := True;
      Break;
    end;
  end;
end;

// ---------------------------------------------------------------
// Helper to remove existing installation
// ---------------------------------------------------------------
procedure RemoveExistingInstallation();
var
  UninstallExe: string;
  UninstallKey: string;
  ResultCode: Integer;
begin
  UninstallKey := 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{B0BACAFE-D326-4A7B-B6BA-1437C0DEBABE}_is1';

  // Uninstall the existing application if it exists by running the uninstaller silently
  if RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', UninstallExe) then
  begin
    UninstallExe := RemoveQuotes(UninstallExe);
    
    if FileExists(UninstallExe) then
    begin
      Exec(UninstallExe, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

// ---------------------------------------------------------------
// Helper to remove existing local app data
// ---------------------------------------------------------------
procedure RemoveExistingAppData();
var
  AppDataPath: string;
begin
  // Forcefully remove local app data folder
  AppDataPath := ExpandConstant('{localappdata}\RobloxChatLauncher');
  if (Length(AppDataPath) > 3) and DirExists(AppDataPath) then
  begin
    DelTree(AppDataPath, True, True, True);   
  end;
end;

// -----------------------------------------------------
// .NET Desktop Runtime installation check and installer
// -----------------------------------------------------
const
  DotNet10Url = 'https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.1-windows-x64-installer';

function IsDotNet10Installed(): Boolean;
var
  TmpFileName: String;
  ResultCode: Integer;
  OutputLines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  TmpFileName := ExpandConstant('{tmp}\dotnet_runtimes.txt');

  // Execute dotnet --list-runtimes and pipe output to a temp file
  // Using 'cmd /c' allows us to use the '>' redirection operator
  if Exec(ExpandConstant('{cmd}'), '/c dotnet --list-runtimes > "' + TmpFileName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringsFromFile(TmpFileName, OutputLines) then
    begin
      for I := 0 to GetArrayLength(OutputLines) - 1 do
      begin
        // Look for the specific Desktop App string followed by version 10
        // WindowsDesktop is Desktop Runtime
        if Pos('Microsoft.WindowsDesktop.App 10.', OutputLines[I]) = 1 then
        begin
          Log('Found .NET 10 Runtime: ' + OutputLines[I]);
          Result := True;
          Break;
        end;
      end;
    end;
  end;
  
  // Clean up the temporary file
  DeleteFile(TmpFileName);
end;

// ---------------------------------------------------------------
// InitializeSetup override
// ---------------------------------------------------------------
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  // If /CLEANINSTALL is passed, forcefully remove old installation before installing new version
  if IsCleanInstallFlagPresent() then
  begin
    RemoveExistingInstallation();
  end;

  // If /CLEARAPPDATA is passed, forcefully remove local app data before installation
  if IsClearAppDataFlagPresent() then
  begin
    RemoveExistingAppData();
  end;

  if not IsDotNet10Installed() then
  begin
    // If the installer was launched with /SILENT or /VERYSILENT, skip the popups
    if WizardSilent then
    begin
      Log('WARNING: .NET 10.0 not found during silent install.');
      Result := True;
      Exit;
    end;
    
    if MsgBox('.NET Desktop Runtime 10.0 was not detected.' #13#13 +
              'Would you like to download and install it now?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', DotNet10Url, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      
    // Show instructions after opening the link
    MsgBox('The .NET Desktop Runtime 10.0 installer will be downloaded in the background. ' +
      'Please run the installer from your Downloads folder and then rerun this installer.',
      mbInformation, MB_OK);
           
      Result := False;
    end
    else
    begin
      MsgBox('Installation cannot proceed without .NET Desktop Runtime 10.0.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// ---------------------------------------------------------------
// CurStepChanged override
// ---------------------------------------------------------------
// Inno Setup already warns users if the chosen installation directory already exists, but it does
// not remove its contents if the user proceeds anyways. If the /FORCEPURGE flag is passed, we will
// forcefully remove all contents after the user proceeds.
procedure CurStepChanged(CurStep: TSetupStep);
var
  TargetDir: string;
begin
  if CurStep = ssInstall then
  begin
    if IsForcePurgeFlagPresent() then
    begin
      // The target installation directory chosen by the user exists by now
      TargetDir := ExpandConstant('{app}');
      
      if (Length(TargetDir) > 3) and DirExists(TargetDir) then
      begin
        Log('Purging directory: ' + TargetDir);
        // DelTree(Path, IsDir, DeleteFiles, DeleteSubdirs)
        if not DelTree(TargetDir, True, True, True) then
        begin
           Log('Could not purge directory: ' + TargetDir);
        end;
      end;
    end;
  end;
end;

// ------------------------------------------------------------
// Clickwrap agreements for Terms of Service and Privacy Policy
// ------------------------------------------------------------
var
  TermsPage, PrivacyPage: TOutputMsgMemoWizardPage;
  TermsAcceptedRadio, TermsNotAcceptedRadio: TRadioButton;
  PrivacyAcceptedRadio, PrivacyNotAcceptedRadio: TRadioButton;

{ Logic to enable/disable Next button based on selection }
procedure UpdateNextButton(Sender: TObject);
begin
  // If silent, we don't need to toggle the Next button's enabled state
  if WizardSilent then Exit;

  { We add 'Assigned' checks to ensure the pages exist before checking IDs }
  if Assigned(TermsPage) and (WizardForm.CurPageID = TermsPage.ID) then
    WizardForm.NextButton.Enabled := TermsAcceptedRadio.Checked
  else if Assigned(PrivacyPage) and (WizardForm.CurPageID = PrivacyPage.ID) then
    WizardForm.NextButton.Enabled := PrivacyAcceptedRadio.Checked;
end;

{ Helper function to skip pages during silent install }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if WizardSilent then
  begin
    if (Assigned(TermsPage) and (PageID = TermsPage.ID)) or 
       (Assigned(PrivacyPage) and (PageID = PrivacyPage.ID)) then
      Result := True;
  end;
end;

{ Helper function to create and place the radio buttons }
function CreateLicenseRadio(ParentPage: TOutputMsgMemoWizardPage; Original: TRadioButton; Text: string): TRadioButton;
begin
  Result := TRadioButton.Create(WizardForm);
  Result.Parent := ParentPage.Surface;
  Result.Caption := Text;
  Result.Left := Original.Left;
  Result.Top := Original.Top;
  Result.Width := Original.Width;
  Result.Height := Original.Height;
  Result.Anchors := Original.Anchors;
  Result.OnClick := @UpdateNextButton;
end;


procedure InitializeWizard();
var
  TermsPath, PrivacyPath: string;
begin

  { wpLicense means it comes right after the standard License page }
  TermsPage := CreateOutputMsgMemoPage(wpLicense, 
    'Terms of Service Agreement', 'Please read the following important information before continuing.',
    'Please read the following Terms of Service Agreement. You must accept the terms of this agreement before continuing with the installation.', '');

  { We use TermsPage.ID so this appears right after the Terms page }
  PrivacyPage := CreateOutputMsgMemoPage(TermsPage.ID, 
    'Privacy Policy Agreement', 'Please read the following important information before continuing.',
    'Please read the following Privacy Policy Agreement. You must accept the terms of this agreement before continuing with the installation.', '');

  if not WizardSilent then
  begin
    { --- 1. TERMS OF SERVICE PAGE --- }
    TermsPage.RichEditViewer.Height := WizardForm.LicenseMemo.Height;
    
    ExtractTemporaryFile('TERMS');
    TermsPath := ExpandConstant('{tmp}\TERMS');
    TermsPage.RichEditViewer.Lines.LoadFromFile(TermsPath);

    TermsAcceptedRadio := CreateLicenseRadio(TermsPage, WizardForm.LicenseAcceptedRadio, 'I accept the agreement');
    TermsNotAcceptedRadio := CreateLicenseRadio(TermsPage, WizardForm.LicenseNotAcceptedRadio, 'I do not accept the agreement');
    TermsNotAcceptedRadio.Checked := True;

    { --- 2. PRIVACY POLICY PAGE --- }
    PrivacyPage.RichEditViewer.Height := WizardForm.LicenseMemo.Height;

    ExtractTemporaryFile('PRIVACY');
    PrivacyPath := ExpandConstant('{tmp}\PRIVACY');
    PrivacyPage.RichEditViewer.Lines.LoadFromFile(PrivacyPath);

    PrivacyAcceptedRadio := CreateLicenseRadio(PrivacyPage, WizardForm.LicenseAcceptedRadio, 'I accept the agreement');
    PrivacyNotAcceptedRadio := CreateLicenseRadio(PrivacyPage, WizardForm.LicenseNotAcceptedRadio, 'I do not accept the agreement');
    PrivacyNotAcceptedRadio.Checked := True;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if not WizardSilent then
  begin
    { Ensure the Next button state is correct when the user navigates to these pages }
    if (CurPageID = TermsPage.ID) or (CurPageID = PrivacyPage.ID) then
    begin
      UpdateNextButton(nil);
    end;
  end;
end;

// ------------------------------------------------------------
// Restore Roblox/Bootstrapper registry on uninstall
// ------------------------------------------------------------
// Before uninstalling, we silently restore the original Roblox
// or Bootstrapper registry key so that the Roblox client continues
// to work after our app is uninstalled.
procedure CurUninstallStepChanged(UninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  AppPath: string;
begin
  // This triggers at the very beginning of the uninstallation process
  if UninstallStep = usUninstall then
  begin
    // Points to your executable in the installation directory
    AppPath := ExpandConstant('{app}\RobloxChatLauncher.exe');
    
    if FileExists(AppPath) then
    begin
      // Log for debugging (visible in the uninstall log)
      Log('Launching app to restore registry: ' + AppPath);
      
      // Execute with the --uninstall flag 
      // SW_HIDE makes it invisible to the user
      // ewWaitUntilTerminated ensures the registry is fixed before files are deleted
      if not Exec(AppPath, '--uninstall', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      begin
        Log('Failed to launch restoration app. Result code: ' + IntToStr(ResultCode));
      end;
    end
    else
    begin
      Log('Restoration app not found at: ' + AppPath);
    end;
  end;
end;