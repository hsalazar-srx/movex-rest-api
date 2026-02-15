# OpenAPI / Swagger

The API exposes Swagger UI and the OpenAPI JSON document when enabled:

- Swagger UI: `http://localhost:5000/swagger`
- OpenAPI JSON: `http://localhost:5000/swagger/v1/swagger.json`

Swagger availability is controlled by `Swagger:Enabled` (or `Swagger__Enabled` via environment variables). In production, Swagger endpoints require a valid `X-API-Key` header. In Development, Swagger is available without a key for convenience.

## Generate the committed spec

The repository keeps a generated spec at `docs/openapi.json`. Use the script below to refresh it after API changes:

1. From the project root, run `scripts/generate-openapi.ps1`.
2. Commit the updated `docs/openapi.json`.

The script runs in **Swagger generation mode**, which bypasses M3 configuration checks so you can generate the spec without secrets. It also ensures the Swashbuckle CLI is installed.

## Runtime requirement (Windows)

The Swashbuckle CLI (`swagger.exe`) targets **.NET 7.0** on Windows x64. If you only have .NET 6/8/9 installed, generation will fail with a missing framework error. Install the **.NET 7.0 runtime (x64)** before running the script.

## Environment flags

- `SWAGGER_GENERATION=true` enables spec generation mode.
- `ASPNETCORE_ENVIRONMENT=Development` is recommended for local generation.
- `Swagger__Enabled=true` enables Swagger UI/JSON when running in Production (requires `X-API-Key`).
