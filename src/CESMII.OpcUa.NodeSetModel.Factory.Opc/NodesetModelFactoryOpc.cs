using Opc.Ua;
using ua = Opc.Ua;

using System;
using System.Collections.Generic;
using System.Linq;

using CESMII.OpcUa.NodeSetModel;
using CESMII.OpcUa.NodeSetModel.Opc.Extensions;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Export;

namespace CESMII.OpcUa.NodeSetModel
{
    public static class LocalizedTextExtension
    {
        public static NodeModel.LocalizedText ToModelSingle(this ua.LocalizedText text) => text != null ? new NodeModel.LocalizedText { Text = text.Text, Locale = text.Locale } : null;
        public static List<NodeModel.LocalizedText> ToModel(this ua.LocalizedText text) => text != null ? new List<NodeModel.LocalizedText> { text.ToModelSingle() } : new List<NodeModel.LocalizedText>();
        public static List<NodeModel.LocalizedText> ToModel(this IEnumerable<ua.LocalizedText> texts) => texts?.Select(text => text.ToModelSingle()).Where(lt => lt != null).ToList();
    }
}

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
        NodeSetModel GetOrAddNodesetModel(NodeModel nodeModel);
        NodeModel GetModelForNode(string nodeId);
        ILogger Logger { get; }
        string JsonEncodeVariant(Variant wrappedValue);
    }

    public class DefaultOpcUaContext : IOpcUaContext
    {
        private readonly ISystemContext _systemContext;
        private readonly NodeStateCollection _importedNodes;
        private readonly Dictionary<string, NodeSetModel> _nodesetModels;
        private readonly ILogger _logger;

        public DefaultOpcUaContext(ISystemContext systemContext, NodeStateCollection importedNodes, Dictionary<string, NodeSetModel> nodesetModels, ILogger logger)
        {
            _systemContext = systemContext;
            _importedNodes = importedNodes;
            _nodesetModels = nodesetModels;
            _logger = logger;
        }

        private Dictionary<NodeId, NodeState> _importedNodesByNodeId;

        public NamespaceTable NamespaceUris { get => _systemContext.NamespaceUris; }

        ILogger IOpcUaContext.Logger => _logger;


        public string GetNodeIdWithUri(NodeId nodeId, out string namespaceUri)
        {
            namespaceUri = GetNamespaceUri(nodeId.NamespaceIndex);
            var nodeIdWithUri = new ExpandedNodeId(nodeId, namespaceUri).ToString();
            return nodeIdWithUri;
        }

        public NodeState GetNode(ExpandedNodeId expandedNodeId)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, _systemContext.NamespaceUris);
            return GetNode(nodeId);
        }

        public NodeState GetNode(NodeId nodeId)
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

        public string GetNamespaceUri(ushort namespaceIndex)
        {
            return _systemContext.NamespaceUris.GetString(namespaceIndex);
        }

        public NodeModel GetModelForNode(string nodeId)
        {
            var expandedNodeId = ExpandedNodeId.Parse(nodeId, _systemContext.NamespaceUris);
            var uaNamespace = GetNamespaceUri(expandedNodeId.NamespaceIndex);
            if (!_nodesetModels.TryGetValue(uaNamespace, out var nodeSetModel))
            {
                return null;
            }
            if (nodeSetModel.AllNodesByNodeId.TryGetValue(nodeId, out var nodeModel))
            {
                return nodeModel;
            }
            return null;
        }

        public NodeSetModel GetOrAddNodesetModel(NodeModel nodeModel)
        {
            var uaNamespace = nodeModel.Namespace;
            if (!_nodesetModels.TryGetValue(uaNamespace, out var nodesetModel))
            {
                nodesetModel = new NodeSetModel();
                nodesetModel.ModelUri = uaNamespace;
                _nodesetModels.Add(uaNamespace, nodesetModel);
            }
            nodeModel.NodeSet = nodesetModel;
            return nodesetModel;
        }
        public List<NodeStateHierarchyReference> GetHierarchyReferences(NodeState nodeState)
        {
            var hierarchy = new Dictionary<NodeId, string>();
            var references = new List<NodeStateHierarchyReference>();
            nodeState.GetHierarchyReferences(_systemContext, null, hierarchy, references);
            return references;
        }

        string IOpcUaContext.JsonEncodeVariant(Variant wrappedValue)
        {
            return JsonEncodeVariant(_systemContext, wrappedValue);
        }
        public static string JsonEncodeVariant(ISystemContext systemContext, Variant value)
        {
            string encodedValue = null;
            using (var ms = new MemoryStream())
            {
                using (var sw = new StreamWriter(ms))
                {
                    var encoder = new JsonEncoder(new ServiceMessageContext { NamespaceUris = systemContext.NamespaceUris, }, true, sw, false);
                    encoder.WriteVariant("Value", value, true);
                    sw.Flush();
                    encodedValue = Encoding.UTF8.GetString(ms.ToArray());
                }
            }
            return encodedValue;
        }
    }

    public class NodeModelFactoryOpc : NodeModelFactoryOpc<NodeModel>
    {
        public static Task<List<NodeSetModel>> LoadNodeSetAsync(IOpcUaContext opcContext, UANodeSet nodeSet, Object customState, Dictionary<string, NodeSetModel> NodesetModels, ISystemContext systemContext,
                NodeStateCollection allImportedNodes, out List<NodeState> importedNodes, Dictionary<string, string> Aliases, bool doNotReimport = false)
        {
            if (!nodeSet.Models.Any())
            {
                var ex = new Exception($"Invalid nodeset: no models specified");
                opcContext.Logger.LogError(ex.Message);
                throw ex;
            }

            // Find all models that are used by another nodeset
            var requiredModels = nodeSet.Models.Where(m => m.RequiredModel != null).SelectMany(m => m.RequiredModel).Select(m => m?.ModelUri).Distinct().ToList();
            var missingModels = requiredModels.Where(rm => !NodesetModels.ContainsKey(rm)).ToList();
            if (missingModels.Any())
            {
                throw new Exception($"Missing dependent node sets: {string.Join(", ", missingModels)}");
            }

            var loadedModels = new List<NodeSetModel>();

            foreach (var model in nodeSet.Models)
            {
                var nodesetModel = new NodeSetModel();
                nodesetModel.ModelUri = model.ModelUri;
                nodesetModel.Version = model.Version;
                nodesetModel.PublicationDate = model.PublicationDateSpecified ? model.PublicationDate : null;
                nodesetModel.CustomState = customState;
                if (model.RequiredModel != null)
                {

                    foreach (var requiredModel in model.RequiredModel)
                    {
                        bool bCreated = false;
                        var requiredModelInfo = nodesetModel.RequiredModels.FirstOrDefault(rm => rm.ModelUri == requiredModel.ModelUri);
                        if (requiredModelInfo == null)
                        {
                            requiredModelInfo = new RequiredModelInfo();
                            bCreated = true;
                        }
                        requiredModelInfo.ModelUri = requiredModel.ModelUri;
                        requiredModelInfo.PublicationDate = requiredModel.PublicationDateSpecified ? requiredModel.PublicationDate : null;
                        requiredModelInfo.Version = requiredModel.Version;
                        if (NodesetModels.TryGetValue(requiredModel.ModelUri, out var requiredNodesetModel))
                        {
                            requiredModelInfo.AvailableModel = requiredNodesetModel;
                        }
                        if (bCreated)
                        {
                            nodesetModel.RequiredModels.Add(requiredModelInfo);
                        }
                    }
                }
                if (nodeSet.Aliases?.Length > 0)
                {
                    foreach (var alias in nodeSet.Aliases)
                    {
                        Aliases[alias.Value] = alias.Alias;
                    }
                }

                if (!NodesetModels.ContainsKey(model.ModelUri))
                {
                    NodesetModels.Add(model.ModelUri, nodesetModel);
                }
                else
                {
                    // Nodeset already imported
                    if (doNotReimport)
                    {
                        // Don't re-import dependencies
                        nodesetModel = NodesetModels[model.ModelUri];
                    }
                    else
                    {
                        // Replace with new nodeset model 
                        // TODO: verify the assumption that  there's at most one nodeset per namespace)
                        NodesetModels[model.ModelUri] = nodesetModel;
                    }
                }
                loadedModels.Add(nodesetModel);
            }
            if (nodeSet.Items == null)
            {
                nodeSet.Items = new UANode[0];
            }
            var previousNodes = allImportedNodes.ToList();

            nodeSet.Import(systemContext, allImportedNodes);
            importedNodes = allImportedNodes.Except(previousNodes).ToList();

            // TODO Read nodeset poperties like author etc. and expose them in Profile editor
            //var nodesInModel = _importedNodes.Where(n => nodeSet.Models.Any(m => m.ModelUri == GetNamespaceUri(n.NodeId))).ToList();
            //if (nodesInModel.Count != nodeSet.Items.Count())
            //{
            //    //  Model defines nodes outside of it's namespace: TODO - investigate if his is allowed and if so how to cleanly support it.
            //}

            foreach (var node in importedNodes)
            {
                var nodeModel = NodeModelFactoryOpc.Create(opcContext, node, customState, out var bAdded);
                if (nodeModel != null && !bAdded)
                {
                    var namespaceUri = systemContext.NamespaceUris.GetString(node.NodeId.NamespaceIndex);
                    var nodeIdString = new ExpandedNodeId(node.NodeId, namespaceUri).ToString();
                    if (NodesetModels.TryGetValue(nodeModel.Namespace, out var nodesetModel))// TODO support multiple models per namespace
                    {
                        if (!nodesetModel.AllNodesByNodeId.ContainsKey(nodeIdString))
                        {
                            nodesetModel.UnknownNodes.Add(nodeModel);
                        }
                    }
                    else
                    {
                        throw new Exception($"Unknown node {nodeIdString} for undefined namespace {nodeModel.Namespace}");
                    }
                }
            }
#if NODESETDBTEST
            if (doNotReimport)
            {
                foreach (var model in loadedModels)
                {
                    nsDBContext.Attach(model);
                    foreach (var node in model.AllNodes.Values)
                    {
                        nsDBContext.Attach(node);
                    }
                }
                //nsDBContext.ChangeTracker.AcceptAllChanges();
            }
#endif
            return Task.FromResult(loadedModels);
        }


    }
    public class NodeModelFactoryOpc<T> where T : NodeModel, new()
    {

        protected T _model;
        protected ILogger Logger;

        protected virtual void Initialize(IOpcUaContext opcContext, NodeState opcNode)
        {
            Logger.LogTrace($"Creating node model for {opcNode}");
            // TODO capture multiple locales from a nodeset: UA library seems to offer only one locale
            _model.DisplayName = opcNode.DisplayName.ToModel();

            var browseNameNamespace = opcContext.NamespaceUris.GetString(opcNode.BrowseName.NamespaceIndex);
            _model.BrowseName = $"{browseNameNamespace};{opcNode.BrowseName.Name}";
            _model.SymbolicName = opcNode.SymbolicName;
            _model.Description = opcNode.Description.ToModel();
            if (opcNode.Categories != null)
            {
                if (_model.Categories == null)
                {
                    _model.Categories = new List<string>();
                }
                _model.Categories.AddRange(opcNode.Categories);
            }
            _model.Documentation = opcNode.NodeSetDocumentation;

            var references = opcContext.GetHierarchyReferences(opcNode);

            foreach (var reference in references)
            {
                var referenceType = opcContext.GetNode(reference.ReferenceTypeId) as ReferenceTypeState;
                var referenceTypes = GetBaseTypes(opcContext, referenceType);

                var referencedNode = opcContext.GetNode(reference.TargetId);
                if (referencedNode == null)
                {
                    throw new Exception($"Referenced node {reference.TargetId} not found for {opcNode}");
                }

                if (reference.IsInverse)
                {
                    // TODO UANodeSet.Import should already handle inverse references: investigate why these are not processed
                    // Workaround for now:
                    AddChildToNodeModel(
                    () => NodeModelFactoryOpc<T>.Create(opcContext, referencedNode, this._model.CustomState, out _)
                    , opcContext, referenceTypes, opcNode);
                    continue;
                }
                AddChildToNodeModel(() => this._model, opcContext, referenceTypes, referencedNode);
            }
            Logger.LogTrace($"Created node model {this._model} for {opcNode}");
        }

        private static void AddChildToNodeModel(Func<NodeModel> parentFactory, IOpcUaContext opcContext, List<BaseTypeState> referenceTypes, NodeState referencedNode)
        {
            if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.HasComponent))
            {
                if (referencedNode is BaseObjectState objectState)
                {
                    var parent = parentFactory();
                    var uaChildObject = Create<ObjectModelFactoryOpc, ObjectModel>(opcContext, objectState, parent?.CustomState);
                    if (parent?.Namespace != uaChildObject.Namespace)
                    {
                        // TODO If parent is in another nodeset/namespace, the reference may not be stored (Example: Server/Namespaces node (OPC i=11715): nodesets add themselves to that global node).
                        opcContext.Logger.LogWarning($"Object {uaChildObject} is added to {parent} in a different namespace: reference is ignored.");
                        // Add the reverse reference to the referencing node (parent)
                        var referencingNodeAndReference = new NodeModel.NodeAndReference { Node = parent, Reference = opcContext.GetNodeIdWithUri(referenceTypes[0].NodeId, out _), };
                        AddChildIfNotExists(uaChildObject, uaChildObject.OtherReferencingNodes, referencingNodeAndReference, opcContext.Logger);
                    }
                    AddChildIfNotExists(parent, parent?.Objects, uaChildObject, opcContext.Logger);
                    if (referenceTypes[0].NodeId != ReferenceTypeIds.HasComponent)
                    {
                        // Preserve the more specific reference type as well
                        var nodeAndReference = new NodeModel.NodeAndReference { Node = uaChildObject, Reference = opcContext.GetNodeIdWithUri(referenceTypes[0].NodeId, out _) };
                        AddChildIfNotExists(parent, parent?.OtherReferencedNodes, nodeAndReference, opcContext.Logger);
                    }
                }
                else if (referencedNode is BaseObjectTypeState objectTypeState)
                {
                    // TODO
                }
                else if (referencedNode is BaseDataVariableState variableState)
                {
                    var parent = parentFactory();
                    var variable = Create<DataVariableModelFactoryOpc, DataVariableModel>(opcContext, variableState, parent?.CustomState);
                    AddChildIfNotExists(parent, parent?.DataVariables, variable, opcContext.Logger);
                }
                else if (referencedNode is MethodState methodState)
                {
                    var parent = parentFactory();
                    var method = Create<MethodModelFactoryOpc, MethodModel>(opcContext, methodState, parent?.CustomState);
                    AddChildIfNotExists(parent, parent?.Methods, method, opcContext.Logger);
                }
                else
                {
                    opcContext.Logger.LogWarning($"Ignoring component {referencedNode} with unexpected node type {referencedNode.GetType()}");
                }
            }
            else if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.HasProperty))
            {
                if (referencedNode.BrowseName?.Name == BrowseNames.EngineeringUnits || (referencedNode as BaseVariableState).DataType == DataTypeIds.EUInformation)
                {
                    var parent = parentFactory();
                    if (parent is VariableModel parentVariable && parentVariable != null)
                    {
                        parentVariable.EngUnitNodeId = opcContext.GetNodeIdWithUri(referencedNode.NodeId, out _);

                        var modelingRuleId = (referencedNode as BaseInstanceState)?.ModellingRuleId;
                        if (modelingRuleId != null)
                        {
                            var modelingRule = opcContext.GetNode(modelingRuleId);
                            if (modelingRule == null)
                            {
                                throw new Exception($"Unable to resolve modeling rule {modelingRuleId}: dependency on UA nodeset not declared?");
                            }
                            parentVariable.EngUnitModelingRule = modelingRule.DisplayName.Text;
                        }
                        var euInfo = ((referencedNode as BaseVariableState)?.Value as ExtensionObject)?.Body as EUInformation;
                        if (euInfo != null)
                        {
                            parentVariable.SetEngineeringUnits(euInfo);
                            return;
                        }
                        else
                        {
                            // Nodesets commonly indicate that EUs are required on instances by specifying an empty EU in the class
                        }
                    }
                    else
                    {
                        opcContext.Logger.LogInformation($"Unexpected parent {parent} of type {parent.GetType()} for engineering unit property {referencedNode}");
                    }
                }
                else if (referencedNode.BrowseName?.Name == BrowseNames.EURange)
                {
                    var parent = parentFactory();
                    if (parent is VariableModel parentVariable && parentVariable != null)
                    {
                        parentVariable.EURangeNodeId = opcContext.GetNodeIdWithUri(referencedNode.NodeId, out _);
                        var modelingRuleId = (referencedNode as BaseInstanceState)?.ModellingRuleId;
                        if (modelingRuleId != null)
                        {
                            var modelingRule = opcContext.GetNode(modelingRuleId);
                            if (modelingRule == null)
                            {
                                throw new Exception($"Unable to resolve modeling rule {modelingRuleId}: dependency on UA nodeset not declared?");
                            }
                            parentVariable.EURangeModelingRule = modelingRule.DisplayName.Text;
                        }
                        var euRange = ((referencedNode as BaseVariableState)?.Value as ExtensionObject)?.Body as ua.Range;
                        if (euRange != null)
                        {
                            parentVariable.SetRange(euRange);
                            return;
                        }
                        else
                        {
                            // Nodesets commonly indicate that EURange are required on instances by specifying an enpty EURange in the class
                        }
                    }
                    else
                    {
                        opcContext.Logger.LogInformation($"Unexpected parent {parent} of type {parent.GetType()} for EU Range property {referencedNode}");
                    }

                }
                else if (referencedNode.BrowseName?.Name == BrowseNames.InstrumentRange)
                {
                    var parent = parentFactory();
                    if (parent is VariableModel parentVariable && parentVariable != null)
                    {
                        var euRange = ((referencedNode as BaseVariableState)?.Value as ExtensionObject)?.Body as ua.Range;
                        if (euRange != null)
                        {
                            parentVariable.SetInstrumentRange(euRange);
                            return;
                        }
                        else
                        {
                            // Nodesets commonly indicate that an Instrument Range is required on instances by specifying an enpty Instrument Range in the class
                        }
                    }
                    else
                    {
                        opcContext.Logger.LogInformation($"Unexpected parent {parent} of type {parent.GetType()} for Instrument Range property {referencedNode}");
                    }
                }
                // OptionSetValues are not commonly used and if they are they don't differ from the enum definitiones except for reserved bits: just preserve as regular properties/values for now so we can round trip without designer support
                //else if (referencedNode.BrowseName?.Name == BrowseNames.OptionSetValues)
                //{
                //    var parent = parentFactory();
                //    if (parent is DataTypeModel dataType && dataType != null)
                //    {
                //        var optionSetValues = ((referencedNode as BaseVariableState)?.Value as LocalizedText[]);
                //        if (optionSetValues != null)
                //        {
                //            dataType.SetOptionSetValues(optionSetValues.ToModel());
                //            return;
                //        }
                //        else
                //        {
                //            opcContext.Logger.LogInformation($"No or invalid OptionSetValues in {parent} for {referencedNode}");
                //        }
                //    }
                //    else
                //    {
                //        opcContext.Logger.LogInformation($"Unexpected parent {parent} of type {parent.GetType()} for OptionSetValues property {referencedNode}");
                //    }
                //}
                if (referencedNode is PropertyState propertyState)
                {
                    var parent = parentFactory();
                    var property = Create<PropertyModelFactoryOpc, PropertyModel>(opcContext, propertyState, parent?.CustomState);
                    AddChildIfNotExists(parent, parent?.Properties, property, opcContext.Logger);
                }
                else if (referencedNode is BaseDataVariableState variableState)
                {
                    // Surprisingly, properties can also be of type DataVariable
                    var parent = parentFactory();
                    var variable = Create<DataVariableModelFactoryOpc, DataVariableModel>(opcContext, variableState, parent?.CustomState);
                    AddChildIfNotExists(parent, parent?.Properties, variable, opcContext.Logger);
                }
                else
                {
                    var parent = parentFactory();
                    opcContext.Logger.LogWarning($"Ignoring property reference {referencedNode} with unexpected type {referencedNode.GetType()} in {parent}");
                }
            }

            else if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.HasInterface))
            {
                if (referencedNode is BaseObjectTypeState interfaceTypeState)
                {
                    var parent = parentFactory();
                    var uaInterface = Create<InterfaceModelFactoryOpc, InterfaceModel>(opcContext, interfaceTypeState, parent?.CustomState);
                    AddChildIfNotExists(parent, parent?.Interfaces, uaInterface, opcContext.Logger);
                    if (referenceTypes[0].NodeId != ReferenceTypeIds.HasInterface)
                    {
                        // Preserve the more specific reference type as well
                        var nodeAndReference = new NodeModel.NodeAndReference { Node = uaInterface, Reference = opcContext.GetNodeIdWithUri(referenceTypes[0].NodeId, out _) };
                        AddChildIfNotExists(parent, parent?.OtherReferencedNodes, nodeAndReference, opcContext.Logger);
                    }
                }
                else
                {
                    var parent = parentFactory();
                    opcContext.Logger.LogWarning($"Ignoring interface {referencedNode} with unexpected type {referencedNode.GetType()} in {parent}");
                }
            }
            //else if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.Organizes))
            //{
            //    if (referencedNode is BaseObjectState)
            //    {
            //        var parent = parentFactory();
            //        AddChildIfNotExists(parent, parent?.Objects, Create<ObjectModelFactoryOpc, ObjectModel>(opcContext, referencedNode, parent.CustomState));
            //    }
            //}
            //else if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.FromState))
            //{ }
            //else if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.ToState))
            //{ }
            //else if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.HasEffect))
            //{ }
            //else if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.HasCause))
            //{ }
            else if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.GeneratesEvent))
            {
                if (referencedNode is BaseObjectTypeState eventTypeState)
                {
                    var parent = parentFactory();
                    var uaEvent = Create<ObjectTypeModelFactoryOpc, ObjectTypeModel>(opcContext, eventTypeState, parent?.CustomState);
                    AddChildIfNotExists(parent, parent?.Events, uaEvent, opcContext.Logger);
                    if (referenceTypes[0].NodeId != ReferenceTypeIds.GeneratesEvent)
                    {
                        // Preserve the more specific reference type as well
                        var nodeAndReference = new NodeModel.NodeAndReference { Node = uaEvent, Reference = opcContext.GetNodeIdWithUri(referenceTypes[0].NodeId, out _) };
                        AddChildIfNotExists(parent, parent?.OtherReferencedNodes, nodeAndReference, opcContext.Logger);
                    }
                }
                else
                {
                    throw new Exception($"Unexpected event type {referencedNode}");
                }
            }
            else
            {
                var parent = parentFactory();
                var referencedModel = Create(opcContext, referencedNode, parent?.CustomState, out _);
                if (referencedModel != null)
                {
                    var nodeAndReference = new NodeModel.NodeAndReference { Node = referencedModel, Reference = opcContext.GetNodeIdWithUri(referenceTypes.FirstOrDefault().NodeId, out _) };
                    AddChildIfNotExists(parent, parent?.OtherReferencedNodes, nodeAndReference, opcContext.Logger);

                    // Add the reverse reference to the referencing node (parent)
                    var referencingNodeAndReference = new NodeModel.NodeAndReference { Node = parent, Reference = nodeAndReference.Reference, };
                    AddChildIfNotExists(referencedModel, referencedModel.OtherReferencingNodes, referencingNodeAndReference, opcContext.Logger);

                    if (referenceTypes.Any(n => n.NodeId == ReferenceTypeIds.HierarchicalReferences) && referencedModel is InstanceModelBase referencedInstanceModel)
                    {
                        if (referencedInstanceModel.Parent == null)
                        {
                            referencedInstanceModel.Parent = parent;
                            if (referencedInstanceModel.Parent != parent)
                            {
                                opcContext.Logger.LogWarning($"{referencedInstanceModel} has more than one parent. Ignored parent: {parent}, using {referencedInstanceModel.Parent}");
                            }
                        }
                    }
                }
                else
                {
                    opcContext.Logger.LogWarning($"Ignoring reference {referenceTypes.FirstOrDefault()} from {parent} to {referencedNode}: unable to resolve node.");
                }
                // Potential candidates for first class representation in the model:
                // {ns=1;i=6030} - ConnectsTo / Hierarchical
                // {ns=2;i=18179} - Requires / Hierarchical
                // {ns=2;i=18178} - Moves / Hierarchical
                // {ns=2;i=18183} - HasSlave / Hierachical
                // {ns=2;i=18180} - IsDrivenBy / Hierarchical
                // {ns=2;i=18182} - HasSafetyStates - Hierarchical
                // {ns=2;i=4002}  - Controls / Hierarchical
            }

        }
        static void AddChildIfNotExists<TColl>(NodeModel parent, IList<TColl> collection, TColl uaChildObject, ILogger logger)
        {
            if (uaChildObject is InstanceModelBase uaInstance)
            {
                uaInstance.Parent = parent;
                if (uaInstance.Parent != parent)
                {
                    logger.LogWarning($"{uaInstance} has more than one parent. Ignored parent: {parent}, using {uaInstance.Parent}");
                }
            }
            if (collection?.Contains(uaChildObject) == false)
            {
                collection.Add(uaChildObject);
            }
        }


        public static NodeModel Create(IOpcUaContext opcContext, NodeState node, object customState, out bool added)
        {
            NodeModel nodeModel;
            added = true;
            if (node is DataTypeState dataType)
            {
                nodeModel = Create<DataTypeModelFactoryOpc, DataTypeModel>(opcContext, dataType, customState);
            }
            else if (node is BaseVariableTypeState variableType)
            {
                nodeModel = Create<VariableTypeModelFactoryOpc, VariableTypeModel>(opcContext, variableType, customState);
            }
            else if (node is BaseObjectTypeState objectType)
            {
                if (objectType.IsAbstract && GetBaseTypes(opcContext, objectType).Any(n => n.NodeId == ObjectTypeIds.BaseInterfaceType))
                {
                    nodeModel = Create<InterfaceModelFactoryOpc, InterfaceModel>(opcContext, objectType, customState);
                }
                else
                {
                    nodeModel = Create<ObjectTypeModelFactoryOpc, ObjectTypeModel>(opcContext, objectType, customState);
                }
            }
            else if (node is BaseObjectState uaObject)
            {
                nodeModel = Create<ObjectModelFactoryOpc, ObjectModel>(opcContext, uaObject, customState);
            }
            else if (node is PropertyState property)
            {
                nodeModel = Create<PropertyModelFactoryOpc, PropertyModel>(opcContext, property, customState);
            }
            else if (node is BaseDataVariableState dataVariable)
            {
                nodeModel = Create<DataVariableModelFactoryOpc, DataVariableModel>(opcContext, dataVariable, customState);
            }
            else if (node is MethodState methodState)
            {
                nodeModel = Create<MethodModelFactoryOpc, MethodModel>(opcContext, methodState, customState);
            }
            else if (node is ReferenceTypeState referenceState)
            {
                nodeModel = Create<ReferenceTypeModelFactoryOpc, ReferenceTypeModel>(opcContext, referenceState, customState);
            }
            else
            {
                if (!(node is ViewState))
                {
                    nodeModel = Create<NodeModelFactoryOpc<T>, T>(opcContext, node, customState);
                }
                else
                {
                    // TODO support Views
                    nodeModel = null;
                }
                added = false;
            }
            return nodeModel;

        }

        public static List<BaseTypeState> GetBaseTypes(IOpcUaContext opcContext, BaseTypeState objectType)
        {
            var baseTypes = new List<BaseTypeState>();
            if (objectType != null)
            {
                baseTypes.Add(objectType);
            }
            var currentObjectType = objectType;
            while (currentObjectType?.SuperTypeId != null)
            {
                var objectSuperType = opcContext.GetNode(currentObjectType.SuperTypeId);
                if (objectSuperType is BaseTypeState)
                {
                    baseTypes.Add(objectSuperType as BaseTypeState);
                }
                else
                {
                    baseTypes.Add(new BaseObjectTypeState { NodeId = objectType.SuperTypeId, Description = "Unknown type: more base types may exist" });
                }
                currentObjectType = objectSuperType as BaseTypeState;
            }
            return baseTypes;
        }


        protected static TNodeModel Create<TNodeModelOpc, TNodeModel>(IOpcUaContext opcContext, NodeState opcNode, object customState) where TNodeModelOpc : NodeModelFactoryOpc<TNodeModel>, new() where TNodeModel : NodeModel, new()
        {
            var nodeId = opcContext.GetNodeIdWithUri(opcNode.NodeId, out var namespaceUri);
            var nodeModel = Create<TNodeModel>(opcContext, nodeId, namespaceUri, customState, out var created);
            var nodeModelOpc = new TNodeModelOpc { _model = nodeModel, Logger = opcContext.Logger };
            if (created)
            {
                nodeModelOpc.Initialize(opcContext, opcNode);
            }
            else
            {
                opcContext.Logger.LogTrace($"Using previously created node model {nodeModel} for {opcNode}");
            }
            return nodeModel;
        }

        public static TNodeModel Create<TNodeModel>(IOpcUaContext opcContext, string nodeId, string opcNamespace, object customState, out bool created) where TNodeModel : NodeModel, new()
        {
            created = false;
            opcContext.NamespaceUris.GetIndexOrAppend(opcNamespace); // Ensure the namespace is in the namespace table
            var nodeModelBase = opcContext.GetModelForNode(nodeId);
            var nodeModel = nodeModelBase as TNodeModel;
            if (nodeModel == null)
            {
                if (nodeModelBase != null)
                {
                    throw new Exception("Internal error - Type mismatch: NodeModel was previously created with a different, incompatible type");
                }
                nodeModel = new TNodeModel();
                nodeModel.Namespace = opcNamespace;
                nodeModel.NodeId = nodeId;
                nodeModel.CustomState = customState;
                created = true;

                var nodesetModel = opcContext.GetOrAddNodesetModel(nodeModel);
                if (!nodesetModel.AllNodesByNodeId.ContainsKey(nodeModel.NodeId))
                {
                    nodesetModel.AllNodesByNodeId.Add(nodeModel.NodeId, nodeModel);
                    if (nodeModel is InterfaceModel uaInterface)
                    {
                        nodesetModel.Interfaces.Add(uaInterface);
                    }
                    if (nodeModel is ObjectTypeModel objectType)
                    {
                        nodesetModel.ObjectTypes.Add(objectType);
                    }
                    else if (nodeModel is DataTypeModel dataType)
                    {
                        nodesetModel.DataTypes.Add(dataType);
                    }
                    else if (nodeModel is DataVariableModel dataVariable)
                    {
                        nodesetModel.DataVariables.Add(dataVariable);
                    }
                    else if (nodeModel is VariableTypeModel variableType)
                    {
                        nodesetModel.VariableTypes.Add(variableType);
                    }
                    else if (nodeModel is ObjectModel uaObject)
                    {
                        nodesetModel.Objects.Add(uaObject);
                    }
                    else if (nodeModel is PropertyModel property)
                    {
                        nodesetModel.Properties.Add(property);
                    }
                    else if (nodeModel is MethodModel method)
                    {
                        // TODO nodesetModel.Methods.Add(method);
                    }
                    else if (nodeModel is ReferenceTypeModel referenceType)
                    {
                        nodesetModel.ReferenceTypes.Add(referenceType);
                    }
                    else
                    {
                        throw new Exception($"Unexpected node model type {nodeModel.GetType().FullName} for node {nodeModel}");
                    }
                }
                else
                {
                    // Node already processed
                    opcContext.Logger.LogWarning($"Node {nodeModel} was already in the nodeset model.");
                }

            }
            if (customState != null && nodeModel != null && nodeModel.CustomState == null)
            {
                nodeModel.CustomState = customState;
            }
            return nodeModel;
        }

    }

    public static class NodeModelUtils
    {
        public static string GetNodeIdIdentifier(string nodeId)
        {
            return nodeId.Substring(nodeId.LastIndexOf(';') + 1);
        }

        public static string GetNamespaceFromNodeId(string nodeId)
        {
            var parsedNodeId = ExpandedNodeId.Parse(nodeId);
            var namespaceUri = parsedNodeId.NamespaceUri;
            return namespaceUri;
        }
    }

    public class InstanceModelFactoryOpc<TInstanceModel, TBaseTypeModel, TBaseTypeModelFactoryOpc> : NodeModelFactoryOpc<TInstanceModel>
        where TInstanceModel : InstanceModel<TBaseTypeModel>, new()
        where TBaseTypeModel : NodeModel, new()
        where TBaseTypeModelFactoryOpc : NodeModelFactoryOpc<TBaseTypeModel>, new()
    {
        protected override void Initialize(IOpcUaContext opcContext, NodeState opcNode)
        {
            base.Initialize(opcContext, opcNode);
            var uaInstance = opcNode as BaseInstanceState;
            var variableTypeDefinition = opcContext.GetNode(uaInstance.TypeDefinitionId);
            if (variableTypeDefinition != null) //is BaseTypeState)
            {
                var typeDefModel = NodeModelFactoryOpc.Create(opcContext, variableTypeDefinition, _model.CustomState, out _); // Create<TBaseTypeModelFactoryOpc, TBaseTypeModel>(opcContext, variableTypeDefinition, null);
                _model.TypeDefinition = typeDefModel as TBaseTypeModel;
                if (_model.TypeDefinition == null)
                {
                    throw new Exception($"Unexpected type definition {variableTypeDefinition}");
                }
            }

            if (uaInstance.ModellingRuleId != null)
            {
                var modelingRuleId = uaInstance.ModellingRuleId;
                var modelingRule = opcContext.GetNode(modelingRuleId);
                if (modelingRule == null)
                {
                    throw new Exception($"Unable to resolve modeling rule {modelingRuleId}: dependency on UA nodeset not declared?");
                }
                _model.ModelingRule = modelingRule.DisplayName.Text;
            }
            if (uaInstance.Parent != null)
            {
                var instanceParent = NodeModelFactoryOpc.Create(opcContext, uaInstance.Parent, null, out _);
                _model.Parent = instanceParent;
                if (_model.Parent != instanceParent)
                {
                    opcContext.Logger.LogWarning($"{_model} has more than one parent. Ignored parent: {instanceParent}, using {_model.Parent}.");
                }
            }
        }

    }

    public class ObjectModelFactoryOpc : InstanceModelFactoryOpc<ObjectModel, ObjectTypeModel, ObjectTypeModelFactoryOpc>
    {
    }

    public class BaseTypeModelFactoryOpc<TBaseTypeModel> : NodeModelFactoryOpc<TBaseTypeModel> where TBaseTypeModel : BaseTypeModel, new()
    {
        protected override void Initialize(IOpcUaContext opcContext, NodeState opcNode)
        {
            base.Initialize(opcContext, opcNode);
            var uaType = opcNode as BaseTypeState;

            if (uaType.SuperTypeId != null)
            {
                var superTypeNodeId = new ExpandedNodeId(uaType.SuperTypeId, opcContext.NamespaceUris.GetString(uaType.SuperTypeId.NamespaceIndex)).ToString();
                var superTypeModel = opcContext.GetModelForNode(superTypeNodeId) as BaseTypeModel;
                if (superTypeModel == null)
                {
                    var superTypeState = opcContext.GetNode(uaType.SuperTypeId) as BaseTypeState;
                    if (superTypeState != null)
                    {
                        superTypeModel = NodeModelFactoryOpc.Create(opcContext, superTypeState, this._model.CustomState, out _) as BaseTypeModel;
                        if (superTypeModel == null)
                        {
                            throw new Exception($"Invalid node {superTypeState} is not a Base Type");
                        }
                    }
                }
                _model.SuperType = superTypeModel;
                _model.RemoveInheritedAttributes(_model.SuperType);
                foreach (var uaInterface in _model.Interfaces)
                {
                    _model.RemoveInheritedAttributes(uaInterface);
                }
            }
            else
            {
                _model.SuperType = null;
            }
            _model.IsAbstract = uaType.IsAbstract;
        }

        private static BaseTypeModel Create(IOpcUaContext opcContext, BaseTypeState opcNode, object customState)
        {
            if (opcNode is BaseObjectTypeState)
            {
                return Create<ObjectTypeModelFactoryOpc, ObjectTypeModel>(opcContext, opcNode, customState);
            }
            else if (opcNode is BaseVariableTypeState)
            {
                return Create<VariableTypeModelFactoryOpc, VariableTypeModel>(opcContext, opcNode, customState);
            }
            else if (opcNode is DataTypeState)
            {
                return Create<DataTypeModelFactoryOpc, DataTypeModel>(opcContext, opcNode, customState);
            }
            throw new Exception("Unexpected/unsupported node type");
        }
    }

    public class ObjectTypeModelFactoryOpc<TTypeModel> : BaseTypeModelFactoryOpc<TTypeModel> where TTypeModel : ObjectTypeModel, new()
    {
    }

    public class ObjectTypeModelFactoryOpc : ObjectTypeModelFactoryOpc<ObjectTypeModel>
    {
    }

    public class InterfaceModelFactoryOpc : ObjectTypeModelFactoryOpc<InterfaceModel>
    {
    }

    public class VariableModelFactoryOpc<TVariableModel> : InstanceModelFactoryOpc<TVariableModel, VariableTypeModel, VariableTypeModelFactoryOpc>
        where TVariableModel : VariableModel, new()
    {
        protected override void Initialize(IOpcUaContext opcContext, NodeState opcNode)
        {
            base.Initialize(opcContext, opcNode);
            var variableNode = opcNode as BaseVariableState;

            InitializeDataTypeInfo(_model, opcContext, variableNode);
            if (variableNode.AccessLevel != 1) _model.AccessLevel = variableNode.AccessLevel;
            if (variableNode.UserAccessLevel != 1) _model.UserAccessLevel = variableNode.UserAccessLevel;
            if (variableNode.AccessRestrictions != 0) _model.AccessRestrictions = (ushort)variableNode.AccessRestrictions;
            if (variableNode.WriteMask != 0) _model.WriteMask = (uint)variableNode.WriteMask;
            if (variableNode.UserWriteMask != 0) _model.UserWriteMask = (uint)variableNode.UserWriteMask;
        }

        internal static void InitializeDataTypeInfo(VariableModel _model, IOpcUaContext opcContext, BaseVariableState variableNode)
        {
           VariableTypeModelFactoryOpc.InitializeDataTypeInfo(_model, opcContext, variableNode, variableNode.DataType, variableNode.ValueRank, variableNode.ArrayDimensions, variableNode.WrappedValue);
        }
    }

    public class DataVariableModelFactoryOpc : VariableModelFactoryOpc<DataVariableModel>
    {
    }

    public class PropertyModelFactoryOpc : VariableModelFactoryOpc<PropertyModel>
    {
    }

    public class MethodModelFactoryOpc : InstanceModelFactoryOpc<MethodModel, MethodModel, MethodModelFactoryOpc> // TODO determine if intermediate base classes of MethodState are worth exposing in the model
    {
        protected override void Initialize(IOpcUaContext opcContext, NodeState opcNode)
        {
            base.Initialize(opcContext, opcNode);
            if (opcNode is MethodState methodState)
            {
                // Already captured in NodeModel as properties: only need to parse out if we want to provide designer experience for methods
                //_model.MethodDeclarationId = opcContext.GetNodeIdWithUri(methodState.MethodDeclarationId, out var _);
                //_model.InputArguments = _model.Properties.Select(p => p as PropertyModel).ToList();
            }
            else
            {
                throw new Exception($"Unexpected node type for method {opcNode}");
            }
        }
    }

    public class VariableTypeModelFactoryOpc : BaseTypeModelFactoryOpc<VariableTypeModel>
    {
        protected override void Initialize(IOpcUaContext opcContext, NodeState opcNode)
        {
            base.Initialize(opcContext, opcNode);
            var variableTypeState = opcNode as BaseVariableTypeState;
            InitializeDataTypeInfo(_model, opcContext, variableTypeState);
            //variableTypeState.ValueRank
            //variableTypeState.Value
            //variableTypeState.ArrayDimensions
            //_model.
        }

        internal static void InitializeDataTypeInfo(VariableTypeModel model, IOpcUaContext opcContext, BaseVariableTypeState variableTypeNode)
        {
            VariableTypeModelFactoryOpc.InitializeDataTypeInfo(model, opcContext, variableTypeNode, variableTypeNode.DataType, variableTypeNode.ValueRank, variableTypeNode.ArrayDimensions, variableTypeNode.WrappedValue);
        }

        internal static void InitializeDataTypeInfo(IVariableDataTypeInfo model, IOpcUaContext opcContext, NodeState variableNode, NodeId dataTypeNodeId, int valueRank, ReadOnlyList<uint> arrayDimensions, Variant wrappedValue)
        {
            var dataType = opcContext.GetNode(dataTypeNodeId);
            if (dataType is DataTypeState)
            {
                model.DataType = Create<DataTypeModelFactoryOpc, DataTypeModel>(opcContext, dataType as DataTypeState, null);
            }
            else
            {
                if (dataType == null)
                {
                    throw new Exception($"{variableNode.GetType()} {variableNode}: did not find data type {dataTypeNodeId} (Namespace {opcContext.NamespaceUris.GetString(dataTypeNodeId.NamespaceIndex)}).");
                }
                else
                {
                    throw new Exception($"{variableNode.GetType()} {variableNode}: Unexpected node state {dataTypeNodeId}/{dataType?.GetType().FullName}.");
                }
            }
            if (valueRank != -1)
            {
                model.ValueRank = valueRank;
                if (arrayDimensions != null && arrayDimensions.Any())
                {
                    model.ArrayDimensions = String.Join(",", arrayDimensions);
                }
            }
            if (wrappedValue.Value != null)
            {
                var encodedValue = opcContext.JsonEncodeVariant(wrappedValue);
                model.Value = encodedValue;
            }
        }

    }
    public class DataTypeModelFactoryOpc : BaseTypeModelFactoryOpc<DataTypeModel>
    {
        protected override void Initialize(IOpcUaContext opcContext, NodeState opcNode)
        {
            base.Initialize(opcContext, opcNode);

            var dataTypeState = opcNode as DataTypeState;
            if (dataTypeState.DataTypeDefinition?.Body != null)
            {
                var sd = dataTypeState.DataTypeDefinition.Body as StructureDefinition;
                if (sd != null)
                {
                    _model.StructureFields = new List<DataTypeModel.StructureField>();
                    foreach (var field in sd.Fields)
                    {
                        var dataType = opcContext.GetNode(field.DataType);
                        if (dataType is DataTypeState)
                        {
                            var dataTypeModel = Create<DataTypeModelFactoryOpc, DataTypeModel>(opcContext, dataType as DataTypeState, null);
                            if (dataTypeModel == null)
                            {
                                throw new Exception($"Unable to resolve data type {dataType.DisplayName}");
                            }
                            var structureField = new DataTypeModel.StructureField
                            {
                                Name = field.Name,
                                DataType = dataTypeModel,
                                ValueRank = field.ValueRank != -1 ? field.ValueRank : null,
                                ArrayDimensions = field.ArrayDimensions != null && field.ArrayDimensions.Any() ? String.Join(",", field.ArrayDimensions) : null,
                                MaxStringLength = field.MaxStringLength != 0 ? field.MaxStringLength : null,
                                Description = field.Description.ToModel(),
                                IsOptional = field.IsOptional,
                            };
                            _model.StructureFields.Add(structureField);
                        }
                        else
                        {
                            throw new Exception($"Unexpected node state {dataType.GetType().FullName} for data type {field.DataType}");
                        }
                    }
                }
                else
                {
                    var enumFields = dataTypeState.DataTypeDefinition.Body as EnumDefinition;
                    if (enumFields != null)
                    {
                        _model.IsOptionSet = enumFields.IsOptionSet;
                        _model.EnumFields = new List<DataTypeModel.UaEnumField>();
                        foreach (var field in enumFields.Fields)
                        {
                            var enumField = new DataTypeModel.UaEnumField
                            {
                                Name = field.Name,
                                DisplayName = field.DisplayName.ToModel(),
                                Value = field.Value,
                                Description = field.Description.ToModel(),
                            };
                            _model.EnumFields.Add(enumField);
                        }
                    }
                    else
                    {
                        throw new Exception($"Unknown data type definition in {dataTypeState}");
                    }
                }
            }
        }
    }

    public class ReferenceTypeModelFactoryOpc : BaseTypeModelFactoryOpc<ReferenceTypeModel>
    {
        protected override void Initialize(IOpcUaContext opcContext, NodeState opcNode)
        {
            base.Initialize(opcContext, opcNode);
            var referenceTypeState = opcNode as ReferenceTypeState;

            _model.InverseName = referenceTypeState.InverseName?.ToModel();
            _model.Symmetric = referenceTypeState.Symmetric;
        }
    }
}