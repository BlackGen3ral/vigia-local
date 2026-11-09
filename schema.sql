CREATE TABLE IF NOT EXISTS stores (id TEXT PRIMARY KEY, last_seen INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS events (
  id TEXT NOT NULL, store TEXT NOT NULL REFERENCES stores(id), time INTEGER NOT NULL, kind TEXT NOT NULL,
  PRIMARY KEY(store,id)
);
CREATE INDEX IF NOT EXISTS events_recent ON events(store,time DESC);
CREATE TABLE IF NOT EXISTS subscriptions (
  store TEXT NOT NULL REFERENCES stores(id), endpoint TEXT NOT NULL, payload TEXT NOT NULL,
  PRIMARY KEY(store,endpoint)
);
