const axios = require('axios');
const crypto = require('crypto');
const express = require('express');
const http = require('http');
const WebSocket = require('ws');

const { rateLimit, ipKeyGenerator } = require('express-rate-limit');

const Constants = require('./config/constants');
const Env = require('./config/env');
const { pool, initDatabase } = require('./db/postgresPool');
const { isMessageAllowed } = require('./services/moderationService');
const { generateCode, verifyProfile, unverifyUser } = require('./services/verification');
const { mailboxStore, pushToMailbox } = require('./services/mailboxService');
const { authenticateGameServer, getAllGames, upsertGame, removeGame } = require('./services/registry');

// ----- Express App Setup -----
const app = express();
// Trust proxy (Render / Heroku / etc.)
// Always use proxy from Render as the trusted IP
app.set('trust proxy', 1); // trust only the first proxy hop (Render)
// Middleware to parse plain text bodies (sent by C# client)
// Limit messages to 1kb (more than enough for a chat message)
app.use(express.text({ limit: Constants.TEXT_LIMIT_BYTES }));

// ----- PostgreSQL Setup -----
(async () => {
    try {
        await initDatabase();
        console.log("Database initialized");
    } catch (err) {
        console.error("DB init failed:", err);
        process.exit(1);
    }
})();

// ----- WebSocket Setup -----
const server = http.createServer(app);
const wss = new WebSocket.Server({
    server,
    maxPayload: Constants.WEBSOCKET_LIMIT_BYTES
});
// Memory-efficient storage for dynamic channels
// Key: channelId (string), Value: Set of socket objects
const channels = new Map();

// Queue of messages to be filtered
const messageQueue = [];
let processing = false;

function hashIp(ip) {
    return crypto.createHash('sha256').update(ip + '||' + Env.USER_SALT).digest('hex');
}

const userChannelMap = new Map(); // userKey -> channelId

// For HTTP, req.ip is trusted
// For WebSocket, fallback to the proxy IP (req.socket.remoteAddress)
//
// req.ip is the trusted private IP (NOT origin IP/public IP)
// This is not your home IP address. We never see your origin IP.
function getTrustedIp(req, ws = null) {
    // HTTP requests (echo endpoint)
    if (req.ip) return req.ip; // Returns a 10.x.x.x IP address

    // WebSocket: combine proxy IP + remote port
    if (ws && ws._socket) {
        const ip = ws._socket.remoteAddress;
        // Each WS connection gets a unique remotePort even if multiple users connect via the same proxy.
        const port = ws._socket.remotePort; // unique per connection
        return `${ip}:${port}`; // Returns a 127.0.0.1:x IP address
    }
    
    return req.socket?.remoteAddress || "unknown";
}

// --- Message Queueing and Processing ---
function enqueueMessage(text) {
    return new Promise((resolve) => {
        // Reject immediately if the queue is too long
        if (messageQueue.length >= Constants.MAX_QUEUE_SIZE) {
            resolve({ allowed: false, reason: "queue_full" });
            return;
        }
        
        // Otherwise, add the message to the queue
        messageQueue.push({ text, resolve });
        processQueue();
    });
}

async function processQueue() {
    if (processing || messageQueue.length === 0) return;

    processing = true;
    const { text, resolve } = messageQueue.shift();

    try {
        const result = await isMessageAllowed(text);
        if (!result.allowed) {
            resolve({ allowed: false, reason: "moderation" });
        } else {
            resolve({ allowed: true });
        }
    } catch (err) {
        resolve({ allowed: false, reason: "api_error" });
    }

    // Delay to respect Perspective API limits (1 request per second)
    setTimeout(() => {
        processing = false;
        processQueue();
    }, 1000);
}

// --- Admin Authorization Middleware ---
const validateAdmin = (req, res, next) => {
    const authHeader = req.headers['authorization'];
    if (!authHeader || authHeader !== `Bearer ${Env.RCL_ADMIN_KEY}`) {
        return res.status(401).json({ error: "Unauthorized" });
    }
    next();
};

// --------------------------------
// --- Admin Registry Endpoints ---
// --------------------------------
// ----- registry -----
// 1. List all registered universes
app.get('/api/v1/admin/registry', validateAdmin, async (req, res) => {
    try {
        const games = await getAllGames();
        res.json(games);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 2. Add or Update a game (JSON Body: { "universeId": 123, "apiKey": "secret" })
app.post('/api/v1/admin/registry', express.json(), validateAdmin, async (req, res) => {
    const { universeId, apiKey } = req.body;
    if (!universeId || !apiKey) return res.status(400).send("Missing data");

    try {
        await upsertGame(universeId, apiKey);
        res.json({ status: "success", message: `Universe ${universeId} updated.` });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 3. Delete a game
app.delete('/api/v1/admin/registry/:id', validateAdmin, async (req, res) => {
    try {
        await removeGame(req.params.id);
        res.json({ status: "deleted" });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 1. Add or Update a user (JSON Body: { "hwid": "...", "robloxId": 12345 })
app.post('/api/v1/admin/verified', express.json(), validateAdmin, async (req, res) => {
    const { hwid, robloxId } = req.body;
    if (!hwid || !robloxId) return res.status(400).send("Missing hwid or robloxId");

    try {
        await upsertUser(hwid, robloxId);
        res.json({ status: "success", message: `User with HWID ${hwid} linked to ${robloxId}.` });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 2. Delete a user by HWID
app.delete('/api/v1/admin/verified/:hwid', validateAdmin, async (req, res) => {
    const { hwid } = req.params;
    try {
        await removeUser(hwid);
        res.json({ status: "deleted", message: `HWID ${hwid} removed from verified users.` });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// ----- verified users -----
app.get('/api/v1/admin/verified', validateAdmin, async (req, res) => {
    try {
        const result = await pool.query('SELECT hwid, roblox_id FROM verified_users ORDER BY roblox_id');
        res.json(result.rows); // Returns an array of objects { hwid, roblox_id }
    } catch (err) {
        console.error("Admin DB List Error:", err);
        res.status(500).json({ error: err.message });
    }
});

// ----- Command Mailbox Middleware -----
const validateRegistry = async (req, res, next) => {
    const universeId = req.headers['x-universe-id'];
    const apiKey = req.headers['x-api-key'];
    const jobId = req.headers['x-job-id'];

    if (!universeId || !apiKey || !jobId) {
        return res.status(401).json({ error: "Missing identity headers." });
    }

    const isAuthenticated = await authenticateGameServer(universeId, apiKey);

    if (!isAuthenticated) {
        console.warn(`Unauthorized access attempt: Universe ${universeId}`);
        return res.status(403).json({ error: "Invalid credentials." });
    }

    // Attach jobId to request for use in the actual endpoint logic
    req.jobId = jobId;
    next();
};

// --------------------------------
// ----- The Mailbox Endpoint -----
// --------------------------------
// This endpoint is protected by the registry module
app.get('/api/v1/commands', validateRegistry, (req, res) => {
    const { jobId } = req;

    // 1. Get current mail
    const messages = mailboxStore.get(jobId) || [];

    // 2. Filter out any that expired exactly now (edge case)
    const validMessages = messages.filter(msg => msg.expiresAt > Date.now());

    // 3. Clear the mailbox (Destructive Read)
    mailboxStore.delete(jobId);

    // 4. Return only the raw payloads to Roblox
    // This allows Roblox to receive [ {type: "Emote"...}, {type: "Emote"...} ]
    const payloads = validMessages.map(m => m.payload);

    res.json(payloads);
});

// --------------------------------------
// ----- The Verification Endpoints -----
// --------------------------------------
app.post('/api/v1/verify/generate', express.json(), generateCode);
app.post('/api/v1/verify/confirm', express.json(), verifyProfile);
app.post('/api/v1/verify/unverify', express.json(), unverifyUser);

// -----------------------------
// ----- The Echo Endpoint -----
// -----------------------------
// Rate limiter per real client IP (req.ip trusted)
// WARNING: Render free tier may be slow to startup as it spins down inactive services
app.use(
    '/echo',
    rateLimit({
        windowMs: 1000, 
        max: 5,
        // This satisfies the security check for IPv6
        keyGenerator: (req, res) => ipKeyGenerator(req, res)
    })
);
// Echo endpoint
app.post('/echo', async (req, res) => {
    const receivedText = req.body;
    const userKey = getTrustedIp(req);

    if (typeof receivedText !== 'string' || !receivedText.trim()) {
        return res.status(400).send('Invalid message');
    }

    try {
        const moderation = await enqueueMessage(receivedText);

        if (!moderation.allowed) {
            // IMPORTANT: do NOT log message contents if rejected
            console.log(`Message rejected from ${getTrustedIp(req)} (reason: ${moderation.reason})`);

            return res.status(403).json({
                status: "rejected",
                reason: moderation.reason,
                message: "Message not sent due to community guidelines or server limits."
            });
        }

        console.log(`Message received from ${getTrustedIp(req)}: ${receivedText}`);
        // Message is allowed → echo it
        res.send(receivedText);

    } catch (err) {
        console.error("Moderation error:", err.message);

        // Fail closed (safer)
        res.status(503).json({
            status: "error",
            message: "Message could not be processed.",
        });
    }
});

// --------------------------------------
// WebSocket connection handling
// Usage:
// Connect: wss://RobloxChatLauncher.onrender.com/
// Join: {"type": "join", "channelId": "c91feeaf-ef07-4a39-af05-a88032c358d2"}
// channelId is the ID found using the RobloxAreaService class (a.k.a. JobId)
// Chat: {"type": "message", "text": "Hello world!"}
// --------------------------------------
// Heatbeat mechanism to detect dead connections (e.g., due to network issues or Render spinning down)
const interval = setInterval(() => {
    wss.clients.forEach((ws) => {
        if (ws.isAlive === false) {
            // No pong received since last ping → dead connection
            // console.log(`Terminating dead client: ${userKey}`);
            return ws.terminate();
        }

        ws.isAlive = false;
        ws.ping(); // send ping frame
    });
}, Constants.HEARTBEAT_INTERVAL);

wss.on('close', () => clearInterval(interval));

wss.on('connection', (ws, req) => {
    const trustedIp = getTrustedIp(req, ws); // Get the 127.0.0.1:x IP address from the WebSocket connection
    const connectionPort = trustedIp.split(':')[1] || '0'; // Extract the port to return to use as the guest number

    // Identify this socket so we can find it by name later
    ws.senderName = `Guest ${connectionPort}`;

    const userKey = hashIp(getTrustedIp(req, ws));
    let currentChannel = null;

    ws.isAlive = true;
    ws.on('pong', () => { ws.isAlive = true; });

    // --- Begin WS message handling ---
    ws.on('message', async (data) => {
        try {
            const payload = JSON.parse(data);

            // 1. JOIN LOGIC: Creates channel on the fly if it doesn't exist
            if (payload.type === 'join') {
                const { channelId, hwid } = payload;
                const { getRobloxIdByHwid, getRobloxUsername } = require('./verification');

                // Only attempt database lookup if hwid was actually provided
                let robloxId = null;
                if (hwid) {
                    robloxId = await getRobloxIdByHwid(hwid);
                }

                if (robloxId) {
                    const username = await getRobloxUsername(robloxId);
                    ws.senderName = username;
                    ws.isVerified = true;
                } else {
                    // Fallback for users who aren't verified or didn't send an HWID
                    ws.senderName = `Guest ${connectionPort}`;
                    ws.isVerified = false;
                }

                // If user is already in another channel, remove them
                const previousChannel = userChannelMap.get(userKey);
                if (previousChannel && previousChannel !== channelId) {
                    const prevClients = channels.get(previousChannel);
                    if (prevClients) {
                        prevClients.delete(ws);
                        if (prevClients.size === 0) {
                            channels.delete(previousChannel);
                        }
                    }
                }

                // Join new channel
                if (!channels.has(channelId)) {
                    channels.set(channelId, new Set());
                }

                channels.get(channelId).add(ws);
                userChannelMap.set(userKey, channelId);
                currentChannel = channelId;
            }

            // 2. CHAT LOGIC: Moderates then broadcasts
            if (payload.type === 'message' && currentChannel) {
                const moderation = await enqueueMessage(payload.text);

                if (moderation.allowed) {
                    console.log(`Message received from ${getTrustedIp(req, ws)} on channel ${currentChannel}: ${payload.text}`);
                    broadcastToChannel(currentChannel, {
                        type: 'message',
                        text: payload.text,
                        sender: ws.senderName, // This will now be "RobloxUser:123" or "Guest 456"
                                               // Note that guest numbers are temporary and change on every reconnect, so they cannot be used to track users across sessions.
                                               //They are only for display purposes within a single session.
                        verified: ws.isVerified
                    });
                } else {
                    // IMPORTANT: do NOT log message contents if rejected
                    console.log(`Message rejected from ${getTrustedIp(req, ws)} on channel ${currentChannel} (reason: ${moderation.reason})`);
                    ws.send(JSON.stringify({
                        status: 'rejected',
                        reason: moderation.reason,
                        message: "Message not sent due to community guidelines or server limits."
                    }));
                }
            }

            // 3. WHISPER LOGIC
            if (payload.type === 'whisper' && currentChannel) {
                const moderation = await enqueueMessage(payload.text);
                if (moderation.allowed) {
                    const targetName = payload.target;
                    const clients = channels.get(currentChannel);
                    let found = false;

                    clients.forEach(client => {
                        // 1. Send to the Recipient
                        if (client.senderName === targetName) {
                            client.send(JSON.stringify({
                                type: 'message',
                                text: payload.text,
                                sender: `From ${ws.senderName}`
                            }));
                            found = true;
                        }
                    });

                    // 2. Send back to the Sender (You)
                    // This confirms the server processed it.
                    ws.send(JSON.stringify({
                        type: 'message',
                        text: payload.text,
                        sender: `To ${targetName}`
                    }));

                    if (!found) {
                        ws.send(JSON.stringify({
                            type: 'message',
                            sender: 'System',
                            text: `User ${targetName} not found in this channel.`
                        }));
                    }
                }
            }
        } catch (e) {
            console.error("WS Message Error:", e);
        }
    });
    // --- End WS message handling ---

    // Remove the user mapping on disconnect
    ws.on('close', () => {
        const channelId = userChannelMap.get(userKey);
        if (channelId && channels.has(channelId)) {
            const clients = channels.get(channelId);
            clients.delete(ws);
            if (clients.size === 0) {
                channels.delete(channelId);
            }
        }
        userChannelMap.delete(userKey);
    });
});

// Helper to send to everyone in a specific channel
function broadcastToChannel(channelId, data) {
    const clients = channels.get(channelId);
    if (clients) {
        const message = JSON.stringify(data);
        clients.forEach(client => {
            if (client.readyState === WebSocket.OPEN) {
                client.send(message);
            }
        });
    }
}

// Health check endpoint (Good practice for Render)
app.get('/health', (req, res) => {
    res.status(200).send('OK');
});

server.listen(Env.PORT, '0.0.0.0', () => {
    console.log(`Server listening on port ${Env.PORT}`);
});
