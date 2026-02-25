#define Root ".."

[Setup]
; In Inno Setup you must use double curly braces at the start of the GUID to escape the character
AppId={{B0BACAFE-D326-4A7B-B6BA-1437C0DEBABE}
AppName=Roblox Chat Launcher
AppVersion=1.0.0 ; Do not manually update this version; it is auto-updated by release workflow
AppVerName=Roblox Chat Launcher
DefaultDirName={pf}\AlinaWan\RobloxChatLauncher
DefaultGroupName=Roblox Chat Launcher
OutputDir=.
OutputBaseFilename=Installer
Compression=lzma
SolidCompression=yes
LicenseFile={#Root}\LICENSE

[Files]
Source: "{#Root}\LICENSE"; DestDir: "{app}"
Source: "{#Root}\PRIVACY"; DestDir: "{app}"
Source: "{#Root}\TERMS"; DestDir: "{app}"
; Copy everything from the publish folder
Source: "{#Root}\client\bin\Release\net10.0-windows\publish\*"; DestDir: "{app}"

[UninstallDelete]
; This recursively deletes the folder and everything inside it
Type: filesandordirs; Name: "{localappdata}\RobloxChatLauncher"

[Run]
; Silently run the app to register it as the Roblox launcher
Filename: "{app}\ConsoleApp1.exe"; Flags: nowait runhidden

[Code]
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

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not IsDotNet10Installed() then
  begin
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
  { We add 'Assigned' checks to ensure the pages exist before checking IDs }
  if Assigned(TermsPage) and (WizardForm.CurPageID = TermsPage.ID) then
    WizardForm.NextButton.Enabled := TermsAcceptedRadio.Checked
  else if Assigned(PrivacyPage) and (WizardForm.CurPageID = PrivacyPage.ID) then
    WizardForm.NextButton.Enabled := PrivacyAcceptedRadio.Checked;
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
  { --- 1. TERMS OF SERVICE PAGE --- }
  { wpLicense means it comes right after the standard License page }
  TermsPage := CreateOutputMsgMemoPage(wpLicense, 
    'Terms of Service Agreement', 'Please read the following important information before continuing.',
    'Please read the following Terms of Service Agreement. You must accept the terms of this agreement before continuing with the installation.', '');
  
  TermsPage.RichEditViewer.Height := WizardForm.LicenseMemo.Height;
  
  ExtractTemporaryFile('TERMS');
  TermsPath := ExpandConstant('{tmp}\TERMS');
  TermsPage.RichEditViewer.Lines.LoadFromFile(TermsPath);

  TermsAcceptedRadio := CreateLicenseRadio(TermsPage, WizardForm.LicenseAcceptedRadio, 'I accept the agreement');
  TermsNotAcceptedRadio := CreateLicenseRadio(TermsPage, WizardForm.LicenseNotAcceptedRadio, 'I do not accept the agreement');
  TermsNotAcceptedRadio.Checked := True;

  { --- 2. PRIVACY POLICY PAGE --- }
  { We use TermsPage.ID so this appears right after the Terms page }
  PrivacyPage := CreateOutputMsgMemoPage(TermsPage.ID, 
    'Privacy Policy Agreement', 'Please read the following important information before continuing.',
    'Please read the following Privacy Policy Agreement. You must accept the terms of this agreement before continuing with the installation.', '');

  PrivacyPage.RichEditViewer.Height := WizardForm.LicenseMemo.Height;

  ExtractTemporaryFile('PRIVACY');
  PrivacyPath := ExpandConstant('{tmp}\PRIVACY');
  PrivacyPage.RichEditViewer.Lines.LoadFromFile(PrivacyPath);

  PrivacyAcceptedRadio := CreateLicenseRadio(PrivacyPage, WizardForm.LicenseAcceptedRadio, 'I accept the agreement');
  PrivacyNotAcceptedRadio := CreateLicenseRadio(PrivacyPage, WizardForm.LicenseNotAcceptedRadio, 'I do not accept the agreement');
  PrivacyNotAcceptedRadio.Checked := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { Ensure the Next button state is correct when the user navigates to these pages }
  if (CurPageID = TermsPage.ID) or (CurPageID = PrivacyPage.ID) then
  begin
    UpdateNextButton(nil);
  end;
end;
