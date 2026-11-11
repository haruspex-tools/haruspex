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

-- A chain folds left to right: units (bigint) beside price (money) gives money, whose 15 integer digits the decimal(9,2) then
-- counts, so the column is decimal(19,4) and bigint's ends overflow it (A_chain_is_typed_left_to_right_so_money_absorbs_an_integer_before_a_decimal).
-- Literals that do not fit the column's type overflow it too: 9999999 as smallmoney, '300' as tinyint.
SELECT CONVERT(decimal(19,4), c.amount) AS amount, CONVERT(smallmoney, c.fee) AS fee, CONVERT(tinyint, c.bids) AS bids
FROM (
    SELECT b.units AS amount, b.small_fee AS fee, a.bids AS bids
    FROM bazaar.barter AS b
    CROSS JOIN (SELECT TOP (1) bids FROM bazaar.auction_lot ORDER BY lot_id) AS a
    UNION ALL
    SELECT w.price, 9999999, '300' FROM bazaar.web_order AS w
    UNION ALL
    SELECT s.unit_price, 1, 1 FROM bazaar.stall_sale AS s
) AS c;

-- The same literal in CASE and COALESCE, which read fine directly: NULL where it is taken (barters 3 to 5, and barter 4,
-- whose fee is 0).
SELECT b.barter_id,
       IIF(b.barter_id > 2, 9999999, b.small_fee) AS fee_or_big,
       COALESCE(NULLIF(b.small_fee, 0), 9999999) AS fee_or_default
FROM bazaar.barter AS b
ORDER BY b.barter_id;
