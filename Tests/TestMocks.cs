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
                // CORRECTION (pour CS1061) : BrowseNames.ObjectsFolder est un string, il n'a pas de propriété .Name
                DisplayName = new LocalizedText(BrowseNames.ObjectsFolder)
            };
            NodeStates.Add(objectsFolder);
        }

        // --- Implémentation des membres requis par VOTRE ISystemContext ---
        
        // CORRECTION (pour CS8603) : Le type de retour doit être nullable
        public object? SystemHandle => null;
        public NodeId SessionId { get; set; }
        // CORRECTION (pour CS8625) : Le type de retour doit être nullable
        public IUserIdentity? UserIdentity { get; set; } = null;
        public IList<string> PreferredLocales { get; set; }
        // CORRECTION (pour CS8625) : Le type de retour doit être nullable
        public string? AuditEntryId { get; set; } = null;
        public NamespaceTable NamespaceUris { get; }
        public StringTable ServerUris { get; }
        public ITypeTable TypeTable { get; } 
        public IEncodeableFactory EncodeableFactory { get; }
        // CORRECTION (pour CS8625) : Le type de retour doit être nullable
        public INodeIdFactory? NodeIdFactory { get; set; } = null;
        public NodeStateFactory NodeStateFactory { get; set; } 
    }
}