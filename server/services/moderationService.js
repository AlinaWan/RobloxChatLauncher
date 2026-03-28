const axios = require('axios');

const Env = require('../config/env');

// Using Perspective API to filter messages
// See: https://developers.perspectiveapi.com/s/about-the-api?language=en_US
// WARNING: Perspective API currently has a 1 QPS limit on free tier

// Perspective users must register for access
// See: https://developers.perspectiveapi.com/s/docs-get-started?language=en_US

// Map attributes
// See: https://developers.perspectiveapi.com/s/about-the-api-attributes-and-languages?language=en_US
const ATTRIBUTES = {
    TOXICITY: {},
    INSULT: {},
    PROFANITY: {},
    SEVERE_TOXICITY: {},
    IDENTITY_ATTACK: {},
    THREAT: {},
    SEXUALLY_EXPLICIT: {} // Experimental attribute
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
            `https://commentanalyzer.googleapis.com/v1alpha1/comments:analyze?key=${Env.PERSPECTIVE_API_KEY}`,
            payload,
            { timeout: 10000 }
        );

        console.dir(response.data, { depth: null }); // Print full response for debugging

        const scores = response.data.attributeScores;

        // === Custom policy logic ===
        // Block severe toxicity, threats, sexual explicit, etc.
        // The client will locally filter more strictly based on user preferences
        if (
            (scores.IDENTITY_ATTACK?.summaryScore?.value || 0) > 0.50 ||
            (scores.SEXUALLY_EXPLICIT?.summaryScore?.value || 0) > 0.60 ||
            (scores.SEVERE_TOXICITY?.summaryScore?.value || 0) > 0.60 ||
            (scores.THREAT?.summaryScore?.value || 0) > 0.70 ||
            (scores.TOXICITY?.summaryScore?.value || 0) > 0.80 ||
            (scores.INSULT?.summaryScore?.value || 0) > 0.80 ||
            (scores.PROFANITY?.summaryScore?.value || 0) > 0.95
        ) {
            return {
                allowed: false,
                reason: 'moderation'
            };
        }

        // Everything else is allowed
        return {
            allowed: true,
            attributeScores: scores
        };
    } catch (err) {
        console.error("Perspective API error:", err.message);
        return {
            allowed: false,
            reason: 'api_error'
        }; // Fail closed
    }
}

module.exports = { isMessageAllowed };
