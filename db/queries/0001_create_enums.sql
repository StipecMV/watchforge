-- Define enum for user roles
CREATE TYPE user_role AS ENUM ('user', 'admin', 'superadmin');

-- Define enum for authentication providers
CREATE TYPE auth_provider AS ENUM ('local', 'google', 'facebook', 'github');