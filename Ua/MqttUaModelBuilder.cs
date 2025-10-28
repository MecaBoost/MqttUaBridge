using Opc.Ua;
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
        public void Build(ISystemContext context, FolderState rootFolder, MqttNamePayload payload)
        {
            // ⚠️ La gestion réelle du serveur OPC UA pour le rechargement nécessite un verrou global et une gestion fine des nœuds existants.
            
            // 1. Trouver ou créer le nœud racine du pont (ex: MqttDataBridge)
            FolderState baseRoot = FindOrCreateFolder(context, rootFolder, _settings.RootNodeName);

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
                FolderState parentFolder = GetOrCreateHierarchy(context, baseRoot, hierarchyPath);

                // 3. Créer le nœud Variable
                var variable = new BaseDataVariableState<object>(parentFolder)
                {
                    SymbolicName = variableName,
                    // Utiliser l'ID MQTT préfixé pour garantir un NodeId unique dans le Namespace Index
                    NodeId = new NodeId($"mqtt-id-{def.Id}", context.NamespaceIndex), 
                    BrowseName = new QualifiedName(variableName),
                    DisplayName = new LocalizedText(variableName.Replace('_', ' ')),
                    DataType = OpcUaTypeMapper.MapToNodeId(def.DataType),
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead
                };

                // Initialisation de la valeur (importante pour le démarrage du serveur)
                variable.Value = TypeInfo.Get(variable.DataType).Get.GetValue();
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
        private FolderState GetOrCreateHierarchy(ISystemContext context, FolderState root, string[] path)
        {
            FolderState current = root;
            foreach (var name in path)
            {
                current = FindOrCreateFolder(context, current, name);
            }
            return current;
        }

        /// <summary>
        /// Cherche un FolderState existant ou en crée un nouveau sous le parent donné.
        /// </summary>
        private FolderState FindOrCreateFolder(ISystemContext context, FolderState parent, string name)
        {
            var browseName = new QualifiedName(name);
            
            var existing = parent.FindChildByBrowseName(context, browseName) as FolderState;
            
            if (existing != null) return existing;

            var newFolder = new FolderState(parent)
            {
                SymbolicName = name,
                // Utilise le chemin parent + nom pour créer un NodeId unique
                NodeId = new NodeId($"{parent.NodeId.Identifier.ToString()}/{name}", context.NamespaceIndex), 
                BrowseName = browseName,
                DisplayName = new LocalizedText(name.Replace('_', ' ')),
                ReferenceTypeId = ReferenceTypeIds.Organizes, // Utilisation de 'Organizes' pour la hiérarchie
                TypeDefinitionId = ObjectTypeIds.FolderType
            };
            
            parent.AddChild(newFolder);
            return newFolder;
        }
    }
}