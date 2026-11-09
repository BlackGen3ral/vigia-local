-- Additive migration: preserve existing events, stores and pairings.
ALTER TABLE events ADD COLUMN notified INTEGER NOT NULL DEFAULT 1;
ALTER TABLE stores ADD COLUMN total_entries INTEGER NOT NULL DEFAULT 0;
ALTER TABLE stores ADD COLUMN counting_since INTEGER NOT NULL DEFAULT 0;
ALTER TABLE stores ADD COLUMN last_arrival_notice INTEGER NOT NULL DEFAULT 0;
UPDATE stores SET counting_since = CAST(strftime('%s','now') AS INTEGER) * 1000;
CREATE TRIGGER count_new_arrival AFTER INSERT ON events WHEN NEW.kind = 'arrival'
BEGIN
  UPDATE stores SET total_entries = total_entries + 1,
    counting_since = CASE WHEN counting_since = 0 THEN NEW.time ELSE counting_since END,
    last_arrival_notice = CASE WHEN NEW.notified = 1 THEN NEW.time ELSE last_arrival_notice END
    WHERE id = NEW.store;
END;
