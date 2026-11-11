CREATE PROCEDURE ledger.line_values
AS
-- Decimal arithmetic within and past 38 digits, % and division by literals, a bit beside a decimal, and money divided by a
-- literal. None of these overflows on the seed's extremes (README.md); the ones that can are in ledger.lenient.
SELECT l.line_id,
       l.qty * l.unit_price AS gross,
       l.unit_price * l.rate AS rated,
       -1.0 * l.unit_price AS negated,
       l.unit_price * l.discount AS discounted,
       l.frac * l.frac AS frac_squared,
       l.unit_price / 100 AS hundredth,
       l.unit_price / 0.05 AS per_twentieth,
       l.unit_price / l.qty AS per_unit,
       l.unit_price % 7 AS price_mod,
       l.big % 1000 AS big_mod,
       l.qty % 10 AS qty_mod,
       l.flag * l.unit_price AS flagged,
       l.fee / 4 AS quarter_fee,
       -l.big AS big_negated
FROM ledger.line AS l
ORDER BY l.line_id;
