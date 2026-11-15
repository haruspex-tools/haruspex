-- JSON with a variable: a key in json_object and a document spelt in a literal leave what they build unknown, while the
-- object around them is typed.
CREATE PROCEDURE kiosk.stall_card @stall_id int
AS
SELECT s.stall_id AS id,
       json_object('$(Label)': s.kind) AS labelled,
       json_query('{"region":"$(Region)"}') AS origin
FROM kiosk.stall AS s
WHERE s.stall_id = @stall_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
GO
