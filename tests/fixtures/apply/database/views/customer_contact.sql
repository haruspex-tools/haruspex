-- A column list renames the view's columns.
CREATE VIEW mart.customer_contact (id, display_name, email)
AS
SELECT c.customer_id, c.name, c.email
FROM mart.customer AS c;
