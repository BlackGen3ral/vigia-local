ALTER TABLE event_thumbnails ADD COLUMN store TEXT;
ALTER TABLE event_thumbnails ADD COLUMN event_id TEXT;
CREATE UNIQUE INDEX IF NOT EXISTS event_thumbnails_event ON event_thumbnails(store, event_id);
