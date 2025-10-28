using System.Text.Json.Serialization;

namespace MqttUaBridge.Models
{
    /// <summary>
    /// Représente le payload complet (charge utile) d'un message de données MQTT (Mqtt_Data).
    /// </summary>
    public class MqttDataPayload
    {
        [JsonPropertyName("seq")]
        public long Sequence { get; set; } // Numéro de séquence (ex: 12459406)

        [JsonPropertyName("vals")]
        // Le tableau 'vals' contient la liste des mises à jour de points de donnée (MqttValue)
        public List<MqttValue> Values { get; set; } = new List<MqttValue>();
    }
}