-- SELECT * is bound when the view is created; haruspex assumes against the table as the project defines it.
CREATE VIEW mart.product_all
AS
SELECT *
FROM mart.product;
