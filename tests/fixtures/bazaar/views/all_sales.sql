-- Every sale, of whatever kind, in one shape: each kind computes its columns its own way, so each column's queries have
-- different types, which the view's column takes by data type precedence over all four.
CREATE VIEW bazaar.all_sales
AS
SELECT 'stall' AS kind, s.sale_id AS sale_id, s.sold_on AS sold_at, s.qty AS units, s.qty * s.unit_price AS amount,
       s.paid_by AS method, s.note AS memo
FROM bazaar.stall_sale AS s
UNION ALL
SELECT 'web', w.order_id, w.placed_at, w.qty, w.price * w.qty, w.channel, NULL
FROM bazaar.web_order AS w
UNION ALL
SELECT 'auction', a.lot_id, a.closed_at, a.bids, a.hammer, a.paid_by, NULL
FROM bazaar.auction_lot AS a
UNION ALL
SELECT 'barter', b.barter_id, b.swapped_at, b.units, b.small_fee, NULL, b.memo
FROM bazaar.barter AS b;
