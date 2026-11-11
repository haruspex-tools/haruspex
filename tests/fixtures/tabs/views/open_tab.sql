-- The tabs not yet closed: read inside a function below.
CREATE VIEW cafe.open_tab
AS
SELECT t.tab_id, t.guest_id, t.opened_at
FROM cafe.tab AS t
WHERE t.closed_at IS NULL;
