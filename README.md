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

### Google sign-in (optional)

"Sign in with Google" appears once a Google OAuth client is configured (ADR-015). Without one, only Microsoft is offered. The same client is used locally, through a phone tunnel and in Azure.

**1. Create the OAuth client (once)**

1. In the [Google Cloud console](https://console.cloud.google.com), create a project (e.g. "Home Budget").
2. Go to **APIs & Services → OAuth consent screen** (called **Google Auth Platform** in newer accounts). Choose **External**, fill in the app name and your email, and add the scopes `openid`, `email` and `profile`. While it's in **Testing**, only the Google accounts added as **test users** can sign in; add yours, or publish the app.
3. Go to **Credentials → Create credentials → OAuth client ID**, choose **Web application**, and add every address the app is used from as an **Authorised redirect URI**:
   - `https://localhost:7180/signin-google` (local)
   - `https://<tunnel host>/signin-google` (your phone, see below)
   - `https://<Azure app address>/signin-google` (Azure, from the Deploy run's summary)
4. Copy the **Client ID** and **Client secret**. Google shows the secret in full only once; put it only in the places below, never in the repo.

**2. Locally**

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "<client id>" --project src/Budget.Server
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client secret>" --project src/Budget.Server
```

Restart the app and the Google button appears.

**3. In Azure**

1. In GitHub, go to **Settings → Secrets and variables → Actions → Variables** and add `GOOGLE_CLIENT_ID` (it isn't secret).
2. Run **Actions → Deploy → Run workflow**, or merge to `main`. The deploy sets `Authentication__Google__ClientId`, and points `Authentication__Google__ClientSecret` at Key Vault.
3. Put the secret in Key Vault as `google-client-secret` and restart the app (BUD-147). See step 5 in [infra/README.md](infra/README.md).

**Good to know**

- Google verifies email addresses, so someone signing in with Google whose email already belongs to a user (say, from Microsoft sign-in) joins that user rather than becoming a new one. Microsoft emails aren't treated as verified, so the reverse doesn't happen automatically.
- `redirect_uri_mismatch` from Google means the address you're using isn't one of the redirect URIs in step 1.3. Check `https`, the host and the port match exactly.

### Try it on your phone

Sign-in needs HTTPS and a redirect URI registered in Entra, so a phone can't just browse to your PC's IP address. Use a tunnel instead:

1. Install the dev tunnels CLI (`winget install Microsoft.devtunnel`) and run `devtunnel user login`.
2. Start the app, then run `devtunnel host -p 7180 --protocol https --allow-anonymous`. It prints a URL such as `https://abc123-7180.uks1.devtunnels.ms`.
3. In the Entra app registration, go to **Authentication** and add `https://<that host>/signin-microsoft` as a Web redirect URI. If you use Google sign-in, add `https://<that host>/signin-google` to the Google OAuth client too.
4. Open the URL on your phone. It's HTTPS, so you can also install the app from the browser menu.

In Development the app trusts the tunnel's `X-Forwarded-*` headers for `*.devtunnels.ms`, `*.trycloudflare.com` and ngrok hosts (`ForwardedHeaders` in `appsettings.Development.json`). This means sign-in sends you back to the tunnel, not to `localhost`.

## Build and test

```bash
dotnet build HomeBudget.slnx
dotnet test HomeBudget.slnx
```

The integration tests start their own SQL Server in Docker (Testcontainers), so Docker must be running. The `docker compose` database isn't used.

End-to-end and accessibility tests (Playwright with axe-core) live in `tests/e2e`. They run the published app at phone and desktop sizes in the CI `e2e` job; see [tests/e2e/README.md](tests/e2e/README.md) to run them locally.

A security scan (OWASP ZAP's baseline scan) runs against the published app, signed in, in the CI `security-scan` job. It fails on medium or high alerts that haven't been accepted; see [tests/security/README.md](tests/security/README.md).

## Database migrations

EF Core migrations live in `src/Budget.Infrastructure/Persistence/Migrations`. After changing the model, add a migration in one of two ways:

- **Locally:**

  ```bash
  dotnet tool restore
  dotnet ef migrations add <Name> --project src/Budget.Infrastructure --startup-project src/Budget.Server --output-dir Persistence/Migrations
  ```

- **From GitHub:** go to Actions → *Add EF Core migration* → *Run workflow*, pick your branch and enter the name. The workflow commits the migration to that branch.

A test fails if the model has changes that aren't in a migration. CI also builds a self-contained migration bundle (`efbundle` artifact), which the deployment pipeline uses to apply migrations.

## Hosting

Dev runs in Azure on free tiers, deployed by GitHub Actions after CI passes on `main`. The templates, one-off setup and trade-offs are in [infra/README.md](infra/README.md).

## Documentation

Design and operational documentation is in Confluence (space SD, Home Budget folder): architecture, ADRs, the domain model, roles, the export format, UI conventions, local development, deploying to Azure and monitoring. [CLAUDE.md](CLAUDE.md) lists which page covers what; keep the relevant page up to date with every change.

## Conventions

- UI work follows the UI conventions in [CLAUDE.md](CLAUDE.md) and the UI Conventions page in Confluence.
- Nullable reference types are on and warnings are treated as errors.
- Package versions are managed centrally in `Directory.Packages.props`.
- Branch from `main`, name branches `feature/BUD-<number>-short-description`, and open a pull request. CI must pass before merging.
- Reference the Jira key (e.g. `BUD-7`) in commit messages and PR titles so they link to the issue.
