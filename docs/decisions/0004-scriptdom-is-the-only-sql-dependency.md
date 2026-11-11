# ScriptDom is the only SQL dependency

**Date:** 9 Nov 2026

**Decision.** T-SQL is parsed with ScriptDom (`Microsoft.SqlServer.TransactSql.ScriptDom`, MIT), Microsoft's own parser.
DacFx is not used, and a DACPAC is to be read directly from its `model.xml`.

**Context.** DacFx is Microsoft's library for database projects and DACPACs, and the usual way to read one.

**Alternatives.**
- *DacFx.* Its licence lets it be redistributed only if the distributor makes end users accept terms that protect Microsoft
  at least as well, and indemnifies Microsoft against claims about the application. It also brings Azure Storage,
  Application Insights, MSBuild and SqlClient with it.
- *A parser of our own.* T-SQL's grammar is large and changes with each release; matching SQL Server's own reading of it is
  ScriptDom's whole purpose.

**Reason.** An indemnity is a personal liability that no maintainer of a free tool should take on. ScriptDom reads T-SQL as
SQL Server does, under a licence with no such term. A DACPAC is a zip holding XML, which can be read without DacFx.

**In the code:** [`src/Haruspex.SqlServer.Sources/README.md`](../../src/Haruspex.SqlServer.Sources/README.md), "What it
deliberately doesn't do"; CONTRIBUTING.md, "Dependencies".
