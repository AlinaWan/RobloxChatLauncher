// Small helper module to manage a PostgreSQL connection pool using the 'pg' library.
const { Pool } = require('pg');

// Setup connection pool
const pool = new Pool({
    connectionString: process.env.DATABASE_URL,
    ssl: { rejectUnauthorized: false } // Required for Render/Heroku/managed DBs
});

// Initialize tables if they don't exist
await pool.query(`
CREATE TABLE IF NOT EXISTS verified_users (
    hwid TEXT PRIMARY KEY,
    roblox_id BIGINT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
`);

await pool.query(`
CREATE TABLE IF NOT EXISTS game_registry (
    universe_id BIGINT PRIMARY KEY,
    api_key TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
`);