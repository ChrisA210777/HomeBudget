# Azure hosting (BUD-9, BUD-11)

`main.bicep` defines one environment. `dev.bicepparam` is the Dev environment, which runs entirely on free tiers (ADR-010):

| Resource | Tier | Cost |
|---|---|---|
| App Service plan + Linux web app | F1 (free) | £0 |
| Azure SQL database | Free offer (serverless), pauses for the rest of the month if the free allowance runs out | £0 |
| Log Analytics + Application Insights | Pay as you go, capped at 0.1 GB a day | £0 (within the free allowance) |
| User-assigned managed identity | | £0 |
| Key Vault (BUD-147) | Standard: no monthly fee, $0.03 per 10,000 operations; the app reads two secrets about once a day | £0 (well under 1p a month) |
| Budget | £1 a month, emails on any spend | £0 |
| SQL auditing (BUD-165) | Server audit to the Log Analytics workspace above; the app's own queries are left out | £0 (a few MB a month, within the cap and free allowance) |
| Action group + activity log alert (BUD-165) | Emails the budget contacts when the database's admin changes | £0 (activity log alerts and the first 1,000 emails a month are free) |

The app reaches the database with its managed identity; there are no database passwords. The sign-in client secrets (Microsoft and Google) live only in Key Vault (BUD-147). You set them there once; the app reads them with its managed identity through Key Vault references, and the deploy never sees them. Access is by Azure roles:

| Who | Role | On |
|---|---|---|
| The app's identity | Key Vault Secrets User (read secrets) | the vault (granted by Bicep) |
| You, from `bootstrap.sh` | Key Vault Secrets Officer (add and change secrets) | the resource group |
| The GitHub deploy identity | Contributor, plus Role Based Access Control Administrator limited to granting *Key Vault Secrets User* to anyone but itself | the resource group |

The deploy identity can't read the secrets itself. It can deploy code that runs as the app, so it's trusted, as it was before.

## Deploying

`.github/workflows/deploy.yml` runs after CI passes on `main`, or by hand from **Actions → Deploy → Run workflow** on `main`. It:

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

3. **GitHub environment.** Go to **Settings → Environments → New environment**, name it `dev`. Under **Deployment
   branches and tags**, choose **Selected branches and tags** and add `main` (BUD-164): Azure trusts any workflow run in
   the `dev` environment, and the deploy identity is the database's admin, so only reviewed code on `main` may use it.
   For Google sign-in, also add the repository variable `GOOGLE_CLIENT_ID` (step 2); without it, Google sign-in is simply off. Creating the Google OAuth client is described in the main README's *Google sign-in* section.

4. **First deploy.** Run **Actions → Deploy → Run workflow**. The summary shows the app's address, e.g. `https://app-homebudget-dev-abc123.azurewebsites.net`, and the Key Vault's name, e.g. `kv-homebudget-dev-abc123`.

5. **Sign-in secrets.** In Cloud Shell, put the secrets in the vault and restart the app so it reads them (`az keyvault secret set` asks for nothing else; the values are your "Sign in with Microsoft" client secret and, if you use it, your Google client secret):

   ```bash
   VAULT=kv-homebudget-dev-abc123   # from the deploy summary
   az keyvault secret set --vault-name "$VAULT" --name microsoft-client-secret --value '<Microsoft client secret>' --output none
   az keyvault secret set --vault-name "$VAULT" --name google-client-secret --value '<Google client secret>' --output none
   az webapp restart --resource-group rg-homebudget-dev --name <the app's name>
   ```

   Until they're set, signing in fails. To change a secret later (e.g. when it expires), set it again and restart the app.

6. **Sign-in redirect.** In the Entra app registration, go to **Authentication** and add `https://<that address>/signin-microsoft` as a Web redirect URI. For Google, add `https://<that address>/signin-google` to the OAuth client's redirect URIs.

## Moving an existing environment's secrets to Key Vault (BUD-147, once)

If Dev was set up before Key Vault:

1. Re-run `bootstrap.sh` in Cloud Shell (step 1). It's safe to repeat; it adds the Key Vault roles.
2. Merge the change, and let the deploy run. It creates the vault, and the app's sign-in secrets now point at it.
3. Set the secrets in the vault and restart the app (step 5). Signing in fails between the deploy and this step, so do it straight away.
4. Delete the `MICROSOFT_CLIENT_SECRET` and `GOOGLE_CLIENT_SECRET` secrets from the GitHub `dev` environment. Nothing uses them now.

## Things to know

- **The first visit after a quiet spell is slow** (up to about a minute): the free web app and the free database both go to sleep when idle.
- **The database's free allowance** is 100,000 vCore-seconds a month. If it runs out, the database pauses until the 1st (by design, so it can't cost anything); the app shows an error until then. Usage is under the database's **Free amount remaining** metric in the portal.
- **Upgrading** is a parameter change: set `appServiceSku` to `B1` (Always On, no sleeping) or `S1` (deployment slots).
- **Key Vault references** are read when the app starts and about once a day after. After changing a secret, restart the app. If a reference can't be read, the app gets the reference text instead of the secret and signing in fails: check the app's **Environment variables** page, where each Key Vault reference shows whether it resolved.
- **Deleted secrets** are kept for 7 days (soft delete) and can be recovered in that time.
- **Looking at the data:** the database's Entra admin is the GitHub identity. To query it yourself in the portal's Query editor, temporarily set yourself as admin under the SQL server's **Microsoft Entra ID** page. That sends an alert email (BUD-165), and everything you run is recorded in the SQL audit; the next deploy sets
  the admin back. Only do it when you need to, e.g. to look into a reported problem (ADR-017).
- **SQL audit:** sign-ins and statements from anyone but the app's identity are kept for 30 days in Log Analytics. Query
  `AzureDiagnostics | where Category == "SQLSecurityAuditEvents"` (see the Monitoring page in Confluence).
