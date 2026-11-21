-- Each product's budget spread over working days and calls. The working days are a derived table's CASE of literals on the
-- product's credits (0 without credits) and the month's days a derived table's literal: inside the division the server
-- counts each by its type's 10 digits, not the literal's 2. The calls are a CTE's CASE reading a count, never folded.
CREATE VIEW store.budget
AS
WITH q AS (SELECT CASE WHEN COUNT(*) > 0 THEN COUNT(*) ELSE 1 END AS calls FROM store.tag)
SELECT p.product_id,
       p.budget / NULLIF(w.days, 0) AS per_day,
       CONVERT(decimal(19,6), p.credits) / q.calls AS per_call,
       p.budget * g.days / NULLIF(w.days, 0) AS per_month,
       p.budget / NULLIF(p.credits, 0) AS per_credit
FROM store.product AS p
CROSS APPLY (SELECT CASE WHEN p.credits > 0 THEN 21 ELSE 0 END AS days) AS w
CROSS JOIN (SELECT 30 AS days) AS g
CROSS JOIN q;
