CREATE PROCEDURE sales.get_order
    @order_id int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT o.order_id AS id,
           o.reference,
           o.placed_at AS placedAt,
           o.shipped_at AS shippedAt,
           o.delivery_time AS deliveryTime,
           o.status,
           o.total,
           o.is_priority AS isPriority,
           c.customer_id AS [customer.id],
           c.name AS [customer.name],
           c.email AS [customer.email],
           sp.salesperson_id AS 'salesperson.id',
           sp.display_name AS 'salesperson.name',
           sp.hired_on AS 'salesperson.hiredOn',
           json_query((
               SELECT l.order_line_id AS id,
                      l.quantity,
                      l.unit_price AS unitPrice,
                      l.note,
                      p.sku AS [product.sku],
                      p.name AS [product.name],
                      p.weight_kg AS [product.weightKg]
               FROM sales.order_line AS l
               JOIN sales.product AS p ON p.product_id = l.product_id
               WHERE l.order_id = o.order_id
               ORDER BY l.order_line_id
               FOR JSON PATH
           )) AS lines
    FROM sales.[order] AS o
    JOIN sales.customer AS c ON c.customer_id = o.customer_id
    LEFT JOIN hr.salesperson AS sp ON sp.salesperson_id = o.salesperson_id
    WHERE o.order_id = @order_id
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END;
