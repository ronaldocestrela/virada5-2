@description('Região Azure para provisionamento da infraestrutura.')
param location string = resourceGroup().location

@description('Nome base da aplicação (prefixo para App Service e Azure SQL).')
param appName string = 'virada5x2'

@description('SKU do App Service Plan Linux.')
param appServicePlanSku string = 'B1'

@description('Administrador do Azure SQL Server.')
param sqlAdminLogin string

@secure()
@description('Senha do administrador do Azure SQL Server (fornecida no deploy via secrets/Key Vault).')
param sqlAdminPassword string

@description('Endpoint OTLP opcional para OpenTelemetry.')
param otlpEndpoint string = ''

@secure()
@description('Cabeçalhos opcionais de autenticação OTLP (injetados via secrets no deploy).')
param otlpHeaders string = ''

var webAppName = '${appName}-web-${uniqueString(resourceGroup().id)}'
var planName = '${appName}-plan-${uniqueString(resourceGroup().id)}'
var sqlServerName = '${appName}-sql-${uniqueString(resourceGroup().id)}'
var sqlDbName = 'Scale5x2Db'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: appServicePlanSku
  }
  properties: {
    reserved: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
  }
}

resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

var dbConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDbName};Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/health/ready'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'DB_CONNECTION_STRING'
          value: dbConnectionString
        }
        {
          name: 'DATA_PROTECTION_KEYS_PATH'
          value: '/home/dataprotection-keys'
        }
        {
          name: 'OTEL_SERVICE_NAME'
          value: 'Virada5x2.WebApp'
        }
        {
          name: 'OTEL_EXPORTER_OTLP_ENDPOINT'
          value: otlpEndpoint
        }
        {
          name: 'OTEL_EXPORTER_OTLP_HEADERS'
          value: otlpHeaders
        }
        {
          name: 'SERILOG_MINIMUM_LEVEL'
          value: 'Information'
        }
      ]
    }
  }
}

output webAppDefaultHostName string = webApp.properties.defaultHostName
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName

