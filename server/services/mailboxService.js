const Constants = require('../config/constants');

// Memory storage for mailboxes
// Key: universeId:jobId (Composite Key for isolation), Value: Array of { payload: object, expiresAt: number }
const mailboxStore = new Map();

/**
 * Pushes data into a mailbox associated with a specific universe and job.
 *
 * Each entry in the mailbox is stored with an expiration timestamp.
 * If the mailbox does not already exist for the given universeId and jobId, it is created.
 *
 * @param {string|number} universeId - The unique identifier for the universe.
 * @param {string|number} jobId - The unique identifier for the job within the universe.
 * @param {any|any[]} data - The data to store in the mailbox; can be a single item or an array of items.
 */
function pushToMailbox(universeId, jobId, data) {
    // We create a unique key that binds the mail to the specific Universe
    const storageKey = `${universeId}:${jobId}`;

    if (!mailboxStore.has(storageKey)) {
        mailboxStore.set(storageKey, []);
    }

    const expiresAt = Date.now() + Constants.MAIL_TTL_MS;
    const mailbox = mailboxStore.get(storageKey);

    if (Array.isArray(data)) {
        data.forEach(item => mailbox.push({ payload: item, expiresAt }));
    } else {
        mailbox.push({ payload: data, expiresAt });
    }
}

// Automatic Cleanup: Runs every 5 seconds to clear expired mail
setInterval(() => {
    const now = Date.now();
    for (const [storageKey, messages] of mailboxStore.entries()) {
        const freshMessages = messages.filter(msg => msg.expiresAt > now);
        if (freshMessages.length === 0) {
            mailboxStore.delete(storageKey);
        } else {
            mailboxStore.set(storageKey, freshMessages);
        }
    }
}, 5000);

module.exports = { mailboxStore, pushToMailbox };