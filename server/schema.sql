CREATE TABLE IF NOT EXISTS zora_licenses (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    key_hash char(64) NOT NULL UNIQUE,
    key_label varchar(32) NOT NULL,
    username varchar(24),
    status varchar(12) NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'banned', 'revoked')),
    max_devices integer NOT NULL DEFAULT 1 CHECK (max_devices BETWEEN 1 AND 5),
    created_at timestamptz NOT NULL DEFAULT now(),
    activated_at timestamptz,
    expires_at timestamptz,
    last_seen_at timestamptz,
    note text NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_zora_licenses_status ON zora_licenses(status, created_at DESC);
CREATE TABLE IF NOT EXISTS zora_devices (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    license_id uuid NOT NULL REFERENCES zora_licenses(id) ON DELETE CASCADE,
    device_hash char(64) NOT NULL,
    refresh_hash char(64) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    last_seen_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (license_id, device_hash)
);
CREATE INDEX IF NOT EXISTS idx_zora_devices_license ON zora_devices(license_id);
CREATE TABLE IF NOT EXISTS zora_audit (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    license_id uuid REFERENCES zora_licenses(id) ON DELETE SET NULL,
    action varchar(48) NOT NULL,
    actor varchar(64) NOT NULL,
    details jsonb NOT NULL DEFAULT '{}'::jsonb
);
CREATE INDEX IF NOT EXISTS idx_zora_audit_at ON zora_audit(occurred_at DESC);
