# Response Parsing Flow

Updated: 2026-01-12

```mermaid
---
config:
  theme: light
  layout: elk
  look: classic
---
flowchart TD
  A[Raw response string] --> B{Any fields have StartIndex?}
  B -- Yes --> C[Absolute parsing]
  C --> C1[For each field with StartIndex]
  C1 --> C2[Extract substring StartIndex..Length]
  C2 --> C3["Convert type (numeric/integer/decimal/date/boolean/text)"]
  C3 --> R[Result dict]

  B -- No --> D[Sequential parsing]
  D --> D1[Sort fields by Position]
  D1 --> D2[Iterate, slice by Length]
  D2 --> D3[Convert type]
  D3 --> R

  R --> E{ReturnsList?}
  E -- Yes --> L["List<dict> (via line or fixed-length split)"]
  E -- No --> O[Dictionary<string, object?>]
```

Notes:
- Empty values convert to defaults: 0/false/empty string; zero-dates become null.
- Parsing is bounds-checked; out-of-range accesses log warnings and yield nulls.
- Output values are nullable to reflect missing/invalid fields gracefully.
