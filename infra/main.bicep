// Home Budget: one environment (BUD-9). Deployed by .github/workflows/deploy.yml.
// See ADR-010: Dev runs entirely on free tiers (App Service F1, Azure SQL free offer).

targetScope = 'resourceGroup'

@description('Short environment name used in resource names, e.g. dev or prod.')
param environmentName string

param location string = resourceGroup().location

@description('App Service plan size. F1 is free; B1 and up allow Always On; S1 and up allow deployment slots.')
@allowed([
  'F1'
  'B1'
  'S1'
])
param appServiceSku string = 'F1'

@description('Object id of the identity that deploys (the GitHub Actions service principal). It becomes the database server\'s Entra admin, so the pipeline can run migrations.')
param sqlAdminObjectId string

@description('Display name of that identity.')
param sqlAdminName string

@description('Client id of the Entra app registration used for "Sign in with Microsoft".')
param microsoftClientId string = ''

@secure()
@description('Client secret for that app registration.')
param microsoftClientSecret string = ''

@description('Monthly budget for this resource group, in the billing currency. Alerts go out as soon as anything is spent.')
param budgetAmount int = 1

@description('First day of the month the budget starts (yyyy-MM-01). Fixed, because a budget\'s start date can\'t change.')
param budgetStartDate string

@description('Who gets budget alerts.')
param budgetContactEmails array

@description('Cap on log data per day, in GB, so telemetry stays within the free allowance.')
param logDailyCapGb string = '0.1'

var name = 'homebudget-${environmentName}'
var suffix = take(uniqueString(resourceGroup().id), 6)
var databaseName = 'homebudget'
var isFree = appServiceSku == 'F1'

// ---- Monitoring -------------------------------------------------------------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${name}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json(logDailyCapGb)
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${name}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    IngestionMode: 'LogAnalytics'
  }
}

// ---- Identity the app uses to reach the database ---------------------------

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${name}'
  location: location
}

// ---- Database ----------------------------------------------------------------

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${name}-${suffix}'
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminName
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      principalType: 'Application'
    }
  }
}

// Lets Azure services (the app) reach the server. Only Entra identities can sign in, and only the
// app's identity has a database user. The free App Service tier can't join a virtual network.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// The Azure SQL free offer: 100,000 vCore-seconds and 32 GB a month at no cost. When the month's
// allowance is used up the database pauses until the 1st rather than charging.
resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
  }
}

// ---- Web app -------------------------------------------------------------------

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'asp-${name}'
  location: location
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    reserved: true
  }
}

var microsoftSettings = empty(microsoftClientId) ? [] : [
  {
    name: 'Authentication__Microsoft__ClientId'
    value: microsoftClientId
  }
  {
    name: 'Authentication__Microsoft__ClientSecret'
    value: microsoftClientSecret
  }
]

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${name}-${suffix}'
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: !isFree
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      appSettings: concat([
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ConnectionStrings__Budget'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${appIdentity.properties.clientId};Encrypt=True;Connect Timeout=120'
        }
        {
          // App Service's front end terminates HTTPS and passes the scheme on, so sign-in
          // redirects and the secure cookie use https.
          name: 'ForwardedHeaders__Enabled'
          value: 'true'
        }
        {
          name: 'ForwardedHeaders__TrustAllProxies'
          value: 'true'
        }
        {
          name: 'ForwardedHeaders__ForwardHost'
          value: 'false'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'false'
        }
      ], microsoftSettings)
    }
  }
}

// ---- Cost guard ------------------------------------------------------------------

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-${name}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      anySpend: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 1
        thresholdType: 'Actual'
        contactEmails: budgetContactEmails
      }
      forecastOverBudget: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: budgetContactEmails
      }
    }
  }
}

output webAppName string = site.name
output webAppHostName string = site.properties.defaultHostName
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = databaseName
output appIdentityName string = appIdentity.name
output appIdentityClientId string = appIdentity.properties.clientId
