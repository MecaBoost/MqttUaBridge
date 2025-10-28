using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Server; // <-- CORRECTION : Ajouté pour FolderState
using MqttUaBridge.Ua;
using MqttUaBridge.Models;
using MqttUaBridge.Configuration;
using Microsoft.Extensions.Options;
using MqttUaBridge.Tests;
using System.Collections.Generic; // <-- CORRECTION : Ajouté pour List<>

namespace MqttUaBridge.Tests
{
    [TestFixture]
    public class MqttUaModelBuilderTests
    {
        // CORRECTION (pour CS8618) : Initialisés à 'null!' pour satisfaire le compilateur.
        // NUnit garantit que SetUp() est appelé avant tout test.
        private MockSystemContext _context = null!;
        private MqttUaModelBuilder _builder = null!;
        private FolderState _rootFolder = null!;

        [SetUp]
        public void SetUp()
        {
            var settings = Options.Create(new MqttSettings { 
                RootNodeName = "MqttDataBridge" 
            });
            _context = new MockSystemContext(settings.Value);
            _builder = new MqttUaModelBuilder(settings);
            
            // L'ObjectsFolder est la racine OPC UA standard
            // CORRECTION (pour CS1061) : 'NodeStates.FindNode' remplacé par 'FindNode' sur le contexte.
            _rootFolder = _context.FindNode(ObjectIds.ObjectsFolder) as FolderState ?? new FolderState(null);
        }

        [Test]
        public void Build_ShouldCreateDeepHierarchyAndMapIds_FromMqttName()
        {
            // CORRECTION (pour CS7014) : 'cite_start' (artefact de copier-coller) supprimé.
            // Arrange: Simulation du chemin le plus profond de votre exemple
            var mockPayload = new MqttNamePayload
            {
                Connections = new List<MqttConnection>
                {
                    new MqttConnection
                    {
                        DataPoints = new List<MqttDataPoint>
                        {
                            new MqttDataPoint
                            {
                                DataPointDefinitions = new List<MqttDataPointDefinition>
                                {
                                    new MqttDataPointDefinition { 
                                        Id = "649", 
                                        Name = "DB_OVEN_MVT_SEND_WS.Oven_Mvt.Process.Climate_Chamber_02.Oura_Exit.Mass_Flow_Rate", 
                                        DataType = "Real" 
                                    },
                                    new MqttDataPointDefinition { 
                                        Id = "470", 
                                        Name = "DB_OVEN_MVT_SEND_WS.Oven_Mvt.Cons_Electricity", 
                                        DataType = "Real" 
                                    } 
                                }
                            }
                        }
                    }
                }
            };
            
            // Act
            // CORRECTION PROACTIVE : Ajout de '_context.NamespaceIndex' comme requis 
            // par le changement de signature de la méthode 'Build' (vu dans MqttToUaBridgeService)
            _builder.Build(_context, _context.NamespaceIndex, _rootFolder, mockPayload); 

            // Assert 1: Vérifier le nœud racine de notre pont
            // CORRECTION (pour CS1061) : 'FindChildByBrowseName' (obsolète) remplacé par 'FindChild'
            var bridgeRoot = _rootFolder.FindChild(_context, new QualifiedName("MqttDataBridge"), false, null) as FolderState;
            Assert.That(bridgeRoot, Is.Not.Null, "Le nœud racine 'MqttDataBridge' n'a pas été créé.");

            // Assert 2: Valider le chemin profond
            var ovenMvt = bridgeRoot.FindChild(_context, new QualifiedName("Oven_Mvt"), false, null) as FolderState;
            Assert.That(ovenMvt, Is.Not.Null);

            var process = ovenMvt.FindChild(_context, new QualifiedName("Process"), false, null) as FolderState;
            Assert.That(process, Is.Not.Null);
            
            var chamber2 = process.FindChild(_context, new QualifiedName("Climate_Chamber_02"), false, null) as FolderState;
            Assert.That(chamber2, Is.Not.Null);

            var ouraExit = chamber2.FindChild(_context, new QualifiedName("Oura_Exit"), false, null) as FolderState;
            Assert.That(ouraExit, Is.Not.Null, "Le dossier Oura_Exit n'a pas été trouvé.");

            var flowRateVariable = ouraExit.FindChild(_context, new QualifiedName("Mass_Flow_Rate"), false, null) as BaseDataVariableState;
            Assert.That(flowRateVariable, Is.Not.Null, "La variable finale n'a pas été trouvée.");

            // Assert 3: Vérifier le mappage ID -> Node
            Assert.That(_builder.MqttIdToNodeMap.ContainsKey("649"), Is.True, "L'ID '649' n'est pas mappé.");
            
            // CORRECTION : Comparaison avec le NodeId de type OPC UA correct, pas un Mapper custom.
            Assert.That(flowRateVariable?.DataType, Is.EqualTo(DataTypeIds.Double), "Le type de donnée est incorrect.");
            
            // Assert 4: Vérifier la variable simple (Cons_Electricity)
            var consElectricity = ovenMvt.FindChild(_context, new QualifiedName("Cons_Electricity"), false, null) as BaseDataVariableState;
            Assert.That(consElectricity, Is.Not.Null);
            Assert.That(_builder.MqttIdToNodeMap.ContainsKey("470"), Is.True);
        }
    }
}