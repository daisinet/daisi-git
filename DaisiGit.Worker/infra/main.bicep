@description('Location for all resources')
param location string = resourceGroup().location

@description('Name prefix for resources')
param namePrefix string = 'daisigit'

@description('Cosmos DB connection string for the worker')
@secure()
param cosmoConnectionString string

@description('Daisi secret key for workflow secret decryption')
@secure()
param daisiSecretKey string

// ── Azure Container Registry ──

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: '${namePrefix}acr'
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// ── Storage Account (for workflow dispatch queue) ──

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: '${namePrefix}wfqueue'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource workflowQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: 'workflow-executions'
}

// ── Log Analytics (required by Container Apps Environment) ──

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ── Container Apps Environment ──

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ── Container Apps Job (queue-triggered) ──

resource workerJob 'Microsoft.App/jobs@2024-03-01' = {
  name: '${namePrefix}-worker'
  location: location
  properties: {
    environmentId: containerAppsEnv.id
    configuration: {
      triggerType: 'Event'
      replicaTimeout: 1800 // 30 minutes (matches max RunScript timeout)
      replicaRetryLimit: 1
      eventTriggerConfig: {
        scale: {
          minExecutions: 0
          maxExecutions: 10
          rules: [
            {
              name: 'queue-trigger'
              type: 'azure-queue'
              metadata: {
                queueName: 'workflow-executions'
                queueLength: '1'
                accountName: storageAccount.name
              }
              auth: [
                {
                  secretRef: 'storage-connection'
                  triggerParameter: 'connection'
                }
              ]
            }
          ]
        }
      }
      secrets: [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
        {
          name: 'storage-connection'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'cosmo-connection'
          value: cosmoConnectionString
        }
        {
          name: 'daisi-secret-key'
          value: daisiSecretKey
        }
      ]
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${acr.properties.loginServer}/${namePrefix}-worker:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'Cosmo__ConnectionString'
              secretRef: 'cosmo-connection'
            }
            {
              name: 'WorkflowQueue__ConnectionString'
              secretRef: 'storage-connection'
            }
            {
              name: 'Daisi__SecretKey'
              secretRef: 'daisi-secret-key'
            }
          ]
        }
      ]
    }
  }
}

// ── Outputs ──

output acrLoginServer string = acr.properties.loginServer
output storageAccountName string = storageAccount.name
output containerAppsEnvironmentName string = containerAppsEnv.name
output workerJobName string = workerJob.name
output queueConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value}'
