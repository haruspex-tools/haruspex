-- Reads a table of crane's project, and one of quay's that harbour does not reference itself.
CREATE PROCEDURE dock.list_cranes AS
SELECT c.crane_id, c.lift_t, b.name AS berth
FROM dock.crane AS c
JOIN dock.berth AS b ON b.berth_id = c.berth_id;
GO
-- Reads a table of another database, named through a SQLCMD variable.
CREATE PROCEDURE dock.crane_fees AS
SELECT c.crane_id, e.fee, e.state
FROM dock.crane AS c
JOIN [$(Tally)].ledger.entry AS e ON e.crane_id = c.crane_id;
GO
-- Reads a system view, which master.dacpac lists.
CREATE PROCEDURE dock.own_tables AS
SELECT o.name FROM sys.objects AS o WHERE o.type = 'U';
