# Environment Setup Guide — UAT & PROD

## Overview

The API runs as two isolated IIS applications on SRXWEBAPP1:

| Environment | App Pool          | Physical Path                          | Port |
|-------------|-------------------|----------------------------------------|------|
| UAT         | `Movex.API.UAT`   | `C:\inetpub\wwwroot\movex-api-uat\`   | 5001 |
| PROD        | `Movex.API.PROD`  | `C:\inetpub\wwwroot\movex-api-prod\`  | 5000 |

Each environment has its own:
- IIS App Pool (process isolation — a UAT crash cannot affect PROD)
- Physical directory (log files are automatically separated: `<AppPath>\logs\`)
- IIS environment variables (secrets never stored in files)
- `appsettings.{Environment}.json` on-server (non-sensitive settings only)

---

## One-Time IIS Provisioning

Run these steps **once** on SRXWEBAPP1 before the first deployment to each environment.

### UAT

```powershell
# 1. Create directory
New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\movex-api-uat" -Force
New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\movex-api-uat\logs" -Force

# 2. Copy published artifact files to the directory, then:

# 3. Copy the appsettings template and rename it
Copy-Item appsettings.UAT.json.template "C:\inetpub\wwwroot\movex-api-uat\appsettings.UAT.json"
# Edit AllowedOrigins if needed

# 4. Run pre-deployment check
.\scripts\pre-deployment-check.ps1 `
  -M3Host "<UAT_M3_IP>" `
  -M3Port "6300" `
  -AppPath "C:\inetpub\wwwroot\movex-api-uat" `
  -AppPoolName "Movex.API.UAT"

# 5. Deploy
.\scripts\deploy-to-iis.ps1 `
  -Environment "UAT" `
  -AppPoolName "Movex.API.UAT" `
  -AppName "movex-api-uat" `
  -AppPath "C:\inetpub\wwwroot\movex-api-uat" `
  -KestrelPort "5001" `
  -M3Host "<UAT_M3_IP>" `
  -M3Port "6300" `
  -M3User "<UAT_M3_USER>" `
  -M3Password (ConvertTo-SecureString "<UAT_M3_PASSWORD>" -AsPlainText -Force) `
  -ApiKey "<UAT_API_KEY>"
```

### PROD

```powershell
# 1. Create directory
New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\movex-api-prod" -Force
New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\movex-api-prod\logs" -Force

# 2. Copy published artifact files to the directory, then:

# 3. Copy the appsettings template and rename it
Copy-Item appsettings.Production.json.template "C:\inetpub\wwwroot\movex-api-prod\appsettings.Production.json"
# Edit AllowedOrigins to match your actual portal URL

# 4. Run pre-deployment check
.\scripts\pre-deployment-check.ps1 `
  -M3Host "150.3.2.100" `
  -M3Port "6300" `
  -AppPath "C:\inetpub\wwwroot\movex-api-prod" `
  -AppPoolName "Movex.API.PROD"

# 5. Deploy
.\scripts\deploy-to-iis.ps1 `
  -Environment "Production" `
  -AppPoolName "Movex.API.PROD" `
  -AppName "movex-api-prod" `
  -AppPath "C:\inetpub\wwwroot\movex-api-prod" `
  -KestrelPort "5000" `
  -M3Host "150.3.2.100" `
  -M3Port "6300" `
  -M3User "<PROD_M3_USER>" `
  -M3Password (ConvertTo-SecureString "<PROD_M3_PASSWORD>" -AsPlainText -Force) `
  -ApiKey "<PROD_API_KEY>" `
  -AdminKey "<PROD_ADMIN_KEY>"
```

---

## Environment Variables Reference

All sensitive values are injected as IIS App-level environment variables by `deploy-to-iis.ps1`.
They appear in IIS Manager → Sites → Default Web Site → `movex-api-{env}` → Configuration Editor
→ `system.webServer/aspNetCore/environmentVariables`.

> **Important:** ASP.NET Core requires `__` (double underscore) as the config section separator.
> `M3__HOST` binds to the `M3:HOST` JSON key. Single underscore (`M3_HOST`) is silently ignored.

| IIS Variable Name             | Config key                  | Description                        |
|-------------------------------|-----------------------------|------------------------------------|
| `ASPNETCORE_ENVIRONMENT`      | —                           | `UAT` or `Production`              |
| `ASPNETCORE_URLS`             | —                           | `http://+:5001` (UAT) / `:5000`    |
| `M3__HOST`                    | `M3:HOST`                   | M3 server IP / hostname            |
| `M3__PORT`                    | `M3:PORT`                   | M3 MI port (typically 6300)        |
| `M3__USER`                    | `M3:USER`                   | M3 API username                    |
| `M3__PASSWORD`                | `M3:PASSWORD`               | M3 API password                    |
| `M3__DEFAULT_COMPANY`         | `M3:DEFAULT_COMPANY`        | M3 company number (default: 300)   |
| `ApiKeys__Primary`            | `ApiKeys:Primary`           | Primary API key (X-API-Key header) |
| `ApiKeys__Admin`              | `ApiKeys:Admin`             | Admin key (key rotation endpoint)  |
| `Swagger__Enabled`            | `Swagger:Enabled`           | `true` (UAT) / `false` (PROD)      |
| `Logging__LogLevel__Default`  | `Logging:LogLevel:Default`  | `Debug` (UAT) / `Information` (PROD) |

---

## Branching & Promotion Strategy

```
feature/* ──→  develop ──→  main
                  │              │
               (UAT deploy)  (PROD deploy, manual approval)
```

- All feature work branches from `develop`
- `develop` → UAT (continuous)
- `develop` → `main` via PR after UAT sign-off → PROD (gated)

---

## GitHub Actions CI/CD (Future — requires self-hosted runner on SRXWEBAPP1)

Once a self-hosted GitHub Actions runner is installed on SRXWEBAPP1, add these two workflow files.

### Setup: Install Self-Hosted Runner

On SRXWEBAPP1, follow the GitHub documentation:
`Settings → Actions → Runners → New self-hosted runner → Windows`

Then in GitHub: `Settings → Environments` — create `UAT` and `PROD` environments.
Add `required reviewers` to the `PROD` environment.
Add environment-scoped secrets (UAT secrets cannot be read by PROD runners and vice versa):

| Secret name (UAT env) | Secret name (PROD env) |
|-----------------------|------------------------|
| `M3_HOST`             | `M3_HOST`              |
| `M3_PORT`             | `M3_PORT`              |
| `M3_USER`             | `M3_USER`              |
| `M3_PASSWORD`         | `M3_PASSWORD`          |
| `API_KEY`             | `API_KEY`              |
| `ADMIN_KEY`           | `ADMIN_KEY`            |

### `.github/workflows/deploy-uat.yml`

```yaml
name: Deploy to UAT

on:
  push:
    branches: [develop]

jobs:
  deploy:
    runs-on: self-hosted
    environment: UAT

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Publish
        run: dotnet publish -c Release -o C:\temp\movex-api-uat-release

      - name: Copy artifact to deployment directory
        shell: pwsh
        run: |
          Stop-WebAppPool -Name "Movex.API.UAT" -ErrorAction SilentlyContinue
          Start-Sleep -Seconds 3
          Copy-Item -Path "C:\temp\movex-api-uat-release\*" `
            -Destination "C:\inetpub\wwwroot\movex-api-uat\" -Recurse -Force

      - name: Deploy to IIS (UAT)
        shell: pwsh
        run: |
          .\scripts\deploy-to-iis.ps1 `
            -Environment "UAT" `
            -AppPoolName "Movex.API.UAT" `
            -AppName "movex-api-uat" `
            -AppPath "C:\inetpub\wwwroot\movex-api-uat" `
            -KestrelPort "5001" `
            -M3Host "${{ secrets.M3_HOST }}" `
            -M3Port "${{ secrets.M3_PORT }}" `
            -M3User "${{ secrets.M3_USER }}" `
            -M3Password (ConvertTo-SecureString "${{ secrets.M3_PASSWORD }}" -AsPlainText -Force) `
            -ApiKey "${{ secrets.API_KEY }}"
```

### `.github/workflows/deploy-prod.yml`

```yaml
name: Deploy to PROD

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: self-hosted
    environment: PROD   # requires manual approval from configured reviewers

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Publish
        run: dotnet publish -c Release -o C:\temp\movex-api-prod-release

      - name: Copy artifact to deployment directory
        shell: pwsh
        run: |
          Stop-WebAppPool -Name "Movex.API.PROD" -ErrorAction SilentlyContinue
          Start-Sleep -Seconds 3
          Copy-Item -Path "C:\temp\movex-api-prod-release\*" `
            -Destination "C:\inetpub\wwwroot\movex-api-prod\" -Recurse -Force

      - name: Deploy to IIS (PROD)
        shell: pwsh
        run: |
          .\scripts\deploy-to-iis.ps1 `
            -Environment "Production" `
            -AppPoolName "Movex.API.PROD" `
            -AppName "movex-api-prod" `
            -AppPath "C:\inetpub\wwwroot\movex-api-prod" `
            -KestrelPort "5000" `
            -M3Host "${{ secrets.M3_HOST }}" `
            -M3Port "${{ secrets.M3_PORT }}" `
            -M3User "${{ secrets.M3_USER }}" `
            -M3Password (ConvertTo-SecureString "${{ secrets.M3_PASSWORD }}" -AsPlainText -Force) `
            -ApiKey "${{ secrets.API_KEY }}" `
            -AdminKey "${{ secrets.ADMIN_KEY }}"
```
