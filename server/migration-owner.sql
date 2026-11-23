-- Non-destructive owner-password authentication migration (Zora 0.3).
-- DO NOT store plaintext passwords, tokens, or owner API secrets in the database.
CREATE TABLE IF NOT EXISTS zora_owner_auth (
  id integer PRIMARY KEY CHECK (id=1),
  password_hash text NOT NULL,
  password_version integer NOT NULL DEFAULT 1,
  failed_attempts integer NOT NULL DEFAULT 0,
  locked_until timestamptz,
  updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS zora_owner_sessions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  token_hash char(64) NOT NULL UNIQUE,
  password_version integer NOT NULL,
  user_agent varchar(180) NOT NULL DEFAULT '',
  created_at timestamptz NOT NULL DEFAULT now(),
  last_seen_at timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz NOT NULL,
  revoked_at timestamptz
);
CREATE INDEX IF NOT EXISTS zora_owner_sessions_active_idx ON zora_owner_sessions(expires_at DESC) WHERE revoked_at IS NULL;
