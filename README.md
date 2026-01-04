# Roblox Chat Launcher

> [!WARNING]
> **THIS IS A PROOF OF CONCEPT**
>
> You cannot chat with anyone yet. However, the vision is to eventually exchange messages through a PaaS, using the exposed server instance ID to connect only with people in the same Roblox server.

A lightweight Windows desktop helper that mirrors the Roblox in‑game chat experience in a separate overlay window because Roblox is removing or limiting in-game chat for many users with the Facial Age Estimation/Age Groups update.

This project is primarily intended for experimentation, tooling, and UI behavior research around Roblox chat input and not yet ready for production. Feel free to contribute and open pull requests to improve this launcher!

---

## Why Not Just Use Discord?

The most common objection is: "But both people need to download this to talk—why not just use Discord?" While Discord is great for pre-planned groups, it fails the spontaneous player. This launcher isn't just a Discord alternative; it’s a native-feel bypass that solves the "Stranger Friction" Discord can't touch.

1. **Zero-Friction Connection (No "Add Me" Required)**  
To chat on Discord, you have to stop playing, exchange usernames, send a friend request, and join a call. By then, the round is over.

   **The Launcher Way:** It uses your Server Instance ID to automatically put you in a room with everyone else in your game who has the app. No links, no tags, no friction. You just join the game and start typing.

3. **Context-Aware Intelligence**  
Discord is a global "everything" app. This is a precision tool for the game you are currently playing.

   **Automatic Filtering:** You only hear from people in your specific server. When you hop to a new game, the chat channel hops with you. You never have to manually switch "servers" or "channels" to keep up with your current teammates.

5. **Integrated "Native" Ergonomics**  
Using Discord involves a clunky overlay or constant Alt-Tabbing, which can cause Roblox to lag or crash.

   **Seamless Input:** This launcher mirrors the native Roblox experience. Pressing / to start and Enter to send works exactly like the original chat, allowing you to stay focused on the game while using a modern, unrestricted UI.

7. **Reliable Communication in an "Age-Restricted" Era**  
As Roblox moves toward Facial Age Estimation and restricted chat categories, many players are losing the ability to communicate effectively in-game.

   This project provides a consistent, high-performance communication layer that bypasses UI limitations while remaining 100% compliant with Roblox’s Terms of Service (no injection or memory tampering).

---

## Features

* Passthrough input: You do not have to unfocus Roblox to type; pressing `/` and `Enter` is captured and lets you type like native chat
* Synchronizes minimized/restored state with the Roblox window
* No Roblox injection or memory modification

---

## Prerequisites

* Windows 10 or newer
* [.NET 7.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-7.0.20-windows-x64-installer)
* Roblox installed on the system

---

## Installation

Clone the repository:

```powershell
git clone https://github.com/AlinaWan/RobloxChatLauncher
cd RobloxChatLauncher
```

The project already references **Gma.System.MouseKeyHook** in the `.csproj`, but if you need to install it manually, the command is:

```powershell
dotnet add package MouseKeyHook --version 5.7.1
```

---

## First Run (Important)

On the **first run only**, you must execute the app once so it can update the Windows registry to associate Roblox chat handling with this application.

Run:

```powershell
dotnet run
```

This step switches the relevant Roblox registry key to point to the launcher. After this, launching Roblox will automatically launch the chat window alongside the client.

---

## Usage

1. Launch Roblox normally
2. The chat window will activate
3. Type as usual, pressing `/` to start typing and `Enter` to send

---

## License

[GNU General Public License v3.0](LICENSE)
