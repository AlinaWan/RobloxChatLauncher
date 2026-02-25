>[!NOTE]
> Privacy is the cornerstone of this project. Every contribution must prioritize user anonymity and data minimization. We do not accept features that require telemetry, invasive tracking, or the collection of personally identifiable information (PII).

<p align="center">
    <img src="https://github.com/AlinaWan/RobloxChatLauncher/raw/main/assets/readme/rcl_logo_dark.png#gh-dark-mode-only" width="580">
    <img src="https://github.com/AlinaWan/RobloxChatLauncher/raw/main/assets/readme/rcl_logo_light.png#gh-light-mode-only" width="580">
</p>

<div align="center">

[![Contributors welcome](https://img.shields.io/badge/contributions-welcome-brightgreen.svg?style=flat)](CONTRIBUTING.md)
[![License](https://img.shields.io/github/license/AlinaWan/RobloxChatLauncher)](LICENSE)
[![C#](https://custom-icon-badges.demolab.com/badge/C%23-%23239120.svg?logo=cshrp&logoColor=white)](#)
[![Node.js](https://img.shields.io/badge/Node.js-6DA55F?logo=node.js&logoColor=white)](#)
[![Docker](https://img.shields.io/badge/Docker-2496ED?logo=docker&logoColor=fff)](#)
[![❤︎](https://img.shields.io/badge/Made%20with%20%E2%9D%A4%20by%20Riri-FFCAE9)](#)

</div>

---

# Contributing to Roblox Chat Launcher

This guide covers everything you need to start contributing: environment setup, testing, commit conventions, building the project, and submitting pull requests.

---

## 🛠️ Recommended Development Environment

To ensure your environment matches production builds:

### IDE & Toolchain

* **Visual Studio 2022 (preferred) or VS 2026:** primary IDE.
* **Inno Setup (latest version):** for building the installer.
* **.NET 10 SDK:** the target framework for the C# client.
* **Docker Desktop:** to build images and deploy the back-end server.

### Testing & Virtualization

Use a sandboxed environment for safe testing:

* **VirtualBox or any other hypervisor:** recommended hypervisor (Type 2 virtualization is sufficient).
* **Windows 11:** full windows image.
  * [Download](https://www.microsoft.com/en-us/software-download/windows11)
* **Tiny11 Core Beta 1 (Windows 11 Pro 23H2, Build 22631.2361):** minimal testing image.
  * [Download](https://archive.org/details/tiny-11-core-x-64-beta-1)
* **Tiny11 25H2 / Tiny11 Core 25H2:** newer minimal Windows 11 images.
  * [Download](https://archive.org/details/tiny11_25H2)

> [!WARNING]
> **Tiny11 Core Safety Notes:**
>
> * Tiny11 Core is not a replacement for Tiny11; use for testing only.
> * Windows Defender is removed. Exercise caution when browsing inside the VM.

---

## 💬 Commit Message Guidelines

All commits **must follow [Conventional Commits v1.0.0](https://www.conventionalcommits.org/en/v1.0.0)**.

> [!IMPORTANT]
> **Use Scopes!**
> 
> Please make sure that every commit type includes a scope indicating the part of the project affected (e.g., `client`, `server`, `installer`, `docs`).

### Examples

| Commit Message | Description |
| :--- | :--- |
| `feat(client):` add WebSocket listener for server instance IDs | Adds a new functional feature to the C# client. |
| `fix(server):` resolve memory leak in connection pooling | Fixes a bug within the Node.js/Express backend. |
| `refactor(client):` clean up input capture logic | Improving code structure without changing behavior. |
| `perf(server):` optimize message broadcasting latency | A change specifically focused on improving speed. |
| `chore(installer):` update Inno Setup script for .NET 10.0 | Routine maintenance or dependency updates. |
| `docs(readme):` add security research citations for Persona | Documentation-only changes. |

### Common Commit Types

* `feat(scope):` new feature
* `fix(scope):` bug fix
* `refactor(scope):` code changes that don’t add features or fix bugs
* `perf(scope):` performance improvements
* `chore(scope):` maintenance tasks

---

## 📥 Installing .NET 10 and Downloading Roblox Chat Launcher from CLI

<details>
  <summary>Click to expand</summary>
  <br>
  <p>Follow these steps to install .NET 10 and Roblox Chat Launcher without Git, GitHub CLI, or a browser.</p>
  
  <h3>1️⃣ Prepare Directories</h3>
  <p>Open <b>PowerShell (Admin)</b>:</p>
  <pre><code>mkdir C:\Downloads
mkdir C:\dotnet
cd C:\Downloads</code></pre>

  <hr>

  <h3>2️⃣ Install .NET 10 Desktop Runtime via Script</h3>
  <pre><code>Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile C:\dotnet\dotnet-install.ps1
powershell -ExecutionPolicy Bypass -File C:\dotnet\dotnet-install.ps1 -Runtime windowsdesktop -Channel 10.0
setx PATH "$env:PATH;C:\dotnet"
dotnet --list-runtimes</code></pre>
  <p>You should see a <code>Microsoft.WindowsDesktop.App 10.0.x</code> entry.</p>
    <p>Or .NET SDK:</p>
  <pre><code>Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile C:\dotnet\dotnet-install.ps1
powershell -ExecutionPolicy Bypass -File C:\dotnet\dotnet-install.ps1 -Channel 10.0
$env:PATH += ";C:\dotnet"
dotnet --info</code></pre>

  <hr>

  <h3>3️⃣ Download RobloxChatLauncher Repository as Zip</h3>
  <pre><code>Invoke-WebRequest -Uri "https://github.com/AlinaWan/RobloxChatLauncher/archive/refs/heads/main.zip" -OutFile "C:\Downloads\RobloxChatLauncher.zip"</code></pre>
  
  <p>Or a specific tag/commit hash:</p>
  <pre><code>Invoke-WebRequest -Uri "https://github.com/AlinaWan/RobloxChatLauncher/archive/refs/tags/v1.0.0.zip" -OutFile "C:\Downloads\RobloxChatLauncher.zip"
Invoke-WebRequest -Uri "https://github.com/AlinaWan/RobloxChatLauncher/archive/a1b2c3d.zip" -OutFile "C:\Downloads\RobloxChatLauncher.zip"</code></pre>

  <hr>

  <h3>4️⃣ Extract the Repository</h3>
  <pre><code>Expand-Archive -Path "C:\Downloads\RobloxChatLauncher.zip" -DestinationPath "C:\Downloads\RobloxChatLauncher"</code></pre>
  
  <p>Verify that all files are available:</p>
  <pre><code>Get-ChildItem "C:\Downloads\RobloxChatLauncher"</code></pre>
</details>

---

## 💻 Development Guidelines

* Follow existing code style for C# and JavaScript.
* Keep commits small, descriptive, and scoped.

---

## 📜 Pull Request Checklist

* [ ] Builds successfully.
* [ ] Commits follow Conventional Commits.
* [ ] Changes documented clearly.
* [ ] I agree that my contributions will be licensed under the **GNU GPLv3**.
