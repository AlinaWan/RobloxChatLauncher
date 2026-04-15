const crypto = require('crypto');

const Constants = require('./config/constants');

class PoW {
    constructor(baseDifficulty = 4, maxDifficulty = 7, ttlMs = 5 * 60 * 1000) {
        this.baseDifficulty = baseDifficulty;
        this.maxDifficulty = maxDifficulty;
        this.ttlMs = ttlMs;
        this.issuedChallenges = new Map(); // seed -> { expiresAt, difficulty }

        // Traffic Tracking
        this.requestHistory = []; // Timestamps of issued challenges

        setInterval(() => {
            const now = Date.now();
            for (const [seed, { expiresAt }] of this.issuedChallenges.entries()) {
                if (now > expiresAt) {
                    this.issuedChallenges.delete(seed);
                }
            }
        }, 60000).unref();
    }

    // Calculate difficulty based on requests in the last 60 seconds
    getDynamicDifficulty() {
        const now = Date.now();
        // Clean up history older than 1 minute
        this.requestHistory = this.requestHistory.filter(ts => now - ts < 60000);

        const rpm = this.requestHistory.length;

        // Increase difficulty every x requests/min
        const boost = Math.floor(rpm / Constants.POW_THRESHOLD_STEP);
        return Math.min(this.baseDifficulty + boost, this.maxDifficulty);
    }

    generateChallenge() {
        const seed = crypto.randomBytes(16).toString('hex');
        const expiresAt = Date.now() + this.ttlMs;

        // Record this request for traffic tracking
        this.requestHistory.push(Date.now());

        const currentDiff = this.getDynamicDifficulty();

        // Store the difficulty used for THIS specific seed so verification works
        this.issuedChallenges.set(seed, {
            expiresAt,
            difficulty: currentDiff
        });

        return { seed, difficulty: currentDiff };
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