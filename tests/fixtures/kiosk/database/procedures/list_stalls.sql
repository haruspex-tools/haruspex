-- A column whose CHECK set holds a variable (no set), beside one whose set holds none (a set), and a CASE of literals, one
-- holding a variable.
CREATE PROCEDURE kiosk.list_stalls
AS
SELECT s.stall_id,
       s.region,
       s.kind,
       CASE WHEN s.region = 'shared' THEN 'everywhere' ELSE '$(Region)' END AS reach
FROM kiosk.stall AS s;
GO
