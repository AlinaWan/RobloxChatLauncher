const axios = require('axios');
const { pool } = require('./postgresPool');

const pendingVerifications = new Map();
const nameCache = new Map(); // Keep names in memory to avoid API spam

async function generateCode(req, res) {
    const { robloxUsername } = req.body;
    if (!robloxUsername) return res.status(400).send("Username required");

    try {
        const userRes = await axios.post('https://users.roblox.com/v1/usernames/users', {
            usernames: [robloxUsername],
            excludeBannedUsers: true
        });

        if (!userRes.data.data.length) return res.status(404).send("User not found");

        const robloxId = userRes.data.data[0].id;
        const code = `RCL-${Math.random().toString(36).substring(2, 8).toUpperCase()}`;

        pendingVerifications.set(robloxId, code);
        res.json({ code, robloxId });
    } catch (err) {
        console.error("Generate Error:", err);
        res.status(500).send("API Error");
    }
}

async function verifyProfile(req, res) {
    const { robloxId, hwid } = req.body;
    const expectedCode = pendingVerifications.get(robloxId);

    if (!expectedCode) return res.status(400).send("No pending verification.");

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
    if (!hwid) return res.status(400).send("Hardware ID required");

    try {
        await pool.query('DELETE FROM verified_users WHERE hwid = $1', [hwid]);
        res.json({ success: true, message: "Account unlinked successfully" });
    } catch (err) {
        console.error("Unverify Error:", err);
        res.status(500).send("Server Error");
    }
}

async function getRobloxUsername(userId) {
    if (nameCache.has(userId)) return nameCache.get(userId);

    try {
        const res = await axios.get(`https://users.roblox.com/v1/users/${userId}`);
        const username = res.data.name; // Use .displayName if you prefer their nickname
        nameCache.set(userId, username);
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
    unverifyUser
};