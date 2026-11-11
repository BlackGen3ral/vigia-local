ALTER TABLE events ADD COLUMN people INTEGER NOT NULL DEFAULT 1;
DROP TRIGGER count_new_arrival;
CREATE TRIGGER count_new_arrival AFTER INSERT ON events WHEN NEW.kind = 'arrival'
BEGIN
  UPDATE stores SET total_entries = total_entries + NEW.people,
    counting_since = CASE WHEN counting_since = 0 THEN NEW.time ELSE counting_since END,
    last_arrival_notice = CASE WHEN NEW.notified = 1 THEN NEW.time ELSE last_arrival_notice END
    WHERE id = NEW.store;
END;
