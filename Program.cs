// Fichier: Program.cs
using MqttUaBridge.Configuration;
using MqttUaBridge.Services;
using MqttUaBridge.Ua;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using Opc.Ua; 
using Opc.Ua.Server; 

// CORRECTION (pour CS7022) : Rétablissement de la structure de classe 'Program'
// pour résoudre le conflit de point d'entrée avec le SDK de test (NUnit).
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
                    services.AddSingleton<MqttUaNodeManager>(sp => 
                    {
                        var manager = sp.GetRequiredService<MqttUaServerManager>();
                        var masterManager = manager.ServerInstance.CurrentInstance.NodeManager as MasterNodeManager;
                        
                        return masterManager?.NodeManagers.OfType<MqttUaNodeManager>().FirstOrDefault() 
                            ?? throw new InvalidOperationException("MqttUaNodeManager not found in OPC UA Server.");
                    });
                    
                    // 3. Services MQTTnet et Logique de Pontage
                    
                    // CORRECTION (pour CS0234) : 'MqttFactory' pleinement qualifié
                    services.AddSingleton<IMqttClient>(sp => new MqttClientFactory().CreateMqttClient());
                    
                    services.AddSingleton<MqttUaModelBuilder>(sp => 
                        sp.GetRequiredService<MqttUaNodeManager>().ModelBuilder);
                    
                    // 4. L'ISystemContext réel
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