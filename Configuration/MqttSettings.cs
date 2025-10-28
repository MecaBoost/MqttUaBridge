namespace MqttUaBridge.Configuration
{
    public class MqttSettings
    {
        public string BrokerHost { get; set; } = string.Empty;
        public int BrokerPort { get; set; } = 1883;
        public string StructureTopicTemplate { get; set; } = string.Empty;
        public string DataTopicTemplate { get; set; } = string.Empty;
        public string OpcUaNamespaceUri { get; set; } = string.Empty;
        public string RootNodeName { get; set; } = string.Empty;
    }
}