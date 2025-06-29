CREATE TABLE login_identities (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL,
    provider auth_provider NOT NULL,
    provider_id TEXT,
    password_hash TEXT,
    UNIQUE (provider, provider_id),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);