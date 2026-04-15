const axios = require('axios');
const crypto = require('crypto');
const express = require('express');
const http = require('http');
const WebSocket = require('ws');
const swaggerUi = require('swagger-ui-express');
const swaggerJsdoc = require('swagger-jsdoc');

const { rateLimit, ipKeyGenerator } = require('express-rate-limit');

const Constants = require('./config/constants');
const Env = require('./config/env');
const { createApiKeyMiddleware } = require('./middleware/apiKeyAuth');
const { pool, initDatabase } = require('./db/postgresPool');
const { isMessageAllowed } = require('./services/moderationService');
const { getRobloxIdByHwid, getRobloxUsername, generateCode, verifyProfile, unverifyUser, checkLogin, upsertUser, removeUser } = require('./services/verification');
const { mailboxStore, pushToMailbox } = require('./services/mailboxService');
const { authenticateGameServer, getAllGames, upsertGame, removeGame } = require('./services/registry');

const swaggerOptions = {
    definition: {
        openapi: '3.0.0',
        info: {
            title: 'Roblox Chat Launcher API',
            version: '1.0.0'
        },
        servers: [{ url: 'https://RobloxChatLauncher.onrender.com' }],
        components: {
            securitySchemes: {
                AdminAuth: {
                    type: 'http',
                    scheme: 'bearer',
                    bearerFormat: 'Opaque'
                },
                RegistryAuth: {
                    type: 'apiKey',
                    in: 'header',
                    name: 'x-api-key'
                },
                HwidAuth: {
                    type: 'apiKey',
                    in: 'header',
                    name: 'x-hwid'
                }
            }
        },
        tags: [
            { name: "Admin" },
            { name: "Universe" },
            {name: "Verified"},
            { name: "Public" }
        ]
    },
    apis: ['./server.js'],
};

// Allowed mail types for the mail push endpoint
const ALLOWED_MAIL_TYPES = new Set([
    "Emote",
]);

// ----- Express App Setup -----
const app = express();
// Trust proxy (Render / Heroku / etc.)
// Always use proxy from Render as the trusted IP
app.set('trust proxy', 1); // trust only the first proxy hop (Render)
// Middleware to parse plain text bodies (sent by C# client)
// Limit messages to 1kb (more than enough for a chat message)
app.use(express.text({ limit: Constants.TEXT_LIMIT_BYTES }));

// ----- Swagger Setup -----
const swaggerDocs = swaggerJsdoc(swaggerOptions);
app.use('/api-docs', swaggerUi.serve, swaggerUi.setup(swaggerDocs));

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
            resolve({
                allowed: false,
                reason: result.reason || "moderation"
            });
        } else {
            resolve({
                allowed: true,
                attributeScores: result.attributeScores || null
            });
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
const validateAdmin = createApiKeyMiddleware([
    Env.RCL_ADMIN_KEY
]);

const validateWrite = createApiKeyMiddleware([
    Env.RCL_ADMIN_KEY,
    Env.RCL_WRITE_KEY
]);

const validateRead = createApiKeyMiddleware([
    Env.RCL_ADMIN_KEY,
    Env.RCL_WRITE_KEY,
    Env.RCL_READ_KEY
]);

// -----------------------
// --- Admin Endpoints ---
// -----------------------
// ------- Registry ------
// 1. List all registered universes
/**
 * @openapi
 * /api/v1/admin/registry:
 *   get:
 *     summary: List all registered universes
 *     description: Returns every universe currently registered with the chat launcher registry.
 *     tags: [Admin]
 *     security:
 *       - AdminAuth: []
 *     responses:
 *       200:
 *         description: List of registered universes
 *         content:
 *           application/json:
 *             schema:
 *               type: array
 *               items:
 *                 type: object
 *                 properties:
 *                   universeId:
 *                     type: integer
 *                     example: 123456
 *                   apiKey:
 *                     type: string
 *                     example: "secret"
 *       500:
 *         description: Server error
 */
app.get('/api/v1/admin/registry', validateRead, async (req, res) => {
    try {
        const games = await getAllGames();
        res.json(games);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 2. Add or Update a game (JSON Body: { "universeId": 123, "apiKey": "secret" })
/**
 * @openapi
 * /api/v1/admin/registry:
 *   post:
 *     summary: Add or update a registered universe
 *     description: Registers a new universe or updates the API key of an existing one.
 *     tags: [Admin]
 *     security:
 *       - AdminAuth: []
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - universeId
 *               - apiKey
 *             properties:
 *               universeId:
 *                 type: integer
 *                 example: 123456
 *               apiKey:
 *                 type: string
 *                 example: "secret"
 *     responses:
 *       200:
 *         description: Universe successfully added or updated
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 status:
 *                   type: string
 *                   example: success
 *                 message:
 *                   type: string
 *                   example: Universe 123456 updated.
 *       400:
 *         description: Missing required data
 *       500:
 *         description: Server error
 */
app.post('/api/v1/admin/registry', express.json(), validateWrite, async (req, res) => {
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
/**
 * @openapi
 * /api/v1/admin/registry/{id}:
 *   delete:
 *     summary: Delete a registered universe
 *     description: Removes a universe from the registry.
 *     tags: [Admin]
 *     security:
 *       - AdminAuth: []
 *     parameters:
 *       - in: path
 *         name: id
 *         required: true
 *         description: Universe ID to remove
 *         schema:
 *           type: integer
 *           example: 123456
 *     responses:
 *       200:
 *         description: Universe successfully deleted
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 status:
 *                   type: string
 *                   example: deleted
 *       500:
 *         description: Server error
 */
app.delete('/api/v1/admin/registry/:id', validateAdmin, async (req, res) => {
    try {
        await removeGame(req.params.id);
        res.json({ status: "deleted" });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// ------- Verified ------
// 1. List all verified users
/**
 * @openapi
 * /api/v1/admin/verified:
 *   get:
 *     summary: List all verified users
 *     description: Returns every verified user and the Roblox account linked to their HWID.
 *     tags: [Admin]
 *     security:
 *       - AdminAuth: []
 *     responses:
 *       200:
 *         description: List of verified users
 *         content:
 *           application/json:
 *             schema:
 *               type: array
 *               items:
 *                 type: object
 *                 properties:
 *                   hwid:
 *                     type: string
 *                     example: "ABC123-XYZ789"
 *                   roblox_id:
 *                     type: integer
 *                     example: 12345678
 *       500:
 *         description: Database or server error
 */
app.get('/api/v1/admin/verified', validateRead, async (req, res) => {
    try {
        const result = await pool.query('SELECT hwid, roblox_id FROM verified_users ORDER BY roblox_id');
        res.json(result.rows); // Returns an array of objects { hwid, roblox_id }
    } catch (err) {
        console.error("Admin DB List Error:", err);
        res.status(500).json({ error: err.message });
    }
});


// 2. Add or Update a user (JSON Body: { "hwid": "...", "robloxId": 12345 })
/**
 * @openapi
 * /api/v1/admin/verified:
 *   post:
 *     summary: Add or update a verified user
 *     description: Links a hardware ID (HWID) to a Roblox account ID or updates the existing link.
 *     tags: [Admin]
 *     security:
 *       - AdminAuth: []
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - hwid
 *               - robloxId
 *             properties:
 *               hwid:
 *                 type: string
 *                 example: "ABC123-XYZ789"
 *               robloxId:
 *                 type: integer
 *                 example: 12345678
 *     responses:
 *       200:
 *         description: User successfully added or updated
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 status:
 *                   type: string
 *                   example: success
 *                 message:
 *                   type: string
 *                   example: User with HWID ABC123-XYZ789 linked to 12345678.
 *       400:
 *         description: Missing HWID or Roblox ID
 *       500:
 *         description: Server error
 */
app.post('/api/v1/admin/verified', express.json(), validateWrite, async (req, res) => {
    const { hwid, robloxId } = req.body;
    if (!hwid || !robloxId) return res.status(400).send("Missing hwid or robloxId");

    try {
        await upsertUser(hwid, robloxId);
        res.json({ status: "success", message: `User with HWID ${hwid} linked to ${robloxId}.` });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 3. Delete a user by HWID
/**
 * @openapi
 * /api/v1/admin/verified/{hwid}:
 *   delete:
 *     summary: Delete a verified user
 *     description: Removes a verified user entry using their hardware ID.
 *     tags: [Admin]
 *     security:
 *       - AdminAuth: []
 *     parameters:
 *       - in: path
 *         name: hwid
 *         required: true
 *         description: Hardware ID of the verified user
 *         schema:
 *           type: string
 *           example: "ABC123-XYZ789"
 *     responses:
 *       200:
 *         description: Verified user removed
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 status:
 *                   type: string
 *                   example: deleted
 *                 message:
 *                   type: string
 *                   example: HWID ABC123-XYZ789 removed from verified users.
 *       500:
 *         description: Server error
 */
app.delete('/api/v1/admin/verified/:hwid', validateAdmin, async (req, res) => {
    const { hwid } = req.params;
    try {
        await removeUser(hwid);
        res.json({ status: "deleted", message: `HWID ${hwid} removed from verified users.` });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// ------ Global Broadcast ------
/**
 * @openapi
 * /api/v1/admin/broadcast:
 *   post:
 *     summary: Send a broadcast message
 *     description: Sends a message to all connected clients or to a specific channel.
 *     tags: [Admin]
 *     security:
 *       - AdminAuth: []
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - text
 *             properties:
 *               text:
 *                 type: string
 *                 description: Message text to broadcast
 *                 example: "Server maintenance in 5 minutes."
 *               sender:
 *                 type: string
 *                 description: Display name of the message sender
 *                 example: "Riri"
 *               color:
 *                 type: string
 *                 description: Message color (hex code, System.Drawing.Color name, or null for Roblox default)
 *                 example: "HotPink"
 *               verified:
 *                 type: boolean
 *                 description: Whether the sender should appear verified
 *                 example: true
 *               isBroadcast:
 *                 type: boolean
 *                 description: Whether the message should be styled as a broadcast
 *                 example: true
 *               target:
 *                 type: object
 *                 description: Optional targeting options
 *                 properties:
 *                   channelId:
 *                     type: string
 *                     description: Channel ID to send the message to instead of all channels
 *                     example: "main"
 *     responses:
 *       200:
 *         description: Broadcast sent successfully
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 status:
 *                   type: string
 *                   example: success
 *                 stats:
 *                   type: object
 *                   properties:
 *                     totalRecipients:
 *                       type: integer
 *                       example: 42
 *                     totalChannels:
 *                       type: integer
 *                       example: 3
 *                     targeted:
 *                       type: boolean
 *                       example: false
 *       400:
 *         description: Missing message text
 *       500:
 *         description: Server error
 */
app.post('/api/v1/admin/broadcast', express.json(), validateWrite, async (req, res) => {
    const { text, sender, color, verified, isBroadcast, target } = req.body;

    if (!text) {
        return res.status(400).json({ error: "Missing 'text' field in JSON body" });
    }

    const targetType = target?.channelId
        ? `to channel ${target.channelId}`
        : "globally";

    console.log(`[Admin::Broadcast] Broadcast sent ${targetType}: "${text}"`);

    const payload = JSON.stringify({
        type: 'message',
        text: text,
        sender: sender || "Global Broadcast",
        color: color || null, // Can be a hex code, System.Drawing.Color name, or null for Roblox default (client side will handle this)
        verified: verified ?? true,
        isBroadcast: isBroadcast ?? true // If false, the formatting will be the same as a normal message and color override will be ignored
    });

    let recipientCount = 0;
    const channelCount = channels.size;

    // If a target channelId is provided, send only to that channel
    if (target?.channelId) {
        const clients = channels.get(target.channelId);
        if (clients) {
            clients.forEach(client => {
                if (client.readyState === WebSocket.OPEN) {
                    try {
                        client.send(payload);
                        recipientCount++;
                    } catch (err) {
                        console.error("[Admin::Broadcast] Channel broadcast send failed:", err);
                    }
                }
            });
        }
    } else {
        // Send to every client in every channel
        channels.forEach((clients) => {
            clients.forEach(client => {
                if (client.readyState === WebSocket.OPEN) {
                    try {
                        client.send(payload);
                        recipientCount++;
                    } catch (err) {
                        console.error("[Admin::Broadcast] Global broadcast send failed:", err);
                    }
                }
            });
        });
    }

    res.json({
        status: "success",
        stats: {
            totalRecipients: recipientCount,
            totalChannels: channelCount,
            targeted: !!target // Returns true if a target object was used
        }
    });
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

    // Attach jobId and universeId to request for use in the actual endpoint logic
    req.jobId = jobId;
    req.universeId = universeId;
    next();
};

// --------------------------------
// ----- The Mailbox Endpoint -----
// --------------------------------
// This endpoint is protected by the registry module
/**
 * @openapi
 * /api/v1/commands:
 *   get:
 *     summary: Retrieve pending commands/messages for a universe job
 *     description: >
 *       Returns all queued mail for a specific universe/job combination.
 *       Only messages that have not expired will be returned. After retrieval, the mailbox
 *       for this universe/job pair is cleared automatically.
 *     tags: [Universe]
 *     security:
 *       - ApiKeyAuth: []
 *     parameters:
 *       - in: header
 *         name: x-universe-id
 *         required: true
 *         description: Universe ID for authentication
 *         schema:
 *           type: integer
 *           example: 987654321
 *       - in: header
 *         name: x-api-key
 *         required: true
 *         description: API key for the universe
 *         schema:
 *           type: string
 *           example: "secret_api_key"
 *       - in: header
 *         name: x-job-id
 *         required: true
 *         description: Job ID of the server instance
 *         schema:
 *           type: string
 *           example: "1234567890"
 *     responses:
 *       200:
 *         description: Array of queued mail payloads
 *         content:
 *           application/json:
 *             schema:
 *               type: array
 *               items:
 *                 type: object
 *                 properties:
 *                   type:
 *                     type: string
 *                     example: Emote
 *                   targetPlayer:
 *                     type: string
 *                     example: "12345"
 *                   data:
 *                     type: object
 *                     example:
 *                       name: "Dance"
 *       401:
 *         description: Missing identity headers
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 error:
 *                   type: string
 *                   example: Missing identity headers.
 *       403:
 *         description: Invalid universe credentials
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 error:
 *                   type: string
 *                   example: Invalid credentials.
 *       500:
 *         description: Server error
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 error:
 *                   type: string
 *                   example: Server error while retrieving mailbox.
 */
app.get('/api/v1/commands', validateRegistry, (req, res) => {
    // Extract universeId and jobId from the request (attached by validateRegistry)
    const { universeId, jobId } = req;

    // Construct the private storage key
    const storageKey = `${universeId}:${jobId}`;

    // 1. Get mail specifically for THIS universe + jobId combo
    const messages = mailboxStore.get(storageKey) || [];

    const validMessages = messages.filter(msg => msg.expiresAt > Date.now());

    // 2. Clear ONLY this specific mailbox
    mailboxStore.delete(storageKey);

    const payloads = validMessages.map(m => m.payload);
    res.json(payloads);
});

// ----- Verified User Middleware -----
const validateVerifiedUser = async (req, res, next) => {
    const hwid = req.headers['x-hwid'];

    if (!hwid) {
        return res.status(401).json({ error: "Missing HWID header." });
    }

    try {
        const robloxId = await getRobloxIdByHwid(hwid);

        if (!robloxId) {
            return res.status(403).json({ error: "User not verified." });
        }

        // Attach verified identity to request
        req.robloxId = robloxId;
        req.hwid = hwid;

        next();
    } catch (err) {
        console.error("Verified middleware error:", err);
        res.status(500).json({ error: "Verification failed." });
    }
};

// ----------------------------------
// ----- The Mail Push Endpoint -----
// ----------------------------------
/**
 * @openapi
 * /api/v1/mail:
 *   post:
 *     summary: Queue a mail/message for a universe job
 *     description: >
 *       Allows a verified user to push a mail payload to a specific universe and jobId.
 *       After validation, the message is queued for delivery.
 *     tags: [Verified]
 *     security:
 *       - HwidAuth: []
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - jobId
 *               - universeId
 *               - type
 *             properties:
 *               jobId:
 *                 type: string
 *                 description: The Job ID of the target universe instance
 *                 example: "1234567890"
 *               universeId:
 *                 type: integer
 *                 description: Universe ID the mail belongs to
 *                 example: 987654321
 *               type:
 *                 type: string
 *                 description: Type of mail being sent
 *                 enum: ["Emote"]
 *                 example: "Emote"
 *               data:
 *                 type: object
 *                 description: Optional additional data for the mail payload
 *                 example:
 *                   name: "Wave"
 *     responses:
 *       200:
 *         description: Mail successfully queued
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 status:
 *                   type: string
 *                   example: queued
 *                 jobId:
 *                   type: string
 *                   example: "1234567890"
 *                 universeId:
 *                   type: integer
 *                   example: 987654321
 *       400:
 *         description: Missing required fields or invalid mail type
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 error:
 *                   type: string
 *                   example: Missing required fields.
 *       500:
 *         description: Server error while queuing mail
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 error:
 *                   type: string
 *                   example: Failed to queue mail.
 */
app.post(
    '/api/v1/mail',
    express.json(),
    validateVerifiedUser,
    async (req, res) => {
        // Expecting universeId from the body to verify jobId belongs to the same universe
        const { jobId, universeId, type, data } = req.body;

        // 1. Validate required fields
        if (!jobId || !universeId || !type) {
            return res.status(400).json({ error: "Missing required fields." });
        }

        // 2. Validate allowed mail type
        if (!ALLOWED_MAIL_TYPES.has(type)) {
            return res.status(400).json({ error: "Invalid mail type." });
        }

        // 3. Construct the payload
        const payload = {
            type,
            targetPlayer: req.robloxId, // The sender
            data: data || {}
        };

        try {
            // Bind the mail to the universeId provided by the sender
            pushToMailbox(universeId, jobId, payload);

            console.log(`[MAIL] Sent to Universe ${universeId} | Job ${jobId}`);
            res.json({
                status: "queued",
                jobId,
                universeId
            });
        } catch (err) {
            console.error("Mail push error:", err);
            res.status(500).json({ error: "Failed to queue mail." });
        }
    }
);

// --------------------------------------
// ----- The Verification Endpoints -----
// --------------------------------------
/**
 * @openapi
 * /api/v1/verify/generate:
 *   post:
 *     summary: Generate a verification code for a Roblox username
 *     description: Creates a temporary verification code that the user must place in their Roblox profile description to verify ownership.
 *     tags: [Public]
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - robloxUsername
 *             properties:
 *               robloxUsername:
 *                 type: string
 *                 example: "Riri"
 *     responses:
 *       200:
 *         description: Verification code generated successfully
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 code:
 *                   type: string
 *                   example: "RCL-AB12CD"
 *                 robloxId:
 *                   type: integer
 *                   example: 12345678
 *       400:
 *         description: Missing username
 *       404:
 *         description: Roblox user not found
 *       500:
 *         description: API error or server error
 */
app.post('/api/v1/verify/generate', express.json(), generateCode);
/**
 * @openapi
 * /api/v1/verify/confirm:
 *   post:
 *     summary: Confirm a pending verification
 *     description: >
 *       Checks the Roblox profile description for the generated code. If the code is found, the HWID is linked to the Roblox account.
 *     tags: [Public]
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - robloxId
 *               - hwid
 *             properties:
 *               robloxId:
 *                 type: integer
 *                 example: 12345678
 *               hwid:
 *                 type: string
 *                 example: "ABC123-XYZ789"
 *     responses:
 *       200:
 *         description: Verification successful
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 success:
 *                   type: boolean
 *                   example: true
 *       400:
 *         description: Code not found in profile or expired
 *       500:
 *         description: Verification failed or server error
 */
app.post('/api/v1/verify/confirm', express.json(), verifyProfile);
/**
 * @openapi
 * /api/v1/verify/unverify:
 *   post:
 *     summary: Remove a verified user
 *     description: Deletes the HWID-Roblox account link, un-verifying the user.
 *     tags: [Verified]
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - hwid
 *             properties:
 *               hwid:
 *                 type: string
 *                 example: "ABC123-XYZ789"
 *     responses:
 *       200:
 *         description: Account unlinked successfully
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 success:
 *                   type: boolean
 *                   example: true
 *                 message:
 *                   type: string
 *                   example: "Account unlinked successfully"
 *       400:
 *         description: Missing HWID
 *       404:
 *         description: No matching record found
 *       500:
 *         description: Server error
 */
app.post('/api/v1/verify/unverify', express.json(), unverifyUser);
/**
 * @openapi
 * /api/v1/verify/login:
 *   post:
 *     summary: Check if a HWID is verified
 *     description: Returns the linked Roblox ID if the HWID is verified.
 *     tags: [Verified]
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - hwid
 *             properties:
 *               hwid:
 *                 type: string
 *                 example: "ABC123-XYZ789"
 *     responses:
 *       200:
 *         description: HWID is verified
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 success:
 *                   type: boolean
 *                   example: true
 *                 robloxId:
 *                   type: integer
 *                   example: 12345678
 *       401:
 *         description: HWID is not verified
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 success:
 *                   type: boolean
 *                   example: false
 *       500:
 *         description: Server error
 */
app.post('/api/v1/verify/login', express.json(), checkLogin);

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
/**
 * @openapi
 * /echo:
 *   post:
 *     summary: Echo a moderated message
 *     description: >
 *       Accepts a plain text message and returns it if it passes moderation checks.
 *       Messages are evaluated using Google's Perspective moderation API. If the message
 *       violates moderation policies or server limits, it will be rejected.
 *
 *       This endpoint is rate limited to **5 requests per second per IP**.
 *
 *     tags: [Public]
 *     requestBody:
 *       required: true
 *       content:
 *         text/plain:
 *           schema:
 *             type: string
 *             example: "Hello world!"
 *     responses:
 *       200:
 *         description: Message accepted and echoed back
 *         content:
 *           text/plain:
 *             schema:
 *               type: string
 *               example: "Hello world!"
 *       400:
 *         description: Invalid or empty message
 *         content:
 *           text/plain:
 *             schema:
 *               type: string
 *               example: "Invalid message"
 *       403:
 *         description: Message rejected by moderation policy
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 status:
 *                   type: string
 *                   example: rejected
 *                 reason:
 *                   type: string
 *                   example: moderation
 *                 message:
 *                   type: string
 *                   example: Message not sent due to community guidelines or server limits.
 *       503:
 *         description: Moderation service unavailable
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 status:
 *                   type: string
 *                   example: error
 *                 message:
 *                   type: string
 *                   example: Message could not be processed.
 */
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

    // Handle oversized messages so the server doesn't crash
    ws.on('error', (err) => {
        if (err.code === 'WS_ERR_UNSUPPORTED_MESSAGE_LENGTH') {
            console.warn('Oversized message dropped');
        } else {
            console.error('WS error:', err);
        }
    });

    // --- Begin WS message handling ---
    ws.on('message', async (data) => {
        try {
            const payload = JSON.parse(data);

            // 1. JOIN LOGIC: Creates channel on the fly if it doesn't exist
            if (payload.type === 'join') {
                const { channelId, hwid } = payload;

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
                        verified: ws.isVerified,
                        attributeScores: moderation.attributeScores
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

                    const whisperPayload = {
                        type: 'whisper',
                        text: payload.text,
                        sender: ws.senderName,
                        target: targetName,
                        attributeScores: moderation.attributeScores
                    };

                    // 1. Send to the Recipient (isTo = false)
                    clients.forEach(client => {
                        if (client.senderName === targetName) {
                            client.send(JSON.stringify({ ...whisperPayload, isTo: false }));
                            found = true;
                        }
                    });

                    // 2. Send back to the Sender (isTo = true)
                    ws.send(JSON.stringify({ ...whisperPayload, isTo: true }));

                    if (!found) {
                        ws.send(JSON.stringify({
                            status: 'rejected',
                            reason: 'not_found',
                            target: targetName
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

process.on("SIGTERM", () => {
    clearInterval(interval);
    wss.clients.forEach(ws => {
        ws.close(1001, "Going Away");
    });
    wss.close();
});

// Health check endpoint (Good practice for Render)
/**
 * @openapi
 * /health:
 *   get:
 *     summary: Service health check
 *     description: >
 *       Returns a simple OK response to indicate the server is running.
 *       This endpoint is typically used by hosting platforms or load balancers
 *       to verify service availability.
 *     tags: [Public]
 *     responses:
 *       200:
 *         description: Server is healthy
 *         content:
 *           text/plain:
 *             schema:
 *               type: string
 *               example: OK
 */
app.get('/health', (req, res) => {
    res.status(200).send('OK');
});

server.listen(Env.PORT, '0.0.0.0', () => {
    console.log(`Server listening on port ${Env.PORT}`);
});
