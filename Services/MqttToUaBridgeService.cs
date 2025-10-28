using System.Text;
using System.Text.Json;
using MQTTnet;
using MqttUaBridge.Configuration;
using MqttUaBridge.Models;
using MqttUaBridge.Ua;
using Opc.Ua;
using Opc.Ua.Server;
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
        private readonly ISystemContext _systemContext; // Reste ISystemContext pour l'injection
        private readonly MqttUaNodeManager _nodeManager;
        private readonly ILogger<MqttToUaBridgeService> _logger; // Bonne pratique

        private readonly object _uaUpdateLock = new object();

        public MqttToUaBridgeService(
            IMqttClient mqttClient,
            MqttUaModelBuilder modelBuilder,
            IOptions<MqttSettings> settings,
            ISystemContext systemContext, // Injection reste ISystemContext
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
            byte[] payloadBytes = e.ApplicationMessage.Payload ?? Array.Empty<byte>();
            string payloadJson = Encoding.UTF8.GetString(payloadBytes);

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

                    FolderState? opcuaRoot = null;
                    if (_nodeManager.Server is ServerInternal serverInternal) // Tente de caster vers l'implémentation
                    {
                        opcuaRoot = serverInternal.NodeManager.NodeCache.Find(ObjectIds.ObjectsFolder) as FolderState;
                    }

                    // Si le cast échoue ou le nœud n'est pas trouvé, log l'erreur
                    if (opcuaRoot == null)
                    {
                        _logger.LogError("Could not find ObjectsFolder root node in OPC UA Server NodeCache.");
                        // Essayer via le contexte système comme fallback si NodeCache échoue ?
                        // opcuaRoot = _systemContext.FindNode(ObjectIds.ObjectsFolder) as FolderState; // FindNode n'existe pas sur ISystemContext
                        return Task.CompletedTask; // Sortir si non trouvé
                    }

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
                if (_modelBuilder.MqttIdToNodeMap.TryGetValue(mqttValue.Id, out BaseDataVariableState? variableNode))
                {
                    object? convertedValue = ConvertValue(mqttValue.Value, variableNode.DataType);

                    var newValue = new DataValue(
                        new Variant(convertedValue),
                        MqttQualityToStatusCode(mqttValue.QualityCode),
                        timestamp // Utiliser timestamp commun ou celui du message si pertinent
                    );

                    lock (_uaUpdateLock)
                    {
                        if (newValue?.Value != null)
                        {
                            // CORRECTION (CS1061 'Value'): Assigner la Variant à WrappedValue
                            variableNode.WrappedValue = newValue.Value;
                            variableNode.StatusCode = newValue.StatusCode;
                            variableNode.Timestamp = newValue.SourceTimestamp; // Préférer SourceTimestamp

                            variableNode.ClearChangeMasks(_systemContext, false);
                        }
                        else
                        {
                            // CORRECTION (CS1503 GetDefaultValue): Fournir ValueRank et TypeTable
                            variableNode.Value = Opc.Ua.TypeInfo.GetDefaultValue(variableNode.DataType,
                                                                                 Opc.Ua.ValueRanks.Scalar,
                                                                                 _systemContext.TypeTable);
                            variableNode.StatusCode = newValue?.StatusCode ?? StatusCodes.BadNoData;
                            variableNode.Timestamp = newValue?.SourceTimestamp ?? timestamp;
                            variableNode.ClearChangeMasks(_systemContext, false);
                            _logger.LogWarning($"Update failed for NodeId {variableNode.NodeId} (MQTT ID: {mqttValue.Id}), setting default value.");
                        }
                    }
                }
                else
                {
                     _logger.LogTrace($"MQTT ID '{mqttValue.Id}' not found in OPC UA model map."); // LogTrace est moins verbeux
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

                // Gérer le cas où rawValue est un JsonElement (fréquent avec System.Text.Json)
                if (rawValue is JsonElement jsonElement)
                {
                    // Essayer d'extraire la valeur appropriée du JsonElement
                    if (targetSystemType == typeof(double) && jsonElement.TryGetDouble(out var doubleVal)) return doubleVal;
                    if (targetSystemType == typeof(float) && jsonElement.TryGetSingle(out var floatVal)) return floatVal;
                    if (targetSystemType == typeof(int) && jsonElement.TryGetInt32(out var intVal)) return intVal;
                    if (targetSystemType == typeof(uint) && jsonElement.TryGetUInt32(out var uintVal)) return uintVal;
                    if (targetSystemType == typeof(long) && jsonElement.TryGetInt64(out var longVal)) return longVal;
                    if (targetSystemType == typeof(ulong) && jsonElement.TryGetUInt64(out var ulongVal)) return ulongVal;
                    if (targetSystemType == typeof(short) && jsonElement.TryGetInt16(out var shortVal)) return shortVal;
                    if (targetSystemType == typeof(ushort) && jsonElement.TryGetUInt16(out var ushortVal)) return ushortVal;
                    if (targetSystemType == typeof(byte) && jsonElement.TryGetByte(out var byteVal)) return byteVal;
                    if (targetSystemType == typeof(sbyte) && jsonElement.TryGetSByte(out var sbyteVal)) return sbyteVal;
                    if (targetSystemType == typeof(bool) && jsonElement.TryGetBoolean(out var boolVal)) return boolVal;
                    if (targetSystemType == typeof(string)) return jsonElement.GetString();
                    if (targetSystemType == typeof(decimal) && jsonElement.TryGetDecimal(out var decimalVal)) return decimalVal;
                    if (targetSystemType == typeof(DateTime) && jsonElement.TryGetDateTime(out var dtVal)) return dtVal;
                    // ... autres types si nécessaire

                    // Si aucun type ne correspond, essayer une conversion générique depuis la chaîne
                    rawValue = jsonElement.ToString();
                }

                return Convert.ChangeType(rawValue, targetSystemType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Conversion error: Cannot convert '{rawValue}' (Type: {rawValue.GetType().Name}) to OPC UA type '{targetDataTypeId}'. {ex.Message}");

                // CORRECTION (CS1503 GetDefaultValue): Fournir ValueRank et TypeTable
                return Opc.Ua.TypeInfo.GetDefaultValue(targetDataTypeId,
                                                       Opc.Ua.ValueRanks.Scalar,
                                                       _systemContext.TypeTable);
            }
        }

        private StatusCode MqttQualityToStatusCode(int qualityCode)
        {
            // Simple mapping : Dans un vrai scénario, cela serait plus complexe.
            return qualityCode == 3 ? StatusCodes.Good : StatusCodes.Bad; // Code 3 = Bon dans Simatic S7 Connector
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MQTT-UA Bridge service is stopping.");
            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                // Options de déconnexion optionnelles (par ex. pour publier un message Last Will)
                var disconnectOptions = new MqttClientDisconnectOptionsBuilder().Build();
                try
                {
                    await _mqttClient.DisconnectAsync(disconnectOptions, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disconnecting MQTT client.");
                }
            }
             _mqttClient?.Dispose(); // Libérer les ressources
        }
    }
}