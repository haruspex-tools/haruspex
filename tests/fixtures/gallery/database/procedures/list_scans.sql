CREATE PROCEDURE gallery.list_scans AS
SELECT s.scan_id, s.scan, DATALENGTH(s.scan) AS scan_bytes FROM gallery.legacy_scan AS s;
