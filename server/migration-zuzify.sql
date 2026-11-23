-- Zora license -> verified Zuzify ID (OAuth code + PKCE).
-- Safe to apply repeatedly; the same migration is deployed to the Zora Neon project.
CREATE TABLE IF NOT EXISTS zora_zuzify_links (
  license_id uuid PRIMARY KEY REFERENCES zora_licenses(id) ON DELETE CASCADE,
  zuzify_user_id uuid NOT NULL UNIQUE,
  linked_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS zora_zuzify_link_requests (
  state_hash char(64) PRIMARY KEY,
  license_id uuid NOT NULL REFERENCES zora_licenses(id) ON DELETE CASCADE,
  code_verifier varchar(128) NOT NULL,
  expires_at timestamptz NOT NULL,
  consumed_at timestamptz
);
CREATE INDEX IF NOT EXISTS zora_zuzify_link_requests_license_idx ON zora_zuzify_link_requests(license_id, expires_at DESC);
