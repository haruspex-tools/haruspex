-- Integers of every width, bit, a GUID, floating point, an OUTPUT parameter and a return code.
CREATE PROCEDURE till.ring_up
    @product_id int,
    @quantity smallint,
    @basket_id bigint,
    @discount tinyint = 0,
    @gift bit = 0,
    @token uniqueidentifier = NULL,
    @weight real = NULL,
    @ratio float = NULL,
    @sale_id int = NULL OUTPUT
AS
SET @sale_id = @product_id * 10 + @quantity;
SELECT @basket_id AS basket_id, @discount AS discount, @gift AS gift, @token AS token, @weight AS weight, @ratio AS ratio;
RETURN 0;
