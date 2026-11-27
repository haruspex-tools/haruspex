-- AUTO and ROOT as subqueries inside PATH: AUTO with its wrapper and ROOT nest as JSON, AUTO without it as text, and
-- json_query reads into the ROOT document. Each is null, so left out, over no orders.
CREATE PROCEDURE nest.nested_documents
AS
SET NOCOUNT ON;

SELECT (SELECT o.order_id, l.sku
        FROM nest.orders AS o
        JOIN nest.order_lines AS l ON l.order_id = o.order_id
        ORDER BY o.order_id, l.line_id
        FOR JSON AUTO) AS tree,
       (SELECT TOP (1) o.order_id, l.sku
        FROM nest.orders AS o
        JOIN nest.order_lines AS l ON l.order_id = o.order_id
        ORDER BY o.order_id, l.line_id
        FOR JSON AUTO, WITHOUT_ARRAY_WRAPPER) AS first_line_text,
       (SELECT o.order_id FROM nest.orders AS o ORDER BY o.order_id FOR JSON PATH, ROOT('orders')) AS rooted,
       json_query((SELECT o.order_id FROM nest.orders AS o ORDER BY o.order_id FOR JSON AUTO, ROOT('orders')), '$.orders') AS unrooted
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
