using Opc.Ua;

namespace MqttUaBridge.Ua
{
    public static class OpcUaTypeMapper
    {
        /// <summary>
        /// Mappe les chaînes de types de données (issues du payload MQTT Name) aux NodeId de types de données OPC UA.
        /// </summary>
        public static NodeId MapToNodeId(string dataType)
        {
            return dataType.ToLowerInvariant() switch
            {
                "real" => DataTypes.Float,      // Mappe à Float (System.Single)
                "udint" => DataTypes.UInt32,    // Mappe à UInt32 (System.UInt32)
                "string" => DataTypes.String,
                "int" => DataTypes.Int16,       // Assumé Int16 pour "Int"
                "uint" => DataTypes.UInt16,     // Assumé UInt16 pour "UInt"
                "dint" => DataTypes.Int32,      // Assumé Int32 pour "DInt"
                _ => DataTypes.BaseDataType,    // Type générique par défaut
            };
        }
    }
}