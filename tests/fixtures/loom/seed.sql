-- Three patterns: one whose draft has every attribute the procedures read and notes by a weaver; one whose draft has none of
-- them and whose notes are an empty instance, which is not NULL; and one with no notes at all, whose name isn't XML. The first
-- and last drafts are longer than 20 characters, and the second isn't.
INSERT INTO loom.pattern (pattern_id, name, draft, notes)
VALUES
    (1, N'Twill', N'<draft shafts="4" sett="12.5" title="Twill"><threading><end colour="indigo"/><end colour="white"/></threading><treadling/></draft>', N'<notes by="Ada"/>'),
    (2, N'Plain', N'<draft/>', N''),
    (3, N'<rib', N'<draft shafts="2"><threading/></draft>', NULL);
