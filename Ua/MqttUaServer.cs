using Opc.Ua;
using Opc.Ua.Server;
using MqttUaBridge.Configuration;

namespace MqttUaBridge.Ua
{
    /// <summary>
    /// Implémentation du serveur OPC UA standard qui héberge notre espace d'adressage dynamique.
    /// </summary>
    public class MqttUaServer : StandardServer
    {
        private readonly MqttSettings _settings;

        public MqttUaServer(MqttSettings settings)
        {
            _settings = settings;
        }

        // Méthode pour initialiser l'espace d'adressage
        protected override MasterNodeManager Create          
        // Crée l'objet MasterNodeManager qui gère tous les NodeManagers.
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            List<INodeManager> nodeManagers = new();

            // Création de notre NodeManager personnalisé pour gérer l'espace d'adressage MQTT
            nodeManagers.Add(new MqttUaNodeManager(server, configuration, _settings));

            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        // Configuration minimale pour la sécurité et les informations de serveur (à compléter en production)
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);
            // Ici, vous pourriez enregistrer le serveur sur un GDS (Global Discovery Server)
        }
    }
}