# 🛡️ Privacy & Data Policy

This document provides transparency for users and reviewers regarding how data is handled within this server.

## 👤 Roblox Verification

If you choose to verify your Roblox account, we prioritize data minimization:

* **No IP Storage:** We do not store your IP address during or after the verification process.
* **HWID Storage:** We only store a **HWID GUID** (Hardware Identifier) of your machine so that only you can log into your own account.
* **Data Deletion:** You can unverify at any time by using the `/unverify` command. This will immediately delete your associated data from our systems.

## 📝 General Chat Privacy

### What the server sees:

* Your messages.
* The channel/server instance they are sent on (required for routing).

### What the server does NOT see:

* **Your Public IP:** While Render (our hosting provider) processes your IP for standard internet communication, our application code does **not** log it or provide us with access to view it.
* **Permanent Identifiers:** We do not use permanent identifiers, or even assign pseudo-anonymous IDs. The server only sees a temporary **Guest Label** based on the WebSocket connection port.

> [!IMPORTANT]
> **Guest labels change on every reconnect.** You can verify this by running `/rc` on the client. This reconnects the WebSocket and assigns you a new guest number. Note that ports may be reused across sessions over time.

---

## 📂 Example of Logged Data

This is exactly how messages appear in our internal volatile logs. Notice the use of local loopback or generic ports rather than user-identifiable tracking:

```text
Message received from 127.0.0.1:59938 on channel c4d2c979...: Hello World!
Message received from 127.0.0.1:59938 on channel c4d2c979...: I'm testing the chat server.
Message received from 127.0.0.1:42668 on channel c4d2c979...: I'm another user with a different guest number!
Message received from 127.0.0.1:56198 on channel c4d2c979...: Yet another user on the same channel!
Message received from 127.0.0.1:45768 on channel d3ea7bfa...: New channel, new user!

```

---

## ⏳ Retention Policy

* **Automatic Deletion:** All logs are automatically deleted by Render after **7 days**.
* **No External Persistence:** We do not store, move, or persist these logs outside of Render's standard logging system.

For more details, please see the full [Privacy Policy](./PRIVACY).
