const { safeCompare } = require('../utils/secureCompare');

function createApiKeyMiddleware(validKeys = []) {
    return function (req, res, next) {
        const authHeader = req.headers['authorization'];

        if (!authHeader || !authHeader.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Unauthorized' });
        }

        const providedKey = authHeader.slice(7).trim();

        const isValid = validKeys.some(validKey =>
            safeCompare(providedKey, validKey)
        );

        if (!isValid) {
            return res.status(401).json({ error: 'Unauthorized' });
        }

        next();
    };
}

module.exports = { createApiKeyMiddleware };
