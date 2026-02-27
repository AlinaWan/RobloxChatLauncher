const axios = require('axios');

const Env = require('../config/env');

// Using Perspective API to filter messages
// See: https://developers.perspectiveapi.com/s/about-the-api?language=en_US
// WARNING: Perspective API currently has a 1 QPS limit on free tier

// Perspective users must register for access
// See: https://developers.perspectiveapi.com/s/docs-get-started?language=en_US

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
            `https://commentanalyzer.googleapis.com/v1alpha1/comments:analyze?key=${Env.PERSPECTIVE_API_KEY}`,
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

module.exports = { isMessageAllowed };