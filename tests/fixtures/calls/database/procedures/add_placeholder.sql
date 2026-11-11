-- A helper that writes its caller's #picked: a row per oven, leaving weight_g and round_no null.
CREATE PROCEDURE calls.add_placeholder AS
SET NOCOUNT ON;
INSERT INTO #picked (batch_id, product) SELECT o.oven_id, o.label FROM calls.oven AS o;
