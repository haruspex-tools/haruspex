# The SARIF schema

`sarif-2.1.0.json` is the JSON Schema of SARIF 2.1.0 that `ReportFormatTests` validates `--format sarif`'s output against,
through `tests/emit-check/src/validate.mjs`. It isn't haruspex's: it is Microsoft's, from the SARIF SDK, under the MIT
licence, whose text is `LICENSE` here. Nothing builds or ships either file.

| | |
|---|---|
| Repository | [microsoft/sarif-sdk](https://github.com/microsoft/sarif-sdk) |
| Commit | `724b6a8869142628e2f060115e506f9609a98f73` (14 Nov 2026) |
| `sarif-2.1.0.json` | `src/Sarif/Schemata/sarif-2.1.0.json`, unchanged; SHA-256 `0de6555c956d0e2081bb40f0b877f275634cba28a6f8954e6d858a49138143d6` |
| `LICENSE` | `LICENSE`, unchanged; SHA-256 `5ca93c6ae1bff4692a04550366277550eb7c149fcc639abd9ee51305d1836ca7` |

It is the SDK's copy of the schema OASIS publishes, moved to draft 2020-12, the draft `validate.mjs` reads both schemas in;
OASIS's own copies name older drafts (draft-07 in the 2.1.0 standard, draft-04 in its errata).

To check the copy, or to move to a newer commit, download both files from the commit and compare (here with the commit
above):

```sh
commit=724b6a8869142628e2f060115e506f9609a98f73
curl -sSf "https://raw.githubusercontent.com/microsoft/sarif-sdk/$commit/src/Sarif/Schemata/sarif-2.1.0.json" | cmp - tests/sarif/sarif-2.1.0.json
curl -sSf "https://raw.githubusercontent.com/microsoft/sarif-sdk/$commit/LICENSE" | cmp - tests/sarif/LICENSE
```

A newer copy is committed with this page's commit and hashes changed to match, and the SARIF snapshots checked against it.
