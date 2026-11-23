-- Returns its argument as lib.counter_code, its own schema's type, which cuts it to 6 characters; NULL for maps, which has no code.
CREATE FUNCTION lib.counter_code_of (@label varchar(20))
RETURNS counter_code
AS
BEGIN
    RETURN NULLIF(@label, 'maps');
END
