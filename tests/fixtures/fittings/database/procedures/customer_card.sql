CREATE PROCEDURE tailor.customer_card
    @customer_id int
AS
SELECT customer_id, name, email, phone, band, size_code, chest_cm, born, joined, swatch, is_trade, note
FROM tailor.customer
WHERE customer_id = @customer_id;
