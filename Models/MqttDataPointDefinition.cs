using System.Text.Json.Serialization;

namespace MqttUaBridge.Models
{
    // Définition de base pour un point de donnée dans la structure Mqtt_Name
    public class MqttDataPointDefinition
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty; [cite_start]// ID de liaison [cite: 10]

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty; [cite_start]// Nom hiérarchique [cite: 14]

        [JsonPropertyName("dataType")]
        public string DataType { get; set; } = string.Empty; [cite_start]// Type de donnée (ex: UDInt, Real) [cite: 9, 27]
        
        // Les autres propriétés sont omises pour la concision du modèle
    }

    [cite_start]// Modèles conteneurs pour la désérialisation de Mqtt_Name [cite: 8]
    public class MqttDataPoint
    {
        [JsonPropertyName("dataPointDefinitions")]
        public List<MqttDataPointDefinition> DataPointDefinitions { get; set; } = new List<MqttDataPointDefinition>();
    }

    public class MqttConnection
    {
        [JsonPropertyName("dataPoints")]
        public List<MqttDataPoint> DataPoints { get; set; } = new List<MqttDataPoint>();
    }

    public class MqttNamePayload
    {
        [JsonPropertyName("connections")]
        public List<MqttConnection> Connections { get; set; } = new List<MqttConnection>();
    }
}