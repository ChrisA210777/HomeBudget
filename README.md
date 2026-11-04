# Home Budget

A responsive, installable Progressive Web App for household budgeting, built with Blazor WebAssembly and ASP.NET Core on .NET 10.

- **Jira:** [BUD project](https://roughcommon.atlassian.net/jira/software/projects/BUD)
- **Docs:** [Home Budget App on Confluence](https://roughcommon.atlassian.net/wiki/spaces/SD/folder/285540353) (architecture, ADRs, domain model, roles)

## Solution layout

| Project | Purpose |
|---|---|
| `src/Budget.Client` | Blazor WebAssembly app (MudBlazor UI) |
| `src/Budget.Server` | ASP.NET Core host: serves the client, the API (`/api/*`) and sign-in |
| `src/Budget.Domain` | Entities, value objects and domain rules; no infrastructure dependencies |
| `src/Budget.Infrastructure` | EF Core, Identity stores, email |
| `src/Budget.Shared` | DTOs, routes and validation shared by client and server |
| `tests/*` | xUnit unit/integration tests and bUnit component tests |

Server code is organised by feature; see `src/Budget.Server/Features/README.md`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/) (for the local SQL Server)

## Run locally

```bash
# 1. Start SQL Server (local development only)
docker compose up -d

# 2. Run the app (serves the API and the Blazor client)
dotnet run --project src/Budget.Server --launch-profile https
```

Then open https://localhost:7180. The health check is at https://localhost:7180/health.

In Development the app creates the `HomeBudget` database and applies any pending migrations on startup, so SQL Server must be running first.

If the HTTPS development certificate isn't trusted yet, run `dotnet dev-certs https --trust`.

### Microsoft sign-in

Sign-in needs a Microsoft Entra app registration. Without one, the sign-in page says no providers are set up.

1. In the [Microsoft Entra admin center](https://entra.microsoft.com), go to **App registrations → New registration**:
   - **Supported account types:** *Accounts in any organizational directory and personal Microsoft accounts*. Personal accounts (outlook.com, hotmail.com) need this option.
   - **Redirect URI:** platform *Web*, `https://localhost:7180/signin-microsoft`
2. Copy the **Application (client) ID**. Under **Certificates & secrets**, create a client secret and copy its **Value**.
3. Store both as user secrets. They stay outside the repo:

   ```bash
   dotnet user-secrets set "Authentication:Microsoft:ClientId" "<client id>" --project src/Budget.Server
   dotnet user-secrets set "Authentication:Microsoft:ClientSecret" "<secret value>" --project src/Budget.Server
   ```

The first time someone signs in, a user is created for them.

## Build and test

```bash
dotnet build HomeBudget.slnx
dotnet test HomeBudget.slnx
```

The integration tests start their own SQL Server in Docker (Testcontainers), so Docker must be running. The `docker compose` database isn't used.

## Database migrations

EF Core migrations live in `src/Budget.Infrastructure/Persistence/Migrations`. After changing the model, add a migration in one of two ways:

- **Locally:**

  ```bash
  dotnet tool restore
  dotnet ef migrations add <Name> --project src/Budget.Infrastructure --startup-project src/Budget.Server --output-dir Persistence/Migrations
  ```

- **From GitHub:** go to Actions → *Add EF Core migration* → *Run workflow*, pick your branch and enter the name. The workflow commits the migration to that branch.

A test fails if the model has changes that aren't in a migration. CI also builds a self-contained migration bundle (`efbundle` artifact), which the deployment pipeline uses to apply migrations.

## Conventions

- Nullable reference types are on and warnings are treated as errors.
- Package versions are managed centrally in `Directory.Packages.props`.
- Branch from `main`, name branches `feature/BUD-<number>-short-description`, and open a pull request. CI must pass before merging.
- Reference the Jira key (e.g. `BUD-7`) in commit messages and PR titles so they link to the issue.
