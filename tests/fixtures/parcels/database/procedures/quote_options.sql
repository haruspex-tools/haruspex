-- A parameter whose JSON is named in its procedure's script (decision 0143): its reads take the schema as declared, and stay
-- nullable, since a caller may send NULL, and a key the schema doesn't require may be left out.
CREATE PROCEDURE parcels.quote_options @options nvarchar(max)
AS
SELECT json_value(@options, '$.speed') AS speed, json_value(@options, '$.note') AS note, json_query(@options, '$.window') AS delivery_window;
GO
EXECUTE sp_addextendedproperty @name = N'haruspex.jsonSchema', @value = N'schemas/options.schema.json', @level0type = N'SCHEMA', @level0name = N'parcels', @level1type = N'PROCEDURE', @level1name = N'quote_options', @level2type = N'PARAMETER', @level2name = N'@options';
GO
