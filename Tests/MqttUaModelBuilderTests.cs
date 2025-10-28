using NUnit.Framework;
using Opc.Ua;
using MqttUaBridge.Ua;
using MqttUaBridge.Models;
using MqttUaBridge.Configuration;
using Microsoft.Extensions.Options;
using MqttUaBridge.Tests;

namespace MqttUaBridge.Tests
{
    [TestFixture]
    public class MqttUaModelBuilderTests
    {
        private MockSystemContext _context;
        private MqttUaModelBuilder _builder;
        private FolderState _rootFolder;

        [SetUp]
        public void SetUp()
        {
            var settings = Options.Create(new MqttSettings { 
                RootNodeName = "MqttDataBridge" 
            });
            _context = new MockSystemContext(settings.Value);
            _builder = new MqttUaModelBuilder(settings);
            // L'ObjectsFolder est la racine OPC UA standard
            _rootFolder = _context.NodeStates.FindNode(ObjectIds.ObjectsFolder.Identifier) as FolderState;
        }

        [Test]
        public void Build_ShouldCreateDeepHierarchyAndMapIds_FromMqttName()
        {
            [cite_start]// Arrange: Simulation du chemin le plus profond de votre exemple [cite: 360]
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
            _builder.Build(_context, _rootFolder, mockPayload); 

            // Assert 1: Vérifier le nœud racine de notre pont
            var bridgeRoot = _rootFolder.FindChildByBrowseName(_context, new QualifiedName("MqttDataBridge")) as FolderState;
            Assert.That(bridgeRoot, Is.Not.Null, "Le nœud racine 'MqttDataBridge' n'a pas été créé.");

            // Assert 2: Valider le chemin profond
            var ovenMvt = bridgeRoot.FindChildByBrowseName(_context, new QualifiedName("Oven_Mvt")) as FolderState;
            Assert.That(ovenMvt, Is.Not.Null);

            var process = ovenMvt.FindChildByBrowseName(_context, new QualifiedName("Process")) as FolderState;
            Assert.That(process, Is.Not.Null);
            
            var chamber2 = process.FindChildByBrowseName(_context, new QualifiedName("Climate_Chamber_02")) as FolderState;
            Assert.That(chamber2, Is.Not.Null);

            var flowRateVariable = chamber2
                .FindChildByBrowseName(_context, new QualifiedName("Oura_Exit"))?.FindChildByBrowseName(_context, new QualifiedName("Mass_Flow_Rate")) as BaseDataVariableState;
                
            Assert.That(flowRateVariable, Is.Not.Null, "La variable finale n'a pas été trouvée.");

            // Assert 3: Vérifier le mappage ID -> Node
            Assert.That(_builder.MqttIdToNodeMap.ContainsKey("649"), Is.True, "L'ID '649' n'est pas mappé.");
            Assert.That(flowRateVariable?.DataType, Is.EqualTo(OpcUaTypeMapper.MapToNodeId("Real")), "Le type de donnée est incorrect.");
            
            // Assert 4: Vérifier la variable simple (Cons_Electricity)
            var consElectricity = ovenMvt.FindChildByBrowseName(_context, new QualifiedName("Cons_Electricity")) as BaseDataVariableState;
            Assert.That(consElectricity, Is.Not.Null);
            Assert.That(_builder.MqttIdToNodeMap.ContainsKey("470"), Is.True);
        }
    }
}