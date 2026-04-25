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

@description('Azure Blob Storage connection string used by repos with Provider=AzureBlob (matches the web app value)')
@secure()
param azureBlobConnectionString string

@description('Workflow runtimes that get a dedicated job + queue. Each name maps to daisigit-worker-<name>:latest in ACR.')
param runtimes array = [
  'minimal'
  'dotnet'
  'node'
  'python'
  'full'
]

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

// ── Storage Account ──

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

// File share for cross-execution build caches (NuGet, npm, pip, etc.). Mounted into
// every per-runtime job at the standard cache locations so successive runs reuse
// already-downloaded packages.
resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource buildCacheShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: 'build-cache'
  properties: {
    shareQuota: 50  // 50 GiB cap; raise if your monorepo restores grow large.
  }
}

// One queue per runtime: workflow-executions-<runtime>.
resource runtimeQueues 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = [for runtime in runtimes: {
  parent: queueService
  name: 'workflow-executions-${runtime}'
}]

// Legacy single queue retained so dispatchers/jobs deployed against the old shape can drain.
// Safe to remove once nothing references it.
resource legacyQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: 'workflow-executions'
}

// ── Log Analytics + Container Apps Environment ──

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

// Register the build-cache share with the environment so per-job volumes can mount it.
resource buildCacheStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: containerAppsEnv
  name: 'build-cache'
  properties: {
    azureFile: {
      accountName: storageAccount.name
      accountKey: storageAccount.listKeys().keys[0].value
      shareName: buildCacheShare.name
      accessMode: 'ReadWrite'
    }
  }
}

// ── Container Apps Jobs — one per runtime ──

resource workerJobs 'Microsoft.App/jobs@2024-03-01' = [for runtime in runtimes: {
  name: '${namePrefix}-worker-${runtime}'
  location: location
  properties: {
    environmentId: containerAppsEnv.id
    configuration: {
      triggerType: 'Event'
      replicaTimeout: 1800
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
                queueName: 'workflow-executions-${runtime}'
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
        {
          name: 'azure-blob-connection'
          value: azureBlobConnectionString
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
          image: '${acr.properties.loginServer}/${namePrefix}-worker-${runtime}:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'Cosmo__ConnectionString',          secretRef: 'cosmo-connection' }
            { name: 'WorkflowQueue__ConnectionString',  secretRef: 'storage-connection' }
            { name: 'WorkflowQueue__Name',              value: 'workflow-executions-${runtime}' }
            { name: 'WorkflowQueue__Runtime',           value: runtime }
            { name: 'Daisi__SecretKey',                 secretRef: 'daisi-secret-key' }
            { name: 'AzureBlob__ConnectionString',      secretRef: 'azure-blob-connection' }
            // Point the popular package managers at our shared cache.
            { name: 'NUGET_PACKAGES',                   value: '/cache/nuget' }
            { name: 'NPM_CONFIG_CACHE',                 value: '/cache/npm' }
            { name: 'PIP_CACHE_DIR',                    value: '/cache/pip' }
          ]
          volumeMounts: [
            { volumeName: 'build-cache', mountPath: '/cache' }
          ]
        }
      ]
      volumes: [
        {
          name: 'build-cache'
          storageType: 'AzureFile'
          storageName: 'build-cache'
        }
      ]
    }
  }
}]

// ── Outputs ──

output acrLoginServer string = acr.properties.loginServer
output storageAccountName string = storageAccount.name
output containerAppsEnvironmentName string = containerAppsEnv.name
output queueConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value}'
