-- An order as one JSON object whose arrays are assembled as text, each form read with json_query: integers, decimals and
-- bits aggregated, escaped strings each in quotes and with the quotes in the separator, quoted literals with concat_ws
-- through a function and a variable, and JSON fragments joined.
CREATE PROCEDURE bead.order_card
    @order_id int
AS
SET NOCOUNT ON;

-- NULL for an order with no channel, so its key is left out.
DECLARE @channels nvarchar(max) = (SELECT bead.channels(o.channel) FROM bead.[order] AS o WHERE o.order_id = @order_id AND o.channel IS NOT NULL);

SELECT o.order_id AS id,
       json_query(N'[' + coalesce((
           SELECT string_agg(convert(nvarchar(max), l.line_id), N',') WITHIN GROUP (ORDER BY l.line_id)
           FROM bead.order_line AS l WHERE l.order_id = o.order_id), N'') + N']') AS lineIds,
       json_query(concat(N'[', (
           SELECT string_agg(convert(nvarchar(max), l.price), N',') WITHIN GROUP (ORDER BY l.line_id)
           FROM bead.order_line AS l WHERE l.order_id = o.order_id), N']')) AS prices,
       json_query(concat(N'[', (
           SELECT string_agg(convert(nvarchar(max), l.gift), N',') WITHIN GROUP (ORDER BY l.line_id)
           FROM bead.order_line AS l WHERE l.order_id = o.order_id), N']')) AS gifts,
       json_query(N'[' + coalesce((
           SELECT string_agg(cast(N'"' + string_escape(t.tag, 'json') + N'"' AS nvarchar(max)), N',') WITHIN GROUP (ORDER BY t.tag)
           FROM bead.order_tag AS t WHERE t.order_id = o.order_id), N'') + N']') AS tags,
       -- Over no tags this is [""]: one empty string.
       json_query(concat(N'["', (
           SELECT string_agg(string_escape(t.tag, 'json'), N'","') WITHIN GROUP (ORDER BY t.tag)
           FROM bead.order_tag AS t WHERE t.order_id = o.order_id), N'"]')) AS tagNames,
       json_query(@channels) AS channels,
       json_query(concat(N'[', json_object('ref': o.ref), N',', json_array(o.order_id, o.channel NULL ON NULL), N']')) AS parts
FROM bead.[order] AS o
WHERE o.order_id = @order_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
GO
