-- A view's column is masked by what its query reads.
CREATE VIEW tailor.contact
AS
SELECT customer_id, email, phone FROM tailor.customer;
