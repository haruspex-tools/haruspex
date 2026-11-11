-- A multi-statement function: its declared return table, many rows; and one line by its declared key, at most one.
CREATE PROCEDURE cafe.tab_lines_list
    @tab_id int
AS
SET NOCOUNT ON;

SELECT l.line_no, l.title, l.amount, l.code
FROM cafe.tab_lines(@tab_id) AS l
ORDER BY l.line_no;

SELECT l.title, l.amount
FROM cafe.tab_lines(@tab_id) AS l
WHERE l.line_no = 1;
