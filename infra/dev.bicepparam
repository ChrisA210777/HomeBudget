using 'main.bicep'

// The Dev environment (ADR-010), now on B1: its App Service plan is shared with Poker Manager (PMP-86, 20 Nov 2026),
// which needs Always On. The database is Basic (BUD-170, 20 Nov 2026), so it never pauses. Values that differ per
// person come from the deploy workflow's environment (GitHub repository variables). The sign-in secrets are in Key
// Vault (BUD-147).

param environmentName = 'dev'
param appServiceSku = 'B1'
param databaseTier = 'Basic'
// The shared B1 plan (about £10) and the Basic database (about £4.50).
param budgetAmount = 15
param budgetStartDate = '2026-11-01'
param budgetContactEmails = [readEnvironmentVariable('BUDGET_EMAIL', 'nobody@example.com')]
param sqlAdminObjectId = readEnvironmentVariable('AZURE_DEPLOYER_OBJECT_ID', '00000000-0000-0000-0000-000000000000')
param sqlAdminName = 'github-homebudget-deploy'
param microsoftClientId = readEnvironmentVariable('MICROSOFT_CLIENT_ID', '')
param googleClientId = readEnvironmentVariable('GOOGLE_CLIENT_ID', '')
