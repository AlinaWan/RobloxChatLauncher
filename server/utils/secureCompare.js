const crypto = require('crypto');

function safeCompare(a, b) {
    if (typeof a !== 'string' || typeof b !== 'string') {
        return false;
    }

    const aBuf = Buffer.from(a, 'utf8');
    const bBuf = Buffer.from(b, 'utf8');

    // Prevent length leak crash
    if (aBuf.length !== bBuf.length) {
        // Compare against itself to keep timing consistent
        return crypto.timingSafeEqual(aBuf, aBuf) && false;
    }

    return crypto.timingSafeEqual(aBuf, bBuf);
}

module.exports = { safeCompare };
