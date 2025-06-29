CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    email TEXT,
    role user_role NOT NULL,
    last_login TIMESTAMP,
    created_at TIMESTAMP DEFAULT NOW()
);