-- A scalar function whose JSON reads a view: typed now that views are read inside functions.
CREATE FUNCTION cafe.open_tabs_json ()
RETURNS nvarchar(max)
AS
BEGIN
    RETURN (SELECT ot.tab_id, ot.opened_at FROM cafe.open_tab AS ot ORDER BY ot.tab_id FOR JSON PATH);
END
