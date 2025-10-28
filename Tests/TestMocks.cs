using MqttUaBridge.Configuration;
using Opc.Ua;

namespace MqttUaBridge.Tests
{
    // Un Mock de l'ISystemContext est ESSENTIEL pour pouvoir créer des nœuds dans l'espace d'adressage
    public class MockSystemContext : ISystemContext
    {
        private readonly MqttSettings _settings;
        public ushort NamespaceIndex { get; }

        public NodeStateCollection NodeStates { get; } = new NodeStateCollection(); 
        
        public MockSystemContext(MqttSettings settings)
        {
            _settings = settings;
            // Dans un serveur réel, un Namespace Index est attribué. Nous utilisons '1' pour le test.
            NamespaceIndex = 1; 

            // Simuler l'objet racine standard 'ObjectsFolder' (NodeId i=85)
            var objectsFolder = new FolderState(null)
            {
                NodeId = ObjectIds.ObjectsFolder,
                BrowseName = BrowseNames.ObjectsFolder,
                DisplayName = new LocalizedText(BrowseNames.ObjectsFolder.Name)
            };
            NodeStates.Add(objectsFolder);
        }

        // Implémentation minimaliste des membres requis pour le bon fonctionnement des tests/bridge
        public NodeState FindNode(ExpandedNodeId nodeId) => NodeStates.FindNode(nodeId);
        
        // Les autres membres ISystemContext sont laissés comme des implémentations de base pour la concision
        public NodeState FindNode(NodeId nodeId) => NodeStates.FindNode(nodeId);
        public object DiagnosticsLock => new object();
        public DiagnosticsNodeState DiagnosticsNode => null;
        public uint SystemContextId => 0;
        public object SystemHandle => null;
        // ... (autres membres avec implémentations minimales)
    }
}