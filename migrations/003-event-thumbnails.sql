CREATE TABLE IF NOT EXISTS event_thumbnails (
  token TEXT PRIMARY KEY,
  jpeg TEXT NOT NULL,
  expires_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS event_thumbnails_expiry ON event_thumbnails(expires_at);
CREATE INDEX IF NOT EXISTS events_kind_recent ON events(store, kind, time DESC);
