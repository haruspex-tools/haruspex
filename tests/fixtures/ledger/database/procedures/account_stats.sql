CREATE PROCEDURE ledger.account_stats
    @account_id int
AS
-- AVG and the statistical aggregates, grouped (a group of one line, a group whose rates are all NULL) and over one account
-- without GROUP BY (an account without lines gives NULL).
SELECT l.account_id,
       AVG(l.qty) AS avg_qty,
       AVG(l.units) AS avg_units,
       AVG(l.delta) AS avg_delta,
       AVG(l.unit_price) AS avg_price,
       AVG(l.rate) AS avg_rate,
       AVG(l.discount) AS avg_discount,
       AVG(l.fee) AS avg_fee,
       AVG(l.score) AS avg_score,
       AVG(DISTINCT l.frac) AS avg_frac,
       STDEV(l.unit_price) AS sd_price,
       STDEVP(l.rate) AS sdp_rate,
       VAR(l.fee) AS var_fee,
       VARP(l.score) AS varp_score,
       VAR(l.weight) AS var_weight
FROM ledger.line AS l
GROUP BY l.account_id
ORDER BY l.account_id;

SELECT AVG(l.unit_price) AS avg_price,
       STDEV(l.unit_price) AS sd_price,
       VARP(l.units) AS varp_units
FROM ledger.line AS l
WHERE l.account_id = @account_id;
