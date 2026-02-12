# Deployment Topology

Updated: 2026-01-12

```mermaid
---
config:
  theme: light
  layout: elk
  look: classic
---
flowchart LR
  Dev[Dev Machine] -->|publish zip| Temp[C:\\temp\\movex-api.zip]
  Temp -->|transfer| SRX[SRXWEBAPP1]

  subgraph SRXWEBAPP1
    IIS["IIS Site + App Pool (Movex.API)"] --> Kestrel[ASP.NET Core Host]
    Kestrel --> App[Movex.API.dll]
    App --> Logs["logs/ (daily files)"]
    App --> Health["/health (endpoint)"]
    ENV[("Environment Variables (User Secrets N/A in Prod)")] --> App
    Files["C:\inetpub\wwwroot\movex-api (binaries + transactions/)"] --> App
  end

  App -->|TCP| M3[(M3 MI Server)]
```

Notes:
- Use `scripts/pre-deployment-check.ps1` and `scripts/deploy-to-iis.ps1` on SRXWEBAPP1.
- Logs stored under `logs/` with rotation and retention.
- Health endpoint exposed for monitoring.
- Swagger UI/JSON is optional in production and requires `X-API-Key` when enabled.
