-- One row by a table-valued parameter's unique key, and a value its CHECK closes.
CREATE PROCEDURE till.grade_of
    @tags till.tag_list READONLY,
    @tag varchar(20)
AS
SELECT t.grade FROM @tags AS t WHERE t.tag = @tag;
