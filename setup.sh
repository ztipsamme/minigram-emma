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
  --output tsv)

az storage account network-rule add \
  --resource-group "$RG" \
  --account-name "$STORAGE" \
  --subnet "$SUBNET_ID"

az storage account update \
  --name "$STORAGE" \
  --resource-group "$RG" \
  --default-action Deny


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
#   --output tsv)

# az webapp auth update \
#   --resource-group "$RG" \
#   --name "$API_NAME" \
#   --enabled true \
#   --action Return401 \
#   --require-https true \
#   --set \
#     identityProviders.azureActiveDirectory.registration.clientId="$ENTRA_CLIENT_ID"


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

printf '\n'
printf '%s\n' "Återstår manuellt:"
printf '%s\n' "1. Entra ID-användare: admin, fotograf, betraktare"
printf '%s\n' "2. App Roles: Admin, Fotograf, Betraktare"
printf '%s\n' "3. Tilldela App Roles till användarna"
printf '%s\n' "4. Storage RBAC:"
printf '%s\n' "   Fotograf    → Storage Blob Data Contributor"
printf '%s\n' "   Betraktare  → Storage Blob Data Reader"
printf '%s\n' "   Admin       → Storage Blob Data Owner"
printf '%s\n' "5. Koppla API App Service till Entra ID/Easy Auth"
printf '%s\n' "6. Testa 401 utan inloggning"
printf '%s\n' "7. Testa 403 för Betraktare vid DELETE"
printf '\n'

printf '%s\n' "Klart."