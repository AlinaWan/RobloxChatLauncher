const express = require('express');
const app = express();
const OpenAI = require("openai");

const openai = new OpenAI({
    apiKey: process.env.OPENAI_API_KEY,
});

// Render and other PaaS providers usually use port 10000 by default
const PORT = process.env.PORT || 10000;
const rateLimit = require('express-rate-limit');

// Trust proxy (Render / Heroku / etc.)
app.set('trust proxy', 1); // trust only the first proxy hop (Render)

// Middleware to parse plain text bodies (sent by C# client)
app.use(express.text());

// Using OpenAI Moderation API to filter messages
// See: https://platform.openai.com/docs/guides/moderation

/*
Future architecture:
    Depending on user preference, the following may be allowed or filtered:
        - Mild, non-toxic use of swearing/profanity (e.g., "damn", "hell", "holy shit this sucks", "what the fuck?")
        - Mild, non-prejudiced insults (e.g., "you're stupid", "I hate you", "you're annoying")
        - Mild, non-graphic sexual content (e.g., "that's hot", "I'm into you")
*/

// In the future, the user will be able to set preferences to block or see the content described above
function violatesPolicy(result) {
    const c = result.categories;
    const s = result.category_scores;

    // === HARD BLOCKS (never allowed) ===
    if (c["sexual/minors"]) return true;
    if (c["hate"] || c["hate/threatening"]) return true;
    if (c["violence/graphic"]) return true;
    if (c["self-harm"] || c["self-harm/intent"] || c["self-harm/instructions"]) return true;
    if (c["illicit"] || c["illicit/violent"]) return true;

    // === THREATS ===
    if (c["harassment/threatening"]) return true;

    // === SEXUAL CONTENT ===
    // Allow mild sexual content, block graphic / high-confidence sexual
    if (c["sexual"] && s["sexual"] > 0.85) return true;

    // === VIOLENCE (non-graphic) ===
    // Optional: allow mild mentions, block strong violence
    if (c["violence"] && s["violence"] > 0.75) return true;

    // Everything else is allowed:
    // - Swearing
    // - "I hate you"
    // - Mild insults
    // - Mild sexual references
    return false;
}

async function isMessageAllowed(text) {
    const response = await openai.moderations.create({
        model: "omni-moderation-latest",
        input: text,
    });

    const result = response.results[0];

    return {
        allowed: !violatesPolicy(result),
    };
}

// The Echo Endpoint
// Rate limiter per real client IP (req.ip trusted)
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
        const moderation = await isMessageAllowed(receivedText);

        if (!moderation.allowed) {
            // IMPORTANT: do NOT log message contents
            console.log(`Message rejected from ${fullChain}`);
            console.log(`Trusted Rate-limit IP: ${req.ip}`);

            return res.status(403).json({
                status: "rejected",
                reason: "moderation",
                message: "Message not sent due to community guidelines.",
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