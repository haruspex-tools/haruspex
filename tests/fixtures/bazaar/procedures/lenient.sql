-- Run only under the caller's ANSI_WARNINGS and ARITHABORT off: each column converts values that, on some rows, overflow its
-- type and come out NULL, and reads only columns that are never null.
--
-- Each set operation is read through a CONVERT to its own type, which passes its value and its claims through unchanged:
-- read directly, a set operation's overflow under these options reaches SqlClient as a row it misreads (a wrong value, then
-- "Internal connection fatal error"), though the server's value is NULL, which the CONVERT shows (observed:
-- A_set_operations_overflow_under_the_callers_options_is_null_on_the_server_and_misread_by_sqlclient).
CREATE PROCEDURE bazaar.lenient
AS
SET NOCOUNT ON;

SELECT CONVERT(smallmoney, u.fee) AS fee, CONVERT(datetime, u.moment) AS moment, CONVERT(int, u.ticket) AS ticket
FROM (
    SELECT b.units AS fee, b.estimate AS moment, s.ticket AS ticket
    FROM bazaar.barter AS b
    CROSS JOIN (SELECT TOP (1) ticket FROM bazaar.stall_sale ORDER BY sale_id DESC) AS s
    UNION ALL
    SELECT b.small_fee, a.closed_at, 0
    FROM bazaar.barter AS b
    CROSS JOIN (SELECT TOP (1) closed_at FROM bazaar.auction_lot ORDER BY lot_id) AS a
) AS u;

SELECT CONVERT(smalldatetime, d.day) AS day
FROM (
    SELECT s.sold_on AS day FROM bazaar.stall_sale AS s
    UNION ALL
    SELECT b.swapped_at FROM bazaar.barter AS b
) AS d;
