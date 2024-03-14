using CESMII.OpcUa.NodeSetModel.Factory.Opc;
using Opc.Ua;
using System.Collections.Generic;

namespace CESMII.OpcUa.NodeSetModel.Factory.OpcClient
{
    public static class OpcClientNodeModelFactory
    {
        public static NodeModel ReadNodeModel(IOpcUaContext opcContext, ExpandedNodeId nodeId)
        {
            return ReadNodeModel(opcContext, ExpandedNodeId.ToNodeId(nodeId, opcContext.NamespaceUris));
        }
        public static NodeModel ReadNodeModel(IOpcUaContext opcContext, NodeId nodeId)
        {
            var opcNodeState = opcContext.GetNode(nodeId);
            var nodeModel = NodeModelFactoryOpc.Create(opcContext, opcNodeState, null, out _, 1);

            // Add required model information
            foreach (var nodeSetModel in opcContext.NodeSetModels.Values)
            {
                if (nodeSetModel != nodeModel.NodeSet)
                {
                    if (nodeModel.NodeSet.RequiredModels == null)
                    {
                        nodeModel.NodeSet.RequiredModels = new List<RequiredModelInfo>();
                    }
                    nodeModel.NodeSet.RequiredModels.Add(new RequiredModelInfo
                    {
                        ModelUri = nodeSetModel.ModelUri,
                        PublicationDate = nodeSetModel.PublicationDate,
                        Version = nodeSetModel.Version,
                        AvailableModel = nodeSetModel,
                    });
                }
            }

            // Ensure we resolve reference types are used in AllReferenceNodes but that may not have been instatiated during node model creation
            opcContext.GetNode(ReferenceTypeIds.HasComponent);
            opcContext.GetNode(ReferenceTypeIds.HasProperty);
            opcContext.GetNode(ReferenceTypeIds.HasInterface);
            opcContext.GetNode(ReferenceTypeIds.GeneratesEvent);
            foreach (var nodeSetModel in opcContext.NodeSetModels.Values)
            {
                nodeSetModel.UpdateIndices();
            }
            return nodeModel;

        }
    }
}