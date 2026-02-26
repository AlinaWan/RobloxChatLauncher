// Small helper module to manage a PostgreSQL connection pool using the 'pg' library.
const { Pool } = require('pg');

// Setup connection pool
const pool = new Pool({
    connectionString: process.env.DATABASE_URL,
    ssl: { rejectUnauthorized: false } // Required for Render/Heroku/managed DBs
});