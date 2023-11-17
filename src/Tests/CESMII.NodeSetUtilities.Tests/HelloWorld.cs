using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CESMII.OpcUa.NodeSetImporter;
using CESMII.OpcUa.NodeSetModel;
using CESMII.OpcUa.NodeSetModel.Factory.Opc;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Export;
using Xunit;
using Xunit.Abstractions;

namespace CESMII.NodeSetUtilities.Tests
{
    public class HelloWorldTest
    {
        private readonly ITestOutputHelper output;

        public HelloWorldTest(ITestOutputHelper output)
        {
            this.output = output;
        }

        public const string strTestNodeSetDirectory = "TestNodeSets";

        [Fact]
        public async Task HelloWorld()
        {
            // Set up the importer
            var importer = new UANodeSetModelImporter(NullLogger.Instance);

            // Read and import the base nodeset
            var file = Path.Combine(strTestNodeSetDirectory, "opcfoundation.org.UA.NodeSet2.xml");
            var nodesetXml = File.ReadAllText(file);
            var baseNodeSets = (await importer.ImportNodeSetModelAsync(nodesetXml)).ToDictionary(n => n.ModelUri);

            // All required models are loaded: now we can use them to build a new model

            var uaBaseModel = baseNodeSets[Namespaces.OpcUa];

            var myNodeSetModel = new NodeSetModel
            {
                ModelUri = "https://opcua.rocks/UA",
            };

            uint nextNodeId = 1000;

            var animalType = new ObjectTypeModel
            {
                DisplayName = new List<NodeModel.LocalizedText> { "AnimalType" },
                SuperType = uaBaseModel.ObjectTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("BaseObjectType")),
                NodeSet = myNodeSetModel,
                NodeId = new ExpandedNodeId(nextNodeId++, myNodeSetModel.ModelUri).ToString(),
                Properties = new List<VariableModel>
                {
                    new PropertyModel
                    {
                        NodeSet = myNodeSetModel,
                        NodeId = new ExpandedNodeId(nextNodeId++, myNodeSetModel.ModelUri).ToString(),
                        DisplayName = new List<NodeModel.LocalizedText> { "Name" },
                        DataType = uaBaseModel.DataTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("String")),
                    },
                },
                DataVariables = new List<DataVariableModel>
                {
                    new DataVariableModel
                    {
                        NodeSet = myNodeSetModel,
                        NodeId = new ExpandedNodeId(nextNodeId++, myNodeSetModel.ModelUri).ToString(),
                        DisplayName = new List<NodeModel.LocalizedText> { "Height" },
                        DataType = uaBaseModel.DataTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("Float")),
                        EngineeringUnit = new VariableModel.EngineeringUnitInfo { DisplayName = "metre"  },
                        Value = NodeModelUtils.JsonEncodeVariant((float) 0),
                    },
                },
            };

            myNodeSetModel.ObjectTypes.Add(animalType);

            myNodeSetModel.UpdateIndices();
            var exportedNodeSetXml = UANodeSetModelExporter.ExportNodeSetAsXml(myNodeSetModel, baseNodeSets);
        }

        [Fact]
        public async Task HelloManyWorld()
        {
            // Set up the importer
            var importer = new UANodeSetModelImporter(NullLogger.Instance);
            Dictionary<string, NodeSetModel> nodeSetModels = new();
            var opcContext = new DefaultOpcUaContext(nodeSetModels, NullLogger.Instance);

            // Read and import the base nodeset
            var file = Path.Combine(strTestNodeSetDirectory, "opcfoundation.org.UA.NodeSet2.xml");
            var nodeSet = UANodeSet.Read(new FileStream(file, FileMode.Open));
            await importer.LoadNodeSetModelAsync(opcContext, nodeSet);

            // Read and import DI nodeset
            file = Path.Combine(strTestNodeSetDirectory, "opcfoundation.org.UA.DI.NodeSet2.xml");
            nodeSet = UANodeSet.Read(new FileStream(file, FileMode.Open));
            await importer.LoadNodeSetModelAsync(opcContext, nodeSet);

            // Read and import Robotics nodeset
            file = Path.Combine(strTestNodeSetDirectory, "opcfoundation.org.UA.Robotics.NodeSet2.xml");
            nodeSet = UANodeSet.Read(new FileStream(file, FileMode.Open));
            await importer.LoadNodeSetModelAsync(opcContext, nodeSet);

            // Read and import HumanRobot nodeset
            file = Path.Combine(strTestNodeSetDirectory, "clabs.com.UA.HumanRobot.NodeSet2.xml");
            nodeSet = UANodeSet.Read(new FileStream(file, FileMode.Open));
            await importer.LoadNodeSetModelAsync(opcContext, nodeSet);

            // All required models are loaded: now we can use them to build a new model
            var uaBaseModel = nodeSetModels[Namespaces.OpcUa];
            var uaHumanRobotModel = nodeSetModels["http://clabs.com/UA/HumanRobot"];

            var myNodeSetModel = new NodeSetModel
            {
                ModelUri = "https://opcua.rocks/UA",
            };

            uint nextNodeId = 1000;

            var animalType = new ObjectTypeModel
            {
                DisplayName = new List<NodeModel.LocalizedText> { "AnimalType" },
                SuperType = uaHumanRobotModel.ObjectTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("RobotEye")),
                NodeSet = myNodeSetModel,
                NodeId = new ExpandedNodeId(nextNodeId++, myNodeSetModel.ModelUri).ToString(),
                Properties = new List<VariableModel>
                {
                    new PropertyModel
                    {
                        NodeSet = myNodeSetModel,
                        NodeId = new ExpandedNodeId(nextNodeId++, myNodeSetModel.ModelUri).ToString(),
                        DisplayName = new List<NodeModel.LocalizedText> { "Name" },
                        DataType = uaBaseModel.DataTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("String")),
                    },
                },
                DataVariables = new List<DataVariableModel>
                {
                    new DataVariableModel
                    {
                        NodeSet = myNodeSetModel,
                        NodeId = new ExpandedNodeId(nextNodeId++, myNodeSetModel.ModelUri).ToString(),
                        DisplayName = new List<NodeModel.LocalizedText> { "Height" },
                        DataType = uaBaseModel.DataTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("Float")),
                        EngineeringUnit = new VariableModel.EngineeringUnitInfo { DisplayName = "metre"  },
                        Value = NodeModelUtils.JsonEncodeVariant((float) 0),
                    },
                },
            };

            myNodeSetModel.ObjectTypes.Add(animalType);

            // the new nodeset model is ready: export it to XML
            myNodeSetModel.UpdateIndices();
            var exportedNodeSetXml = UANodeSetModelExporter.ExportNodeSetAsXml(myNodeSetModel, nodeSetModels);
        }


        [Fact]
        public async Task HelloManyWorldResolver()
        {
            // The resolver's ResolveNodeSetsAsync is called to load any required nodeset XML
            // The resolver in this sample tries to find missing nodesets in the TestNodeSets directory
            // The Profile Designer project contains a resolver that reads from an OPC UA Cloud Library (https://github.com/cesmii/ProfileDesigner/tree/main/api/CESMII.OpcUa.CloudLibraryResolver)
            var resolver = new MyDependencyResolver();

            // The importer handles resolving and loading of nodeset XMLs
            var importer = new UANodeSetModelImporter(NullLogger.Instance, resolver);

            // Read the nodest file
            var file = Path.Combine(strTestNodeSetDirectory, "clabs.com.UA.HumanRobot.NodeSet2.xml");
            var nodesetXml = File.ReadAllText(file);

            // Load the Human Robot nodeset as a model, resolving any required dependencies using MyDependencyResolver
            var nodeSetModels = (await importer.ImportNodeSetModelAsync(nodesetXml, loadAllDependentModels: true)).ToDictionary(n => n.ModelUri);

            // All required models are loaded: now we can use them to build a new model
            var uaBaseModel = nodeSetModels[Namespaces.OpcUa];
            var uaHumanRobotModel = nodeSetModels["http://clabs.com/UA/HumanRobot"];

            var myNodeSetModel = new NodeSetModel
            {
                ModelUri = "https://opcua.rocks/UA",
            };

            uint nextNodeId = 1000;

            var animalType = new ObjectTypeModel
            {
                DisplayName = new List<NodeModel.LocalizedText> { "AnimalType" },
                SuperType = uaHumanRobotModel.ObjectTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("RobotEye")),
                NodeSet = myNodeSetModel,
                NodeId = new ExpandedNodeId(nextNodeId++, myNodeSetModel.ModelUri).ToString(),
                Properties = new List<VariableModel>
                {
                    new PropertyModel
                    {
                        NodeSet = myNodeSetModel,
                        NodeId = new ExpandedNodeId(nextNodeId++, myNodeSetModel.ModelUri).ToString(),
                        DisplayName = new List<NodeModel.LocalizedText> { "Name" },
                        DataType = uaBaseModel.DataTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("String")),
                    },
                },
                DataVariables = new List<DataVariableModel>
                {
                    new DataVariableModel
                    {
                        NodeSet = myNodeSetModel,
                        NodeId = new ExpandedNodeId(nextNodeId++, myNodeSetModel.ModelUri).ToString(),
                        DisplayName = new List<NodeModel.LocalizedText> { "Height" },
                        DataType = uaBaseModel.DataTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("Float")),
                        EngineeringUnit = new VariableModel.EngineeringUnitInfo { DisplayName = "metre"  },
                        Value = NodeModelUtils.JsonEncodeVariant((float) 0),
                    },
                },
            };

            myNodeSetModel.ObjectTypes.Add(animalType);

            // the new nodeset model is ready: export it to XML
            myNodeSetModel.UpdateIndices();
            var exportedNodeSetXml = UANodeSetModelExporter.ExportNodeSetAsXml(myNodeSetModel, nodeSetModels);
        }

        [Fact]
        public async Task HelloEmptyWorld()
        {
            // Set up the importer
            var importer = new UANodeSetModelImporter(NullLogger.Instance);

            // Read and import the base nodeset
            var file = Path.Combine(strTestNodeSetDirectory, "opcfoundation.org.UA.NodeSet2.xml");
            var nodesetXml = File.ReadAllText(file);
            var baseNodeSets = (await importer.ImportNodeSetModelAsync(nodesetXml)).ToDictionary(n => n.ModelUri);

            // All required models are loaded: now we can use them to build a new model

            var uaBaseModel = baseNodeSets[Namespaces.OpcUa];

            var myNodeSetModel = new NodeSetModel
            {
                ModelUri = "https://opcua.rocks/UA",
            };

            myNodeSetModel.UpdateIndices();
            var exportedNodeSetXml = UANodeSetModelExporter.ExportNodeSetAsXml(myNodeSetModel, baseNodeSets);
        }
    }

    internal class MyDependencyResolver : IUANodeSetResolver
    {
        public Task<IEnumerable<string>> ResolveNodeSetsAsync(List<ModelNameAndVersion> missingModels)
        {
            List<string> nodeSetsXml = new();
            foreach (var missingModel in missingModels)
            {
                var fileName = missingModel.ModelUri.Replace("http://", "").Replace("/", ".");
                if (!fileName.EndsWith("."))
                {
                    fileName += ".";
                }
                fileName += "NodeSet2.xml";

                var filePath = Path.Combine(HelloWorldTest.strTestNodeSetDirectory, fileName);

                var nodeSetXml = File.ReadAllText(filePath);
                nodeSetsXml.Add(nodeSetXml);
            }
            return Task.FromResult<IEnumerable<string>>(nodeSetsXml);
        }
    }
}
