const Constants = require('../config/constants');

// Memory storage for mailboxes
// Key: jobId, Value: Array of { payload: object, expiresAt: number }
const mailboxStore = new Map();

/**
 * Adds mail to a specific server's mailbox
 * @param {string} jobId - The Roblox JobId
 * @param {object|array} data - The payload (Emote, etc.)
 */
function pushToMailbox(jobId, data) {
    if (!mailboxStore.has(jobId)) {
        mailboxStore.set(jobId, []);
    }

    const expiresAt = Date.now() + Constants.MAIL_TTL_MS;
    const mailbox = mailboxStore.get(jobId);

    // If data is an array, push each item, otherwise push the single object
    if (Array.isArray(data)) {
        data.forEach(item => mailbox.push({ payload: item, expiresAt }));
    } else {
        mailbox.push({ payload: data, expiresAt });
    }
}

// Automatic Cleanup: Runs every 5 seconds to clear expired mail
setInterval(() => {
    const now = Date.now();
    for (const [jobId, messages] of mailboxStore.entries()) {
        const freshMessages = messages.filter(msg => msg.expiresAt > now);
        if (freshMessages.length === 0) {
            mailboxStore.delete(jobId);
        } else {
            mailboxStore.set(jobId, freshMessages);
        }
    }
}, 5000);

module.exports = { mailboxStore, pushToMailbox };