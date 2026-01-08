; Using Inno Setup Compiler 6.4.3
#define Root ".."

[Setup]
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
; Copy everything from the publish folder
Source: "{#Root}\bin\Release\net10.0-windows\publish\*"; DestDir: "{app}"

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
    if MsgBox('.NET Desktop Runtime 10.0 is was not detected.' #13#13 +
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
