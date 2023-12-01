using CESMII.OpcUa.NodeSetModel.Factory.Opc;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CESMII.OpcUa.NodeSetModel.Factory.OpcClient
{

    public class OpcClientContext : DefaultOpcUaContext, IDisposable
    {
        private readonly Session m_session;
        public Session Session { get { return m_session; } }
        private bool disposedValue;

        public OpcClientContext(Session session, Dictionary<string, NodeSetModel> nodesetModels, ILogger logger)
            : base(session.SystemContext, new NodeStateCollection(), nodesetModels, logger)
        {
            m_session = session;
            ReencodeExtensionsAsJson = true;
            EncodeJsonScalarsAsValue = true;
        }

        public override NodeState GetNode(NodeId nodeId)
        {
            var node = m_session.NodeCache.Find(nodeId);
            if (node != null)
            {
                if (node is DataTypeNode dtNode)
                {
                    var dataTypeState = new DataTypeState
                    {
                        DataTypeDefinition = dtNode.DataTypeDefinition,
                        IsAbstract = dtNode.IsAbstract,
                    };
                    InitBaseTypeState(dataTypeState, dtNode);
                    return dataTypeState;
                }
                else if (node is ObjectTypeNode otNode)
                {
                    var objectTypeState = new BaseObjectTypeState();
                    InitBaseTypeState(objectTypeState, otNode);
                    return objectTypeState;
                }
                else if (node is VariableTypeNode vtNode)
                {
                    var variableTypeState = new BaseDataVariableTypeState()
                    {
                        DataType = vtNode.DataType,
                        WrappedValue = vtNode.Value,
                        ValueRank = vtNode.ValueRank,
                        ArrayDimensions = new ReadOnlyList<uint>(vtNode.ArrayDimensions),
                    };
                    InitBaseTypeState(variableTypeState, vtNode);
                    return variableTypeState;
                }
                else if (node is ReferenceTypeNode rtNode)
                {
                    var referenceTypeState = new ReferenceTypeState()
                    {
                        Symmetric = rtNode.Symmetric,
                        InverseName = rtNode.InverseName,
                    };
                    InitBaseTypeState(referenceTypeState, rtNode);
                    return referenceTypeState;
                }
                else if (node is ObjectNode objectNode)
                {
                    var parentState = GetNode(objectNode.ReferenceTable.Find(ReferenceTypeIds.HasComponent, true, true, m_session.TypeTree).Where(r => ExpandedNodeId.ToNodeId(r.TargetId, m_session.NamespaceUris) == objectNode.NodeId).FirstOrDefault()?.TargetId);
                    var objectState = new BaseObjectState(parentState);
                    InitBaseInstanceState(objectState, objectNode);
                    return objectState;
                }
                else if (node is VariableNode variableNode)
                {
                    BaseVariableState variableState;
                    var typeDefinitionId = ExpandedNodeId.ToNodeId(variableNode.TypeDefinitionId, m_session.NamespaceUris);
                    if (typeDefinitionId == VariableTypeIds.PropertyType)
                    {
                        var parentState = GetNode(variableNode.ReferenceTable.Find(ReferenceTypeIds.HasProperty, true, true, m_session.TypeTree).Where(r => ExpandedNodeId.ToNodeId(r.TargetId, m_session.NamespaceUris) == variableNode.NodeId).FirstOrDefault()?.TargetId);
                        variableState = new PropertyState(parentState);
                    }
                    else
                    {
                        var parentState = GetNode(variableNode.ReferenceTable.Find(ReferenceTypeIds.HasComponent, true, true, m_session.TypeTree).Where(r => ExpandedNodeId.ToNodeId(r.TargetId, m_session.NamespaceUris) == variableNode.NodeId).FirstOrDefault()?.TargetId);
                        variableState = new BaseDataVariableState(parentState);
                    }
                    InitBaseVariableState(variableState, variableNode);
                    return variableState;
                }
                else if (node is MethodNode methodNode)
                {
                    var parentState = GetNode(methodNode.ReferenceTable.Find(ReferenceTypeIds.HasComponent, true, true, m_session.TypeTree).Where(r => ExpandedNodeId.ToNodeId(r.TargetId, m_session.NamespaceUris) == methodNode.NodeId).FirstOrDefault()?.TargetId);
                    var methodState = new MethodState(parentState);
                    InitBaseInstanceState(methodState, methodNode);
                    return methodState;
                }
            }
            return base.GetNode(nodeId);
        }

        private static void InitNodeState(NodeState nodeState, Node node)
        {
            nodeState.NodeId = node.NodeId;
            nodeState.DisplayName = node.DisplayName;
            nodeState.Description = node.Description;
            nodeState.BrowseName = node.BrowseName;
            nodeState.SymbolicName = null; // TODO
            nodeState.Categories = new List<string>(); // TODO
            nodeState.WriteMask = (AttributeWriteMask)node.WriteMask;
            nodeState.UserWriteMask = (AttributeWriteMask)node.UserWriteMask;
            nodeState.AccessRestrictions = (AccessRestrictionType)node.AccessRestrictions;
            nodeState.NodeSetDocumentation = null; // TODO
            nodeState.AddReferences((node as ILocalNode).References.AsEnumerable().ToList());
        }
        private void InitBaseTypeState(BaseTypeState nodeState, TypeNode node)
        {
            InitNodeState(nodeState, node);
            nodeState.SuperTypeId = ExpandedNodeId.ToNodeId(node.GetSuperType(m_session.TypeTree), m_session.NamespaceUris);
            //nodeState.IsAbstract = not on TypeNode
        }
        private void InitBaseInstanceState(BaseInstanceState nodeState, InstanceNode node)
        {
            InitNodeState(nodeState, node);
            nodeState.TypeDefinitionId = ExpandedNodeId.ToNodeId(node.TypeDefinitionId, m_session.NamespaceUris);
            nodeState.ModellingRuleId = node.ModellingRule;
        }
        private void InitBaseVariableState(BaseVariableState nodeState, VariableNode node)
        {
            InitBaseInstanceState(nodeState, node);
            nodeState.WrappedValue = node.Value;
            nodeState.DataType = node.DataType;
            nodeState.ValueRank = node.ValueRank;
            nodeState.AccessLevelEx = node.AccessLevelEx;
            nodeState.ArrayDimensions = new ReadOnlyList<uint>(node.ArrayDimensions);
            nodeState.MinimumSamplingInterval = node.MinimumSamplingInterval;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    try
                    {
                        m_session.Close();
                        m_session.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        //~ClientOpcUaContext()
        //{
        //    // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //    Dispose(disposing: false);
        //}

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
