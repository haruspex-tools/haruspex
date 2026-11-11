# A diagram that doesn't parse

The Mermaid check's self-test expects the diagram below to be reported: its last arrow has no target.

```mermaid
flowchart LR
  Sources --> Model -->
```
