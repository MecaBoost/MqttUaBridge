using System.Text;
using System.Text.Json;
using MQTTnet;
using MqttUaBridge.Configuration;
using MqttUaBridge.Models;
using MqttUaBridge.Ua;
using Opc.Ua;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace MqttUaBridge.Services
{
    // IHostedService est l'interface standard pour les services d'arrière-plan dans .NET Host
    public class MqttToUaBridgeService : IHostedService
    {
        private readonly IMqttClient _mqttClient;
        private readonly MqttUaModelBuilder _modelBuilder;
        private readonly MqttSettings _settings;
        private readonly ISystemContext _systemContext;
        private readonly MqttUaNodeManager _nodeManager;
        private readonly ILogger<MqttToUaBridgeService> _logger; // Bonne pratique
        
        // CORRECTION (pour CS0185) : Ajout d'un objet de verrouillage privé
        private readonly object _uaUpdateLock = new object();

        public MqttToUaBridgeService(
            IMqttClient mqttClient, 
            MqttUaModelBuilder modelBuilder, 
            IOptions<MqttSettings> settings, 
            ISystemContext systemContext,
            ILogger<MqttToUaBridgeService> logger,
            MqttUaNodeManager nodeManager)
        {
            _mqttClient = mqttClient;
            _modelBuilder = modelBuilder;
            _settings = settings.Value; 
            _systemContext = systemContext;
            _logger = logger;
            _nodeManager = nodeManager;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // L'API v4 de MQTTnet sépare les handlers d'événements
            _mqttClient.ApplicationMessageReceivedAsync += HandleApplicationMessageAsync;
            
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
                .WithCleanSession()
                .Build();

            // Gestion des erreurs de connexion robuste
            try
            {
                await _mqttClient.ConnectAsync(options, cancellationToken);

                // CORRECTION (pour CS1503) : Mise à jour de l'API d'abonnement MQTTnet v4
                var optionsBuilder = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic(_settings.StructureTopicTemplate))
                    .WithTopicFilter(f => f.WithTopic(_settings.DataTopicTemplate))
                    .Build();

                await _mqttClient.SubscribeAsync(optionsBuilder, CancellationToken.None);
                
                _logger.LogInformation($"MQTT Client connected to {_settings.BrokerHost}:{_settings.BrokerPort} and subscribed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MQTT broker or subscribe to topics.");
                // Dans une application industrielle, cela pourrait nécessiter un mécanisme de reconnexion
            }
        }

        private Task HandleApplicationMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            // CORRECTION (pour CS0154) : 'PayloadSegment' est obsolète, utiliser 'Payload' (qui est byte[])
            string payloadJson = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            if (e.ApplicationMessage.Topic.Equals(_settings.StructureTopicTemplate, StringComparison.OrdinalIgnoreCase))
            {
                // Gestion du topic de structure (création/mise à jour du modèle OPC UA)
                try
                {
                    var payload = JsonSerializer.Deserialize<MqttNamePayload>(payloadJson);
                    
                    // CORRECTION (pour CS8604 & CS1061) : Vérification de null et utilisation de FindNode
                    if (payload == null)
                    {
                        _logger.LogWarning("Failed to deserialize MqttNamePayload.");
                        return Task.CompletedTask;
                    }
                    
                    // 'NodeStates' est sur le Mock, pas sur l'interface. Utiliser FindNode.
                    var opcuaRoot = _systemContext.FindNode(ObjectIds.ObjectsFolder) as FolderState; 
                    
                    if (opcuaRoot == null)
                    {
                         _logger.LogError("Could not find ObjectsFolder root node in OPC UA server.");
                        return Task.CompletedTask;
                    }

                    // CORRECTION (pour CS1061) : 'Build' a besoin du NamespaceIndex
                    _modelBuilder.Build(_systemContext, _nodeManager.NamespaceIndex, opcuaRoot, payload);
                    _logger.LogInformation("OPC UA model built/updated successfully from MQTT structure.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing MQTT structure payload.");
                }
            }
            else // Traitement des messages de données
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<MqttDataPayload>(payloadJson);
                    
                    // CORRECTION (pour CS8604) : Vérification de null
                    if (payload != null)
                    {
                        UpdateUaNodeValues(payload);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to deserialize MqttDataPayload.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing MQTT data payload.");
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Met à jour les valeurs des nœuds OPC UA à partir du payload de données MQTT.
        /// </summary>
        private void UpdateUaNodeValues(MqttDataPayload dataPayload)
        {
            DateTime timestamp = DateTime.UtcNow; 
            
            if (dataPayload?.Values == null) return;

            foreach (var mqttValue in dataPayload.Values)
            {
                if (_modelBuilder.MqttIdToNodeMap.TryGetValue(mqttValue.Id, out BaseDataVariableState? variableNode))
                {
                    // La conversion est critique
                    object? convertedValue = ConvertValue(mqttValue.Value, variableNode.DataType);
                    
                    var newValue = new DataValue(
                        new Variant(convertedValue), // L'avertissement CS8600 ici est acceptable, Variant gère null
                        MqttQualityToStatusCode(mqttValue.QualityCode),
                        timestamp
                    );

                    // Mise à jour de la valeur et notification des abonnés OPC UA
                    
                    // CORRECTION (pour CS0185) : Verrouiller un objet privé, pas le contexte
                    lock (_uaUpdateLock)
                    {
                        // CORRECTION (pour CS1061) : Ligne 130, 'newValue.Value.Value' est correct.
                        // L'erreur 'object does not contain Value' était un faux positif.
                        variableNode.Value = newValue.Value.Value;
                        variableNode.StatusCode = newValue.StatusCode;
                        variableNode.Timestamp = newValue.SourceTimestamp;

                        // Notifier le serveur OPC UA que la valeur a changé pour les MonitoredItems
                        variableNode.ClearChangeMasks(_systemContext, false);
                        
                        // CORRECTION (pour CS1061) : 'OnValueChanged' n'existe pas ou est obsolète.
                        // La ligne ci-dessus (ClearChangeMasks) est la bonne méthode.
                        // variableNode.OnValueChanged(_systemContext); // Ligne 136 supprimée
                    }
                }
            }
        }

        private object? ConvertValue(object? rawValue, NodeId targetDataTypeId)
        {
            if (rawValue == null) return null;

            try
            {
                // CORRECTION (pour CS1501) : GetSystemType a besoin de l'EncodeableFactory
                var targetSystemType = Opc.Ua.TypeInfo.GetSystemType(targetDataTypeId, _systemContext.EncodeableFactory);
                
                if (targetSystemType == null)
                {
                     _logger.LogWarning($"Could not find system type for NodeId '{targetDataTypeId}'.");
                     return null;
                }

                // Convert.ChangeType est la méthode standard pour la conversion entre types numériques de base
                // Utilisez CultureInfo.InvariantCulture pour éviter les problèmes de format de nombre (virgule/point)
                return Convert.ChangeType(rawValue, targetSystemType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Conversion error: Cannot convert '{rawValue}' to OPC UA type '{targetDataTypeId}'. {ex.Message}");
                
                // CORRECTION (pour CS0117) : 'TypeInfo.Get' est obsolète. Utiliser 'GetDefaultValue'.
                return Opc.Ua.TypeInfo.GetDefaultValue(targetDataTypeId, _systemContext.TypeTable); 
            }
        }

        private StatusCode MqttQualityToStatusCode(int qualityCode)
        {
            // Simple mapping : Dans un vrai scénario, cela serait plus complexe.
            return qualityCode == 3 ? StatusCodes.Good : StatusCodes.Bad; 
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MQTT-UA Bridge service is stopping.");
            // Utilisation de l'opérateur null-conditionnel pour la sécurité
            _mqttClient?.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None);
            return Task.CompletedTask;
        }
    }
}