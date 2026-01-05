const express = require('express');
const app = express();
const axios = require('axios');

// Perspective users must register for access
// See: https://developers.perspectiveapi.com/s/docs-get-started?language=en_US
const PERSPECTIVE_API_KEY = process.env.PERSPECTIVE_API_KEY;

// Render and other PaaS providers usually use port 10000 by default
const PORT = process.env.PORT || 10000;
const rateLimit = require('express-rate-limit');

// Queue of messages to be filtered
const messageQueue = [];
let processing = false;
const MAX_QUEUE_SIZE = 100;

// Trust proxy (Render / Heroku / etc.)
app.set('trust proxy', 1); // trust only the first proxy hop (Render)

// Middleware to parse plain text bodies (sent by C# client)
// Limit messages to 10kb (more than enough for a chat message)
app.use(express.text({ limit: '10kb' }));

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
            doNotStore: true
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

// The Echo Endpoint
// Rate limiter per real client IP (req.ip trusted)
// WARNING: Render free tier may be slow to startup as it spins down inactive services
app.use(
    '/echo',
    rateLimit({
        windowMs: 1000, // 1 second
        max: 5,
        keyGenerator: (req, res) => req.ip // per-client IP
    })
);
// Echo endpoint
app.post('/echo', async (req, res) => {
    const receivedText = req.body;
    const fullChain = req.headers['x-forwarded-for'] || req.connection.remoteAddress;

    if (typeof receivedText !== 'string' || !receivedText.trim()) {
        return res.status(400).send('Invalid message');
    }

    try {
        const moderation = await enqueueMessage(receivedText);

        if (!moderation.allowed) {
            // IMPORTANT: do NOT log message contents
            console.log(`Message rejected from ${fullChain} (reason: ${moderation.reason})`);
            console.log(`Trusted Rate-limit IP: ${req.ip}`);

            return res.status(403).json({
                status: "rejected",
                reason: moderation.reason,
                message: "Message not sent due to community guidelines or server limits."
            });
        }

        console.log(`Received from ${fullChain}: ${receivedText}`);
        console.log(`Trusted Rate-limit IP: ${req.ip}`);
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

// Health check endpoint (Good practice for Render)
app.get('/health', (req, res) => {
    res.status(200).send('OK');
});

app.listen(PORT, '0.0.0.0', () => {
    console.log(`Echo server listening on port ${PORT}`);
});
