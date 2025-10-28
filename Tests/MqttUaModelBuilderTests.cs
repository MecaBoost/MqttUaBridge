using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Server;
using MqttUaBridge.Ua;
using MqttUaBridge.Models;
using MqttUaBridge.Configuration;
using Microsoft.Extensions.Options;
using MqttUaBridge.Tests;
using System.Collections.Generic;

namespace MqttUaBridge.Tests
{
    [TestFixture]
    public class MqttUaModelBuilderTests
    {
        // Corrigé pour les avertissements de nullabilité
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
            // CORRECTION (pour CS1061) : 'FindNode' n'existe pas, la méthode est 'Find'
            _rootFolder = _context.NodeStates.Find(ObjectIds.ObjectsFolder) as FolderState ?? new FolderState(null);
        }

        [Test]
        public void Build_ShouldCreateDeepHierarchyAndMapIds_FromMqttName()
        {
            // Arrange
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
            _builder.Build(_context, _context.NamespaceIndex, _rootFolder, mockPayload); 

            // Assert 1: Vérifier le nœud racine de notre pont
            // CORRECTION (pour CS1501) : Utilisation de la surcharge FindChild(context, browseName)
            var bridgeRoot = _rootFolder.FindChild(_context, new QualifiedName("MqttDataBridge")) as FolderState;
            Assert.That(bridgeRoot, Is.Not.Null, "Le nœud racine 'MqttDataBridge' n'a pas été créé.");

            // Assert 2: Valider le chemin profond
            var ovenMvt = bridgeRoot.FindChild(_context, new QualifiedName("Oven_Mvt")) as FolderState;
            Assert.That(ovenMvt, Is.Not.Null);

            var process = ovenMvt.FindChild(_context, new QualifiedName("Process")) as FolderState;
            Assert.That(process, Is.Not.Null);
            
            var chamber2 = process.FindChild(_context, new QualifiedName("Climate_Chamber_02")) as FolderState;
            Assert.That(chamber2, Is.Not.Null);

            var ouraExit = chamber2.FindChild(_context, new QualifiedName("Oura_Exit")) as FolderState;
            Assert.That(ouraExit, Is.Not.Null, "Le dossier Oura_Exit n'a pas été trouvé.");

            var flowRateVariable = ouraExit.FindChild(_context, new QualifiedName("Mass_Flow_Rate")) as BaseDataVariableState;
            Assert.That(flowRateVariable, Is.Not.Null, "La variable finale n'a pas été trouvée.");

            // Assert 3: Vérifier le mappage ID -> Node
            Assert.That(_builder.MqttIdToNodeMap.ContainsKey("649"), Is.True, "L'ID '649' n'est pas mappé.");
            Assert.That(flowRateVariable?.DataType, Is.EqualTo(DataTypeIds.Double), "Le type de donnée est incorrect.");
            
            // Assert 4: Vérifier la variable simple (Cons_Electricity)
            var consElectricity = ovenMvt.FindChild(_context, new QualifiedName("Cons_Electricity")) as BaseDataVariableState;
            Assert.That(consElectricity, Is.Not.Null);
            Assert.That(_builder.MqttIdToNodeMap.ContainsKey("470"), Is.True);
        }
    }
}