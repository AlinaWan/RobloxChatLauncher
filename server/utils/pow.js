const crypto = require('crypto');

const Constants = require('../config/constants');

class PoW {
    constructor(baseDifficulty = 4, maxDifficulty = 7, ttlMs = 5 * 60 * 1000) {
        this.baseDifficulty = baseDifficulty;
        this.maxDifficulty = maxDifficulty;
        this.ttlMs = ttlMs;
        this.issuedChallenges = new Map(); // seed -> { expiresAt, difficulty }

        // Traffic Tracking
        this.requestHistory = new Map(); // ip -> timestamps[]

        setInterval(() => {
            const now = Date.now();
            for (const [seed, { expiresAt }] of this.issuedChallenges.entries()) {
                if (now > expiresAt) {
                    this.issuedChallenges.delete(seed);
                }
            }
        }, 60000).unref();

        setInterval(() => {
            const now = Date.now();
            for (const [ip, history] of this.requestHistory.entries()) {
                const filtered = history.filter(ts => now - ts < 60000);
                if (filtered.length === 0) {
                    this.requestHistory.delete(ip);
                } else {
                    this.requestHistory.set(ip, filtered);
                }
            }
        }, 60000).unref();
    }

    // Calculate difficulty based on requests in the last 60 seconds
    getDynamicDifficulty(ip) {
        const now = Date.now();

        if (!this.requestHistory.has(ip))
            this.requestHistory.set(ip, []);

        const history = this.requestHistory.get(ip);

        // Clean up history older than 1 minute
        const filtered = history.filter(ts => now - ts < 60000);
        this.requestHistory.set(ip, filtered);

        const rpm = filtered.length;

        // Increase difficulty every x requests/min
        const boost = Math.floor(rpm / Constants.POW_THRESHOLD_STEP);
        return Math.min(this.baseDifficulty + boost, this.maxDifficulty);
    }

    generateChallenge(ip) {
        const seed = crypto.randomBytes(16).toString('hex');
        const expiresAt = Date.now() + this.ttlMs;

        if (!this.requestHistory.has(ip))
            this.requestHistory.set(ip, []);

        // Record this request for traffic tracking
        this.requestHistory.get(ip).push(Date.now());

        const difficulty = this.getDynamicDifficulty(ip);

        // Store the difficulty used for THIS specific seed so verification works
        this.issuedChallenges.set(seed, {
            expiresAt,
            difficulty
        });

        return { seed, difficulty };
    }

    verify(seed, nonce) {
        const challenge = this.issuedChallenges.get(seed);
        if (!challenge || Date.now() > challenge.expiresAt) {
            this.issuedChallenges.delete(seed);
            return false;
        }

        // Use the difficulty that was assigned when the seed was created
        const target = '0'.repeat(challenge.difficulty);

        const hash = crypto.createHash('sha256')
            .update(seed + nonce.toString())
            .digest('hex');

        const isValid = hash.startsWith(target);

        // Delete if valid to prevent replay
        if (isValid) this.issuedChallenges.delete(seed);

        return isValid;
    }
}

module.exports = new PoW(5);