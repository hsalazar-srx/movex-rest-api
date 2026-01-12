# API Key Authentication Flow

Updated: 2026-01-12

```mermaid
flowchart LR
  A[Incoming HTTP request] --> B{Path == /health?}
  B -- Yes --> H[Allow anonymous]
  B -- No --> C[Read X-Admin-Key / X-API-Key]
  C --> D{Admin key present?}
  D -- Yes --> E[Validate against ApiKeys:Admin]
  D -- No --> F[Validate against ApiKeys:Primary]
  E -->|valid| G[Set admin context]
  F -->|valid| G[Set user context]
  E -->|invalid| X[401 Unauthorized]
  F -->|invalid| X[401 Unauthorized]
  G --> Y[Invoke next middleware / controller]

  subgraph Config
    K["ApiKeyOptions (IOptions)"]
  end
  K --- E
  K --- F
```

Notes:
- Admin key enables privileged operations (e.g., rotation endpoints) when exposed.
- Primary key protects application routes.
- Health endpoint is intentionally left open for monitoring.
