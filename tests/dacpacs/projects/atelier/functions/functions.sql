CREATE FUNCTION studio.fahrenheit (@c int) RETURNS int WITH RETURNS NULL ON NULL INPUT AS
BEGIN
    RETURN @c * 9 / 5 + 32;
END;
GO
CREATE FUNCTION studio.kilns_hotter (@peak int) RETURNS TABLE AS
RETURN SELECT k.kiln_id, k.peak FROM studio.kiln AS k WHERE k.peak > @peak;
GO
CREATE FUNCTION studio.kiln_firsts () RETURNS @firsts TABLE (kiln_id int NOT NULL PRIMARY KEY, first_pot int NULL) AS
BEGIN
    INSERT INTO @firsts SELECT k.kiln_id, MIN(p.pot_id) FROM studio.kiln AS k LEFT JOIN studio.pot AS p ON p.kiln_id = k.kiln_id GROUP BY k.kiln_id;
    RETURN;
END;
GO
CREATE SYNONYM studio.furnace FOR studio.kiln;
