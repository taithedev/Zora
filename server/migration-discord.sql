-- Run against the existing Zora Neon database before enabling Discord account linking.
-- Only identity information is persisted; OAuth access/refresh tokens are never stored.
CREATE TABLE IF NOT EXISTS zora_discord_links (
    license_id uuid PRIMARY KEY REFERENCES zora_licenses(id) ON DELETE CASCADE,
    discord_user_id varchar(25) NOT NULL UNIQUE,
    discord_username varchar(64) NOT NULL,
    discord_display_name varchar(100),
    discord_avatar_hash varchar(128),
    linked_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS zora_discord_links_discord_user_id_idx
    ON zora_discord_links(discord_user_id);

CREATE TABLE IF NOT EXISTS zora_discord_link_requests (
    state_hash char(64) PRIMARY KEY,
    license_id uuid NOT NULL REFERENCES zora_licenses(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz
);
CREATE INDEX IF NOT EXISTS zora_discord_link_requests_license_idx
    ON zora_discord_link_requests(license_id, expires_at DESC);
