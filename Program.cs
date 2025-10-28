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
            
            // CORRECTION 3 (pour CS1061) : 'ServerInstance.NodeManager' remplacé par 'ServerInstance.CurrentInstance.NodeManager'
            var masterManager = manager.ServerInstance.CurrentInstance.NodeManager as MasterNodeManager;
            
            return masterManager?.NodeManagers.OfType<MqttUaNodeManager>().FirstOrDefault() 
                ?? throw new InvalidOperationException("MqttUaNodeManager not found in OPC UA Server.");
        });
        
        // 3. Services MQTTnet et Logique de Pontage
        
        // CORRECTION 4 (pour CS0246) : 'MqttFactory' pleinement qualifié pour l'API v4
        services.AddSingleton<IMqttClient>(sp => new MQTTnet.MqttFactory().CreateMqttClient());
        
        // Le MqttUaModelBuilder réel est maintenant créé dans MqttUaNodeManager.
        // On le fournit ici en accédant à l'instance du NodeManager.
        services.AddSingleton<MqttUaModelBuilder>(sp => 
            sp.GetRequiredService<MqttUaNodeManager>().ModelBuilder);
        
        // 4. L'ISystemContext réel
        // (Compile maintenant grâce à 'using Opc.Ua;')
        services.AddSingleton<ISystemContext>(sp => 
            sp.GetRequiredService<MqttUaNodeManager>().SystemContext);

        // 5. Les services hôtes
        services.AddHostedService<MqttUaServerManager>();
        services.AddHostedService<MqttToUaBridgeService>();
    })
    .RunConsoleAsync();