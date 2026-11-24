CREATE PROCEDURE tailor.customer_json
    @customer_id int
AS
SELECT customer_id, name, size_code, chest_cm, band
FROM tailor.customer
WHERE customer_id = @customer_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
