-- A table-valued parameter no client can send rows in, so only left out.
CREATE PROCEDURE till.count_serials
    @serials till.serial_list READONLY
AS
SELECT count(*) AS serials FROM @serials;
