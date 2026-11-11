using 'main.bicep'

// The Dev environment: free tiers only (ADR-010). Values that differ per person come from the
// deploy workflow's environment (GitHub repository variables and secrets).

param environmentName = 'dev'
param appServiceSku = 'F1'
param budgetAmount = 1
param budgetStartDate = '2026-11-01'
param budgetContactEmails = [readEnvironmentVariable('BUDGET_EMAIL', 'nobody@example.com')]
param sqlAdminObjectId = readEnvironmentVariable('AZURE_DEPLOYER_OBJECT_ID', '00000000-0000-0000-0000-000000000000')
param sqlAdminName = 'github-homebudget-deploy'
param microsoftClientId = readEnvironmentVariable('MICROSOFT_CLIENT_ID', '')
param microsoftClientSecret = readEnvironmentVariable('MICROSOFT_CLIENT_SECRET', '')
param googleClientId = readEnvironmentVariable('GOOGLE_CLIENT_ID', '')
param googleClientSecret = readEnvironmentVariable('GOOGLE_CLIENT_SECRET', '')
