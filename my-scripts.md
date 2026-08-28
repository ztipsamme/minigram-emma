## 1. Skapa Infrastruktur

```bash
RESOURCE_GROUP="RG-Oskar-Kotlinski-fbed43-DotNetCloudDeveloper-VT-Mars-Goteborg"
APP_PLAN_NAME="minigram-maritiman"
WEB_APP_NAME="minigram-api-maritiman"
VNET_NAME="minigram-vnet"

# App Service plan (Linux, billigaste tier räcker: B1 eller F1 om tillgängligt)
az appservice plan create \
    --name plan-minigram \
    --resource-group $RESOURCE_GROUP \
    --sku B1 \
    --is-linux

# Web App
az webapp create --name $WEB_APP_NAME --resource-group $RESOURCE_GROUP \
--plan $APP_PLAN_NAME --runtime "DOTNETCORE:10.0"

# Deploy från lokal build (kör i mappen med .csproj)
az webapp up --name $WEB_APP_NAME --resource-group $RESOURCE_GROUP
```

## 2. Sätt upp VNet med subnät

### 1. Skapa Vnet med subnät

```bash
az network vnet create \
    --name $VNET_NAME --resource-group $RESOURCE_GROUP \
    --address-prefix 10.0.0.0/16 \
    --subnet-name frontend-subnet --subnet-prefix 10.0.1.0/24

az network vnet subnet create \
    --name backend-subnet --resource-group $RESOURCE_GROUP \
    --vnet-name $VNET_NAME --address-prefix 10.0.2.0/24
```

### 2. Sätt upp NSG för frontend

```bash
az network nsg create --name nsg-frontend --resource-group $RESOURCE_GROUP

# Tillåt HTTPS in från internet
az network nsg rule create \
    --nsg-name nsg-frontend --resource-group $RESOURCE_GROUP \
    --name Allow-HTTPS-In --priority 100 \
    --direction Inbound --access Allow --protocol Tcp \
    --source-address-prefixes Internet --destination-port-ranges 443

# Blockera HTTP explicit (lägre prioritetsnummer = körs innan default-regeln, men vi vill vara explicita)
az network nsg rule create \
    --nsg-name nsg-frontend --resource-group $RESOURCE_GROUP \
    --name Deny-HTTP-In --priority 110 \
    --direction Inbound --access Deny --protocol Tcp \
    --source-address-prefixes Internet --destination-port-ranges 80

# Tillåt trafik mellan subnets (frontend <-> backend)
az network nsg rule create \
    --nsg-name nsg-frontend --resource-group $RESOURCE_GROUP \
    --name Allow-Backend-VNet --priority 120 \
    --direction Inbound --access Allow --protocol '*' \
    --source-address-prefixes 10.0.2.0/24 --destination-port-ranges '*'

# Koppla NSG till subnet
az network vnet subnet update \
    --name frontend-subnet --resource-group $RESOURCE_GROUP \
    --vnet-name $VNET_NAME --network-security-group nsg-frontend
```

### 3. Sätt upp NSG för backend

```bash
az network nsg create --name nsg-backend --resource-group $RESOURCE_GROUP

# Tillåt bara trafik från frontend-subnet
az network nsg rule create \
    --nsg-name nsg-backend --resource-group $RESOURCE_GROUP \
    --name Allow-Frontend-VNet --priority 100 \
    --direction Inbound --access Allow --protocol '*' \
    --source-address-prefixes 10.0.1.0/24 --destination-port-ranges '*'

# Koppla NSG till subnet
az network vnet subnet update \
    --name backend-subnet --resource-group $RESOURCE_GROUP \
    --vnet-name $VNET_NAME --network-security-group nsg-backend
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

### 6. Koppla Storage Account till backend subnet

#### 1. Skapa Storage Account

#### 2. Koppla Storage Account mot en private endpoint.

```bash
az network private-endpoint create \
  --name pe-storage --resource-group $RESOURCE_GROUP \
  --vnet-name $VNET_NAME --subnet backend-subnet \
  --private-connection-resource-id <storage-account-resource-id> \
  --group-id blob --connection-name pe-storage-connection
```

/
