-- A joined view filtered on the parent's key: any number of rows (two for purchase 10).
CREATE PROCEDURE mart.purchase_lines
    @purchase_id int
AS
SET NOCOUNT ON;

SELECT d.line_no, d.quantity, d.status
FROM mart.purchase_line_detail AS d
WHERE d.purchase_id = @purchase_id
ORDER BY d.line_no;
