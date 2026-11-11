CREATE PROCEDURE ledger.lenient
AS
-- Run only under the caller's ANSI_WARNINGS and ARITHABORT off (the lenient setup in runs.json), where an overflow, a division by zero or a math
-- function outside its domain is NULL instead of an error. Each reads only columns never null, so that is its only reason to
-- be NULL, and each is NULL on some lines and not on others: the seed's extremes overflow, units is 0 on some lines, and
-- unit_price is 0 or negative on some.
SELECT l.line_id,
       l.big + l.big AS big_doubled,
       -1.0 * l.big AS big_negated,
       l.big / 10 AS big_tenth,
       l.big * l.frac AS big_scaled,
       l.unit_price / l.units AS per_unit,
       l.unit_price % l.units AS price_mod,
       l.qty % l.units AS qty_mod,
       l.big % 1.0 AS big_mod_tenths,
       l.frac * l.flag AS frac_flagged,
       ROUND(l.unit_price, 1) AS round_price,
       ROUND(l.fee, 3) AS round_fee,
       ROUND(l.qty, -1) AS round_qty,
       FLOOR(l.fee) AS floor_fee,
       CEILING(l.fee) AS ceiling_fee,
       ABS(l.qty) AS abs_qty,
       ABS(l.fee) AS abs_fee,
       POWER(l.unit_price, 3) AS price_cubed,
       POWER(l.qty, 2) AS qty_squared,
       POWER(l.unit_price, 0.5) AS price_root,
       SQRT(l.unit_price) AS sqrt_price,
       LOG(l.unit_price) AS log_price,
       LOG10(l.fee) AS log10_fee,
       EXP(l.unit_price) AS exp_price
FROM ledger.line AS l
ORDER BY l.line_id;

SELECT l.account_id,
       AVG(l.big) AS avg_big,
       AVG(l.qty * 1000) AS avg_qty_thousands
FROM ledger.line AS l
GROUP BY l.account_id
ORDER BY l.account_id;
