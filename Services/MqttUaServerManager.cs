using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Configuration;
using MqttUaBridge.Configuration;
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
            _application = new ApplicationInstance();
        }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting OPC UA Server configuration from XML file...");

        // 1. Charger la configuration à partir du fichier XML standard
        string configurationFile = "Opc.Ua.Config.xml";
        ApplicationConfiguration config = await _application.Load(configurationFile, false, null);
        
        // 2. Écraser l'ApplicationUri par la valeur de appsettings.jsons
        // C'est une bonne pratique pour garantir l'unicité des URIs lors du déploiement.
        config.ApplicationUri = _settings.OpcUaNamespaceUri; 
        
        // 3. Valider et initialiser l'instance d'application (certificats, etc.)
        await _application.Validate(config);
        // Crée le certificat d'application s'il n'existe pas
        await _application.CheckApplicationInstanceCertificate(false, 0); 

        // 4. Démarrer le serveur
        await _application.Start(_server);

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