// Fichier: Program.cs
using MqttUaBridge.Configuration;
using MqttUaBridge.Services;
using MqttUaBridge.Ua;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using Opc.Ua.Server; 

namespace MqttUaBridge
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // 1. Configuration
                    services.Configure<MqttSettings>(hostContext.Configuration.GetSection("MqttSettings"));

                    // 2. Services OPC UA (Réels)
                    services.AddSingleton<MqttUaServerManager>();
                    // Enregistre le MqttUaNodeManager comme un singleton pour que MqttToUaBridgeService puisse y accéder
                    services.AddSingleton<MqttUaNodeManager>(sp => 
                    {
                        var manager = sp.GetRequiredService<MqttUaServerManager>();
                        // Le NodeManager n'est créé qu'après le démarrage du serveur, donc nous devons le récupérer dynamiquement.
                        // Pour ce scénario DI simplifié, on accède à la première instance.
                        var masterManager = manager.ServerInstance.NodeManager as MasterNodeManager;
                        return masterManager?.NodeManagers.OfType<MqttUaNodeManager>().FirstOrDefault() 
                            ?? throw new InvalidOperationException("MqttUaNodeManager not found in OPC UA Server.");
                    });
                    
                    // 3. Services MQTTnet et Logique de Pontage
                    services.AddSingleton<IMqttClient>(sp => new MqttFactory().CreateMqttClient());
                    
                    // Le MqttUaModelBuilder réel est maintenant créé dans MqttUaNodeManager.
                    // On le fournit ici en accédant à l'instance du NodeManager.
                    services.AddSingleton<MqttUaModelBuilder>(sp => 
                        sp.GetRequiredService<MqttUaNodeManager>().ModelBuilder);
                    
                    // 4. L'ISystemContext réel
                    // On utilise le contexte du NodeManager
                    services.AddSingleton<ISystemContext>(sp => 
                        sp.GetRequiredService<MqttUaNodeManager>().SystemContext);


                    // 5. Les services hôtes
                    services.AddHostedService<MqttUaServerManager>();
                    services.AddHostedService<MqttToUaBridgeService>();

                })
                .RunConsoleAsync();
        }
    }
}