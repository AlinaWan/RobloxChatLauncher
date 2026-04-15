const axios = require('axios');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const Constants = require('../config/constants');
const { pool } = require('../db/postgresPool');
const pow = require('../utils/pow');

// Courtesy of the EFF Large Wordlist for Passphrases
// Source: https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt
const WORDLIST_PATH = path.join(__dirname, '../data/eff_large_wordlist.txt');

const WORDS = fs.readFileSync(WORDLIST_PATH, 'utf8')
    .split(/\r?\n/)
    .map(line => line.split('\t')[1]) // take second column
    .filter(Boolean);

// --- Pending verifications ---
const pendingVerifications = new Map();
// Structure: robloxId -> { code: string, expiresAt: number }

// Periodic cleanup for expired codes
setInterval(() => {
    const now = Date.now();
    for (const [robloxId, entry] of pendingVerifications.entries()) {
        if (entry.expiresAt <= now) pendingVerifications.delete(robloxId);
    }
}, 60 * 1000); // run every minute

const nameCache = new Map(); // Keep names in memory to avoid API spam
// Structure: { username: string, expiresAt: number }

// Periodic cleanup for expired name cache entries
setInterval(() => {
    const now = Date.now();
    for (const [userId, entry] of nameCache.entries()) {
        if (entry.expiresAt <= now) nameCache.delete(userId);
    }
}, 10 * 60 * 1000); // run every 10 minutes

async function getChallenge(req, res) {
    const ip = req.ip;
    const challenge = pow.generateChallenge(ip);
    res.json(challenge);
}

// Helper to vaidate if a string is a valid UUID
function isValidUuidV4(uuid) {
    const regex = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
    return regex.test(uuid);
}

async function generateCode(req, res) {
    const { robloxUsername, seed, nonce } = req.body;

    if (!seed || !nonce || !pow.verify(seed, nonce)) {
        return res.status(401).send("Invalid or missing Proof of Work.");
    }

    if (!robloxUsername) return res.status(400).send("Username required");

    try {
        const userRes = await axios.post('https://users.roblox.com/v1/usernames/users', {
            usernames: [robloxUsername],
            excludeBannedUsers: true
        });

        if (!userRes.data.data.length) return res.status(404).send("User not found");

        const robloxId = userRes.data.data[0].id;
        const code = `rcl ${Array.from({ length: 6 }, () =>
            WORDS[crypto.randomInt(0, WORDS.length)].toLowerCase()
        ).join(' ')}`;

        // Store both code and expiration timestamp
        pendingVerifications.set(robloxId, { code, expiresAt: Date.now() + Constants.VERIFICATION_TTL_MS });
        res.json({ code, robloxId });
    } catch (err) {
        console.error("Generate Error:", err);
        res.status(500).send("API Error");
    }
}

async function verifyProfile(req, res) {
    const { robloxId, hwid } = req.body;
    
    if (!hwid || !isValidUuidV4(hwid)) return res.status(400).send("Invalid HWID format. Must be a valid UUID v4.");

    const entry = pendingVerifications.get(robloxId);
    if (!entry) return res.status(400).send("No pending verification or code expired.");

    const expectedCode = entry.code;

    try {
        // 1. Check Roblox profile blurb
        const profileRes = await axios.get(`https://users.roblox.com/v1/users/${robloxId}`);
        const blurb = profileRes.data.description;

        if (blurb.includes(expectedCode)) {
            // 2. Insert into PostgreSQL (Upsert: if HWID exists, update the RobloxID)
            const query = `
                INSERT INTO verified_users (hwid, roblox_id)
                VALUES ($1, $2)
                ON CONFLICT (hwid) 
                DO UPDATE SET roblox_id = EXCLUDED.roblox_id;
            `;
            await pool.query(query, [hwid, robloxId]);

            pendingVerifications.delete(robloxId);
            res.json({ success: true });
        } else {
            res.status(400).send("Code not found in profile description.");
        }
    } catch (err) {
        console.error("DB/Verify Error:", err);
        res.status(500).send("Verification failed.");
    }
}

async function unverifyUser(req, res) {
    const { hwid } = req.body;
    if (!hwid) return res.status(400).send("HWID required");

    try {
        const result = await pool.query(
            'DELETE FROM verified_users WHERE hwid = $1',
            [hwid]
        );

        if (result.rowCount === 0) {
            return res.status(404).json({ success: false, message: "No matching record found" });
        }

        res.json({ success: true, message: "Account unlinked successfully" });
    } catch (err) {
        console.error("Unverify Error:", err);
        res.status(500).send("Server Error");
    }
}

async function checkLogin(req, res) {
    const { hwid } = req.body;

    try {
        const result = await pool.query(
            'SELECT roblox_id FROM verified_users WHERE hwid = $1',
            [hwid]
        );

        if (result.rowCount > 0) {
            return res.json({
                success: true,
                robloxId: Number(result.rows[0].roblox_id)
            });
        } else {
            return res.status(401).json({ success: false });
        }
    } catch (err) {
        console.error("Login Check Error:", err);
        res.status(500).send("Server Error");
    }
}

// Add or Update a verified user manually
async function upsertUser(hwid, robloxId) {
    const query = `
        INSERT INTO verified_users (hwid, roblox_id)
        VALUES ($1, $2)
        ON CONFLICT (hwid) 
        DO UPDATE SET roblox_id = EXCLUDED.roblox_id;
    `;
    await pool.query(query, [hwid, robloxId]);
}

// Remove a verified user by HWID manually
async function removeUser(hwid) {
    await pool.query('DELETE FROM verified_users WHERE hwid = $1', [hwid]);
}

async function getRobloxUsername(userId) {
    if (nameCache.has(userId)) return nameCache.get(userId);

    try {
        const res = await axios.get(`https://users.roblox.com/v1/users/${userId}`);
        const username = res.data.name; // Use .displayName if you prefer their nickname
        nameCache.set(userId, { username, expiresAt: Date.now() + Constants.USERNAME_CACHE_TTL_MS });
        return username;
    } catch (err) {
        return `User:${userId}`; // Fallback if API fails
    }
}

/**
 * Utility to check if a user is verified based on HWID
 */
async function getRobloxIdByHwid(hwid) {
    try {
        const res = await pool.query('SELECT roblox_id FROM verified_users WHERE hwid = $1', [hwid]);
        return res.rowCount > 0 ? res.rows[0].roblox_id : null;
    } catch (err) {
        return null;
    }
}

module.exports = {
    generateCode,
    verifyProfile,
    getRobloxIdByHwid,
    getRobloxUsername,
    unverifyUser,
    checkLogin,
    upsertUser,
    removeUser,
    getChallenge
};
