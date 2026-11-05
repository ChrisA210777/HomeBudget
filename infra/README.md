# Azure hosting (BUD-9, BUD-11)

`main.bicep` defines one environment. `dev.bicepparam` is the Dev environment, which runs entirely on free tiers (ADR-010):

| Resource | Tier | Cost |
|---|---|---|
| App Service plan + Linux web app | F1 (free) | £0 |
| Azure SQL database | Free offer (serverless), pauses for the rest of the month if the free allowance runs out | £0 |
| Log Analytics + Application Insights | Pay as you go, capped at 0.1 GB a day | £0 (within the free allowance) |
| User-assigned managed identity | | £0 |
| Budget | £1 a month, emails on any spend | £0 |

The app reaches the database with its managed identity; there are no database passwords. The Microsoft sign-in secret is an app setting (encrypted at rest by App Service), passed in from a GitHub secret at deploy time.

## Deploying

`.github/workflows/deploy.yml` runs after CI passes on `main`, or by hand from **Actions → Deploy → Run workflow**. It:

1. deploys the Bicep (safe to repeat)
2. opens the database firewall to the runner, applies EF Core migrations, makes sure the app's identity has a database user, and closes the firewall again
3. deploys the app and checks `/health`

## One-off setup

1. **Azure.** Open [Azure Cloud Shell](https://shell.azure.com) (Bash) and run:

   ```bash
   curl -fsSL https://raw.githubusercontent.com/ChrisA210777/HomeBudget/main/infra/bootstrap.sh -o bootstrap.sh
   bash bootstrap.sh
   ```

   It registers the resource providers, creates the `rg-homebudget-dev` resource group and an identity that GitHub Actions signs in as (OIDC: there's no secret to store or rotate), allowed to manage that resource group only. It prints the values for the next step.

2. **GitHub variables.** Go to **Settings → Secrets and variables → Actions → Variables** and add the repository variables the script prints (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `AZURE_DEPLOYER_OBJECT_ID`, `BUDGET_EMAIL`, `MICROSOFT_CLIENT_ID`).

3. **GitHub environment and secret.** Go to **Settings → Environments → New environment**, name it `dev`. In it, add the secret `MICROSOFT_CLIENT_SECRET` with your "Sign in with Microsoft" client secret (the same value as in your local user secrets).

4. **First deploy.** Run **Actions → Deploy → Run workflow**. The summary shows the app's address, e.g. `https://app-homebudget-dev-abc123.azurewebsites.net`.

5. **Sign-in redirect.** In the Entra app registration, go to **Authentication** and add `https://<that address>/signin-microsoft` as a Web redirect URI.

## Things to know

- **The first visit after a quiet spell is slow** (up to about a minute): the free web app and the free database both go to sleep when idle.
- **The database's free allowance** is 100,000 vCore-seconds a month. If it runs out, the database pauses until the 1st (by design, so it can't cost anything); the app shows an error until then. Usage is under the database's **Free amount remaining** metric in the portal.
- **Upgrading** is a parameter change: set `appServiceSku` to `B1` (Always On, no sleeping) or `S1` (deployment slots).
- **Looking at the data:** the database's Entra admin is the GitHub identity. To query it yourself in the portal's Query editor, temporarily set yourself as admin under the SQL server's **Microsoft Entra ID** page.
