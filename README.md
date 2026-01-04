\# Roblox Chat Launcher



> \[!WARNING]

> This is a proof of concept. You cannot chat with anyone yet. However, the vision is to eventually exchange messages through a PaaS, using the exposed server instance ID to connect only with people in the same Roblox server.



A lightweight Windows desktop helper that mirrors the Roblox in‑game chat experience in a separate overlay window. It listens to global keyboard input, respects the user’s keyboard layout (including Shift and non‑US layouts), renders its own blinking caret, and synchronizes minimize/focus state with the Roblox window.



This project is primarily intended for experimentation, tooling, and UI behavior research around Roblox chat input.



---



\## Why Not Just Use Discord?



Roblox is already removing or limiting in-game chat for many users with the Facial Age Estimation/Age Groups update. While this launcher would require both parties to have it to chat, using Discord is no different. This project aims to replace the in-game chat in a more streamlined and integrated way, automatically connecting you only with people in the same Roblox server based on the exposed server instance ID. No extra apps or accounts are required, and the chat experience becomes native to the game environment.



---



\## Features



\* Passthrough input: You do not have to unfocus Roblox to type; pressing `/` and `Enter` is captured and lets you type like native chat

\* Synchronizes minimized/restored state with the Roblox window

\* No Roblox injection or memory modification



---



\## Prerequisites



\* Windows 10 or newer

\* \[.NET 7.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-7.0.20-windows-x64-installer)

\* Roblox installed on the system



---



\## Installation



Clone the repository:



```powershell

git clone https://github.com/AlinaWan/RobloxChatLauncher

cd RobloxChatLauncher

```



The project already references \*\*Gma.System.MouseKeyHook\*\* in the `.csproj`, but if you need to install it manually, the command is:



```powershell

dotnet add package MouseKeyHook --version 5.7.1

```



---



\## First Run (Important)



On the \*\*first run only\*\*, you must execute the app once so it can update the Windows registry to associate Roblox chat handling with this application.



Run:



```powershell

dotnet run

```



This step switches the relevant Roblox registry key to point to the launcher. After this, launching Roblox will automatically launch the chat window alongside the client.



---



\## Usage



1\. Launch Roblox normally

2\. The chat window will activate

3\. Type as usual, pressing `/` to start typing and `Enter` to send



---



\## License



\[GNU General Public License v3.0](LICENSE)

