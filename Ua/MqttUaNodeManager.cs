using Opc.Ua;
using Opc.Ua.Server;
using MqttUaBridge.Configuration;
using System.Collections.Generic; // Requis pour IDictionary
using Microsoft.Extensions.Options; // Requis pour Options.Create

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
            _modelBuilder = new MqttUaModelBuilder(Options.Create(settings));
        }

        /// <summary>
        /// Surcharge correcte pour initialiser l'espace d'adressage au démarrage du serveur.
        /// </summary>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            // Appeler la méthode de base en premier
            base.CreateAddressSpace(externalReferences);
            
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

                // CORRECTION (pour CS0103) : Utiliser 'AddRootNotifier' pour ajouter
                // le nœud au dossier 'ObjectsFolder' et à l'espace d'adressage.
                AddRootNotifier(bridgeRoot);
                
                // On notifie l'OPC UA Server pour que notre nœud racine soit visible.
                // Nous utilisons 'SystemContext' (propriété de la classe de base) 
                // car 'context' n'est pas passé en paramètre ici.
                bridgeRoot.ClearChangeMasks(SystemContext, true);
            }
        }
    }
}