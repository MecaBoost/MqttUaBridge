using System.Text;
using System.Text.Json;
using MQTTnet;
using MqttUaBridge.Configuration;
using MqttUaBridge.Models;
using MqttUaBridge.Ua;
using Opc.Ua;
using Opc.Ua.Server; // <--- Ajouté pour FolderState et BaseDataVariableState
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace MqttUaBridge.Services
{
    public class MqttToUaBridgeService : IHostedService
    {
        private readonly IMqttClient _mqttClient;
        private readonly MqttUaModelBuilder _modelBuilder;
        private readonly MqttSettings _settings;
        private readonly ISystemContext _systemContext;
        private readonly MqttUaNodeManager _nodeManager;
        private readonly ILogger<MqttToUaBridgeService> _logger;
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
            _mqttClient.ApplicationMessageReceivedAsync += HandleApplicationMessageAsync;
            
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
                .WithCleanSession()
                .Build();

            try
            {
                await _mqttClient.ConnectAsync(options, cancellationToken);

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
            }
        }

        private Task HandleApplicationMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            string payloadJson = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            if (e.ApplicationMessage.Topic.Equals(_settings.StructureTopicTemplate, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<MqttNamePayload>(payloadJson);
                    
                    if (payload == null)
                    {
                        _logger.LogWarning("Failed to deserialize MqttNamePayload.");
                        return Task.CompletedTask;
                    }
                    
                    // Ligne 96 : Vérifions si le using Opc.Ua est bien là (il l'est).
                    // L'appel _systemContext.FindNode(ObjectIds.ObjectsFolder) EST correct.
                    // L'erreur CS1061 ici est très suspecte.
                    var opcuaRoot = _systemContext.FindNode(ObjectIds.ObjectsFolder) as FolderState; 
                    
                    if (opcuaRoot == null)
                    {
                         _logger.LogError("Could not find ObjectsFolder root node in OPC UA server.");
                        return Task.CompletedTask;
                    }

                    _modelBuilder.Build(_systemContext, _nodeManager.NamespaceIndex, opcuaRoot, payload);
                    _logger.LogInformation("OPC UA model built/updated successfully from MQTT structure.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing MQTT structure payload.");
                }
            }
            else 
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<MqttDataPayload>(payloadJson);
                    
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

        private void UpdateUaNodeValues(MqttDataPayload dataPayload)
        {
            DateTime timestamp = DateTime.UtcNow; 
            
            if (dataPayload?.Values == null) return;

            foreach (var mqttValue in dataPayload.Values)
            {
                // Utilisation de ?. pour la sécurité
                if (_modelBuilder.MqttIdToNodeMap.TryGetValue(mqttValue.Id, out BaseDataVariableState? variableNode) && variableNode != null)
                {
                    object? convertedValue = ConvertValue(mqttValue.Value, variableNode.DataType);
                    
                    var newValue = new DataValue(
                        new Variant(convertedValue), 
                        MqttQualityToStatusCode(mqttValue.QualityCode),
                        timestamp
                    );

                    lock (_uaUpdateLock)
                    {
                        // Ligne 167 : newValue.Value est Variant, newValue.Value.Value est object.
                        // variableNode.Value est object?. Ceci devrait compiler.
                        // L'erreur CS1061 ici est aussi très suspecte.
                        variableNode.Value = newValue.Value.Value;
                        variableNode.StatusCode = newValue.StatusCode;
                        variableNode.Timestamp = newValue.SourceTimestamp;
                        variableNode.ClearChangeMasks(_systemContext, false);
                    }
                }
            }
        }

        private object? ConvertValue(object? rawValue, NodeId targetDataTypeId)
        {
            if (rawValue == null) return null;

            try
            {
                var targetSystemType = Opc.Ua.TypeInfo.GetSystemType(targetDataTypeId, _systemContext.EncodeableFactory);
                
                if (targetSystemType == null)
                {
                     _logger.LogWarning($"Could not find system type for NodeId '{targetDataTypeId}'.");
                     return null;
                }
                return Convert.ChangeType(rawValue, targetSystemType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Conversion error: Cannot convert '{rawValue}' to OPC UA type '{targetDataTypeId}'. {ex.Message}");
                
                // CORRECTION (pour CS1503) : Le deuxième argument doit être le NamespaceIndex
                // et le troisième l'EncodeableFactory (requis par certaines surcharges).
                return Opc.Ua.TypeInfo.GetDefaultValue(targetDataTypeId, _nodeManager.NamespaceIndex, _systemContext.EncodeableFactory); 
            }
        }

        private StatusCode MqttQualityToStatusCode(int qualityCode)
        {
            return qualityCode == 3 ? StatusCodes.Good : StatusCodes.Bad; 
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MQTT-UA Bridge service is stopping.");
            _mqttClient?.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None);
            return Task.CompletedTask;
        }
    }
}