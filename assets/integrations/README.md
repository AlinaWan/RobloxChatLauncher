> [!IMPORTANT]  
> You'll need an API key from the maintainers to send and receive data from the Roblox Chat Launcher API.

# Roblox Game Integrations

Set up these scripts in your Roblox Game to allow Roblox Chat Launcher users access to commands like `/emote` and team chat within your game.

---

## 🛠 Installation

You can integrate these scripts using **Rojo** or by **manually** copying them into your project.

### Option 1: Rojo (Recommended)
Simply sync this directory to your Roblox project using your preferred Rojo workflow.

### Option 2: Manual Setup
1. Copy the `.lua` scripts from this directory into your game.
2. Create a **RemoteEvent** in `ReplicatedStorage`.
3. Name the RemoteEvent **`RCL_Event`**.

---

## 🔐 API Key Configuration

To securely communicate with the API, you must store your API key as an **Experience Secret** on the Roblox Creator Dashboard.

1. Navigate to your [Creator Dashboard](https://create.roblox.com/dashboard/creations).
2. Select your Experience (Universe).
3. Go to **Settings** > **Secrets** (or navigate directly to `https://create.roblox.com/dashboard/creations/experiences/<your-universe-id>/secrets`).
4. Click **Create Secret**.
5. Set the Name to **`RCL_API_KEY`**.
6. Paste your provided API key into the Value field and save.

> [!CAUTION]  
> **Never** hard-code your API key directly into your scripts. Using the Secrets Store ensures your credentials remain private and secure and are only accessible by the server.

> [!WARNING]  
> Your game **must be published** to Roblox for this to work. If the game is not published, the Universe ID will be `0`, and the API authentication will fail.

---

## 🚦 Performance & Rate Limiting

To ensure stability and prevent script errors, keep the following limits in mind:

* **Roblox HttpService Limits:** By default, each server instance can make up to 500 HTTP requests per minute.
* **Ingress Bridge Polling:** The default Ingress script polls the mailbox at a rate of 1 request per second (60 requests/min). The polling loop runs once per server.
* **Egress Scripts:** Egress scripts may send POST requests independently when triggered by in-game events (e.g., a player joining). Please be mindful that the combined total requests from all scripts do not exceed Roblox's limits.

---

## 📄 License

The `.lua` scripts in this directory are licensed under the [Mozilla Public License 2.0](LICENSE).
