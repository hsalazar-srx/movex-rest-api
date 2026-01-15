# Transaction Configuration Flow

Updated: 2026-01-12

```mermaid
---
config:
  theme: light
  layout: elk
  look: classic
---
flowchart TD
  A[Startup] --> B[TransactionConfigurationService]
  B --> C["LoadConfigurations()"]
  C --> D["Enumerate transactions/*.json (excluding schema.json)"]
  D --> E[Validate JSON with ConfigurationValidator]
  E -->|Invalid| X[Log errors and skip]
  E -->|Valid| F[Deserialize ProgramConfig]
  F --> G[Cache in ConcurrentDictionary]
  G --> H[Ready for GetPrograms/GetTransaction]

  subgraph Watcher
    W["FileSystemWatcher (Created/Changed/Deleted)"]
  end
  W --> C
  Watch[On Delete] --> R["TryRemove(programName)"]

  subgraph API Usage
    U1["GetPrograms()"] --> S1[List of programs]
    U2["GetTransaction(prog, tx)"] --> S2[TransactionDefinition]
  end
  H --> U1
  H --> U2
```

Notes:
- Files are hot-reloaded; changes propagate without restart.
- Deleted files remove corresponding entries from the cache.
- Validator enforces required fields and valid types.
