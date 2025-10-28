using MqttUaBridge.Configuration;
using Opc.Ua;
using Opc.Ua.Server;
using System.Collections.Generic; 

namespace MqttUaBridge.Tests
{
    public class MockSystemContext : ISystemContext
    {
        private readonly MqttSettings _settings;
        public ushort NamespaceIndex { get; }
        public NodeStateCollection NodeStates { get; } = new NodeStateCollection();

        public MockSystemContext(MqttSettings settings)
        {
            _settings = settings;
            NamespaceIndex = 1; 

            NamespaceUris = new NamespaceTable();
            ServerUris = new StringTable();
            TypeTable = new TypeTable(NamespaceUris); 
            PreferredLocales = new List<string> { "en-US" };
            EncodeableFactory = Opc.Ua.EncodeableFactory.GlobalFactory;
            SessionId = new NodeId("mock_session");
            NodeStateFactory = new NodeStateFactory(); 

            var objectsFolder = new FolderState(null)
            {
                NodeId = ObjectIds.ObjectsFolder,
                BrowseName = BrowseNames.ObjectsFolder,
                DisplayName = new LocalizedText(BrowseNames.ObjectsFolder.Name)
            };
            NodeStates.Add(objectsFolder);
        }

        // --- Implémentation des membres requis par VOTRE ISystemContext ---
        
        public object SystemHandle => null;
        public NodeId SessionId { get; set; }
        public IUserIdentity UserIdentity { get; set; } = null;
        public IList<string> PreferredLocales { get; set; }
        public string AuditEntryId { get; set; } = null;
        public NamespaceTable NamespaceUris { get; }
        public StringTable ServerUris { get; }
        public ITypeTable TypeTable { get; } 
        public IEncodeableFactory EncodeableFactory { get; }
        public INodeIdFactory NodeIdFactory { get; set; } = null;
        public NodeStateFactory NodeStateFactory { get; set; } 
    }
}