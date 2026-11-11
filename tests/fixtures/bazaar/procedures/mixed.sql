-- Set operations, COALESCE and CASE over values of different types, none of which overflows on the seed.
CREATE PROCEDURE bazaar.mixed
AS
SET NOCOUNT ON;

-- A cut scale (decimal(38,10) beside decimal(30,0) is decimal(38,8)), a time beside a datetimeoffset, a string beside a
-- uniqueidentifier, a NULL literal taking the other queries' type, a bit-free integer chain.
SELECT a.hammer AS price, CAST(NULL AS time(7)) AS at_time, CAST(NULL AS varchar(36)) AS ref, NULL AS note, a.bids AS count_of
FROM bazaar.auction_lot AS a
UNION ALL
SELECT a.reserve, NULL, NULL, a.paid_by, a.lot_id
FROM bazaar.auction_lot AS a
UNION ALL
SELECT NULL, b.opened, NULL, b.memo, b.units
FROM bazaar.barter AS b
UNION ALL
SELECT NULL, w.placed_at, w.ref_code, w.channel, w.qty
FROM bazaar.web_order AS w
UNION ALL
SELECT NULL, NULL, w.voucher, NULL, NULL
FROM bazaar.web_order AS w;

-- INTERSECT and EXCEPT over different types: the left side's values in the common type.
SELECT s.qty AS units FROM bazaar.stall_sale AS s
INTERSECT
SELECT b.units FROM bazaar.barter AS b;

SELECT s.sold_on AS day FROM bazaar.stall_sale AS s
EXCEPT
SELECT w.placed_at FROM bazaar.web_order AS w;

-- A band: a stall's discount (0.25 or 0.75) beside a lot's tier (2 or 3) is decimal(38,0), so the discounts are rounded to 0
-- and 1: neither CHECK's set nor its bounds hold of the column.
SELECT s.discount AS band FROM bazaar.stall_sale AS s
UNION ALL
SELECT a.tier FROM bazaar.auction_lot AS a;

-- The same rule in COALESCE and CASE: a uniqueidentifier from its text, never null; a quantity or a price as money; a
-- 38-digit literal beside 2.5 is numeric(38,0), so 2.5 comes out as 3 and no set of the two literals holds.
SELECT w.order_id,
       COALESCE(w.voucher, w.ref_code) AS voucher_or_ref,
       CASE WHEN w.qty > 1 THEN w.qty ELSE w.price END AS qty_or_price,
       CASE WHEN w.order_id > 1 THEN 12345678901234567890123456789012345678 ELSE 2.5 END AS big_or_half
FROM bazaar.web_order AS w
ORDER BY w.order_id;
