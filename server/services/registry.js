const axios = require('axios');
const crypto = require('crypto');

const { pool } = require('../db/postgresPool');
const { safeCompare } = require('../utils/secureCompare');
const { hashKey } = require('../utils/hashKey');

function hashKey(key) {
    return crypto.createHash('sha256').update(key).digest('hex');
}

/**
 * Validates the UniverseId and API Key against the PostgreSQL registry.
 */
async function authenticateGameServer(universeId, apiKey) {
    if (!universeId || !apiKey) return false;

    try {
        const query = `
            SELECT api_key
            FROM game_registry
            WHERE universe_id = $1
            LIMIT 1
        `;

        const result = await pool.query(query, [universeId]);

        if (result.rowCount === 0) {
            // Still do fake compare to avoid oracle
            const fakeKey = crypto.randomBytes(32).toString('hex');
            safeCompare(apiKey, fakeKey);
            return false;
        }

        const storedKey = result.rows[0].api_key;

        return safeCompare(apiKey, storedKey);
    } catch (err) {
        console.error("Database Auth Error:", err);
        return false;
    }
}

async function getAllGames() {
    const res = await pool.query('SELECT universe_id, created_at FROM game_registry ORDER BY created_at DESC');
    return res.rows;
}

async function upsertGame(universeId, apiKey) {
    const hashedKey = hashKey(apiKey);

    const query = `
        INSERT INTO game_registry (universe_id, api_key)
        VALUES ($1, $2)
        ON CONFLICT (universe_id) 
        DO UPDATE SET api_key = EXCLUDED.api_key;
    `;

    await pool.query(query, [universeId, hashedKey]);
}

async function removeGame(universeId) {
    await pool.query('DELETE FROM game_registry WHERE universe_id = $1', [universeId]);
}

module.exports = {
    authenticateGameServer,
    pool,
    getAllGames,
    upsertGame,
    removeGame
};
