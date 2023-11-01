# CESMII NodeSet Utilities

CESMII NodeSet Utilities is a set of .Net libraries for reading, validating, manipulating, and creating of OPC UA NodeSets. It is built on top of the OPC Foundation's OPCFoundation.NetStandard.Opc.Ua.Core library.

## Getting Started

The easiest way to start using the nodeset utilites is to NuGet the following packages:

- NuGet: CESMII.OpcUa.NodeSetImporter
- NuGet: CESMII.OpcUa.NodeSetModel
- NuGet: CESMII.OpcUa.NodeSetModel.Factory.Opc

The using statements for the basic code snippets below are:

``` C#
using CESMII.OpcUa.NodeSetModel;
using CESMII.OpcUa.NodeSetModel.Factory.Opc;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Export;
```

## Parsing of Nodeset Files

The snippet below shows how to setup the nodeset utility's importer and parse a couple of OPC UA nodeset xml files.

``` C#
// Location of nodeset xml files
var nodeSetDirectory = "NodeSets";

// Set up the importer
var importer = new UANodeSetModelImporter(NullLogger.Instance);
var nodeSetModels = new Dictionary<string, NodeSetModel>();
var opcContext = new DefaultOpcUaContext(nodeSetModels, NullLogger.Instance);

// Read and import the OPC UA nodeset
var file = Path.Combine(nodeSetDirectory, "opcfoundation.org.UA.NodeSet2.xml");
var nodeSet = UANodeSet.Read(new FileStream(file, FileMode.Open));
await importer.LoadNodeSetModelAsync(opcContext, nodeSet);
var uaBaseModel = baseNodeSetsDict[Namespaces.OpcUa];

// Read and import the OPC UA DI nodeset
file = Path.Combine(strTestNodeSetDirectory, "opcfoundation.org.UA.DI.NodeSet2.xml");
nodeSet = UANodeSet.Read(new FileStream(file, FileMode.Open));
await importer.LoadNodeSetModelAsync(opcContext, nodeSet);
var uaDiModel = nodeSetModels.Last();

// Output Object Types of the DI nodeset
Console.WriteLine($"DI Object Types:");
uaDiModel.Value.ObjectTypes.ForEach(aObjectTypeModel => {
    Console.WriteLine(aObjectTypeModel.DisplayName.First().Text);
});

```



## Creating a New NodeSet

The code below shows how to create a new NodeSetModel and add it to our project.

``` C#
// create new NodeSetModel and add it to dictionary of NodeSetModels
var nodeSetModel = new NodeSetModel
{
    ModelUri = "https://opcua.rocks/UA",
    RequiredModels = new List<RequiredModelInfo>
    {
        new RequiredModelInfo { 
            ModelUri= uaBaseModel.ModelUri, 
            PublicationDate = uaBaseModel.PublicationDate, 
            Version = uaBaseModel.Version
        }
    },
};
nodeSetModels.Add(nodeSetModel.ModelUri, nodeSetModel);
```

## Adding an Object Type to the New NodeSet

Below we show how to create a type with a property and a variable and add it to our new NodeSetModel. Note, how DataTypes and SuperTypes are passed as objects, and how we make use of Engineering Units.

``` C#
// a counter for new node id's
uint nextNodeId = 1000;

// a couple of dictionaries with OPC UA data and object types for convenience
var uaObjectTypes = uaBaseModel.ObjectTypes.ToDictionary(x => x.DisplayName.First().Text);
var uaDataTypes = uaBaseModel.DataTypes.ToDictionary(x => x.DisplayName.First().Text);

// the classic: Dr. Stefan Profanter's opcua.rocks animal type
var animalType = new ObjectTypeModel
{
    DisplayName = new List<NodeModel.LocalizedText> { "AnimalType" },
    BrowseName = "Animal Type",
    SymbolicName = "Animal Type",
    SuperType = uaObjectTypes["BaseObjectType"],
    NodeSet = nodeSetModel,
    NodeId = new ExpandedNodeId(nextNodeId++, nodeSetModel.ModelUri).ToString(),
    Properties = new List<VariableModel>
    {
        new PropertyModel
        {
            NodeSet = nodeSetModel,
            NodeId = new ExpandedNodeId(nextNodeId++, nodeSetModel.ModelUri).ToString(),
            DisplayName = new List<NodeModel.LocalizedText> { "Name" },
            DataType = uaDataTypes["String"],
        },
    },
    DataVariables = new List<DataVariableModel>
    {
        new DataVariableModel
        {
            NodeSet = nodeSetModel,
            NodeId = new ExpandedNodeId(nextNodeId++, nodeSetModel.ModelUri).ToString(),
            DisplayName = new List<NodeModel.LocalizedText> { "Height" },
            DataType = uaDataTypes["Float"],
            EngineeringUnit = new VariableModel.EngineeringUnitInfo { DisplayName = "metre"  },
        },
    },
};

nodeSetModel.ObjectTypes.Add(animalType);


```

## Serialize a NodeSetModel to XML

The snippet below shows how to serialize a NodeSetModel as XML.

``` C#
// validate all node2node references
nodeSetModel.UpdateIndices();

// create nodeset xml
var exportedNodeSetXml = UANodeSetModelExporter.ExportNodeSetAsXml(nodeSetModel, nodeSetModels);

Console.WriteLine(exportedNodeSetXml);
```

## Next Steps

We need a bit more information about how to create and parse values.

A more complete write-up with a walk through example and further references can be found [here](https://www.linkedin.com/pulse/creating-opc-ua-information-models-using-cesmiis-net-vilkner-ph-d-/).
