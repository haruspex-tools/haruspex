-- Scans kept from an older system, in the deprecated image type.
CREATE TABLE gallery.legacy_scan (
    scan_id int NOT NULL CONSTRAINT pk_legacy_scan PRIMARY KEY,
    scan image NULL);
