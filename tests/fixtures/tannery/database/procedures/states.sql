CREATE PROCEDURE dye.states
AS
SELECT v.state FROM dye.vat AS v
UNION
SELECT t.state FROM dye.rinse_tank AS t;
GO
