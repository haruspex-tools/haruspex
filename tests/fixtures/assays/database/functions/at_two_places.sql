-- An inline function whose parameter has two places: an argument of more is rounded to it.
CREATE FUNCTION assay.at_two_places(@level decimal(10,2))
RETURNS TABLE
AS
RETURN SELECT @level AS level;
