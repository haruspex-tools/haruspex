-- Reads a table of cellar's project, which orchard reaches only through press.
CREATE PROCEDURE grove.barrels AS
SELECT b.barrel_id, b.litres FROM grove.barrel AS b;
