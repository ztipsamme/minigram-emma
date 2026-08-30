#!/usr/bin/env bash

set -euo pipefail

TEAM="emma"
PROJECT_NAME="minigram"

RG="RG-Emma-Spitz-a59389-DotNetCloudDeveloper-VT-Mars-Goteborg"
LOCATION="swedencentral"

APP_PLAN="$PROJECT_NAME-plan-$TEAM"
APP_NAME="$PROJECT_NAME-app-$TEAM"
API_NAME="$PROJECT_NAME-api-$TEAM"

VNET="$PROJECT_NAME-vnet-$TEAM"
SUBNET_ONE="backend-subnet"
SUBNET_TWO="frontend-subnet"

NSG_BACKEND="nsg-backend-$TEAM"
NSG_FRONTEND="nsg-frontend-$TEAM"

STORAGE="st$PROJECT_NAME$TEAM"
CONTAINER="bilder"

FRONTEND_URL="https://${APP_NAME}.azurewebsites.net"
API_URL="https://${API_NAME}.azurewebsites.net"

TENANT_DOMAIN="IThogskolan.onmicrosoft.com"

ADMIN_USER="$PROJECT_NAME-$TEAM-admin@$TENANT_DOMAIN"
FOTOGRAF_USER="$PROJECT_NAME-$TEAM-fotograf@$TENANT_DOMAIN"
BETRAKTARE_USER="$PROJECT_NAME-$TEAM-betraktare@$TENANT_DOMAIN"

# ------------------------------------------------------------
# Användare per roll
#
# Lägg till hur många användare du vill.
# Exempel:
#
# ADMIN_USERS=(
#   "emma.spitz@IThogskolan.onmicrosoft.com"
#   "annan.admin@IThogskolan.onmicrosoft.com"
# )
# ------------------------------------------------------------

ADMIN_USERS=(
  # "$ADMIN_USER"
  # "admin1@IThogskolan.onmicrosoft.com"
  # "admin2@IThogskolan.onmicrosoft.com"
)

FOTOGRAF_USERS=(
  # "$FOTOGRAF_USER"
  # "fotograf1@IThogskolan.onmicrosoft.com"
  # "fotograf2@IThogskolan.onmicrosoft.com"
)

BETRAKTARE_USERS=(
  # "$BETRAKTARE_USER"  
  # "betraktare1@IThogskolan.onmicrosoft.com"
  # "betraktare2@IThogskolan.onmicrosoft.com"
)

# ============================================================
# 1. VNet + subnets
# ============================================================

printf '\n1. VNet + subnets...\n'

az network vnet create \
  --resource-group "$RG" \
  --name "$VNET" \
  --location "$LOCATION" \
  --address-prefix 10.0.0.0/16 \
  --subnet-name "$SUBNET_ONE" \
  --subnet-prefix 10.0.1.0/24

az network vnet subnet update \
  --resource-group "$RG" \
  --vnet-name "$VNET" \
  --name "$SUBNET_ONE" \
  --service-endpoints Microsoft.Storage

az network vnet subnet create \
  --resource-group "$RG" \
  --vnet-name "$VNET" \
  --name "$SUBNET_TWO" \
  --address-prefix 10.0.2.0/24


# ============================================================
# 2. NSG
# ============================================================

printf '\n2. NSG...\n'

az network nsg create \
  --name "$NSG_BACKEND" \
  --resource-group "$RG" \
  --location "$LOCATION"

az network nsg create \
  --name "$NSG_FRONTEND" \
  --resource-group "$RG" \
  --location "$LOCATION"


# ------------------------------------------------------------
# Frontend NSG
# ------------------------------------------------------------

# HTTPS in från Internet
az network nsg rule create \
  --nsg-name "$NSG_FRONTEND" \
  --resource-group "$RG" \
  --name Allow-HTTPS-In \
  --priority 100 \
  --direction Inbound \
  --access Allow \
  --protocol Tcp \
  --source-address-prefixes Internet \
  --destination-address-prefixes '*' \
  --destination-port-ranges 443

# HTTP blockeras
az network nsg rule create \
  --nsg-name "$NSG_FRONTEND" \
  --resource-group "$RG" \
  --name Deny-HTTP-In \
  --priority 110 \
  --direction Inbound \
  --access Deny \
  --protocol Tcp \
  --source-address-prefixes Internet \
  --destination-address-prefixes '*' \
  --destination-port-ranges 80

# Intern kommunikation från backend → frontend
az network nsg rule create \
  --nsg-name "$NSG_FRONTEND" \
  --resource-group "$RG" \
  --name Allow-Backend-VNet \
  --priority 120 \
  --direction Inbound \
  --access Allow \
  --protocol '*' \
  --source-address-prefixes 10.0.1.0/24 \
  --destination-address-prefixes '*' \
  --destination-port-ranges '*'


# ------------------------------------------------------------
# Backend NSG
# ------------------------------------------------------------

# Frontend → backend
az network nsg rule create \
  --nsg-name "$NSG_BACKEND" \
  --resource-group "$RG" \
  --name Allow-Frontend-VNet \
  --priority 100 \
  --direction Inbound \
  --access Allow \
  --protocol '*' \
  --source-address-prefixes 10.0.2.0/24 \
  --destination-address-prefixes '*' \
  --destination-port-ranges '*'

# Explicit block av HTTP
az network nsg rule create \
  --nsg-name "$NSG_BACKEND" \
  --resource-group "$RG" \
  --name Deny-HTTP-In \
  --priority 110 \
  --direction Inbound \
  --access Deny \
  --protocol Tcp \
  --source-address-prefixes Internet \
  --destination-address-prefixes '*' \
  --destination-port-ranges 80


# ------------------------------------------------------------
# Koppla NSG till subnets
# ------------------------------------------------------------

az network vnet subnet update \
  --resource-group "$RG" \
  --vnet-name "$VNET" \
  --name "$SUBNET_ONE" \
  --network-security-group "$NSG_BACKEND"

az network vnet subnet update \
  --resource-group "$RG" \
  --vnet-name "$VNET" \
  --name "$SUBNET_TWO" \
  --network-security-group "$NSG_FRONTEND"


# ============================================================
# 3. Storage
# ============================================================

printf '\n3. Storage...\n'

az storage account create \
  --name "$STORAGE" \
  --resource-group "$RG" \
  --location "$LOCATION" \
  --sku Standard_LRS \
  --kind StorageV2 \
  --allow-blob-public-access false \
  --min-tls-version TLS1_2

az storage container create \
  --name "$CONTAINER" \
  --account-name "$STORAGE" \
  --auth-mode login


# ------------------------------------------------------------
# Storage får endast trafik från backend-subnet
# ------------------------------------------------------------

SUBNET_ID=$(az network vnet subnet show \
  --resource-group "$RG" \
  --vnet-name "$VNET" \
  --name "$SUBNET_ONE" \
  --query id \
  -o tsv)

az storage account network-rule add \
  --resource-group "$RG" \
  --account-name "$STORAGE" \
  --subnet "$SUBNET_ID"

az storage account update \
  --name "$STORAGE" \
  --resource-group "$RG" \
  --default-action Deny

MY_IP=$(curl -s https://api.ipify.org)

printf "Tillåter lokal IP (%s) i Storage Account...\n" "$MY_IP"

az storage account network-rule add \
  --resource-group "$RG" \
  --account-name "$STORAGE" \
  --ip-address "$MY_IP"


# ============================================================
# 4. App Service Plan + Apps
# ============================================================

printf '\n4. App Service...\n'

az appservice plan create \
  --name "$APP_PLAN" \
  --resource-group "$RG" \
  --location "$LOCATION" \
  --sku B1 \
  --is-linux

az webapp create \
  --name "$API_NAME" \
  --resource-group "$RG" \
  --plan "$APP_PLAN" \
  --runtime "DOTNETCORE:10.0"

az webapp create \
  --name "$APP_NAME" \
  --resource-group "$RG" \
  --plan "$APP_PLAN" \
  --runtime "DOTNETCORE:10.0"


# ============================================================
# 5. HTTPS + TLS
# ============================================================

printf '\n5. HTTPS + TLS...\n'

# Tvinga HTTPS
az webapp update \
  --resource-group "$RG" \
  --name "$API_NAME" \
  --https-only true

az webapp update \
  --resource-group "$RG" \
  --name "$APP_NAME" \
  --https-only true


# Minsta TLS-version
az webapp config set \
  --resource-group "$RG" \
  --name "$API_NAME" \
  --min-tls-version 1.2

az webapp config set \
  --resource-group "$RG" \
  --name "$APP_NAME" \
  --min-tls-version 1.2


# ============================================================
# 6.  VNet integration
# ============================================================

printf '\n6. VNet integration...\n'

# API → backend-subnet
az webapp vnet-integration add \
  --resource-group "$RG" \
  --name "$API_NAME" \
  --vnet "$VNET" \
  --subnet "$SUBNET_ONE"

# Frontend → frontend-subnet
az webapp vnet-integration add \
  --resource-group "$RG" \
  --name "$APP_NAME" \
  --vnet "$VNET" \
  --subnet "$SUBNET_TWO"


# All outbound traffic from API through VNet
az webapp config set \
  --resource-group "$RG" \
  --name "$API_NAME" \
  --vnet-route-all-enabled true

# Säkerställ Azure Services-åtkomst & DNS på Web App
az webapp config appsettings set \
  --resource-group "$RG" \
  --name "$API_NAME" \
  --settings WEBSITE_DNS_SERVER=168.63.129.16 WEBSITE_VNET_ROUTE_ALL=1

# ============================================================
# 7.  CORS
# ============================================================

printf '\n7. CORS...\n'

az webapp cors add \
  --resource-group "$RG" \
  --name "$API_NAME" \
  --allowed-origins "$FRONTEND_URL"


# ============================================================
# 8. Easy Auth
# ============================================================

printf '\n8. Easy Auth...\n'

# Entra struktur
#                  Entra ID
#                     │
#         ┌───────────┴───────────┐
#         │                       │
#  MinGram API              MinGram Postman
#         │                       │
#   App Roles              OAuth client
#         │                       │
#  Admin                    user_impersonation
#  Fotograf                       │
#  Betraktare                     │
#         └───────────────┬───────┘
#                         ↓
#                   MinGram API

# Install/enable Auth V2 extension if necessary
az extension add \
  --name authV2 \
  --upgrade \
  --only-show-errors

printf '\n'
printf '%s\n' "============================================================"
printf '%s\n' "Easy Auth behöver kopplas till Entra ID manuellt"
printf '%s\n' "============================================================"
printf '\n'
printf 'API:      %s\n' "$API_URL"
printf 'Frontend: %s\n' "$FRONTEND_URL"
printf '\n'
printf '%s\n' "När App Registration finns:"
printf '\n'

# Om du har ett App Registration-client-id kan du sätta:
#
# ENTRA_CLIENT_ID=$(az ad app list \
#   --display-name "$API_NAME" \
#   --query "[].appId" \
#   -o tsv)

# az webapp auth update \
#   --resource-group "$RG" \
#   --name "$API_NAME" \
#   --enabled true \
#   --action Return401 \
#   --require-https true \
#   --set \
#     identityProviders.azureActiveDirectory.registration.clientId="$ENTRA_CLIENT_ID"


# ============================================================
# 9. Deploy with GitHub Actions
# ============================================================

printf '\n9. Deploy with GitHub Actions \n'

printf 'Skapar GitHub Actions Workflow med filer för respective directory.'
mkdir -p .github/workflows

touch .github/workflows/deploy-backend.yml
touch .github/workflows/deploy-frontend.yml

printf 'Skapar service principal'
az ad sp create-for-rbac \
  --name "github-minigram-emma" \
  --role contributor \
  --scopes "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RG" \
  --json-auth

az ad sp create-for-rbac \
  --name "github-minigram-emma" \
  --role contributor \
  --scopes "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RG" \
  --json-auth |
gh secret set AZURE_CREDENTIALS

# ============================================================
# 10. Users
# ============================================================

printf '\n10. Skapar MinGram testanvändare...\n'

# OBS:
# Använd ett tillfälligt/testlösenord och lagra det inte i Git.
read -s -p "Lösenord för testkontona: " TEMP_PASSWORD
# Ex: MittHemligaLösenord123!
echo


# Admin
az ad user create \
  --display-name "MinGram Admin" \
  --user-principal-name "$ADMIN_USER" \
  --password "$TEMP_PASSWORD" \
  # --force-change-password-next-login true

# Fotograf
az ad user create \
  --display-name "MinGram Fotograf" \
  --user-principal-name "$FOTOGRAF_USER" \
  --password "$TEMP_PASSWORD" \
  # --force-change-password-next-login true

# Betraktare
az ad user create \
  --display-name "MinGram Betraktare" \
  --user-principal-name "$BETRAKTARE_USER" \
  --password "$TEMP_PASSWORD" \
  # --force-change-password-next-login true

# ============================================================
# 10. Roller
# ============================================================

printf '\n10. Roller\n'

API_APP_ID=$(az ad app list  \
  --display-name "$API_NAME" \
  --query "[0].appId" \
  -o tsv)

API_APP_OBJECT_ID=$(az ad app list \
  --display-name "$API_NAME" \
  --query "[0].id" \
  -o tsv)

SP_OBJECT_ID=$(az ad sp show \
  --id "$API_APP_ID" \
  --query id \
  -o tsv)
  
USER_OBJECT_ID=$(az ad user show \
  --id "$USER_EMAIL" \
  --query id \
  -o tsv)

ROLE_NAME="Admin"
ROLE_ID=$(az ad app show \
  --id $API_APP_ID \
  --query "appRoles[?displayName=='$ROLE_NAME'].id" \
  -o tsv)

printf 'Skapar roller'
az rest \
  --method PATCH \
  --url "https://graph.microsoft.com/v1.0/applications/$APP_OBJECT_ID" \
  --headers "Content-Type=application/json" \
  --body '{
    "appRoles": [
      {
        "allowedMemberTypes": ["User"],
        "description": "Can manage all MinGram resources.",
        "displayName": "Admin",
        "id": "'"$(uuidgen)"'",
        "isEnabled": true,
        "value": "Admin"
      },
      {
        "allowedMemberTypes": ["User"],
        "description": "Can upload and read images.",
        "displayName": "Fotograf",
        "id": "'"$(uuidgen)"'",
        "isEnabled": true,
        "value": "Fotograf"
      },
      {
        "allowedMemberTypes": ["User"],
        "description": "Can only read images.",
        "displayName": "Betraktare",
        "id": "'"$(uuidgen)"'",
        "isEnabled": true,
        "value": "Betraktare"
      }
    ]
  }'

read -s -p "Din microsoft email: " USER_EMAIL
echo $USER_EMAIL

az ad user show \
  --id "$USER_EMAIL" \
  --query userPrincipalName \
  -o tsv

printf 'Existerande roller'
az ad app show \
  --id "$API_APP_ID" \
  --query "appRoles[].value" \
  -o tsv

  az ad app show \
  --id "$(az ad app list \
    --display-name "$API_NAME" \
    --query "[0].appId" \
    -o tsv)" \
  --query "appRoles[].{Name:displayName,Value:value,Id:id,Enabled:isEnabled}" \
  -o table

# printf 'Tilldela roll till user'
# az rest \
#   --method POST \
#   --url "https://graph.microsoft.com/v1.0/users/$USER_OBJECT_ID/appRoleAssignments" \
#   --headers "Content-Type=application/json" \
#   --body '{
#     "principalId": "'"$USER_OBJECT_ID"'",
#     "resourceId": "'"$SP_OBJECT_ID"'",
#     "appRoleId": "'"$ROLE_ID"'"
#   }'

printf 'Användarens roller i listform\n'
az rest \
  --method GET \
  --url "https://graph.microsoft.com/v1.0/users/$USER_OBJECT_ID/appRoleAssignments" \
  --query "value[].{resource:resourceDisplayName,roleId:appRoleId,resourceId:resourceId}" \
  -o table


# ============================================================
# 11. Storage RBAC
# ============================================================

printf '\n11. Storage RBAC...\n'

printf '\nHämtar Storage Account ID...\n'
STORAGE_ID=$(az storage account show \
  --resource-group "$RG" \
  --name "$STORAGE" \
  --query id \
  -o tsv)

printf 'Storage Resource ID:\n%s\n' "$STORAGE_ID"

# Funktion: tilldela RBAC-roll
assign_role() {
  local USER_UPN="$1"
  local ROLE="$2"

  printf '\nTilldelar:\n'
  printf '  User : %s\n' "$USER_UPN"
  printf '  Role : %s\n' "$ROLE"

  USER_OBJECT_ID=$(az ad user show \
    --id "$USER_UPN" \
    --query id \
    -o tsv)

  printf '  ID   : %s\n' "$USER_OBJECT_ID"

  az role assignment create \
    --assignee-object-id "$USER_OBJECT_ID" \
    --assignee-principal-type User \
    --role "$ROLE" \
    --scope "$STORAGE_ID"

  printf '  ✓ Tilldelad\n'
}

# Example built-in roles for Azure Storage
ADMIN_ROLE="Storage Blob Data Owner"
FOTOGRAF_ROLE="Storage Blob Data Contributor"
BETRAKTARE_ROLE="Storage Blob Data Reader"

# Admin
printf 'ADMIN\n'

for USER in "${ADMIN_USERS[@]}"; do
  assign_role "$USER" "$ADMIN_ROLE"
done


# Fotograf
printf 'FOTOGRAF\n'

for USER in "${FOTOGRAF_USERS[@]}"; do
  assign_role "$USER" "$FOTOGRAF_ROLE"
done


# Betraktare
printf 'BETRAKTARE\n'

for USER in "${BETRAKTARE_USERS[@]}"; do
  assign_role "$USER" "$BETRAKTARE_ROLE"
done


# ============================================================
# Verifiera
printf 'Verifierar RBAC-tilldelningar\n'

az role assignment list \
  --scope "$STORAGE_ID" \
  --query "[].{User:principalName,Role:roleDefinitionName,Scope:scope}" \
  -o table


# ============================================================
# Postman
# ============================================================

printf '\n12. Postman-App Registration\n'

# Postman App
#    │
#    └── Delegated permission
#        │
#        └── user_impersonation
#                │
#                ▼
#           MinGram API

TENANT_ID=$(az account show --query tenantId -o tsv)
POSTMAN_APP_NAME="$PROJECT_NAME-postman-$TEAM"
POSTMAN_REDIRECT_URI="https://oauth.pstmn.io/v1/browser-callback"

POSTMAN_APP_ID=$(az ad app create \
  --display-name "$POSTMAN_APP_NAME" \
  --public-client-redirect-uris "$POSTMAN_REDIRECT_URI" \
  --query appId \
  -o tsv)

az ad app show \
  --id "$POSTMAN_APP_ID" \
  --query id \
  -o tsv

API_APP_ID=$(az ad app list \
  --display-name "$API_NAME" \
  --query "[0].appId" \
  -o tsv)

API_APP_OBJECT_ID=$(az ad app list \
  --display-name "$API_NAME" \
  --query "[0].id" \
  -o tsv)

SCOPE="api://$API_APP_ID/user_impersonation"

USER_IMPERSONATION_SCOPE_ID=$(az ad app show \
  --id "$API_APP_ID" \
  --query "api.oauth2PermissionScopes[?value=='user_impersonation'].id | [0]" \
  -o tsv)

# Skapa service principal
POSTMAN_SP_OBJECT_ID=$(az ad sp create \
  --id "$POSTMAN_APP_ID" \
  --query id \
  -o tsv)

# Permissions för Postman
az ad app permission add \
  --id "$POSTMAN_APP_ID" \
  --api "$API_APP_ID" \
  --api-permissions "$USER_IMPERSONATION_SCOPE_ID=Scope"

# Verifiera
az ad app show \
  --id "$POSTMAN_APP_ID" \
  --query "requiredResourceAccess" \
  -o json

# Delegerad consent (Går ej att genomför p.g.a skolans tenant)
az ad app permission grant \
  --id "$POSTMAN_APP_ID" \
  --api "$API_APP_ID" \
  --scope "user_impersonation"

# Verifiera
az ad app permission list-grants \
  --id "$POSTMAN_APP_ID" \
  -o table

# Sätt som fallback client
az ad app update \
  --id "$POSTMAN_APP_ID" \
  --is-fallback-public-client true

# Postman är public client
az ad app show \
  --id "$POSTMAN_APP_ID" \
  --query "isFallbackPublicClient" \
  -o tsv


# Postman config

API_APP_ID=$(az ad app list  \
  --display-name "$API_NAME" \
  --query "[0].appId" \
  -o tsv)
POSTMAN_AUTH_URL="https://login.microsoftonline.com/$TENANT_ID/oauth2/v2.0/authorize"
POSTMAN_TOKEN_URL="https://login.microsoftonline.com/$TENANT_ID/oauth2/v2.0/token"
POSTMAN_APP_ID=$(az ad app list \
  --display-name "$POSTMAN_APP_NAME" \
  --query "[0].appId" \
  -o tsv)
POSTMAN_SCOPE="api://$API_APP_ID/user_impersonation"

cat <<EOF

Postman OAuth 2.0 configuration

Klistra in följande i postman

Token Name:
MinGram Entra

Grant Type:
Authorization Code (With PKCE)

Callback URL:
https://oauth.pstmn.io/v1/browser-callback

Auth URL:
$POSTMAN_AUTH_URL

Access Token URL:
$POSTMAN_TOKEN_URL

Client ID:
$POSTMAN_APP_ID

Client Secret:
(leave empty)

Code Challenge Method:
SHA-256

Code Verifier:
(leave empty - Postman generates it)

Scope:
$POSTMAN_SCOPE

State:
(leave empty)

Client Authentication:
Send client credentials in body

EOF

# Postman
#    ↓ OAuth 2.0
# Entra ID
#    ↓
# access token
#    ↓
# MinGram API
#    ↓
# Easy Auth
#    ↓
# 200 OK

# ============================================================
# Sammanfattning
# ============================================================

printf '\n'
printf '%s\n' "============================================================"
printf '%s\n' "MinGram Azure-miljö skapad"
printf '%s\n' "============================================================"
printf '\n'

printf 'Resource Group : %s\n' "$RG"
printf 'Location       : %s\n' "$LOCATION"
printf 'VNet           : %s\n' "$VNET"
printf 'Backend subnet : %s\n' "$SUBNET_ONE"
printf 'Frontend subnet: %s\n' "$SUBNET_TWO"
printf 'Storage        : %s\n' "$STORAGE"
printf 'Container      : %s\n' "$CONTAINER"
printf 'API            : %s\n' "$API_URL"
printf 'Frontend       : %s\n' "$FRONTEND_URL"

printf '%s\n' "Klart."



# ============================================================
# Managed Identity 
# ============================================================


# Create managed identity
az webapp identity assign \
  --resource-group "$RG" \
  --name "$API_NAME"

  API_PRINCIPAL_ID=$(az webapp identity show \
  --resource-group "$RG" \
  --name "$API_NAME" \
  --query principalId \
  -o tsv)

az role assignment create \
  --assignee-object-id "$API_PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Contributor" \
  --scope "$STORAGE_ID"

echo "$API_PRINCIPAL_ID"


# ============================================================
# Problem: App Service Appsettings issue
# ============================================================

# Trodde det var en DNS skit som orsakade status 500 p.g.a storage account
# Var förmodligen bara fel value i Storage__AccountUrl och Storage__Container

echo $RG
echo $API_NAME
echo $STORAGE

az webapp config appsettings set --resource-group $RG \
  --name "$API_NAME" \
  --settings WEBSITE_DNS_SERVER="8.8.8.8"


az webapp config appsettings set \
  --resource-group "$RG" \
  --name "$API_NAME" \
  --settings \
    Storage__AccountUrl="https://${STORAGE}.blob.core.windows.net/" \
    Storage__Container="$CONTAINER" \

az webapp config appsettings list \
  --resource-group "$RG" \
  --name "$API_NAME" \
  --output table

az webapp restart --resource-group "$RG" --name "$API_NAME"


# ============================================================
# Byt roll
# ============================================================

USER_ID=$(az ad signed-in-user show --query id -o tsv)

# Se din nuvarande tilldelning
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/users/$USER_ID/appRoleAssignments" \
  --query "value[?resourceId=='$SP_ID']"

# Se rollernas id
az ad app show --id "$API_APP_ID" \
  --query "appRoles[].{Name:displayName,Value:value,Id:id}" \
  -o table

# Radera din gamla roll
ROLE_ID="<rollens-id>"

az rest --method DELETE \
  --url "https://graph.microsoft.com/v1.0/users/$USER_ID/appRoleAssignments/$ROLE_ID"

# Sätt din nya roll
DESIRED_ROLE="Betraktare"

NEW_ROLE=$(az ad app show --id "$API_APP_ID" \
  --query "appRoles[?displayName=='$DESIRED_ROLE'].id" -o tsv)

az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/users/$USER_ID/appRoleAssignments" \
  --headers "Content-Type=application/json" \
  --body "{\"principalId\":\"$USER_ID\",\"resourceId\":\"$SP_ID\",\"appRoleId\":\"$NEW_ROLE\"}"