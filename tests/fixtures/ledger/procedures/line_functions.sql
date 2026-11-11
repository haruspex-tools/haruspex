CREATE PROCEDURE ledger.line_functions
AS
-- The numeric functions over each type, in the forms that cannot overflow on the seed's extremes (the others are in
-- ledger.lenient).
SELECT l.line_id,
       ROUND(l.unit_price, 2) AS round_kept,
       ROUND(l.unit_price, 0, 1) AS round_cut,
       ROUND(l.units, -1) AS round_units,
       ROUND(l.score, 1) AS round_score,
       ROUND(l.small_fee, 0) AS round_small_fee,
       ROUND(l.fee, 2) AS round_fee,
       FLOOR(l.unit_price) AS floor_price,
       CEILING(l.frac) AS ceiling_frac,
       FLOOR(l.small_fee) AS floor_small_fee,
       CEILING(l.score) AS ceiling_score,
       ABS(l.delta) AS abs_delta,
       ABS(l.big) AS abs_big,
       ABS(l.small_fee) AS abs_small_fee,
       SIGN(l.frac) AS sign_frac,
       SIGN(l.fee) AS sign_fee,
       SIGN(l.qty) AS sign_qty,
       SIGN(l.discount) AS sign_discount,
       POWER(l.discount, 2) AS discount_squared,
       POWER(l.units, 3) AS units_cubed,
       POWER(l.flag, 2) AS flag_squared,
       SQRT(l.units) AS sqrt_units,
       SQRT(ABS(l.score)) AS sqrt_score,
       SQUARE(l.big) AS square_big,
       SQUARE(l.score) AS square_score,
       EXP(l.units) AS exp_units,
       EXP(l.discount) AS exp_discount,
       LOG(l.units + 1) AS log_units,
       LOG(l.units + 1, 2) AS log2_units,
       LOG10(l.units + 1) AS log10_units
FROM ledger.line AS l
ORDER BY l.line_id;
