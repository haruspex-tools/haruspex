-- More of the allotment, in a second script with a byte order mark and CRLF line endings, whose objects start after
-- comments and blank lines, two to a batch where SQL Server allows it.


CREATE PROCEDURE plot.crops_of @bed_id int
AS
SELECT c.crop, c.sown, c.harvested FROM plot.bed_crop AS c WHERE c.bed_id = @bed_id ORDER BY c.sown;
GO


        CREATE PROCEDURE plot.bed_json @bed_id int
        AS
        SELECT b.name, b.soil,
               (SELECT s.crop, s.sown, s.harvested FROM plot.sowings_in(b.bed_id) AS s ORDER BY s.sowing_id FOR JSON PATH) AS sowings
        FROM plot.bed AS b
        WHERE b.bed_id = @bed_id
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
GO
