CREATE PROCEDURE tailor.contacts
AS
SELECT c.customer_id, c.email, d.phone
FROM tailor.contact AS c
JOIN (SELECT customer_id, phone FROM tailor.customer) AS d ON d.customer_id = c.customer_id;
