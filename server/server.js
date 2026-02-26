/*
Privacy Note for Reviewers / Users:

What the server sees:
- Your messages
- The channel/server instance they are sent on (for routing purposes)

What the server does NOT see:
- Your public IP address, even as a hashed string.
  (Render, the hosting provider, sees your public IP as a fundamental requirement of standard internet communication, but we do not log it or have access to see it)
- Any permanent identifiers (not even pseudoanonymous ones).
  The server only sees a temporary guest label based on the WebSocket connection port, which changes on every reconnect.

- Guest labels change on every reconnect
  (you can verify this by running `/rc` on the client, which reconnects the WebSocket and will assign you a new guest number)
- Ports may be reused across sessions

This is intended to give users and reviewers peace of mind regarding privacy.

EXAMPLE OF WHAT IS LOGGED:
Message received from 127.0.0.1:59938 on channel c4d2c979-b7f4-453d-b78e-1eab07fda058: Hello World!
Message received from 127.0.0.1:59938 on channel c4d2c979-b7f4-453d-b78e-1eab07fda058: I'm testing the chat server.
Message received from 127.0.0.1:59938 on channel c4d2c979-b7f4-453d-b78e-1eab07fda058: What's up?
Message received from 127.0.0.1:42668 on channel c4d2c979-b7f4-453d-b78e-1eab07fda058: I'm another user with a different guest number!
Message received from 127.0.0.1:42668 on channel c4d2c979-b7f4-453d-b78e-1eab07fda058: But on the same channel!
Message received from 127.0.0.1:56198 on channel c4d2c979-b7f4-453d-b78e-1eab07fda058: Yet another user on the same channel with a different guest number!
Message received from 127.0.0.1:45768 on channel d3ea7bfa-c43a-49cd-8dae-24614c34d15e: New channel, new user!
Message received from 127.0.0.1:45768 on channel d3ea7bfa-c43a-49cd-8dae-24614c34d15e: This is a different channel than the previous messages!

All logs are automatically deleted by Render after 7 days and are irrecoverable by us.
We do not persist your logged messages outside of Render's standard logging system.
Also see the Privacy Policy at ./PRIVACY
*/
const crypto = require('crypto');

const express = require('express');
const http = require('http');
const WebSocket = require('ws');
const axios = require('axios');

const { authenticateGameServer } = require('./registry');
const { getAllGames, upsertGame, removeGame } = require('./registry');
const RCL_ADMIN_KEY = process.env.RCL_ADMIN_KEY;

const { generateCode, verifyProfile, unverifyUser } = require('./verification');

// ----- Express App Setup -----
const app = express();
// Trust proxy (Render / Heroku / etc.)
// Always use proxy from Render as the trusted IP
app.set('trust proxy', 1); // trust only the first proxy hop (Render)
// Middleware to parse plain text bodies (sent by C# client)
// Limit messages to 1kb (more than enough for a chat message)
app.use(express.text({ limit: '1kb' }));

// ----- Environment Variables -----
// Perspective users must register for access
// See: https://developers.perspectiveapi.com/s/docs-get-started?language=en_US
const PERSPECTIVE_API_KEY = process.env.PERSPECTIVE_API_KEY;
const USER_SALT = process.env.USER_SALT;

// ----- PostgreSQL Setup -----
const { pool, initDatabase } = require('./postgresPool');

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
    maxPayload: 1024 // 1 KB limit
});
// Memory-efficient storage for dynamic channels
// Key: channelId (string), Value: Set of socket objects
const channels = new Map();

// Render and other PaaS providers usually use port 10000 by default
const PORT = process.env.PORT || 10000;
const { rateLimit, ipKeyGenerator } = require('express-rate-limit');

// Queue of messages to be filtered
const messageQueue = [];
let processing = false;
const MAX_QUEUE_SIZE = 100;

// Memory storage for mailboxes
// Key: jobId, Value: Array of { payload: object, expiresAt: number }
const mailboxStore = new Map();

const MAIL_TTL_MS = 5000; // seconds before mail is auto-deleted

/**
 * Adds mail to a specific server's mailbox
 * @param {string} jobId - The Roblox JobId
 * @param {object|array} data - The payload (Emote, etc.)
 */
function pushToMailbox(jobId, data) {
    if (!mailboxStore.has(jobId)) {
        mailboxStore.set(jobId, []);
    }

    const expiresAt = Date.now() + MAIL_TTL_MS;
    const mailbox = mailboxStore.get(jobId);

    // If data is an array, push each item, otherwise push the single object
    if (Array.isArray(data)) {
        data.forEach(item => mailbox.push({ payload: item, expiresAt }));
    } else {
        mailbox.push({ payload: data, expiresAt });
    }
}

// Automatic Cleanup: Runs every 5 seconds to clear expired mail
setInterval(() => {
    const now = Date.now();
    for (const [jobId, messages] of mailboxStore.entries()) {
        const freshMessages = messages.filter(msg => msg.expiresAt > now);
        if (freshMessages.length === 0) {
            mailboxStore.delete(jobId);
        } else {
            mailboxStore.set(jobId, freshMessages);
        }
    }
}, 5000);

function hashIp(ip) {
    return crypto.createHash('sha256').update(ip + '||' + USER_SALT).digest('hex');
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
        if (messageQueue.length >= MAX_QUEUE_SIZE) {
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

// Using Perspective API to filter messages
// See: https://developers.perspectiveapi.com/s/about-the-api?language=en_US
// WARNING: Perspective API currently has a 1 QPS limit on free tier

/*
Future architecture:
    Depending on user preference, the following may be allowed or filtered:
        - Mild, non-toxic use of swearing/profanity (e.g., "damn", "hell", "holy shit this sucks", "what the fuck?")
        - Mild, non-prejudiced insults (e.g., "you're stupid", "I hate you", "you're annoying")
        - Mild, non-graphic sexual content (e.g., "that's hot", "I'm into you")
*/

// In the future, the user will be able to set preferences to block or see the content described above
// Map attributes
// See: https://developers.perspectiveapi.com/s/about-the-api-attributes-and-languages?language=en_US
const ATTRIBUTES = {
    TOXICITY: {},
    INSULT: {},
    PROFANITY: {},
    SEVERE_TOXICITY: {},
    IDENTITY_ATTACK: {},
    THREAT: {},
    SEXUALLY_EXPLICIT: {}, // Experimental attribute
    FLIRTATION: {} // Experimental attribute 
};

// See: https://developers.perspectiveapi.com/s/about-the-api-methods?language=en_US
async function isMessageAllowed(text) {
    try {
        const payload = {
            comment: { text, type: "PLAIN_TEXT" },
            requestedAttributes: ATTRIBUTES,
            languages: ["en"],
            doNotStore: true // Important: Tells Google not to store chat logs for training
        };

        const response = await axios.post(
            `https://commentanalyzer.googleapis.com/v1alpha1/comments:analyze?key=${PERSPECTIVE_API_KEY}`,
            payload,
            { timeout: 10000 }
        );

        console.dir(response.data, { depth: null }); // Print full response for debugging

        const scores = response.data.attributeScores;

        // === Custom policy logic ===
        // Block severe toxicity, threats, sexual explicit, etc.
        if (
            (scores.IDENTITY_ATTACK?.summaryScore?.value || 0) > 0.70 ||
            (scores.SEVERE_TOXICITY?.summaryScore?.value || 0) > 0.75 ||
            (scores.THREAT?.summaryScore?.value || 0) > 0.75 ||
            (scores.TOXICITY?.summaryScore?.value || 0) > 0.85 ||
            (scores.SEXUALLY_EXPLICIT?.summaryScore?.value || 0) > 0.85 ||
            (scores.FLIRTATION?.summaryScore?.value || 0) > 0.85 ||

             // More lenient on insults, profanity, etc., but still block extreme cases
             // Again, users will be able to set their own preferences in the future
             // And block these categories wholly if they want
             (scores.INSULT?.summaryScore?.value || 0) > 0.90 ||
             (scores.PROFANITY?.summaryScore?.value || 0) > 0.95
        ) {
            return { allowed: false };
        }

        // Everything else is allowed
        return { allowed: true };
    } catch (err) {
        console.error("Perspective API error:", err.message);
        return { allowed: false }; // Fail closed
    }
}

// --- Admin Authorization Middleware ---
const validateAdmin = (req, res, next) => {
    const authHeader = req.headers['authorization'];
    if (!authHeader || authHeader !== `Bearer ${RCL_ADMIN_KEY}`) {
        return res.status(401).json({ error: "Unauthorized" });
    }
    next();
};

// --------------------------------
// --- Admin Registry Endpoints ---
// --------------------------------
// 1. List all registered universes
app.get('/api/admin/registry', validateAdmin, async (req, res) => {
    try {
        const games = await getAllGames();
        res.json(games);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 2. Add or Update a game (JSON Body: { "universeId": 123, "apiKey": "secret" })
app.post('/api/admin/registry', express.json(), validateAdmin, async (req, res) => {
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
app.delete('/api/admin/registry/:id', validateAdmin, async (req, res) => {
    try {
        await removeGame(req.params.id);
        res.json({ status: "deleted" });
    } catch (err) {
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
app.post('/api/verify/generate', express.json(), generateCode);
app.post('/api/verify/confirm', express.json(), verifyProfile);
app.post('/api/verify/unverify', express.json(), unverifyUser);

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
// Connect: wss://RobloxChatLauncherDemo.onrender.com/
// Join: {"type": "join", "channelId": "c91feeaf-ef07-4a39-af05-a88032c358d2"}
// channelId is the ID found using the RobloxAreaService class (a.k.a. JobId)
// Chat: {"type": "message", "text": "Hello world!"}
// --------------------------------------
// Heartbeat interval
const HEARTBEAT_INTERVAL = 30_000;

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
}, HEARTBEAT_INTERVAL);

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

server.listen(PORT, '0.0.0.0', () => {
    console.log(`Server listening on port ${PORT}`);
});
