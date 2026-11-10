-- Two branches returning JSON objects of different keys, each with a key that is left out when null (note) or not
-- (name): neither variant's keys are a superset of the other's, so the Zod must keep each object's keys whichever
-- branch produced it.
CREATE PROCEDURE flow.notes_or_names
    @notes bit = 1
AS
SET NOCOUNT ON;

IF @notes = 1
    SELECT s.sale_id AS id, s.note FROM flow.sale AS s ORDER BY s.sale_id FOR JSON PATH;
ELSE
    SELECT r.region_id AS id, r.name FROM flow.region AS r ORDER BY r.region_id FOR JSON PATH;
