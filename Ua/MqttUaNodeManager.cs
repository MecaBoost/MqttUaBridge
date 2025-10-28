using Opc.Ua;
using Opc.Ua.Server;
using MqttUaBridge.Configuration;

namespace MqttUaBridge.Ua
{
    /// <summary>
    /// NodeManager responsable du Namespace personnalisé et de l'intégration du MqttUaModelBuilder.
    /// </summary>
    public class MqttUaNodeManager : CustomNodeManager2
    {
        private readonly MqttSettings _settings;
        private readonly MqttUaModelBuilder _modelBuilder;
        private readonly object _lock = new();

        public MqttUaModelBuilder ModelBuilder => _modelBuilder; // Permettre l'accès depuis le Bridge Service

        public MqttUaNodeManager(IServerInternal server, ApplicationConfiguration configuration, MqttSettings settings)
            : base(server, configuration, settings.OpcUaNamespaceUri)
        {
            _settings = settings;
            // Instancier le ModelBuilder avec les paramètres de configuration
            // Note: Nous ne pouvons pas utiliser DI ici directement, donc nous injectons le service plus tard.
            _modelBuilder = new MqttUaModelBuilder(Options.Create(settings)); 
        }

        protected override void PopulateNamespace(ISystemContext context)
        {
            lock (_lock)
            {
                // La méthode Build() sera appelée plus tard par le service MQTT une fois la structure reçue.
                // Pour l'initialisation, nous créons juste le nœud racine.
                
                // Créer le nœud racine de notre pont sous l'ObjectsFolder standard
                FolderState bridgeRoot = new FolderState(null)
                {
                    SymbolicName = _settings.RootNodeName,
                    NodeId = new NodeId(_settings.RootNodeName, NamespaceIndex),
                    BrowseName = new QualifiedName(_settings.RootNodeName),
                    DisplayName = new LocalizedText(_settings.RootNodeName),
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    TypeDefinitionId = ObjectTypeIds.FolderType
                };

                // Ajout à l'espace d'adressage
                AddRoot(bridgeRoot);
                
                // On notifie l'OPC UA Server pour que notre nœud racine soit visible.
                bridgeRoot.ClearChangeMasks(context, true);
            }
        }
    }
}