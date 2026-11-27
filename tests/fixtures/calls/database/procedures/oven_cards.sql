-- oven_list's rows under names and types of this procedure's own, by WITH RESULT SETS: each value converted to its declared
-- column, so a label longer than six characters arrives cut short to six, which haruspex warns of (HAR2104).
CREATE PROCEDURE calls.oven_cards AS
SET NOCOUNT ON;
EXEC calls.oven_list WITH RESULT SETS ((ovenId bigint NOT NULL, name nvarchar(6) NOT NULL, racks smallint NULL));
