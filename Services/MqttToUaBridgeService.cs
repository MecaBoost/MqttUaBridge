using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MqttUaBridge.Configuration;
using MqttUaBridge.Models;
using MqttUaBridge.Ua;
using Opc.Ua;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
            _mqttClient.ApplicationMessageReceivedAsync += HandleApplicationMessageAsync;
            
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
                .WithCleanSession()
                .Build();

            // Gestion des erreurs de connexion robuste
            try
            {
                await _mqttClient.ConnectAsync(options, cancellationToken);

                // Abonnement générique (utilisation du joker pour tous les topics de données)
                await _mqttClient.SubscribeAsync(
                    new MqttTopicFilterBuilder().WithTopic(_settings.StructureTopicTemplate).Build(), 
                    new MqttTopicFilterBuilder().WithTopic(_settings.DataTopicTemplate).Build()
                );
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
            string payloadJson = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            if (e.ApplicationMessage.Topic.Equals(_settings.StructureTopicTemplate, StringComparison.OrdinalIgnoreCase))
            {
                // Gestion du topic de structure (création/mise à jour du modèle OPC UA)
                try
                {
                    var payload = JsonSerializer.Deserialize<MqttNamePayload>(payloadJson);
                    // Le rootFolder standard de l'espace d'adressage OPC UA
                    var opcuaRoot = _systemContext.NodeStates.FindNode(ObjectIds.ObjectsFolder.Identifier) as FolderState; 
                    _modelBuilder.Build(_systemContext, opcuaRoot, payload);
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
                    UpdateUaNodeValues(payload);
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
                if (_modelBuilder.MqttIdToNodeMap.TryGetValue(mqttValue.Id, out BaseDataVariableState variableNode))
                {
                    // La conversion est critique
                    object? convertedValue = ConvertValue(mqttValue.Value, variableNode.DataType);
                    
                    var newValue = new DataValue(
                        new Variant(convertedValue),
                        MqttQualityToStatusCode(mqttValue.QualityCode),
                        timestamp
                    );

                    // Mise à jour de la valeur et notification des abonnés OPC UA
                    lock (_systemContext.NodeStates) // Verrouiller l'accès concurrentiel à l'espace d'adressage
                    {
                        variableNode.Value = newValue.Value.Value;
                        variableNode.StatusCode = newValue.StatusCode;
                        variableNode.Timestamp = newValue.SourceTimestamp;

                        // Notifier le serveur OPC UA que la valeur a changé pour les MonitoredItems
                        variableNode.ClearChangeMasks(_systemContext, false);
                        variableNode.OnValueChanged(_systemContext); 
                    }
                }
            }
        }

        private object? ConvertValue(object? rawValue, NodeId targetDataTypeId)
        {
            if (rawValue == null) return null;

            try
            {
                // Le type cible natif (ex: System.UInt32 pour DataTypes.UInt32)
                var targetSystemType = TypeInfo.GetSystemType(TypeInfo.GetBuiltInType(targetDataTypeId));
                
                // Convert.ChangeType est la méthode standard pour la conversion entre types numériques de base
                // Utilisez CultureInfo.InvariantCulture pour éviter les problèmes de format de nombre (virgule/point)
                return Convert.ChangeType(rawValue, targetSystemType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Conversion error: Cannot convert '{rawValue}' to OPC UA type '{targetDataTypeId.Identifier}'. {ex.Message}");
                // En cas d'échec (ex: overflow), renvoyer la valeur par défaut pour la robustesse
                return TypeInfo.Get(targetDataTypeId).Get.GetValue(); 
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
            _mqttClient?.DisconnectAsync();
            return Task.CompletedTask;
        }
    }
}