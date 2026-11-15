-- An UPDATE whose SET and WHERE name the target by its schema and name; OUTPUT reads it through inserted, as it must.
CREATE PROCEDURE lib.full_name_return @loan_id int, @returned_on date
AS
SET NOCOUNT ON;

UPDATE lib.loan
SET lib.loan.returned_on = @returned_on
OUTPUT inserted.loan_id, inserted.returned_on, deleted.returned_on AS was_returned
WHERE lib.loan.loan_id = @loan_id;
