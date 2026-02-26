const axios = require('axios');
const { pool } = require('./postgresPool');

/**
 * Validates the UniverseId and API Key against the PostgreSQL registry.
 */
async function authenticateGameServer(universeId, apiKey) {
    if (!universeId || !apiKey) return false;

    try {
        const query = `
            SELECT 1 FROM game_registry 
            WHERE universe_id = $1 AND api_key = $2 
            LIMIT 1
        `;
        const result = await pool.query(query, [universeId, apiKey]);
        return result.rowCount > 0;
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
    const query = `
        INSERT INTO game_registry (universe_id, api_key)
        VALUES ($1, $2)
        ON CONFLICT (universe_id) 
        DO UPDATE SET api_key = EXCLUDED.api_key;
    `;
    await pool.query(query, [universeId, apiKey]);
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