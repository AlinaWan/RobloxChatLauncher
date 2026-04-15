const config = {
    MAX_QUEUE_SIZE: 100, // Max number of pending messages in the queue before we start rejecting new ones
    MAIL_TTL_MS: 5_000, // milliseconds before mail is auto-deleted
    VERIFICATION_TTL_MS: 10 * 60 * 1000, // 10 minutes ttl for verification codes
    USERNAME_CACHE_TTL_MS: 60 * 60 * 1000, // 1 hour ttl for username cache
    TEXT_LIMIT_BYTES: 1024,
    WEBSOCKET_LIMIT_BYTES: 1024,
    HEARTBEAT_INTERVAL: 30_000,

    POW_THRESHOLD_STEP: 50, // Request per minute step to increase PoW difficulty
};

module.exports = config;
