-- An invented allotment, laid out as one script: every object of the fixture in this file, in batches separated by GO,
-- saved as UTF-8 with a byte order mark and with CRLF line endings. Every position haruspex reports from it is after a
-- byte order mark, a carriage return and a GO.
CREATE SCHEMA plot;
GO

CREATE TABLE plot.bed
(
    bed_id   int           NOT NULL CONSTRAINT pk_bed PRIMARY KEY,
    name     nvarchar(40)  NOT NULL,
    area_m2  decimal(6, 2) NULL,
    soil     varchar(5)    NOT NULL CONSTRAINT ck_bed_soil CHECK (soil IN ('clay', 'loam', 'sand'))
);
GO
CREATE TABLE plot.sowing
(
    sowing_id  int           NOT NULL CONSTRAINT pk_sowing PRIMARY KEY,
    bed_id     int           NOT NULL CONSTRAINT fk_sowing_bed REFERENCES plot.bed (bed_id) ON DELETE CASCADE,
    crop       nvarchar(30)  NOT NULL,
    sown       date          NOT NULL,
    harvested  date          NULL
);
GO

-- A view after two batches, read from its definition.
CREATE VIEW plot.bed_crop
AS
SELECT b.bed_id, b.name, s.crop, s.sown, s.harvested
FROM plot.bed AS b
    INNER JOIN plot.sowing AS s ON s.bed_id = b.bed_id;
GO

CREATE FUNCTION plot.sowings_in (@bed_id int)
RETURNS TABLE
AS
RETURN
    SELECT s.sowing_id, s.crop, s.sown, s.harvested
    FROM plot.sowing AS s
    WHERE s.bed_id = @bed_id;
GO

CREATE PROCEDURE plot.list_beds
AS
SELECT b.bed_id, b.name, b.area_m2, b.soil,
       (SELECT COUNT(*) FROM plot.sowing AS s WHERE s.bed_id = b.bed_id) AS sowings
FROM plot.bed AS b
ORDER BY b.bed_id;
GO
CREATE PROCEDURE plot.bed_with_crops @bed_id int
AS
SELECT b.name, s.crop, s.sown, s.harvested
FROM plot.bed AS b
    LEFT OUTER JOIN plot.sowing AS s ON s.bed_id = b.bed_id
WHERE b.bed_id = @bed_id;
GO
