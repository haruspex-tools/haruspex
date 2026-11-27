# A REST response is typed from a schema declared for its endpoint

**Date:** 27 Nov 2026; the IR's part is provisional until the coordinator agrees it and the maintainer settles it at the merge.

**Decision.** `sys.sp_invoke_external_rest_endpoint` is typed from what SQL Server 2025 was observed to do
(`RestEndpointObservationTests`), and the body of the response, which only the service decides, is typed from a JSON Schema the
config file declares for the endpoint (`endpoints`), recorded as declared, never as proven.

- **What the server decides, observed.** The procedure returns no result set. Its return code is 0 for a 2xx status and the status
  otherwise (302, 404, 500 and 599 observed); a redirect is not followed. A response of any status sets `@response`; a call that
  fails raises an error (a body that is a scalar or not JSON 11558, no endpoint 31608, a timeout 31629, plain HTTP 31610, an unknown
  method 31605, an Accept it refuses 31638, headers that are JSON but not an object 31620) and ends the batch, leaving
  `@response` as it was for a `CATCH`. One failure lets the caller go on: `@headers` text that isn't JSON (`ISJSON(@headers) = 0`,
  checked before the procedure's own `TRY`) raises 11558 and returns 1, sending nothing and leaving `@response` as it was. So a
  call surely sets `@response` only where every text `@headers` may be is JSON; elsewhere `@response` may keep its earlier value.
- **`@response`'s envelope.** With JSON accepted (no `@headers`, NULL, an object without Accept, or Accept of
  `application/json`, its key and value compared without case), `@response` is the object
  `{"response":{"status":{"http":{"code","description"}},"headers":{…}},"result":…}`: `code` an integer, `description` a string
  that may be empty, `headers` an object whose keys are data and whose values are strings (a key may come twice), and `result`
  the body, left out when the body is empty (204, HEAD, and a 200 with no body). With JSON accepted, `result` is always an object
  or an array. `Accept: application/xml` gives XML instead, and `text/plain`, `text/xml`, `text/html` and `text/csv` give the body
  as a JSON string. The text is cut short without an error when the caller's variable is shorter than `nvarchar(max)`, so only a
  variable of `nvarchar(max)` holds the envelope as JSON. The procedure builds the envelope with `JSON_MODIFY`, which leaves a key
  out where its value is NULL; every reply observed gave `code`, `description` and `headers`, and only `result` was left out.
- **No request header proves the reply is JSON.** On SQL Server 2025 CU10 only `Accept: application/xml` gives the XML form
  (`<output><response><status><http code=… description=…/>…</response><result>…</result></output>`), and an XML answer to a request
  that accepts JSON fails (11558) or leaves `result` out. But another build has given the XML form for a service's XML answer to a
  request with `Accept: application/json`, so the headers are read only for what a JSON reader finds. `@response` stays a string of
  its declared type wherever it is read as one, and holds the envelope only as JSON text (`JsonPiece`), which only a JSON function
  reads: `JSON_VALUE` (lax or strict), `json_query` and `OPENJSON` each raise 13609 on the XML form (observed), so a claim made
  through them, the envelope's nulls and a declared response alike, holds of every call that returns a value, and is wrong of none.
  `ISJSON` stays 0 or 1. The Accept that chooses the form is read decoded, as the server reads it (`application\/xml`, which
  `json_object` writes, asks for XML), while a header's value is sent as the text it is: `json_object` writes `/` as `\/`, and
  the service receives `\/`. No header's value is claimed, and `@headers` built by `json_object` is not read.
- **The declaration.** `"endpoints": [{ "url": "https://billing.example/plans/{id}", "method": "GET", "response":
  "schemas/plan.json" }]`. The response's schema describes `result` for every status, since an error's body is there too; a
  service whose error bodies differ declares a union.
- **Following `@url`, `@method` and `@headers`.** Each is read from the text the SQL builds (`Value.Pieces`): literals,
  concatenation, numbers converted to text, `CASE`, `COALESCE`, variables, and a procedure's parameter, which takes the pieces of
  the argument each call passes (`CallContext.Arguments`), so a wrapper procedure is typed at each call site, and is NULL only
  where the argument may be. A parameter's argument is passed only into a procedure that calls the REST procedure, itself or
  through others, and only such a procedure keeps the text of a variable shorter than `max` that always holds it whole, so no
  other walk changes.
- **How a URL matches.** The scheme and the host compare without case; everything after them compares exactly, the query and a
  fragment included, since the server sends the fragment as part of the path (observed). A `{name}` in the declared URL stands
  for one segment, or one value of the query: text that is never empty and never holds `/`, `?`, `#`, `&` or `=`, which the SQL
  writes as literal text or builds from a number. `method` compares without case, and a call that gives none sends `POST`
  (observed).
- **One declaration or none.** Where every text the call may send matches one and the same declaration, `result` is its
  schema; otherwise `result` is unknown, saying why: text built at run time, no declaration that matches, or more than one. The
  envelope stays typed either way.
- **The IR (0.36).** `endpoints` lists each declaration with the hash of every schema file it read, as `columns` does, so a
  changed declaration or schema file is a change `check` reports; each object whose results may hold a declared response lists
  an `endpoint-json` claim, its `via` the declaration (`GET https://billing.example/plans/{id}`). A wrapper's walk for one call
  records what that call read (`DeclarationReads`), so an object lists only the endpoints its own calls read.

**Context.** A procedure that calls a REST service and returns what it read had its results unknown, since the call's results
were never observed. What the service returns is the service's: nothing in the project can prove it.

**Alternatives.**
- *Type `result` as any JSON.* True, but it types nothing a reader of the result needs, and an undeclared JSON column is unknown,
  not any JSON (decision 0093).
- *Match every value of a wrapper's parameter across the project's call sites.* An application may call the wrapper with any
  value, so a match over the project's calls proves nothing of those. Typing each call with its own argument does.
- *Match a URL with its query string left out.* A query often changes the body (`?fields=`), so a declaration says which it
  covers.

**Reason.** The envelope, the return code and the absent result are the server's, so they are observed and claimed. The body is
the service's, so only a person can say what it is, once, for its endpoint, as a declared column's JSON is declared once for the
column (decision 0093). A match that isn't certain claims nothing, so a wrong match can only come from a wrong declaration,
which the generated validator rejects on real data.

**In the code:** `SystemProcedures` and `SystemProcedure.Arguments` in Semantics; `EndpointDeclaration` in the config layer;
`RestCalls`, `SentText`, `UrlTemplate`, `DeclaredEndpoint`, `DeclaredEndpoints`, `DeclarationReads`, `CallContext.Arguments` and
`Walker.Rest.cs` in Inference; `IrEndpointDeclaration`; the Inference README, "Calls to REST endpoints"; the IR README,
"Endpoint declarations".
