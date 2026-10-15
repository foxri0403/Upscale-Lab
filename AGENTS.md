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

- Current APK version: **0.7.1** (Android build **15**). Keep only this latest version entry; replace it after every generated APK and never append a version history here.
- Use semantic version numbers in `major.minor.patch` form for every new APK release.
- Increase the first number (`major`) by exactly 1 for a large feature or major product update.
- Increase the second number (`minor`) by exactly 1 for a smaller feature update.
- Increase the third number (`patch`) by exactly 1 for UI refinements, bug fixes, and other small adjustments.
- Reset the less-significant components when incrementing a higher component (for example, `0.3.4` → `0.4.0` or `1.0.0`).
- Increment the Android build number after every newly generated APK, independently of the semantic version component selected.
- Keep the APK filename and mobile documentation synchronized with the version in `mobile/pubspec.yaml`.
- Whenever an APK is generated, update the single current-version entry above, `mobile/pubspec.yaml`, and `mobile/README.md` together.
- Read the latest recorded version before assigning a new release; feature releases that also include fixes use the feature increment.
- Verify every generated release APK with Android `apksigner` before delivery.
- All release APKs must use the shared UpscaleLab team signing certificate defined below.
- Never use the Android debug signing certificate for a release APK.
- Never generate a new signing key automatically to replace the shared team signing certificate.

## Mandatory Android shared signing configuration

### 1. Shared team signing policy

All Android release APKs generated for UpscaleLab / LiveLayer **MUST use the existing shared team release signing key**.

This requirement applies to:

- APKs generated by AI coding agents.
- APKs generated manually by developers.
- APKs generated for internal team testing.
- APKs generated for distribution to other devices.
- APKs generated for subsequent application updates.

The signing configuration is defined in:

`mobile/android/app/build.gradle.kts`

The Android application ID must remain:

`com.livelayer.app`

The shared signing key alias is:

`upscalelab`

Do not change the application ID, signing certificate, or key alias without explicit authorization.

### 2. Required signing environment variables

The Android release signing configuration must read the following environment variables:

- `UPSCALELAB_KEYSTORE_PATH` — Absolute path to the shared release keystore.
- `UPSCALELAB_KEYSTORE_PASSWORD` — Password for the shared release keystore.

The current Android Gradle configuration uses the same password for the keystore and the key entry.

These variables must be accessed using `System.getenv()` in `mobile/android/app/build.gradle.kts`.

Never hardcode the actual keystore password, signing credentials, or secret values in source files.

The current local development setup stores the shared signing key outside the Git repository.

The expected local Windows path is:

`C:\UpscaleLab-Secrets\upscalelab-release.jks`

This path is only a local development convention. Other developers or build environments may use a different path through `UPSCALELAB_KEYSTORE_PATH`.

Do not assume that the keystore exists on every machine.

### 3. Signing security requirements

- Never commit `.jks`, `.keystore`, `.p12`, or `.pfx` files.
- Never commit `.env` files containing secrets.
- Never commit `key.properties` files containing signing credentials.
- Never print the signing password in terminal output, build logs, or AI responses.
- Never upload the shared signing key or password to GitHub.
- Never copy the shared signing key into the Git repository.
- Never disable signing verification to bypass a build error.
- Never replace the shared team signing configuration with Android debug signing.
- Never create a temporary signing certificate for release distribution.
- Never expose signing secrets in generated documentation or reports.
- Preserve the existing `.gitignore` security rules.

If the required environment variables or signing key are unavailable, stop the release build and report the missing configuration.

Do not silently substitute another signing key.

### 4. Required APK build procedure

Before generating an Android release APK, the AI agent or developer must perform the following steps.

**Step 1 — Check the application version**

Read the current version from:

`mobile/pubspec.yaml`

Follow the Mobile versioning rules above.

Ensure the version name and Android build number are correctly updated for a new release.

**Step 2 — Check the signing configuration**

Inspect:

`mobile/android/app/build.gradle.kts`

Confirm that the release build uses the shared team signing configuration and not the default debug signing configuration.

**Step 3 — Validate the signing environment**

Confirm that:

- `UPSCALELAB_KEYSTORE_PATH` is configured.
- `UPSCALELAB_KEYSTORE_PASSWORD` is configured.
- The keystore file exists at the configured path.
- The expected alias is `upscalelab`.

Do not print or disclose secret values during validation.

**Step 4 — Install Flutter dependencies**

From the `mobile` directory, execute:

```powershell
flutter pub get
```

**Step 5 — Generate the release APK**

Execute:

```powershell
flutter build apk --release
```

The expected default APK output is:

`mobile/build/app/outputs/flutter-apk/app-release.apk`

Do not distribute the APK until signing verification succeeds.

**Step 6 — Verify the signing certificate**

Use Android SDK `apksigner` to verify the APK.

Example:

```powershell
apksigner verify --print-certs "build/app/outputs/flutter-apk/app-release.apk"
```

The expected shared team signing certificate SHA-256 fingerprint is:

`D2:E2:DC:33:2F:34:DE:8E:A7:2A:0D:1B:4A:A2:C4:28:30:6B:A7:AE:45:29:F2:6E:D6:DD:50:40:18:57:2B:93`

The fingerprint comparison must be case-insensitive and ignore colon separators.

If the certificate fingerprint does not match, the APK must not be distributed.

**Step 7 — Verify APK metadata**

Confirm:

- Android application ID is `com.livelayer.app`.
- APK versionName matches `mobile/pubspec.yaml`.
- APK versionCode matches `mobile/pubspec.yaml`.
- The APK is signed with the expected shared team certificate.

**Step 8 — Finalize the release**

Only after all verification steps succeed:

- Use a filename consistent with the release version, such as `Upscale_Lab-0.7.1.apk`.
- Update `AGENTS.md` with the latest APK version.
- Update `mobile/README.md` with the latest release information.
- Report the generated APK location.
- Report whether signing verification succeeded.
- Never include the keystore or signing credentials in release artifacts.

### 5. Signing compatibility and future APK updates

The first release using the shared team signing certificate is:

- Version name: `0.7.1`
- Android build number: `15`
- Application ID: `com.livelayer.app`

All subsequent release APKs must continue using the exact same shared signing certificate.

Examples:

| Version | Android build number | Signing certificate |
|---|---:|---|
| 0.7.1 | 15 | Shared team certificate |
| 0.7.2 | 16 | Shared team certificate |
| 0.7.3 | 17 | Shared team certificate |
| 0.8.0 | 18 | Shared team certificate |

These are examples of possible future releases, not an instruction to generate them automatically.

When generating future APK versions:

- Increase Android versionCode.
- Update versionName according to the Mobile versioning rules.
- Keep the same application ID.
- Keep the same shared signing certificate.
- Verify the certificate fingerprint after every build.

Previous APKs signed with the old Android debug certificate cannot be updated in place to the new shared-signing release.

The shared-signing APK version `0.7.1` establishes the new signing identity for subsequent releases.

### 6. AI agent behavior

AI coding agents working on this repository must follow these rules whenever APK generation is requested.

- Read `AGENTS.md` before performing Android release build tasks.
- Inspect the existing Gradle signing configuration instead of replacing it.
- Use the shared signing key through the configured environment variables.
- Do not create a new signing key.
- Do not use debug signing for release builds.
- Do not bypass signing verification.
- Do not claim that a release APK is complete until signing verification succeeds.
- Do not modify unrelated backend or mobile functionality solely to generate an APK.
- Do not automatically publish APK files or create GitHub releases unless explicitly requested.

If the AI agent is running in a cloud environment without access to the shared keystore or required environment variables:

1. Do not generate a replacement signing key.
2. Do not create a differently signed release APK.
3. Explain that the shared signing key is unavailable in the current environment.
4. Prepare the source code and build configuration as needed.
5. Report that the release APK must be built in an environment with authorized access to the shared signing key.

These signing requirements take priority over any conflicting release build instructions elsewhere in the repository.

## References

- [README.md](README.md)
- [docs/architecture.md](docs/architecture.md)
- [docs/deployment.md](docs/deployment.md)
- [backend/src/UpscaleLab.Api/Program.cs](backend/src/UpscaleLab.Api/Program.cs)
- [mobile/pubspec.yaml](mobile/pubspec.yaml)
- [mobile/android/app/build.gradle.kts](mobile/android/app/build.gradle.kts)
- [mobile/README.md](mobile/README.md)
