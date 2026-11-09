CREATE TABLE IF NOT EXISTS invite_codes (
  code_hash TEXT PRIMARY KEY,
  store TEXT NOT NULL REFERENCES stores(id),
  expires_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS invite_codes_store ON invite_codes(store, expires_at);
