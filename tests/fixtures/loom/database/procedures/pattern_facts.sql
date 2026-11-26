CREATE PROCEDURE loom.pattern_facts AS
SELECT
    p.pattern_id,
    p.draft.value('(/draft/@shafts)[1]', 'int') AS shafts,
    p.draft.value('(/draft/@sett)[1]', 'decimal(5,1)') AS sett,
    p.draft.value('(/draft/@title)[1]', 'nvarchar(10)') AS title,
    p.draft.value('(/draft/@title)[1]', 'NVARCHAR') AS initial,
    p.notes.value('(/notes/@by)[1]', '[varchar](20)') AS noted_by,
    p.draft.exist('/draft/treadling') AS has_treadling,
    p.notes.exist('/notes') AS has_notes,
    p.draft.query('/draft/threading') AS threading,
    CAST(CAST(p.pattern_id AS nvarchar(11)) AS xml) AS id_as_xml,
    CONVERT(xml, N'<empty/>') AS literal_xml,
    CONVERT(xml, N'<a>  <b/></a>', 1) AS kept_space,
    TRY_CONVERT(xml, p.name) AS tried,
    TRY_CAST(p.draft AS nvarchar(20)) AS draft_head,
    CAST(p.draft AS nvarchar(max)) AS draft_text,
    CAST(p.notes AS nvarchar(max)) AS notes_text,
    COALESCE(p.notes, p.draft) AS notes_or_draft,
    ISNULL(p.notes, N'<none/>') AS notes_or_none,
    CASE WHEN p.pattern_id = 1 THEN p.draft ELSE N'<other/>' END AS chosen
FROM loom.pattern AS p;
