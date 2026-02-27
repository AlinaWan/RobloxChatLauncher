module.exports = {
    PORT: process.env.PORT || 10000,
    MAX_QUEUE_SIZE: 100, // Max number of pending messages in the queue before we start rejecting new ones
    MAIL_TTL_MS: 5_000, // seconds before mail is auto-deleted
    TEXT_LIMIT_BYTES: 1024,
    WEBSOCKET_LIMIT_BYTES: 1024,
    HEARTBEAT_INTERVAL: 30_000,
};