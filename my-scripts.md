## 1. Skapa Infrastruktur

```bash
TEAM="emma"
PROJECT_NAME="minigram"
RG="RG-Emma-Spitz-a59389-DotNetCloudDeveloper-VT-Mars-Goteborg"
APP_PLAN_NAME="$PROJECT_NAME-plan-$TEAM"
WEB_APP_NAME="$PROJECT_NAME-api-$TEAM" #backend
VNET="$PROJECT_NAME-vnet-$TEAM"

# App Service plan (Linux, billigaste tier räcker: B1 eller F1 om tillgängligt)
az appservice plan create \
    --name $APP_PLAN_NAME \
    --resource-group $RESOURCE_GROUP \
    --sku B1 \
    --is-linux

# Web App
az webapp create --name $WEB_APP_NAME \
    --resource-group $RESOURCE_GROUP \
    --plan $APP_PLAN_NAME \
    --runtime "DOTNETCORE:10.0"

# Deploy från lokal build (kör i mappen med .csproj)
az webapp up --name $WEB_APP_NAME \
    --resource-group $RESOURCE_GROUP
```

## 2. Sätt upp VNet med subnät

```bash
az network vnet create \
    --name $VNET_NAME --resource-group $RESOURCE_GROUP \
    --address-prefix 10.0.0.0/16 \
    --subnet-name frontend-subnet --subnet-prefix 10.0.1.0/24

az network vnet subnet create \
    --name backend-subnet --resource-group $RESOURCE_GROUP \
    --vnet-name $VNET_NAME --address-prefix 10.0.2.0/24
```

## 3. Skapa Storage Account and a container

```bash
STORAGE_NAME="storage-$PROJECT_NAME-$TEAM"
STORAGE_CONTAINER_NAME="bilder"

 az storage account create \
      -n $STORAGE_NAME \
      -g $RESOURCE_GROUP \
      -l $REGION \
      --allow-blob-public-access true \
      --sku Standard_LRS

STORAGE_KEY=$(az storage account keys list \
    --account-name $STORAGE_NAME \
    --query "[0].value" \
    -o tsv)

az storage container create \
    --account-name $STORAGE_NAME \
    -n $STORAGE_CONTAINER_NAME \
    --account-key $STORAGE_KEY \
    --public-access blob \
    2>/dev/null || true
```

### 2. Sätt upp NSG för frontend

```bash
NSG_FRONTEND="nsg-frontend-$TEAM"

az network nsg create --name $NSG_FRONTEND --resource-group $RESOURCE_GROUP

# Tillåt HTTPS in från internet
az network nsg rule create \
    --nsg-name $NSG_FRONTEND --resource-group $RESOURCE_GROUP \
    --name Allow-HTTPS-In --priority 100 \
    --direction Inbound --access Allow --protocol Tcp \
    --source-address-prefixes Internet --destination-port-ranges 443

# Blockera HTTP explicit (lägre prioritetsnummer = körs innan default-regeln, men vi vill vara explicita)
az network nsg rule create \
    --nsg-name $NSG_FRONTEND --resource-group $RESOURCE_GROUP \
    --name Deny-HTTP-In --priority 110 \
    --direction Inbound --access Deny --protocol Tcp \
    --source-address-prefixes Internet --destination-port-ranges 80

# Tillåt trafik mellan subnets (frontend <-> backend)
az network nsg rule create \
    --nsg-name $NSG_FRONTEND --resource-group $RESOURCE_GROUP \
    --name Allow-Backend-VNet --priority 120 \
    --direction Inbound --access Allow --protocol '*' \
    --source-address-prefixes 10.0.2.0/24 --destination-port-ranges '*'

# Koppla NSG till subnet
az network vnet subnet update \
    --name frontend-subnet --resource-group $RESOURCE_GROUP \
    --vnet-name $VNET_NAME --network-security-group $NSG_FRONTEND
```

### 3. Sätt upp NSG för backend

```bash
NSG_BACKEND="nsg-backend-$TEAM"

az network nsg create --name $NSG_BACKEND --resource-group $RESOURCE_GROUP

# Tillåt bara trafik från frontend-subnet
az network nsg rule create \
    --nsg-name $NSG_BACKEND --resource-group $RESOURCE_GROUP \
    --name Allow-Frontend-VNet --priority 100 \
    --direction Inbound --access Allow --protocol '*' \
    --source-address-prefixes 10.0.1.0/24 --destination-port-ranges '*'

# Koppla NSG till subnet
az network vnet subnet update \
    --name backend-subnet --resource-group $RESOURCE_GROUP \
    --vnet-name $VNET_NAME --network-security-group $NSG_BACKEND
```

### 4. Sätt Webbappen till HTTPS-Only

```bash
# HTTPS-Only blockar port 80 mot appen på riktigt
az webapp update --name $WEB_APP_NAME --resource-group $RESOURCE_GROUP --https-only true
```

### 5. Koppla App Service till frontend subnet

#### 1. Sätt upp Frontend i Azure

#### 2. Integrera VNet med frontend

```bash
az webapp vnet-integration add \
  --name $WEB_APP_NAME --resource-group $RESOURCE_GROUP \
  --vnet $VNET_NAME --subnet frontend-subnet
```

#### 2. Koppla Storage Account mot en private endpoint.

```bash
az network private-endpoint create \
  --name pe-storage --resource-group $RESOURCE_GROUP \
  --vnet-name $VNET_NAME --subnet backend-subnet \
  --private-connection-resource-id <storage-account-resource-id> \
  --group-id blob --connection-name pe-storage-connection
```
