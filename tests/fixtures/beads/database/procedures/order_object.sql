-- An order as JSON objects assembled as text, each read with json_query: literal keys over a number and an escaped string, an
-- array of objects built as text inside it, keys left out by concat_ws when their item is NULL, keys that are data (one of them
-- written twice when two lines are gifts alike), and a key written twice in literals, which is unknown.
CREATE PROCEDURE bead.order_object
    @order_id int
AS
SET NOCOUNT ON;

SELECT o.order_id AS id,
       json_query(concat(
           N'{"orderId":', o.order_id,
           N',"ref":"', string_escape(o.ref, 'json'),
           N'","lines":[', (
               SELECT string_agg(N'{"lineId":' + convert(nvarchar(max), l.line_id) + N',"qty":' + convert(nvarchar(max), l.qty)
                                 + N',"price":' + convert(nvarchar(max), l.price) + N'}', N',') WITHIN GROUP (ORDER BY l.line_id)
               FROM bead.order_line AS l WHERE l.order_id = o.order_id),
           N'],"channels":{', concat_ws(N',',
               CASE WHEN o.channel IN ('retail', 'both') THEN N'"retail":true' END,
               CASE WHEN o.channel IN ('trade', 'both') THEN N'"trade":true' END),
           N'}}')) AS card,
       -- Keys from data: over no lines {}; order 1 writes the key "0" twice, and a client keeps one of its values.
       json_query(N'{' + coalesce((
           SELECT string_agg(N'"' + convert(nvarchar(max), l.gift) + N'":' + convert(nvarchar(max), l.line_id), N',') WITHIN GROUP (ORDER BY l.line_id)
           FROM bead.order_line AS l WHERE l.order_id = o.order_id), N'') + N'}') AS lineByGift,
       json_query(N'{"n":1,"n":' + convert(nvarchar(max), o.order_id) + N'}') AS twice
FROM bead.[order] AS o
WHERE o.order_id = @order_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
GO
