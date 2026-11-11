CREATE PROCEDURE ledger.line_windows
AS
-- AVG and the statistical aggregates over windows: a partition of one line, and ROWS frames empty at the first and last line,
-- two of them over columns never null, so that the empty frame is their only reason to be NULL.
SELECT l.line_id,
       AVG(l.unit_price) OVER (PARTITION BY l.account_id) AS avg_price,
       AVG(l.qty) OVER (PARTITION BY l.account_id) AS avg_qty,
       STDEV(l.unit_price) OVER (PARTITION BY l.account_id) AS sd_price,
       VARP(l.score) OVER (PARTITION BY l.account_id) AS varp_score,
       VAR(l.units) OVER (ORDER BY l.line_id ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING) AS var_units_before,
       AVG(l.delta) OVER (ORDER BY l.line_id ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS avg_delta_after,
       AVG(l.unit_price) OVER (ORDER BY l.line_id ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS avg_price_after,
       STDEVP(l.units) OVER (ORDER BY l.line_id ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS sdp_units_after
FROM ledger.line AS l
ORDER BY l.line_id;
