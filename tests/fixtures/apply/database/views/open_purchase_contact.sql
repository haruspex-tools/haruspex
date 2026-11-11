-- A view over two views.
CREATE VIEW mart.open_purchase_contact
AS
SELECT o.purchase_id, o.placed_on, cc.display_name, cc.email
FROM mart.open_purchase AS o
JOIN mart.customer_contact AS cc ON cc.id = o.customer_id;
