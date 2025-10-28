using Opc.Ua;
using Opc.Ua.Server; 
using System.Collections.Concurrent;
using MqttUaBridge.Models;
using MqttUaBridge.Configuration;
using Microsoft.Extensions.Options;

namespace MqttUaBridge.Ua
{
    public class MqttUaModelBuilder
    {
        private readonly ConcurrentDictionary<string, BaseDataVariableState> _mqttIdToNodeMap = new();
        private readonly MqttSettings _settings;

        public ConcurrentDictionary<string, BaseDataVariableState> MqttIdToNodeMap => _mqttIdToNodeMap;

        public MqttUaModelBuilder(IOptions<MqttSettings> settings)
        {
            _settings = settings.Value;
        }

        public void Build(ISystemContext context, ushort namespaceIndex, FolderState rootFolder, MqttNamePayload payload)
        {
            FolderState baseRoot = FindOrCreateFolder(context, namespaceIndex, rootFolder, _settings.RootNodeName);

            var definitions = payload.Connections
                .SelectMany(c => c.DataPoints)
                .SelectMany(dp => dp.DataPointDefinitions);
            
            foreach (var def in definitions)
            {
                string[] parts = def.Name.Split('.');
                if (parts.Length < 2) continue;

                string[] hierarchyPath = parts.Skip(1).SkipLast(1).ToArray(); 
                string variableName = parts.Last(); 
                
                FolderState parentFolder = GetOrCreateHierarchy(context, namespaceIndex, baseRoot, hierarchyPath);

                var variable = new BaseDataVariableState<object>(parentFolder)
                {
                    SymbolicName = variableName,
                    NodeId = new NodeId($"mqtt-id-{def.Id}", namespaceIndex), 
                    BrowseName = new QualifiedName(variableName),
                    DisplayName = new LocalizedText(variableName.Replace('_', ' ')),
                    DataType = GetDataTypeNodeId(def.DataType),
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead
                };

                // Initialisation de la valeur
                // CORRECTION (pour CS1503) : Le deuxième argument doit être le NamespaceIndex
                variable.Value = Opc.Ua.TypeInfo.GetDefaultValue(variable.DataType, namespaceIndex, context.EncodeableFactory);
                variable.StatusCode = StatusCodes.BadWaitingForInitialData;
                variable.Timestamp = DateTime.UtcNow;

                parentFolder.AddChild(variable);
                _mqttIdToNodeMap[def.Id] = variable;
            }

            baseRoot.ClearChangeMasks(context, true);
        }
        
        private FolderState GetOrCreateHierarchy(ISystemContext context, ushort namespaceIndex, FolderState root, string[] path)
        {
            FolderState current = root;
            foreach (var name in path)
            {
                current = FindOrCreateFolder(context, namespaceIndex, current, name);
            }
            return current;
        }

        private FolderState FindOrCreateFolder(ISystemContext context, ushort namespaceIndex, FolderState parent, string name)
        {
            var browseName = new QualifiedName(name);
            
            // CORRECTION (pour CS1501) : Utiliser la surcharge FindChild(context, browseName)
            var existing = parent.FindChild(context, browseName) as FolderState;
            
            if (existing != null) return existing;

            var newFolder = new FolderState(parent)
            {
                SymbolicName = name,
                NodeId = new NodeId($"{parent.SymbolicName}/{name}", namespaceIndex), 
                BrowseName = browseName,
                DisplayName = new LocalizedText(name.Replace('_', ' ')),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType
            };
            
            parent.AddChild(newFolder);
            return newFolder;
        }

        private NodeId GetDataTypeNodeId(string dataTypeName)
        {
            switch (dataTypeName?.ToLower())
            {
                case "real":
                case "double":
                    return DataTypeIds.Double;
                case "udint":
                    return DataTypeIds.UInt32;
                case "dint":
                    return DataTypeIds.Int32;
                case "int":
                    return DataTypeIds.Int16; 
                case "string":
                    return DataTypeIds.String;
                case "bool":
                case "boolean":
                    return DataTypeIds.Boolean;
                default:
                    // CORRECTION (pour CS0117) : 'Variant' n'est pas un DataType, utiliser 'BaseDataType'
                    return DataTypeIds.BaseDataType; 
            }
        }
    }
}