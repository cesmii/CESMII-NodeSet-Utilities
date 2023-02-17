using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Xml;
using CESMII.OpcUa.NodeSetModel;
using CESMII.OpcUa.NodeSetModel.Factory.Opc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodeSetDiff;
using Opc.Ua;
using Opc.Ua.Export;
using Org.XmlUnit.Diff;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static System.Net.WebRequestMethods;
using File = System.IO.File;

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
            var file = Path.Combine(strTestNodeSetDirectory, "opcfoundation.org.UA.NodeSet2.xml");
            output.WriteLine($"Importing {file}");

            var nodesetXml = File.ReadAllText(file);

            var opcContext = new DefaultOpcUaContext(NullLogger.Instance);
            var importer = new UANodeSetModelImporter(opcContext);

            var baseNodeSets = (await importer.ImportNodeSetModelAsync(nodesetXml)).ToDictionary(n => n.ModelUri);
            var uaBaseModel = baseNodeSets[Namespaces.OpcUa];

            var nodeSetModel = new NodeSetModel
            {
                ModelUri = "https://opcua.rocks/UA",
            };

            uint nextNodeId = 1000;

            var animalType = new ObjectTypeModel
            {
                DisplayName = new List<NodeModel.LocalizedText> { "AnimalType" },
                SuperType = uaBaseModel.ObjectTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("BaseObjectType")),
                NodeSet = nodeSetModel,
                NodeId = new ExpandedNodeId(nextNodeId++, nodeSetModel.ModelUri).ToString(),
                Properties = new List<VariableModel>
                {
                    new PropertyModel
                    {
                        NodeSet = nodeSetModel,
                        NodeId = new ExpandedNodeId(nextNodeId++, nodeSetModel.ModelUri).ToString(),
                        DisplayName = new List<NodeModel.LocalizedText> { "Name" },
                        DataType = uaBaseModel.DataTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("String")),
                    },
                },
                DataVariables = new List<DataVariableModel>
                {
                    new DataVariableModel
                    {
                        NodeSet = nodeSetModel,
                        NodeId = new ExpandedNodeId(nextNodeId++, nodeSetModel.ModelUri).ToString(),
                        DisplayName = new List<NodeModel.LocalizedText> { "Height" },
                        DataType = uaBaseModel.DataTypes.FirstOrDefault(ot => ot.BrowseName.EndsWith("Float")),
                        EngineeringUnit = new VariableModel.EngineeringUnitInfo { DisplayName = "metre"  },
                        Value = JsonEncodeVariant((float) 0),
                    },
                },
            };

            nodeSetModel.ObjectTypes.Add(animalType);

            nodeSetModel.UpdateIndices();

            var exportedNodeSetXml = UANodeSetModelExporter.ExportNodeSetAsXml(nodeSetModel, baseNodeSets);
        }

        private string JsonEncodeVariant(Variant v)
        {
            using (var encoder = new JsonEncoder(ServiceMessageContext.GlobalContext, true))
            {
                encoder.WriteVariant("Value", v);
                var jsonValue = encoder.CloseAndReturnText();
                return jsonValue;
            }
        }
    }
}
