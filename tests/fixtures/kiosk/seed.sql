-- Seed for tests/fixtures/kiosk. Invented data. The differential tests deploy the scripts as written, as a tool that doesn't
-- replace SQLCMD variables would, so '$(Region)' is stored as those nine characters; SqlcmdVariableObservationTests deploys the
-- same kind of text with values put in.
INSERT INTO kiosk.stall (stall_id, region, kind)
VALUES (1, '$(Region)', 'food'),
       (2, 'shared', 'craft');
