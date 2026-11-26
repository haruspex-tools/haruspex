CREATE PROCEDURE loom.thread_colours AS
SELECT
    p.pattern_id,
    (SELECT e.c.value('@colour', 'nvarchar(20)') AS colour FROM p.draft.nodes('/draft/threading/end') AS e(c) FOR XML PATH(''), TYPE) AS colours_xml,
    (SELECT e.c.value('@colour', 'nvarchar(20)') AS colour FROM p.draft.nodes('/draft/threading/end') AS e(c) FOR XML PATH('')) AS colours_text,
    (SELECT e.c.value('@colour', 'nvarchar(20)') AS colour FROM p.draft.nodes('/draft/threading/end') AS e(c) FOR XML RAW('end'), ROOT('ends')) AS ends_text,
    STUFF((SELECT N', ' + e.c.value('@colour', 'nvarchar(20)') FROM p.draft.nodes('/draft/threading/end') AS e(c) FOR XML PATH('')), 1, 2, N'') AS colour_list,
    (SELECT COUNT(*) AS ends FROM p.draft.nodes('/draft/threading/end') AS e(c) FOR XML RAW, TYPE) AS end_count,
    (SELECT q.name FROM loom.pattern AS q WHERE q.pattern_id < p.pattern_id FOR XML AUTO, ELEMENTS) AS names_before
FROM loom.pattern AS p;
