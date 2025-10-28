using System.Text.Json.Serialization;

namespace MqttUaBridge.Models
{
    /// <summary>
    /// Représente un seul point de donnée (valeur) dans la charge utile de publication MQTT (Mqtt_Data).
    /// </summary>
    public class MqttValue
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty; // ID de liaison (ex: "470")

        [JsonPropertyName("ts")]
        public string Timestamp { get; set; } = string.Empty; // Horodatage

        [JsonPropertyName("val")]
        public object? Value { get; set; } // La valeur elle-même (type dynamique)

        [JsonPropertyName("qc")]
        public int QualityCode { get; set; } // Code de qualité
    }
}