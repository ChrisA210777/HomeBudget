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

If the HTTPS development certificate isn't trusted yet, run `dotnet dev-certs https --trust`.

## Build and test

```bash
dotnet build HomeBudget.slnx
dotnet test HomeBudget.slnx
```

## Conventions

- Nullable reference types are on and warnings are treated as errors.
- Package versions are managed centrally in `Directory.Packages.props`.
- Branch from `main`, name branches `feature/BUD-<number>-short-description`, and open a pull request. CI must pass before merging.
- Reference the Jira key (e.g. `BUD-7`) in commit messages and PR titles so they link to the issue.
