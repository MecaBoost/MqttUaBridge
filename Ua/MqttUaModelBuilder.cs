using Opc.Ua;
using Opc.Ua.Server; // <-- CORRECTION : Ajouté pour FolderState et BaseDataVariableState
using System.Collections.Concurrent;
using MqttUaBridge.Models;
using MqttUaBridge.Configuration;
using Microsoft.Extensions.Options;

namespace MqttUaBridge.Ua
{
    public class MqttUaModelBuilder
    {
        // Maintient la correspondance entre l'ID MQTT ("470") et le nœud VariableState OPC UA
        private readonly ConcurrentDictionary<string, BaseDataVariableState> _mqttIdToNodeMap = new();
        private readonly MqttSettings _settings;

        public ConcurrentDictionary<string, BaseDataVariableState> MqttIdToNodeMap => _mqttIdToNodeMap;

        public MqttUaModelBuilder(IOptions<MqttSettings> settings)
        {
            _settings = settings.Value;
        }

        /// <summary>
        /// Construit l'espace d'adressage OPC UA à partir de la structure MQTT (Mqtt_Name).
        /// Gère la création de la hiérarchie d'objets (Folders).
        /// </summary>
        // CORRECTION (pour CS1061) : Ajout du paramètre 'namespaceIndex'
        public void Build(ISystemContext context, ushort namespaceIndex, FolderState rootFolder, MqttNamePayload payload)
        {
            // ⚠️ La gestion réelle du serveur OPC UA pour le rechargement nécessite un verrou global et une gestion fine des nœuds existants.
            
            // 1. Trouver ou créer le nœud racine du pont (ex: MqttDataBridge)
            // CORRECTION : 'namespaceIndex' est maintenant passé
            FolderState baseRoot = FindOrCreateFolder(context, namespaceIndex, rootFolder, _settings.RootNodeName);

            // 2. Traitement de toutes les définitions
            var definitions = payload.Connections
                .SelectMany(c => c.DataPoints)
                .SelectMany(dp => dp.DataPointDefinitions);
            
            foreach (var def in definitions)
            {
                // Name: "DB_OVEN_MVT_SEND_WS.Oven_Mvt.Conveying.Elevator_2.Torque"
                string[] parts = def.Name.Split('.');
                if (parts.Length < 2) continue; // Doit au moins contenir Object.Variable

                // Le chemin hiérarchique : de l'Object Root (Oven_Mvt) au parent de la Variable (Elevator_2)
                string[] hierarchyPath = parts.Skip(1).SkipLast(1).ToArray(); 
                string variableName = parts.Last(); // Le nom de la Variable (Torque)
                
                // Trouve ou crée toute la hiérarchie
                // CORRECTION : 'namespaceIndex' est maintenant passé
                FolderState parentFolder = GetOrCreateHierarchy(context, namespaceIndex, baseRoot, hierarchyPath);

                // 3. Créer le nœud Variable
                var variable = new BaseDataVariableState<object>(parentFolder)
                {
                    SymbolicName = variableName,
                    // CORRECTION (pour CS1061 & CS1503) : Utilisation de 'namespaceIndex' (ushort)
                    NodeId = new NodeId($"mqtt-id-{def.Id}", namespaceIndex), 
                    BrowseName = new QualifiedName(variableName),
                    DisplayName = new LocalizedText(variableName.Replace('_', ' ')),
                    // CORRECTION : 'OpcUaTypeMapper' remplacé par un helper local
                    DataType = GetDataTypeNodeId(def.DataType),
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead
                };

                // Initialisation de la valeur (importante pour le démarrage du serveur)
                // CORRECTION (pour CS0117) : 'TypeInfo.Get' remplacé par 'TypeInfo.GetDefaultValue'
                variable.Value = Opc.Ua.TypeInfo.GetDefaultValue(variable.DataType, context.TypeTable);
                variable.StatusCode = StatusCodes.BadWaitingForInitialData;
                variable.Timestamp = DateTime.UtcNow;

                parentFolder.AddChild(variable);
                _mqttIdToNodeMap[def.Id] = variable;
            }

            // Notifie l'espace d'adressage des changements
            baseRoot.ClearChangeMasks(context, true);
        }
        
        /// <summary>
        /// Traverse ou crée la hiérarchie de dossiers/objets OPC UA, en commençant par le dossier racine du pont.
        /// </summary>
        // CORRECTION : Ajout du paramètre 'namespaceIndex'
        private FolderState GetOrCreateHierarchy(ISystemContext context, ushort namespaceIndex, FolderState root, string[] path)
        {
            FolderState current = root;
            foreach (var name in path)
            {
                // CORRECTION : 'namespaceIndex' est maintenant passé
                current = FindOrCreateFolder(context, namespaceIndex, current, name);
            }
            return current;
        }

        /// <summary>
        /// Cherche un FolderState existant ou en crée un nouveau sous le parent donné.
        /// </summary>
        // CORRECTION : Ajout du paramètre 'namespaceIndex'
        private FolderState FindOrCreateFolder(ISystemContext context, ushort namespaceIndex, FolderState parent, string name)
        {
            var browseName = new QualifiedName(name);
            
            // CORRECTION (pour CS1061) : 'FindChildByBrowseName' remplacé par 'FindChild'
            var existing = parent.FindChild(context, browseName, false, null) as FolderState;
            
            if (existing != null) return existing;

            var newFolder = new FolderState(parent)
            {
                SymbolicName = name,
                // Utilise le SymbolicName parent + nom pour créer un NodeId unique
                // CORRECTION (pour CS1061 & CS1503) : Utilisation de 'namespaceIndex' (ushort)
                NodeId = new NodeId($"{parent.SymbolicName}/{name}", namespaceIndex), 
                BrowseName = browseName,
                DisplayName = new LocalizedText(name.Replace('_', ' ')),
                ReferenceTypeId = ReferenceTypeIds.Organizes, // Utilisation de 'Organizes' pour la hiérarchie
                TypeDefinitionId = ObjectTypeIds.FolderType
            };
            
            parent.AddChild(newFolder);
            return newFolder;
        }

        // CORRECTION : Ajout d'un helper pour mapper les types de données (similaire à MqttToUaBridgeService)
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
                    return DataTypeIds.Int16; // 'Int' est généralement Int16 en S7, ajustez si c'est Int32
                case "string":
                    return DataTypeIds.String;
                case "bool":
                case "boolean":
                    return DataTypeIds.Boolean;
                default:
                    return DataTypeIds.Variant;
            }
        }
    }
}