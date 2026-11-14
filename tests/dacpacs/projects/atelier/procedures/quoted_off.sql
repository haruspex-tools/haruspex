-- With QUOTED_IDENTIFIER off (the project sets it for this file), "kiln" is a string, not a column.
CREATE PROCEDURE studio.quoted_off AS
SELECT "kiln" AS word;
