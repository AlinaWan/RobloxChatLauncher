const express = require('express');
const app = express();

// Render and other PaaS providers usually use port 10000 by default
const PORT = process.env.PORT || 10000;
const rateLimit = require('express-rate-limit');

// Trust first proxy (Render / Heroku / etc.)
app.set('trust proxy', 1); // '1' = trust the first proxy in front of us

// Middleware to parse plain text bodies (sent by C# client)
app.use(express.text());

// The Echo Endpoint
app.use('/echo', rateLimit({ windowMs: 1000, max: 5 })); // Limit to 5 requests per second per IP
app.post('/echo', (req, res) => {
    const receivedText = req.body;
    console.log(`Received from ${req.ip}: ${receivedText}`);

    // Send the exact same text back
    res.send(receivedText);
});

// Health check endpoint (Good practice for Render)
app.get('/health', (req, res) => {
    res.status(200).send('OK');
});

app.listen(PORT, '0.0.0.0', () => {
    console.log(`Echo server listening on port ${PORT}`);
});