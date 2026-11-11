-- A multi-statement function: what it returns is its declared table, whose key, NOT NULL and CHECK are enforced.
CREATE FUNCTION cafe.tab_lines (@tab_id int)
RETURNS @lines TABLE (
    line_no int NOT NULL PRIMARY KEY,
    title nvarchar(30) NOT NULL,
    amount decimal(19,2) NOT NULL CHECK (amount >= 0),
    code char(4) NULL
)
AS
BEGIN
    INSERT INTO @lines (line_no, title, amount, code)
    SELECT ROW_NUMBER() OVER (ORDER BY ti.tab_item_id), mi.title, ti.qty * mi.price, mi.short_code
    FROM cafe.tab_item AS ti
    JOIN cafe.menu_item AS mi ON mi.item_id = ti.item_id
    WHERE ti.tab_id = @tab_id;
    RETURN;
END
