-- Each finds a row by one of the table's keys: a nonclustered primary key, a unique constraint, a unique index declared in the
-- table and one created apart. build.sh extract disables the index of all but the third before it extracts.
CREATE PROCEDURE studio.shelf_by_id @shelf_id int AS
SELECT s.kiln_id FROM studio.shelf AS s WHERE s.shelf_id = @shelf_id;
GO
CREATE PROCEDURE studio.kiln_by_serial @serial varchar(12) AS
SELECT k.kiln_id FROM studio.kiln AS k WHERE k.serial = @serial;
GO
CREATE PROCEDURE studio.kiln_by_label @label studio.code AS
SELECT k.kiln_id FROM studio.kiln AS k WHERE k.label = @label;
GO
CREATE PROCEDURE studio.kiln_by_serial_and_peak @serial varchar(12), @peak int AS
SELECT k.kiln_id FROM studio.kiln AS k WHERE k.serial = @serial AND k.peak = @peak;
