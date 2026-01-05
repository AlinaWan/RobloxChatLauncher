const express = require('express');
const app = express();

// Render and other PaaS providers usually use port 10000 by default
const PORT = process.env.PORT || 10000;
const rateLimit = require('express-rate-limit');

// Trust proxy (Render / Heroku / etc.)
app.set('trust proxy', 1); // trust only the first proxy hop (Render)

// Middleware to parse plain text bodies (sent by C# client)
app.use(express.text());

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
app.post('/echo', (req, res) => {
    const receivedText = req.body;

    // Print full X-Forwarded-For chain
    const fullChain = req.headers['x-forwarded-for'] || req.connection.remoteAddress;
    console.log(`Received from ${fullChain}: ${receivedText}`);

    // Print the trusted IP used for rate-limiting
    console.log(`Trusted Rate-limit IP: ${req.ip}`);

    res.send(receivedText);
});


// Health check endpoint (Good practice for Render)
app.get('/health', (req, res) => {
    res.status(200).send('OK');
});

app.listen(PORT, '0.0.0.0', () => {
    console.log(`Echo server listening on port ${PORT}`);
});