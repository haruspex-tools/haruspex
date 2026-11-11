-- A purchase joined to its lines: one row per line, so the purchase's key does not pass through.
CREATE VIEW mart.purchase_line_detail
AS
SELECT p.purchase_id, p.status, l.line_no, l.quantity
FROM mart.purchase AS p
JOIN mart.purchase_line AS l ON l.purchase_id = p.purchase_id;
