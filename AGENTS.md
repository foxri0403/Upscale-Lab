# AGENTS.md

## Project overview

This repository contains the backend for Upscale Lab: a .NET 8 ASP.NET Core API used by Windows (WPF) and Android (Flutter) clients. The main architecture and deployment notes live in [README.md](README.md), [docs/architecture.md](docs/architecture.md), and [docs/deployment.md](docs/deployment.md).

## Repository layout

- [backend/src/UpscaleLab.Api](backend/src/UpscaleLab.Api) — HTTP layer, controllers, authentication, Swagger, middleware, SignalR hub
- [backend/src/UpscaleLab.Application](backend/src/UpscaleLab.Application) — DTOs, service contracts, and application logic
- [backend/src/UpscaleLab.Domain](backend/src/UpscaleLab.Domain) — entities, enums, core domain types
- [backend/src/UpscaleLab.Infrastructure](backend/src/UpscaleLab.Infrastructure) — EF Core, JWT, BCrypt, S3, Replicate integrations
- [backend/tests/UpscaleLab.Tests](backend/tests/UpscaleLab.Tests) — xUnit tests

## Key conventions

- Keep API concerns isolated from storage and infrastructure concerns. Do not bypass the application service layer for business rules.
- Store image binaries in S3 and keep only metadata/object keys in PostgreSQL.
- Enforce per-user authorization by checking the authenticated `UserId` whenever a resource is owned by a user.
- Use UTC timestamps for mutable records when adding or updating entity state.
- Prefer existing patterns and naming conventions already used in the application and infrastructure projects.
- Do not commit secrets or environment-specific values. Use user secrets, environment variables, or local `.env` files only.

## Build and validation commands

Run the project from the backend folder:

```powershell
cd backend
dotnet restore
dotnet build UpscaleLab.sln
dotnet test UpscaleLab.sln
```

For local API startup, use the setup described in [README.md](README.md). If the repo-local SDK is not on `PATH`, add it for the current shell session before running commands:

```powershell
$env:PATH = "$PWD\.dotnet;$env:PATH"
```

## Security and configuration rules

The app requires configuration values from a secure provider, not hardcoded values:

- `ConnectionStrings:DefaultConnection`
- `Jwt:Secret`
- `AWS:S3BucketName`
- `Replicate:ApiToken`

These are expected to come from local user secrets, environment variables, or a local `.env` file. The repository includes a sample configuration in [backend/.env.example](backend/.env.example) and the setup in [README.md](README.md).

## Important implementation guardrails

- Keep authentication and JWT validation logic aligned with the current configuration model in [backend/src/UpscaleLab.Api/Program.cs](backend/src/UpscaleLab.Api/Program.cs).
- Maintain the API boundary: controllers should delegate to services instead of embedding data access logic directly.
- Preserve the current external behavior and authorization model when making changes to gallery, image, or device flows.
- Do not introduce automatic deployment or CI/CD changes without explicit instruction; the current documentation states that deployment is still manual.

## Testing expectations

- Prefer xUnit and keep tests close to real service behavior.
- When adding tests, use the existing in-memory EF Core patterns already present in [backend/tests/UpscaleLab.Tests/AuthServiceTests.cs](backend/tests/UpscaleLab.Tests/AuthServiceTests.cs).
- Validate the relevant backend behavior with the smallest focused test or build command before considering the change complete.

## Mobile versioning

- Use semantic version numbers in `major.minor.patch` form for every new APK release.
- Increment `major` for a large feature or major product update.
- Increment `minor` for a smaller feature update.
- Increment `patch` for bug fixes only.
- Reset the less-significant components when incrementing a higher component (for example, `0.3.4` → `0.4.0` or `1.0.0`).
- Increment the Android build number after every newly generated APK, independently of the semantic version component selected.
- Keep the APK filename and mobile documentation synchronized with the version in `mobile/pubspec.yaml`.

## References

- [README.md](README.md)
- [docs/architecture.md](docs/architecture.md)
- [docs/deployment.md](docs/deployment.md)
- [backend/src/UpscaleLab.Api/Program.cs](backend/src/UpscaleLab.Api/Program.cs)
