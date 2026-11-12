#!/usr/bin/env bash
# One-off setup so GitHub Actions can deploy Home Budget to Azure (BUD-9, BUD-11).
# Run it in Azure Cloud Shell (Bash) as an Owner of the subscription:
#
#   curl -fsSL https://raw.githubusercontent.com/ChrisA210777/HomeBudget/main/infra/bootstrap.sh -o bootstrap.sh
#   bash bootstrap.sh
#
# It's safe to run again; it reuses anything that already exists.
# It creates no paid resources: only a resource group and a sign-in identity for GitHub.
set -euo pipefail

REPO="ChrisA210777/HomeBudget"
ENVIRONMENT="dev"
LOCATION="${LOCATION:-uksouth}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-homebudget-dev}"
APP_NAME="github-homebudget-deploy"

SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
echo "Subscription: $(az account show --query name -o tsv) ($SUBSCRIPTION_ID)"

echo "== Registering the Azure services the app uses (first time only; can take a few minutes)"
for ns in Microsoft.Web Microsoft.Sql Microsoft.OperationalInsights Microsoft.Insights Microsoft.ManagedIdentity Microsoft.Consumption Microsoft.KeyVault; do
  az provider register --namespace "$ns" --wait --output none
done

echo "== Resource group $RESOURCE_GROUP in $LOCATION"
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

echo "== Identity GitHub Actions signs in as: $APP_NAME"
APP_ID=$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv)
if [ -z "$APP_ID" ]; then
  APP_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
fi
SP_OBJECT_ID=$(az ad sp list --filter "appId eq '$APP_ID'" --query "[0].id" -o tsv)
if [ -z "$SP_OBJECT_ID" ]; then
  SP_OBJECT_ID=$(az ad sp create --id "$APP_ID" --query id -o tsv)
fi

echo "== Trusting GitHub Actions runs for the '$ENVIRONMENT' environment of $REPO (no secrets involved)"
# GitHub's token names the repo either by name (repo:owner/name:...) or, for newer repos and
# organisations, by name and immutable id (repo:owner@ownerId/name@repoId:...). Trust both.
REPO_IDS=$(curl -fsSL "https://api.github.com/repos/$REPO" | python3 -c "import json,sys; r=json.load(sys.stdin); print(f\"{r['owner']['login']}@{r['owner']['id']}/{r['name']}@{r['id']}\")")
add_subject() {
  local name=$1 subject=$2
  if ! az ad app federated-credential list --id "$APP_ID" --query "[?subject=='$subject']" -o tsv | grep -q .; then
    az ad app federated-credential create --id "$APP_ID" --parameters "{
      \"name\": \"$name\",
      \"issuer\": \"https://token.actions.githubusercontent.com\",
      \"subject\": \"$subject\",
      \"audiences\": [\"api://AzureADTokenExchange\"]
    }" --output none
  fi
}
add_subject "github-$ENVIRONMENT" "repo:$REPO:environment:$ENVIRONMENT"
add_subject "github-$ENVIRONMENT-ids" "repo:$REPO_IDS:environment:$ENVIRONMENT"

echo "== Letting it manage the resource group (and nothing else)"
SCOPE=$(az group show --name "$RESOURCE_GROUP" --query id -o tsv)
if ! az role assignment list --assignee "$SP_OBJECT_ID" --scope "$SCOPE" --role Contributor --query "[0]" -o tsv | grep -q .; then
  # A new identity can take a minute to be visible to role assignments.
  for i in 1 2 3 4 5 6; do
    az role assignment create --assignee-object-id "$SP_OBJECT_ID" --assignee-principal-type ServicePrincipal \
      --role Contributor --scope "$SCOPE" --output none && break
    sleep 20
  done
fi

echo "== Letting it give the app read access to Key Vault secrets (BUD-147), and no other role"
# Bicep grants the app's identity "Key Vault Secrets User" on the vault. Contributor can't assign roles, so the deployer
# also gets "Role Based Access Control Administrator", with a condition that it can only assign or remove that one role,
# and never to itself.
RBAC_ADMIN_ROLE="f58310d9-a9f6-439a-9e8d-f62e7b41a168"
SECRETS_USER_ROLE="4633458b-17de-408a-b874-0445c86b69e6"
CONDITION="((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$SECRETS_USER_ROLE} AND @Request[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidNotEquals {$SP_OBJECT_ID})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$SECRETS_USER_ROLE}))"
if ! az role assignment list --assignee "$SP_OBJECT_ID" --scope "$SCOPE" --role "$RBAC_ADMIN_ROLE" --query "[0]" -o tsv | grep -q .; then
  az role assignment create --assignee-object-id "$SP_OBJECT_ID" --assignee-principal-type ServicePrincipal \
    --role "$RBAC_ADMIN_ROLE" --scope "$SCOPE" --condition "$CONDITION" --condition-version "2.0" --output none
fi

echo "== Letting you set the sign-in secrets in Key Vault"
# You (the person running this) can add and change secrets in the resource group's vaults. The deployer can't read them.
ME=$(az ad signed-in-user show --query id -o tsv)
if ! az role assignment list --assignee "$ME" --scope "$SCOPE" --role "Key Vault Secrets Officer" --query "[0]" -o tsv | grep -q .; then
  az role assignment create --assignee-object-id "$ME" --assignee-principal-type User \
    --role "Key Vault Secrets Officer" --scope "$SCOPE" --output none
fi

cat <<DONE

Done. In GitHub, go to $REPO -> Settings -> Secrets and variables -> Actions -> Variables
and add these repository variables:

  AZURE_CLIENT_ID           $APP_ID
  AZURE_TENANT_ID           $TENANT_ID
  AZURE_SUBSCRIPTION_ID     $SUBSCRIPTION_ID
  AZURE_RESOURCE_GROUP      $RESOURCE_GROUP
  AZURE_DEPLOYER_OBJECT_ID  $SP_OBJECT_ID
  BUDGET_EMAIL              <the email address for cost alerts>
  MICROSOFT_CLIENT_ID       <the Application (client) ID of your "Sign in with Microsoft" app registration>

Then see infra/README.md for the environment and for putting the sign-in secrets in Key Vault.
DONE
