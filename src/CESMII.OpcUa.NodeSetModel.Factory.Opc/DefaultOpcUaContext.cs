using Opc.Ua;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Opc.Ua.Export;
using CESMII.OpcUa.NodeSetModel.Opc.Extensions;
using CESMII.OpcUa.NodeSetModel.Export.Opc;

namespace CESMII.OpcUa.NodeSetModel.Factory.Opc
{
    public class DefaultOpcUaContext : IOpcUaContext
    {
        private readonly ISystemContext _systemContext;
        private readonly NodeStateCollection _importedNodes;
        protected readonly Dictionary<string, NodeSetModel> _nodesetModels;
        protected readonly ILogger _logger;

        public DefaultOpcUaContext(ILogger logger)
        {
            _importedNodes = new NodeStateCollection();
            _nodesetModels = new Dictionary<string, NodeSetModel>();
            _logger = logger;

            var namespaceTable = new NamespaceTable();
            namespaceTable.GetIndexOrAppend(Namespaces.OpcUa);
            var typeTable = new TypeTable(namespaceTable);
            _systemContext = new SystemContext()
            {
                NamespaceUris = namespaceTable,
                TypeTable = typeTable,
                EncodeableFactory = new DynamicEncodeableFactory(EncodeableFactory.GlobalFactory),
            };
        }

        public DefaultOpcUaContext(Dictionary<string, NodeSetModel> nodesetModels, ILogger logger) : this(logger)
        {
            _nodesetModels = nodesetModels;
            _logger = logger;
        }
        public DefaultOpcUaContext(ISystemContext systemContext, NodeStateCollection importedNodes, Dictionary<string, NodeSetModel> nodesetModels, ILogger logger)
        {
            _systemContext = systemContext;
            _importedNodes = importedNodes;
            _nodesetModels = nodesetModels;
            _logger = logger;
        }

        public bool ReencodeExtensionsAsJson { get; set; }
        public bool EncodeJsonScalarsAsValues { get; set; }

        private Dictionary<NodeId, NodeState> _importedNodesByNodeId;
        private Dictionary<string, UANodeSet> _importedUANodeSetsByUri = new();

        public NamespaceTable NamespaceUris { get => _systemContext.NamespaceUris; }

        ILogger IOpcUaContext.Logger => _logger;


        public virtual string GetNodeIdWithUri(NodeId nodeId, out string namespaceUri)
        {
            namespaceUri = GetNamespaceUri(nodeId.NamespaceIndex);
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "Namespace Index ({0}) for node id {1} is not in the namespace table.", nodeId.NamespaceIndex, nodeId);
            }
            var nodeIdWithUri = new ExpandedNodeId(nodeId, namespaceUri).ToString();
            return nodeIdWithUri;
        }

        public virtual NodeState GetNode(ExpandedNodeId expandedNodeId)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, _systemContext.NamespaceUris);
            return GetNode(nodeId);
        }

        public virtual NodeState GetNode(NodeId nodeId)
        {
            if (_importedNodesByNodeId == null)
            {
                _importedNodesByNodeId = _importedNodes.ToDictionary(n => n.NodeId);
            }
            NodeState nodeStateDict = null;
            if (nodeId != null)
            {
                _importedNodesByNodeId.TryGetValue(nodeId, out nodeStateDict);
            }
            return nodeStateDict;
        }

        public virtual string GetNamespaceUri(ushort namespaceIndex)
        {
            return _systemContext.NamespaceUris.GetString(namespaceIndex);
        }

        public virtual TNodeModel GetModelForNode<TNodeModel>(string nodeId) where TNodeModel : NodeModel
        {
            var expandedNodeId = ExpandedNodeId.Parse(nodeId, _systemContext.NamespaceUris);
            var uaNamespace = GetNamespaceUri(expandedNodeId.NamespaceIndex);
            if (!_nodesetModels.TryGetValue(uaNamespace, out var nodeSetModel))
            {
                return null;
            }
            if (nodeSetModel.AllNodesByNodeId.TryGetValue(nodeId, out var nodeModel))
            {
                return nodeModel as TNodeModel;
            }
            return null;
        }

        public virtual NodeSetModel GetOrAddNodesetModel(ModelTableEntry model, bool createNew = true)
        {
            if (!_nodesetModels.TryGetValue(model.ModelUri, out var nodesetModel))
            {
                nodesetModel = new NodeSetModel();
                nodesetModel.ModelUri = model.ModelUri;
                nodesetModel.PublicationDate = model.GetNormalizedPublicationDate();
                nodesetModel.Version = model.Version;
                if (!string.IsNullOrEmpty(model.XmlSchemaUri))
                {
                    nodesetModel.XmlSchemaUri = model.XmlSchemaUri;
                }
                if (model.RequiredModel != null)
                {
                    foreach (var requiredModel in model.RequiredModel)
                    {
                        var existingNodeSet = GetOrAddNodesetModel(requiredModel);
                        var requiredModelInfo = new RequiredModelInfo
                        {
                            ModelUri = requiredModel.ModelUri,
                            PublicationDate = requiredModel.GetNormalizedPublicationDate(),
                            Version = requiredModel.Version,
                            AvailableModel = existingNodeSet,
                        };
                        nodesetModel.RequiredModels.Add(requiredModelInfo);
                    }
                }
                _nodesetModels.Add(nodesetModel.ModelUri, nodesetModel);
            }
            return nodesetModel;
        }

        public virtual List<NodeState> ImportUANodeSet(UANodeSet nodeSet)
        {
            var previousNodes = _importedNodes.ToList();
            if (nodeSet.Items?.Any() == true)
            {
                nodeSet.Import(_systemContext, _importedNodes);
            }
            var newlyImportedNodes = _importedNodes.Except(previousNodes).ToList();
            if (newlyImportedNodes.Any())
            {
                _importedNodesByNodeId = null;
            }
            var modelUri = nodeSet.Models?.FirstOrDefault()?.ModelUri;
            if (modelUri != null)
            {
                _importedUANodeSetsByUri.Add(modelUri, nodeSet);
            }
            return newlyImportedNodes;
        }
        public virtual UANodeSet GetUANodeSet(string modeluri)
        {
            if (_importedUANodeSetsByUri.TryGetValue(modeluri, out var nodeSet))
            {
                return nodeSet;
            }
            return null;
        }

        public virtual List<NodeStateHierarchyReference> GetHierarchyReferences(NodeState nodeState)
        {
            var hierarchy = new Dictionary<NodeId, string>();
            var references = new List<NodeStateHierarchyReference>();
            nodeState.GetHierarchyReferences(_systemContext, null, hierarchy, references);
            return references;
        }

        public virtual string JsonEncodeVariant(Variant wrappedValue, DataTypeModel dataType = null)
        {
            return NodeModelUtils.JsonEncodeVariant(_systemContext, wrappedValue, dataType, ReencodeExtensionsAsJson, EncodeJsonScalarsAsValues);
        }
    }
}