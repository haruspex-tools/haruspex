CREATE PROCEDURE sales.list_customers
AS
BEGIN
    SET NOCOUNT ON;

    SELECT c.customer_id AS id,
           c.name,
           c.email,
           c.region_code AS region,
           c.created_at AS createdAt
    FROM sales.customer AS c
    ORDER BY c.name;
END;
