using Opc.Ua;
using ua = Opc.Ua;
using uaExport = Opc.Ua.Export;

using System;
using System.Collections.Generic;
using System.Linq;

using CESMII.OpcUa.NodeSetModel.Opc.Extensions;
using Opc.Ua.Export;
using CESMII.OpcUa.NodeSetModel.Factory.Opc;

namespace CESMII.OpcUa.NodeSetModel.Export.Opc
{
    public class NodeModelExportOpc : NodeModelExportOpc<NodeModel>
    {

    }
    public class NodeModelExportOpc<T> where T : NodeModel, new()
    {
        protected T _model;
        private HashSet<string> _nodeIdsUsed;
        public static (UANode ExportedNode, List<UANode> AdditionalNodes) GetUANode(NodeModel model, NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            return GetUANode(model, namespaces, aliases, null);
        }
        public static (UANode ExportedNode, List<UANode> AdditionalNodes) GetUANode(NodeModel model, NamespaceTable namespaces, Dictionary<string, string> aliases, HashSet<string> nodeIdsUsed)
        {
            if (model is InterfaceModel uaInterface)
            {
                return new InterfaceModelExportOpc { _model = uaInterface, _nodeIdsUsed = nodeIdsUsed }.GetUANode<UANode>(namespaces, aliases);
            }
            else if (model is ObjectTypeModel objectType)
            {
                return new ObjectTypeModelExportOpc { _model = objectType, _nodeIdsUsed = nodeIdsUsed }.GetUANode<UANode>(namespaces, aliases);
            }
            else if (model is VariableTypeModel variableType)
            {
                return new VariableTypeModelExportOpc { _model = variableType, _nodeIdsUsed = nodeIdsUsed }.GetUANode<UANode>(namespaces, aliases);
            }
            else if (model is DataTypeModel dataType)
            {
                return new DataTypeModelExportOpc { _model = dataType, _nodeIdsUsed = nodeIdsUsed }.GetUANode<UANode>(namespaces, aliases);
            }
            else if (model is DataVariableModel dataVariable)
            {
                return new DataVariableModelExportOpc { _model = dataVariable, _nodeIdsUsed = nodeIdsUsed }.GetUANode<UANode>(namespaces, aliases);
            }
            else if (model is PropertyModel property)
            {
                return new PropertyModelExportOpc { _model = property, _nodeIdsUsed = nodeIdsUsed }.GetUANode<UANode>(namespaces, aliases);
            }
            else if (model is ObjectModel uaObject)
            {
                return new ObjectModelExportOpc { _model = uaObject, _nodeIdsUsed = nodeIdsUsed }.GetUANode<UANode>(namespaces, aliases);
            }
            else if (model is MethodModel uaMethod)
            {
                return new MethodModelExportOpc { _model = uaMethod, _nodeIdsUsed = nodeIdsUsed }.GetUANode<UANode>(namespaces, aliases);
            }
            else if (model is ReferenceTypeModel referenceType)
            {
                return new ReferenceTypeModelExportOpc { _model = referenceType, _nodeIdsUsed = nodeIdsUsed }.GetUANode<UANode>(namespaces, aliases);
            }
            throw new Exception($"Unexpected node model {model.GetType()}");
        }

        public virtual (TUANode ExportedNode, List<UANode> AdditionalNodes) GetUANode<TUANode>(NamespaceTable namespaces, Dictionary<string, string> aliases) where TUANode : UANode, new()
        {
            var node = new TUANode
            {
                Description = _model.Description?.ToExport()?.ToArray(),
                BrowseName = GetBrowseNameForExport(namespaces),
                SymbolicName = _model.SymbolicName,
                DisplayName = _model.DisplayName?.ToExport()?.ToArray(),
                NodeId = GetNodeIdForExport(_model.NodeId, namespaces, aliases),
                Documentation = _model.Documentation,
                Category = _model.Categories?.ToArray(),
            };
            if (Enum.TryParse<ReleaseStatus>(_model.ReleaseStatus, out var releaseStatus))
            {
                node.ReleaseStatus = releaseStatus;
            }

            var references = new List<Reference>();
            foreach (var property in _model.Properties)
            {
                namespaces.GetIndexOrAppend(property.Namespace);
                references.Add(new Reference
                {
                    ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasProperty.ToString(), namespaces, aliases),
                    Value = GetNodeIdForExport(property.NodeId, namespaces, aliases),
                });
            }
            foreach (var uaObject in this._model.Objects)
            {
                namespaces.GetIndexOrAppend(uaObject.Namespace);
                var referenceTypeId = ReferenceTypeIds.HasComponent.ToString();
                var otherReferences = _model.OtherReferencedNodes.Where(nr => nr.Node == uaObject).ToList();
                if (otherReferences.Any())
                {
                    // TODO Verify the other referenceType is derived from HasComponent
                    referenceTypeId = otherReferences[0].Reference;
                }
                references.Add(new Reference
                {
                    ReferenceType = GetNodeIdForExport(referenceTypeId, namespaces, aliases),
                    Value = GetNodeIdForExport(uaObject.NodeId, namespaces, aliases),
                });
            }
            foreach (var nodeRef in this._model.OtherReferencedNodes)
            {
                namespaces.GetIndexOrAppend(nodeRef.Node.Namespace);
                namespaces.GetIndexOrAppend(NodeModelUtils.GetNamespaceFromNodeId(nodeRef.Reference));

                references.Add(new Reference
                {
                    ReferenceType = GetNodeIdForExport(nodeRef.Reference, namespaces, aliases),
                    Value = GetNodeIdForExport(nodeRef.Node.NodeId, namespaces, aliases),
                });
            }
            foreach (var inverseNodeRef in this._model.OtherReferencingNodes)
            {
                namespaces.GetIndexOrAppend(inverseNodeRef.Node.Namespace);
                namespaces.GetIndexOrAppend(NodeModelUtils.GetNamespaceFromNodeId(inverseNodeRef.Reference));

                var inverseRef = new Reference
                {
                    ReferenceType = GetNodeIdForExport(inverseNodeRef.Reference, namespaces, aliases),
                    Value = GetNodeIdForExport(inverseNodeRef.Node.NodeId, namespaces, aliases),
                    IsForward = false,
                };
                if (!references.Any(r => r.IsForward == false && r.ReferenceType == inverseRef.ReferenceType && r.Value == inverseRef.Value))
                {
                    // TODO ensure we pick the most derived reference type
                    references.Add(inverseRef);
                }
            }
            foreach (var uaInterface in this._model.Interfaces)
            {
                namespaces.GetIndexOrAppend(uaInterface.Namespace);
                references.Add(new Reference
                {
                    ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasInterface.ToString(), namespaces, aliases),
                    Value = GetNodeIdForExport(uaInterface.NodeId, namespaces, aliases),
                });

            }
            foreach (var method in this._model.Methods)
            {
                namespaces.GetIndexOrAppend(method.Namespace);
                var referenceTypeId = ReferenceTypeIds.HasComponent.ToString();
                var otherReferences = _model.OtherReferencedNodes.Where(nr => nr.Node == method).ToList();
                if (otherReferences.Any())
                {
                    // TODO Verify the other referenceType is derived from HasComponent
                    referenceTypeId = otherReferences[0].Reference;
                }
                references.Add(new Reference
                {
                    ReferenceType = GetNodeIdForExport(referenceTypeId, namespaces, aliases),
                    Value = GetNodeIdForExport(method.NodeId, namespaces, aliases),
                });
            }
            foreach (var uaEvent in this._model.Events)
            {
                namespaces.GetIndexOrAppend(uaEvent.Namespace);
                var referenceTypeId = ReferenceTypeIds.GeneratesEvent.ToString();
                var otherReferences = _model.OtherReferencedNodes.Where(nr => nr.Node == uaEvent).ToList();
                if (otherReferences.Any())
                {
                    // TODO Verify the other referenceType is derived from GeneratesEvent
                    referenceTypeId = otherReferences[0].Reference;
                }
                references.Add(new Reference 
                { 
                    ReferenceType = GetNodeIdForExport(referenceTypeId, namespaces, aliases),
                    Value = GetNodeIdForExport(uaEvent.NodeId, namespaces, aliases),
                });
            }
            foreach (var variable in this._model.DataVariables)
            {
                namespaces.GetIndexOrAppend(variable.Namespace);
                var referenceTypeId = ReferenceTypeIds.HasComponent.ToString();
                var otherReferences = _model.OtherReferencedNodes.Where(nr => nr.Node == variable).ToList();
                if (otherReferences.Any())
                {
                    // TODO Verify the other referenceType is derived from HasComponent
                    referenceTypeId = otherReferences[0].Reference;
                }
                references.Add(new Reference
                {
                    ReferenceType = GetNodeIdForExport(referenceTypeId, namespaces, aliases),
                    Value = GetNodeIdForExport(variable.NodeId, namespaces, aliases),
                });
            }
            if (references.Any())
            {
                node.References = references.ToArray();
            }
            return (node, null);
        }

        protected string GetNodeIdForExport(string nodeId, NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            if (nodeId == null) return null;
            var expandedNodeId = ExpandedNodeId.Parse(nodeId, namespaces);
            _nodeIdsUsed?.Add(expandedNodeId.ToString());
            if (aliases.TryGetValue(expandedNodeId.ToString(), out var alias))
            {
                return alias;
            }
            return ExpandedNodeId.ToNodeId(expandedNodeId, namespaces).ToString();
        }

        protected string GetBrowseNameForExport(NamespaceTable namespaces)
        {
            return GetQualifiedNameForExport(_model.BrowseName, _model.Namespace, _model.DisplayName, namespaces);
        }

        protected static string GetQualifiedNameForExport(string qualifiedName, string fallbackNamespace, List<NodeModel.LocalizedText> displayName, NamespaceTable namespaces)
        {
            string qualifiedNameForExport;
            if (qualifiedName != null)
            {
                var parts = qualifiedName.Split(new[] { ';' }, 2);
                if (parts.Length >= 2)
                {
                    qualifiedNameForExport = new QualifiedName(parts[1], namespaces.GetIndexOrAppend(parts[0])).ToString();
                }
                else if (parts.Length == 1)
                {
                    qualifiedNameForExport = parts[0];
                }
                else
                {
                    qualifiedNameForExport = "";
                }
            }
            else
            {
                qualifiedNameForExport = new QualifiedName(displayName?.FirstOrDefault()?.Text, namespaces.GetIndexOrAppend(fallbackNamespace)).ToString();
            }

            return qualifiedNameForExport;
        }

        public override string ToString()
        {
            return _model?.ToString();
        }
    }

    public abstract class InstanceModelExportOpc<TInstanceModel, TBaseTypeModel> : NodeModelExportOpc<TInstanceModel> 
        where TInstanceModel : InstanceModel<TBaseTypeModel>, new() 
        where TBaseTypeModel : NodeModel, new()
    {

        protected abstract (bool IsChild, NodeId ReferenceTypeId) ReferenceFromParent(NodeModel parent);

        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result = base.GetUANode<T>(namespaces, aliases);
            var instance = result.ExportedNode as UAInstance;
            if (instance == null)
            {
                throw new Exception("Internal error: wrong generic type requested");
            }
            var references = instance.References?.ToList() ?? new List<Reference>();

            if (!string.IsNullOrEmpty(_model.Parent?.NodeId))
            {
                instance.ParentNodeId = GetNodeIdForExport(_model.Parent.NodeId, namespaces, aliases);
            }
        
            string typeDefinitionNodeIdForExport;
            if (_model.TypeDefinition != null)
            {
                namespaces.GetIndexOrAppend(_model.TypeDefinition.Namespace);
                typeDefinitionNodeIdForExport = GetNodeIdForExport(_model.TypeDefinition.NodeId, namespaces, aliases);
            }
            else
            {
                NodeId typeDefinitionNodeId = null;
                if (_model is PropertyModel)
                {
                    typeDefinitionNodeId = VariableTypeIds.PropertyType;
                }
                else if (_model is DataVariableModel)
                {
                    typeDefinitionNodeId = VariableTypeIds.BaseDataVariableType;
                }
                else if (_model is VariableModel)
                {
                    typeDefinitionNodeId = VariableTypeIds.BaseVariableType;
                }
                else if (_model is ObjectModel)
                {
                    typeDefinitionNodeId = ObjectTypeIds.BaseObjectType;
                }

                typeDefinitionNodeIdForExport = GetNodeIdForExport(typeDefinitionNodeId?.ToString(), namespaces, aliases);
            }
            if (typeDefinitionNodeIdForExport != null && !(_model.TypeDefinition is MethodModel))
            {
                var reference = new Reference
                {
                    ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasTypeDefinition.ToString(), namespaces, aliases),
                    Value = typeDefinitionNodeIdForExport,
                };
                references.Add(reference);
            }

            AddModelingRuleReference(_model.ModelingRule, references, namespaces, aliases);

            if (references.Any())
            {
                instance.References = references.Distinct(new ReferenceComparer()).ToArray();
            }

            return (instance as T, result.AdditionalNodes);
        }

        protected List<Reference> AddModelingRuleReference(string modelingRule, List<Reference> references, NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            if (modelingRule != null)
            {
                var modelingRuleId = modelingRule switch
                {
                    "Optional" => ObjectIds.ModellingRule_Optional,
                    "Mandatory" => ObjectIds.ModellingRule_Mandatory,
                    "MandatoryPlaceholder" => ObjectIds.ModellingRule_MandatoryPlaceholder,
                    "OptionalPlaceholder" => ObjectIds.ModellingRule_OptionalPlaceholder,
                    "ExposesItsArray" => ObjectIds.ModellingRule_ExposesItsArray,
                    _ => null,
                };
                if (modelingRuleId != null)
                {
                    references.Add(new Reference
                    {
                        ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasModellingRule.ToString(), namespaces, aliases),
                        Value = GetNodeIdForExport(modelingRuleId.ToString(), namespaces, aliases),
                    });
                }
            }
            return references;
        }

        protected void AddOtherReferences(List<Reference> references, string parentNodeId, NodeId referenceTypeId, bool bIsChild, NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            if (!string.IsNullOrEmpty(_model.Parent?.NodeId))
            {
                bool bAdded = false;
                foreach (var referencingNode in _model.Parent.OtherReferencedNodes.Where(cr => cr.Node == _model))
                {
                    var referenceType = GetNodeIdForExport(referencingNode.Reference, namespaces, aliases);
                    if (!references.Any(r => r.IsForward == false && r.Value == parentNodeId && r.ReferenceType != referenceType))
                    {
                        references.Add(new Reference { IsForward = false, ReferenceType = referenceType, Value = parentNodeId });
                    }
                    else
                    {
                        // TODO ensure we pick the most derived reference type
                    }
                    bAdded = true;
                }
                if (bIsChild || !bAdded)//_model.Parent.Objects.Contains(_model))
                {
                    var referenceType = GetNodeIdForExport(referenceTypeId.ToString(), namespaces, aliases);
                    if (!references.Any(r => r.IsForward == false && r.Value == parentNodeId && r.ReferenceType != referenceType))
                    {
                        references.Add(new Reference { IsForward = false, ReferenceType = referenceType, Value = parentNodeId });
                    }
                    else
                    {
                        // TODO ensure we pick the most derived reference type
                    }
                }
            }
        }



    }

    public class ObjectModelExportOpc : InstanceModelExportOpc<ObjectModel, ObjectTypeModel>
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result = base.GetUANode<UAObject>(namespaces, aliases);
            var uaObject = result.ExportedNode;

            var references = uaObject.References?.ToList() ?? new List<Reference>();

            if (uaObject.ParentNodeId != null)
            {
                AddOtherReferences(references, uaObject.ParentNodeId, ReferenceTypeIds.HasComponent, _model.Parent.Objects.Contains(_model), namespaces, aliases);
            }
            if (references.Any())
            {
                uaObject.References = references.Distinct(new ReferenceComparer()).ToArray();
            }

            return (uaObject as T, result.AdditionalNodes);
        }

        protected override (bool IsChild, NodeId ReferenceTypeId) ReferenceFromParent(NodeModel parent)
        {
            return (parent.Objects.Contains(_model), ReferenceTypeIds.HasComponent);
        }
    }

    public class BaseTypeModelExportOpc<TBaseTypeModel> : NodeModelExportOpc<TBaseTypeModel> where TBaseTypeModel : BaseTypeModel, new()
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result = base.GetUANode<T>(namespaces, aliases);
            var objectType = result.ExportedNode;
            foreach (var subType in this._model.SubTypes)
            {
                namespaces.GetIndexOrAppend(subType.Namespace);
            }
            if (_model.SuperType != null)
            {
                namespaces.GetIndexOrAppend(_model.SuperType.Namespace);
                var superTypeReference = new Reference
                {
                    ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasSubtype.ToString(), namespaces, aliases),
                    IsForward = false,
                    Value = GetNodeIdForExport(_model.SuperType.NodeId, namespaces, aliases),
                };
                if (objectType.References == null)
                {
                    objectType.References = new Reference[] { superTypeReference };
                }
                else
                {
                    var referenceList = new List<Reference>(objectType.References);
                    referenceList.Add(superTypeReference);
                    objectType.References = referenceList.ToArray();
                }
            }
            if (objectType is UAType uaType)
            {
                uaType.IsAbstract = _model.IsAbstract;
            }
            else
            {
                throw new Exception("Must be UAType or derived");
            }
            return (objectType, result.AdditionalNodes);
        }
    }

    public class ObjectTypeModelExportOpc<TTypeModel> : BaseTypeModelExportOpc<TTypeModel> where TTypeModel : BaseTypeModel, new()
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result = base.GetUANode<UAObjectType>(namespaces, aliases);
            var objectType = result.ExportedNode;
            return (objectType as T, result.AdditionalNodes);
        }
    }

    public class ObjectTypeModelExportOpc : ObjectTypeModelExportOpc<ObjectTypeModel>
    {
    }

    public class InterfaceModelExportOpc : ObjectTypeModelExportOpc<InterfaceModel>
    {
    }

    public abstract class VariableModelExportOpc<TVariableModel> : InstanceModelExportOpc<TVariableModel, VariableTypeModel>
        where TVariableModel : VariableModel, new()
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            if (_model.DataType?.Namespace != null)
            {
                namespaces.GetIndexOrAppend(_model.DataType.Namespace);
            }
            else
            {
                // TODO: should not happen - remove once coded
            }
            var result = base.GetUANode<UAVariable>(namespaces, aliases);
            var dataVariable = result.ExportedNode;

            var references = dataVariable.References?.ToList() ?? new List<Reference>();

            if (!_model.Properties.Concat(_model.DataVariables).Any(p => p.NodeId == _model.EngUnitNodeId) && (_model.EngineeringUnit != null || !string.IsNullOrEmpty(_model.EngUnitNodeId)))
            {
                // Add engineering unit property
                if (result.AdditionalNodes == null)
                {
                    result.AdditionalNodes = new List<UANode>();
                }

                var engUnitProp = new UAVariable
                {
                    NodeId = GetNodeIdForExport(!String.IsNullOrEmpty(_model.EngUnitNodeId) ? _model.EngUnitNodeId : $"nsu={_model.Namespace};g={Guid.NewGuid()}", namespaces, aliases),
                    BrowseName = BrowseNames.EngineeringUnits, // TODO preserve non-standard browsenames (detected based on data type)
                    DisplayName = new uaExport.LocalizedText[] { new uaExport.LocalizedText { Value = BrowseNames.EngineeringUnits } },
                    ParentNodeId = dataVariable.NodeId,
                    DataType = DataTypeIds.EUInformation.ToString(),
                    References = new Reference[]
                    {
                         new Reference { 
                             ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasTypeDefinition.ToString(), namespaces, aliases),
                             Value = GetNodeIdForExport(VariableTypeIds.PropertyType.ToString(), namespaces, aliases)
                         },
                         new Reference {
                             ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasProperty.ToString(), namespaces, aliases),
                             IsForward = false, 
                             Value = GetNodeIdForExport(dataVariable.NodeId, namespaces, aliases),
                         },
                    },
                    AccessLevel = _model.EngUnitAccessLevel ?? 1,
                    // UserAccessLevel: deprecated: never emit
                };
                if (_model.EngUnitModelingRule != null)
                {
                    engUnitProp.References = AddModelingRuleReference(_model.EngUnitModelingRule, engUnitProp.References.ToList(), namespaces, aliases).ToArray();
                }
                if (_model.EngineeringUnit != null)
                {
                    EUInformation engUnits = NodeModelOpcExtensions.GetEUInformation(_model.EngineeringUnit);
                    var euXmlElement = GetExtensionObjectAsXML(engUnits);
                    engUnitProp.Value = euXmlElement;
                }
                result.AdditionalNodes.Add(engUnitProp);
                references.Add(new Reference
                {
                    ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasProperty.ToString(), namespaces, aliases),
                    Value = engUnitProp.NodeId,
                });
            }
            if (!_model.Properties.Concat(_model.DataVariables).Any(p => p.NodeId == _model.EURangeNodeId) && (!string.IsNullOrEmpty(_model.EURangeNodeId) || (_model.MinValue.HasValue && _model.MaxValue.HasValue && _model.MinValue != _model.MaxValue)))
            {
                // Add EURange property
                if (result.AdditionalNodes == null)
                {
                    result.AdditionalNodes = new List<UANode>();
                }

                System.Xml.XmlElement xmlElem = null;

                if (_model.MinValue.HasValue && _model.MaxValue.HasValue)
                {
                    var range = new ua.Range
                    {
                        Low = _model.MinValue.Value,
                        High = _model.MaxValue.Value,
                    };
                    xmlElem = GetExtensionObjectAsXML(range);
                }
                var euRangeProp = new UAVariable
                {
                    NodeId = GetNodeIdForExport(!String.IsNullOrEmpty(_model.EURangeNodeId) ? _model.EURangeNodeId : $"nsu={_model.Namespace};g={Guid.NewGuid()}", namespaces, aliases),
                    BrowseName = BrowseNames.EURange,
                    DisplayName = new uaExport.LocalizedText[] { new uaExport.LocalizedText { Value = BrowseNames.EURange } },
                    ParentNodeId = dataVariable.NodeId,
                    DataType = GetNodeIdForExport(DataTypeIds.Range.ToString(), namespaces, aliases),
                    References = new[] {
                        new Reference {
                            ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasTypeDefinition.ToString(), namespaces, aliases),
                            Value = GetNodeIdForExport(VariableTypeIds.PropertyType.ToString(), namespaces, aliases),
                        },
                        new Reference
                        {
                            ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasProperty.ToString(), namespaces, aliases),
                            IsForward = false,
                            Value = GetNodeIdForExport(dataVariable.NodeId, namespaces, aliases),
                        },
                    },
                    Value = xmlElem,
                    AccessLevel = _model.EURangeAccessLevel ?? 1,
                    // deprecated: UserAccessLevel = _model.EURangeUserAccessLevel ?? 1,
                };

                if (_model.EURangeModelingRule != null)
                {
                    euRangeProp.References = AddModelingRuleReference(_model.EURangeModelingRule, euRangeProp.References?.ToList() ?? new List<Reference>(), namespaces, aliases).ToArray();
                }

                result.AdditionalNodes.Add(euRangeProp);
                references.Add(new Reference
                {
                    ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasProperty.ToString(), namespaces, aliases),
                    Value = GetNodeIdForExport(euRangeProp.NodeId, namespaces, aliases),
                });
            }
            if (_model.DataType != null)
            {
                dataVariable.DataType = GetNodeIdForExport(_model.DataType.NodeId, namespaces, aliases);
            }
            dataVariable.ValueRank = _model.ValueRank??-1;
            dataVariable.ArrayDimensions = _model.ArrayDimensions;

            if (!string.IsNullOrEmpty(_model.Parent?.NodeId))
            {
                dataVariable.ParentNodeId = GetNodeIdForExport(_model.Parent.NodeId, namespaces, aliases);
                if (!references.Any(r => r.Value == dataVariable.ParentNodeId && r.IsForward == false))
                {
                    var reference = new Reference
                    {
                        IsForward = false,
                        ReferenceType = GetNodeIdForExport((_model.Parent.Properties.Contains(_model) ? ReferenceTypeIds.HasProperty : ReferenceTypeIds.HasComponent).ToString(), namespaces, aliases),
                        Value = dataVariable.ParentNodeId
                    };
                    references.Add(reference);
                }
                else
                {
                    // TODO ensure we pick the most derived reference type
                }
            }
            if (_model.Value != null)
            {
                using (var decoder = new JsonDecoder(_model.Value, ServiceMessageContext.GlobalContext))
                {
                    var value = decoder.ReadVariant("Value");
                    var xml = GetVariantAsXML(value);
                    dataVariable.Value = xml;
                }
            }

            dataVariable.AccessLevel = _model.AccessLevel ?? 1;
            // deprecated: dataVariable.UserAccessLevel = _model.UserAccessLevel ?? 1;
            dataVariable.AccessRestrictions = (byte) (_model.AccessRestrictions ?? 0);
            dataVariable.UserWriteMask = _model.UserWriteMask ?? 0;
            dataVariable.WriteMask = _model.WriteMask ?? 0;
            dataVariable.MinimumSamplingInterval = _model.MinimumSamplingInterval ?? 0;

            if (references?.Any() == true)
            {
                dataVariable.References = references.ToArray();
            }
            return (dataVariable as T, result.AdditionalNodes);
        }

        private static System.Xml.XmlElement GetExtensionObjectAsXML(object extensionBody)
        {
            var extension = new ExtensionObject(extensionBody);
            var context = new ServiceMessageContext();
            var ms = new System.IO.MemoryStream();
            using (var xmlWriter = new System.Xml.XmlTextWriter(ms, System.Text.Encoding.UTF8))
            {
                xmlWriter.WriteStartDocument();

                using (var encoder = new XmlEncoder(new System.Xml.XmlQualifiedName("uax:ExtensionObject", null), xmlWriter, context))
                {
                    encoder.WriteExtensionObject(null, extension);
                    xmlWriter.WriteEndDocument();
                    xmlWriter.Flush();
                }
            }
            var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml.Substring(1));
            var xmlElem = doc.DocumentElement;
            return xmlElem;
        }
        internal static System.Xml.XmlElement GetVariantAsXML(Variant value)
        {
            var context = new ServiceMessageContext();
            var ms = new System.IO.MemoryStream();
            using (var xmlWriter = new System.Xml.XmlTextWriter(ms, System.Text.Encoding.UTF8))
            {
                xmlWriter.WriteStartDocument();
                using (var encoder = new XmlEncoder(new System.Xml.XmlQualifiedName("myRoot"/*, "http://opcfoundation.org/UA/2008/02/Types.xsd"*/), xmlWriter, context))
                {
                    encoder.WriteVariant("value", value);
                    xmlWriter.WriteEndDocument();
                    xmlWriter.Flush();
                }
            }
            var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            var doc = new System.Xml.XmlDocument();

            doc.LoadXml(xml.Substring(1));
            var xmlElem = doc.DocumentElement;
            var xmlValue = xmlElem.FirstChild?.FirstChild?.FirstChild as System.Xml.XmlElement;
            return xmlValue;
        }
    }

    public class DataVariableModelExportOpc : VariableModelExportOpc<DataVariableModel>
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result = base.GetUANode<T>(namespaces, aliases);
            var dataVariable = result.ExportedNode;
            //var references = dataVariable.References?.ToList() ?? new List<Reference>();
            //references.Add(new Reference { ReferenceType = "HasTypeDefinition", Value = GetNodeIdForExport(VariableTypeIds.BaseDataVariableType.ToString(), namespaces, aliases), });
            //dataVariable.References = references.ToArray();
            return (dataVariable, result.AdditionalNodes);
        }

        protected override (bool IsChild, NodeId ReferenceTypeId) ReferenceFromParent(NodeModel parent)
        {
            return (parent.DataVariables.Contains(_model), ReferenceTypeIds.HasComponent);
        }
    }

    public class PropertyModelExportOpc : VariableModelExportOpc<PropertyModel>
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result =  base.GetUANode<T>(namespaces, aliases);
            var property = result.ExportedNode;
            var references = property.References?.ToList() ?? new List<Reference>();
            var propertyTypeNodeId = GetNodeIdForExport(VariableTypeIds.PropertyType.ToString(), namespaces, aliases);
            if (references?.Any(r => r.Value == propertyTypeNodeId) == false)
            {
                references.Add(new Reference { ReferenceType = GetNodeIdForExport(ReferenceTypeIds.HasTypeDefinition.ToString(), namespaces, aliases), Value = propertyTypeNodeId, });
            }
            property.References = references.ToArray();
            return (property, result.AdditionalNodes);
        }
        protected override (bool IsChild, NodeId ReferenceTypeId) ReferenceFromParent(NodeModel parent)
        {
            return (false, ReferenceTypeIds.HasProperty);
        }
    }

    public class MethodModelExportOpc : InstanceModelExportOpc<MethodModel, MethodModel>
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result = base.GetUANode<UAMethod>(namespaces, aliases);
            var method = result.ExportedNode;
            method.MethodDeclarationId = GetNodeIdForExport(_model.TypeDefinition?.NodeId, namespaces, aliases);
            // method.ArgumentDescription = null; // TODO - not commonly used
            if (method.ParentNodeId != null)
            {
                var references = method.References?.ToList() ?? new List<Reference>();
                AddOtherReferences(references, method.ParentNodeId, ReferenceTypeIds.HasComponent, _model.Parent.Methods.Contains(_model), namespaces, aliases);
                method.References = references.Distinct(new ReferenceComparer()).ToArray();
            }
            return (method as T, result.AdditionalNodes);
        }
        protected override (bool IsChild, NodeId ReferenceTypeId) ReferenceFromParent(NodeModel parent)
        {
            return (parent.Methods.Contains(_model), ReferenceTypeIds.HasComponent);
        }
    }

    public class VariableTypeModelExportOpc : BaseTypeModelExportOpc<VariableTypeModel>
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result = base.GetUANode<UAVariableType>(namespaces, aliases);
            var variableType = result.ExportedNode;
            variableType.IsAbstract = _model.IsAbstract;
            if (_model.DataType != null)
            {
                variableType.DataType = GetNodeIdForExport(_model.DataType.NodeId, namespaces, aliases);
            }
            if (_model.ValueRank != null)
            {
                variableType.ValueRank = _model.ValueRank.Value;
            }
            variableType.ArrayDimensions = _model.ArrayDimensions;
            if (_model.Value != null)
            {
                using (var decoder = new JsonDecoder(_model.Value, ServiceMessageContext.GlobalContext))
                {
                    var value = decoder.ReadVariant("Value");
                    var xml = VariableModelExportOpc<VariableModel>.GetVariantAsXML(value);
                    variableType.Value = xml;
                }
            }
            return (variableType as T, result.AdditionalNodes);
        }
    }
    public class DataTypeModelExportOpc : BaseTypeModelExportOpc<DataTypeModel>
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result = base.GetUANode<UADataType>(namespaces, aliases);
            var dataType = result.ExportedNode;
            if (_model.StructureFields?.Any() == true)
            {
                var fields = new List<DataTypeField>();
                foreach(var field in _model.StructureFields.OrderBy(f => f.FieldOrder))
                {
                    var uaField = new DataTypeField
                    {
                        Name = field.Name,
                        DataType = GetNodeIdForExport(field.DataType.NodeId, namespaces, aliases),
                        Description = field.Description.ToExport().ToArray(),
                        ArrayDimensions = field.ArrayDimensions,
                        IsOptional = field.IsOptional,
                    };
                    if (field.ValueRank != null)
                    {
                        uaField.ValueRank = field.ValueRank.Value;
                    }
                    if (field.MaxStringLength != null)
                    {
                        uaField.MaxStringLength = field.MaxStringLength.Value;
                    }
                    fields.Add(uaField);
                }
                dataType.Definition = new uaExport.DataTypeDefinition
                {
                    Name = GetBrowseNameForExport(namespaces),
                    SymbolicName = _model.SymbolicName,
                    Field = fields.ToArray(),
                };
            }
            if (_model.EnumFields?.Any() == true)
            {
                var fields = new List<DataTypeField>();
                foreach (var field in _model.EnumFields)
                {
                    fields.Add(new DataTypeField
                    {
                        Name = field.Name,
                        DisplayName = field.DisplayName?.ToExport().ToArray(),
                        Description = field.Description?.ToExport().ToArray(),
                        Value = (int) field.Value,
                        // TODO: 
                        //SymbolicName = field.SymbolicName,
                        //DataType = field.DataType,                         
                    });
                }
                dataType.Definition = new uaExport.DataTypeDefinition
                {
                    Name = GetBrowseNameForExport(namespaces),
                    Field = fields.ToArray(),
                };
            }
            if (_model.IsOptionSet != null)
            {
                if (dataType.Definition == null)
                {
                    dataType.Definition = new uaExport.DataTypeDefinition {  };
                }
                dataType.Definition.IsOptionSet = _model.IsOptionSet.Value;
            }
            return (dataType as T, result.AdditionalNodes);
        }

    }

    public class ReferenceTypeModelExportOpc : BaseTypeModelExportOpc<ReferenceTypeModel>
    {
        public override (T ExportedNode, List<UANode> AdditionalNodes) GetUANode<T>(NamespaceTable namespaces, Dictionary<string, string> aliases)
        {
            var result = base.GetUANode<UAReferenceType>(namespaces, aliases);
            result.ExportedNode.IsAbstract = _model.IsAbstract;
            result.ExportedNode.InverseName = _model.InverseName?.ToExport().ToArray();
            result.ExportedNode.Symmetric = _model.Symmetric;
            return (result.ExportedNode as T, result.AdditionalNodes);
        }
    }

    public static class LocalizedTextExtension
    {
        public static uaExport.LocalizedText ToExport(this NodeModel.LocalizedText localizedText) => localizedText?.Text != null || localizedText?.Locale != null ? new uaExport.LocalizedText { Locale = localizedText.Locale, Value = localizedText.Text } : null;
        public static IEnumerable<uaExport.LocalizedText> ToExport(this IEnumerable<NodeModel.LocalizedText> localizedTexts) => localizedTexts?.Select(d => d.Text != null || d.Locale != null ? new uaExport.LocalizedText { Locale = d.Locale, Value = d.Text } : null).ToArray();
    }

}