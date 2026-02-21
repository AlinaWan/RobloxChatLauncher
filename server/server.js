const crypto = require('crypto');

const express = require('express');
const http = require('http');
const WebSocket = require('ws');
const axios = require('axios');

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

// ----- WebSocket Setup -----
const server = http.createServer(app);
const wss = new WebSocket.Server({ server });
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

// ----- The Echo Endpoint -----
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
                const { channelId } = payload;

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
                        sender: `Guest ${connectionPort}` // Use the connection port as a simple guest identifier (since we don't have real user accounts)
                                                          // Note that this will change on every reconnection
                                                          // And the port may be reassigned to another user after they disconnect
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
