# Architecture Overview

Updated: 2026-01-12

```mermaid
flowchart LR
  subgraph Client
    U[User / MES Client]
  end

  subgraph Edge[SRXWEBAPP1 - IIS]
    I["IIS Site + App Pool (Movex.API)"]
  end

  subgraph API[ASP.NET Core API]
    M[ApiKeyAuthenticationMiddleware]
    C1[TransactionController]
    C2[SecurityController]
  end

  subgraph Services
    S1[TransactionService]
    S2[ItemValidationService]
  end

  subgraph Config[Configuration]
    TCS["TransactionConfigurationService (FileSystemWatcher + cache)"]
    VAL[ConfigurationValidator]
  end

  subgraph Infra[Infrastructure]
    CP[MvxConnectionPool]
    SW["MvxSockWrapper (pure .NET sockets)"]
    RES["Polly Resilience (retry + circuit breaker)"]
    LOG[Serilog File Logging]
  end

  subgraph External
    M3[(M3 MI Server)]
    FS[("transactions/ (JSON files)")]
    ENV[("Environment Variables (User Secrets in Dev)")]
    LOGDIR[("logs/ (directory)")]
  end

  U --> I --> M --> C1
  I --> M --> C2

  C1 --> S1
  C1 --> S2
  C2 -->|status/rotate| C2

  S1 --> TCS
  S1 --> RES
  S1 --> CP
  S1 -->|parse| RP[(M3ResponseParser)]

  S2 --> S1

  TCS --> VAL
  TCS <---> FS
  API --> LOG
  LOG --> LOGDIR
  API --> ENV

  CP --> SW --> M3

  style M fill:#f8f8ff,stroke:#666
  style C1 fill:#f0fff0,stroke:#666
  style C2 fill:#f0fff0,stroke:#666
  style S1 fill:#fff5ee,stroke:#666
  style S2 fill:#fff5ee,stroke:#666
  style CP fill:#eef,stroke:#666
  style SW fill:#eef,stroke:#666
  style TCS fill:#ffe,stroke:#666
  style VAL fill:#ffe,stroke:#666
  style RES fill:#efe,stroke:#666
  style LOG fill:#fee,stroke:#666
```

Notes:
- ApiKeyAuthenticationMiddleware protects all routes except `/health`.
- TransactionService builds MI requests from JSON config, uses connection pool and resilience, and parses responses.
- Configuration files in `transactions/` are validated and hot-reloaded.
- Serilog writes rolling logs to `logs/`.
