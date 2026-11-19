CREATE PROCEDURE gallery.list_scans_json AS
SELECT s.scan_id, s.scan FROM gallery.legacy_scan AS s FOR JSON PATH;
