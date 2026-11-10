-- A view over two views, one with a column list.
CREATE PROCEDURE mart.open_contacts
AS
SET NOCOUNT ON;

SELECT oc.purchase_id,
       oc.placed_on,
       oc.display_name,
       oc.email
FROM mart.open_purchase_contact AS oc
ORDER BY oc.purchase_id;
