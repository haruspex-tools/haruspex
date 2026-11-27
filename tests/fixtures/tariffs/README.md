# Fixture: tariffs

An invented billing database whose procedures call an invented REST service, `https://billing.example`, with
`sys.sp_invoke_external_rest_endpoint`, and whose config file declares the response of each endpoint they call
([decision 0140](../../../docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md)). The schema
files are in `schemas/`.

It is a project for the CLI, not deployed with the fixtures the differential tests share: their server has `external rest
endpoint enabled` off, as SQL Server 2025 has by default, and nothing to call. `RestEndpointObservationTests` observes the
procedure on a server of its own, with an endpoint of the tests' own.

| Procedure | What it shows |
|---|---|
| `billing.call_api` | A wrapper whose URL is built from its parameter `@path`: unknown in itself, since an application may pass any path |
| `billing.get_plan` | The wrapper called with a path built from the plan's number, which `{id}` stands for |
| `billing.list_plans` | The wrapper called with a literal path, its method its default |
| `billing.plan_summary` | A direct call read field by field: the return code, the envelope's status, never NULL, and the body's fields, each NULL where a reply may have no body |
| `billing.plans_by_tier` | A URL of either of two literals, each matching the declaration whose `{tier}` stands for a value of the query, read by `OPENJSON` |
| `billing.linked_page` | A URL read from a table: the body is unknown, saying why, and the envelope still typed |
