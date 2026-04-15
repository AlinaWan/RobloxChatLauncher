const crypto = require('crypto');

class PoW {
    constructor(difficulty = 4, ttlMs = 5 * 60 * 1000) {
        this.difficulty = difficulty;
        this.target = '0'.repeat(difficulty);
        this.ttlMs = ttlMs;
        this.issuedChallenges = new Map(); // seed -> expiresAt

        setInterval(() => {
            const now = Date.now();
            for (const [seed, expiresAt] of this.issuedChallenges.entries()) {
                if (now > expiresAt) this.issuedChallenges.delete(seed);
            }
        }, 60000);
    }

    generateChallenge() {
        const seed = crypto.randomBytes(16).toString('hex');
        const expiresAt = Date.now() + this.ttlMs;
        this.issuedChallenges.set(seed, expiresAt);
        return { seed, difficulty: this.difficulty };
    }

    verify(seed, nonce) {
        const exists = this.issuedChallenges.delete(seed);
        if (!exists) return false;

        const hash = crypto.createHash('sha256')
            .update(seed + nonce.toString())
            .digest('hex');

        return hash.startsWith(this.target);
    }
}

module.exports = new PoW(4);