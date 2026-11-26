CREATE PROCEDURE loom.check_drafts @card xml(DOCUMENT card_schema) = NULL AS
BEGIN
    -- In a module, a name without a schema is looked for in the module's schema, then in dbo.
    DECLARE @draft xml(draft_schema) = N'<draft shafts="8"/>';
    SELECT p.pattern_id, p.checked_draft, p.card, @draft AS draft, @card AS given_card,
        p.checked_draft.value('(/draft/@shafts)[1]', 'int') AS shafts
    FROM loom.pattern AS p;
END;
