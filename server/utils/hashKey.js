const crypto = require('crypto');

function hashKey(key) {
    return crypto
        .createHash('sha256')
        .update(key, 'utf8')
        .digest('hex');
}

module.exports = { hashKey };
