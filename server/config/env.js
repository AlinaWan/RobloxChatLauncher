const config = {
    PORT: process.env.PORT || 10000,
    DATABASE_URL: process.env.DATABASE_URL,
    RCL_ADMIN_KEY: process.env.RCL_ADMIN_KEY,
    PERSPECTIVE_API_KEY: process.env.PERSPECTIVE_API_KEY,
    USER_SALT: process.env.USER_SALT,
};

for (const [key, value] of Object.entries(config)) {
    if (value === undefined) {
        console.error(`FATAL: Missing environment variable '${key}'`);
        process.exit(1);
    }
}

module.exports = config;