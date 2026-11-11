-- A scalar function's JSON, built from a view: NULL when no tab is open.
CREATE PROCEDURE cafe.open_tabs_payload
AS
SET NOCOUNT ON;

SELECT JSON_QUERY(cafe.open_tabs_json()) AS open_tabs;
