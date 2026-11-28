CREATE PROCEDURE gate.admit
AS
INSERT INTO gate.entry (ticket, entered_at) VALUES (NEXT VALUE FOR ticket, SYSUTCDATETIME());
SELECT SCOPE_IDENTITY() AS entry_id, @@ROWCOUNT AS written, NEXT VALUE FOR gate.lane AS lane, NEXT VALUE FOR booth AS booth,
       NEXT VALUE FOR gate.pass AS pass, NEXT VALUE FOR gate.visit AS visit, NEXT VALUE FOR token AS token;
