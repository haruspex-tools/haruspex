CREATE PROCEDURE loom.save_draft @pattern_id int, @draft xml, @notes xml = NULL AS
BEGIN
    UPDATE loom.pattern SET draft = COALESCE(@draft, draft), notes = @notes WHERE pattern_id = @pattern_id;
    SELECT @draft AS draft, @draft.value('(/draft/@shafts)[1]', 'tinyint') AS shafts, @notes.exist('/notes') AS has_notes;
END;
