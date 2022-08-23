using Opc.Ua;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Opc.Ua.Export;

namespace CESMII.OpcUa.NodeSetModel.Factory.Opc
{
    public interface IOpcUaContext
    {
        // OPC utilities
        NamespaceTable NamespaceUris { get; }
        string GetNodeIdWithUri(NodeId nodeId, out string namespaceUri);

        // OPC NodeState cache
        NodeState GetNode(NodeId nodeId);
        NodeState GetNode(ExpandedNodeId expandedNodeId);
        List<NodeStateHierarchyReference> GetHierarchyReferences(NodeState nodeState);

        // NodesetModel cache
        NodeSetModel GetOrAddNodesetModel(ModelTableEntry model, bool createNew = true);
        NodeModel GetModelForNode(string nodeId);
        ILogger Logger { get; }
        string JsonEncodeVariant(Variant wrappedValue);
        List<NodeState> ImportUANodeSet(UANodeSet nodeSet);
    }
}