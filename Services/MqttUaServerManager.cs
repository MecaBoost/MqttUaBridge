using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Configuration;
using MqttUaBridge.Configuration;
using Microsoft.Extensions.Logging;
using MqttUaBridge.Ua;
using System.Security.Cryptography.X509Certificates;

namespace MqttUaBridge.Services
{
    public class MqttUaServerManager : IHostedService
    {
        private readonly MqttSettings _settings;
        private readonly ILogger<MqttUaServerManager> _logger;
        private readonly MqttUaServer _server;
        private readonly ApplicationInstance _application;
        
        public MqttUaServer ServerInstance => _server;

        public MqttUaServerManager(IOptions<MqttSettings> settings, ILogger<MqttUaServerManager> logger)
        {
            _settings = settings.Value;
            _logger = logger;
            _server = new MqttUaServer(_settings);
            _application = new ApplicationInstance
            {
                ApplicationType = ApplicationType.Server // Important de définir le type
            };
        }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting OPC UA Server configuration from XML file...");

        // 1. Charger la configuration à partir du fichier XML standard
        string configurationFile = "Opc.Ua.Config.xml";
        
        // CORRECTION (pour CS1061) : 'Load' est maintenant 'LoadApplicationConfiguration'
        // et il assigne la config à _application.ApplicationConfiguration
        ApplicationConfiguration config = await _application.LoadApplicationConfiguration(configurationFile, false);
        
        // 2. Écraser l'ApplicationUri par la valeur de appsettings.json
        // C'est une bonne pratique pour garantir l'unicité des URIs lors du déploiement.
        config.ApplicationUri = _settings.OpcUaNamespaceUri; 
        
        // 3. Valider et initialiser l'instance d'application (certificats, etc.)
        
        // CORRECTION (pour CS1061) : 'Validate' est maintenant sur l'objet 'config',
        // et il a besoin de savoir le type d'application.
        await config.Validate(ApplicationType.Server);
        
        // Crée le certificat d'application s'il n'existe pas
        // CORRECTION (pour CS1061) : 'CheckApplicationInstanceCertificate' attend un 'ushort' (pas 'int').
        await _application.CheckApplicationInstanceCertificate(false, (ushort)0); 

        // 4. Démarrer le serveur
        // CORRECTION (pour CS0618) : 'Start' est obsolète, utiliser 'StartAsync'
        await _application.StartAsync(_server);

        _logger.LogInformation($"OPC UA Server started. Endpoints: {string.Join(", ", config.ServerConfiguration.BaseAddresses)}");
    }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _server.Stop();
            _logger.LogInformation("OPC UA Server stopped.");
            return Task.CompletedTask;
        }
    }
}