using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace CESMII.OpcUa.NodeSetModel
{
    public class NodeSetModel
    {
        public string ModelUri { get; set; }
        public string Version { get; set; }
        public DateTime? PublicationDate { get; set; }

        // RequiredModels
        public virtual List<RequiredModelInfo> RequiredModels { get; set; } = new List<RequiredModelInfo>();
        // NamespaceUris
        // ServerUris
        // DefaultAccessRules

        public override string ToString() => $"{ModelUri} {Version} ({PublicationDate})";

        /// <summary>
        /// Unique identifier for this nodeset, optionally assigned by the managing application. Not used in the nodeset model classes
        /// </summary>
        public string Identifier { get; set; }

        // For use by the application
        public object CustomState { get; set; }

        /// <summary>
        /// The UA object types defined by this node set
        /// </summary>
        public virtual List<ObjectTypeModel> ObjectTypes { get; set; } = new List<ObjectTypeModel>();

        /// <summary>
        /// The UA variable types defined by this node set
        /// </summary>
        public virtual List<VariableTypeModel> VariableTypes { get; set; } = new List<VariableTypeModel>();

        /// <summary>
        /// The UA data types defined by this node set
        /// </summary>
        public virtual List<DataTypeModel> DataTypes { get; set; } = new List<DataTypeModel>();

        /// <summary>
        /// The UA interfaces defined by this node set
        /// </summary>
        public virtual List<InterfaceModel> Interfaces { get; set; } = new List<InterfaceModel>();
        public virtual List<ObjectModel> Objects { get; set; } = new List<ObjectModel>();

        public virtual List<PropertyModel> Properties { get; set; } = new List<PropertyModel>();
        public virtual List<DataVariableModel> DataVariables { get; set; } = new List<DataVariableModel>();

        public virtual List<NodeModel> UnknownNodes { get; set; } = new List<NodeModel>();

        public virtual List<ReferenceTypeModel> ReferenceTypes { get; set; } = new List<ReferenceTypeModel>();

        public Dictionary<string, NodeModel> AllNodesByNodeId { get; } = new Dictionary<string, NodeModel>();
    }
    public class RequiredModelInfo
    {
        public string ModelUri { get; set; }
        public string Version { get; set; }
        public DateTime? PublicationDate { get; set; }
        virtual public NodeSetModel AvailableModel { get; set; }
    }

    public class NodeModel
    {
        public virtual List<LocalizedText> DisplayName { get; set; }
        public string BrowseName { get; set; }
        public string SymbolicName { get; set; }
        public string GetBrowseName()
        {
            return BrowseName ?? $"{Namespace}:{DisplayName?.FirstOrDefault()?.Text}";
        }

        public virtual List<LocalizedText> Description { get; set; }
        public string Documentation { get; set; }
        public string ReleaseStatus { get; set; }

        [IgnoreDataMember]
        public string Namespace { get => NodeSet.ModelUri; }
        public string NodeId { get; set; }
        public object CustomState { get; set; }
        public virtual List<string> Categories { get; set; }

        public virtual NodeSetModel NodeSet { get; set; }

        public class LocalizedText
        {
            public LocalizedText()
            {
                Text = "";
            }
#nullable enable
            public string Text { get => _text; set => _text = value ?? ""; }
            private string _text;
#nullable restore
            public string Locale { get; set; }

            public static implicit operator LocalizedText(string text) => text == null ? null : new LocalizedText { Text = text };
            public static List<LocalizedText> ListFromText (string text) => text != null ? new List<LocalizedText> { new LocalizedText { Text = text } } : new List<LocalizedText>();
            public override string ToString() => Text;
        }

        /// <summary>
        /// OPC UA: HasProperty references
        /// </summary>
        public virtual List<VariableModel> Properties { get; set; } = new List<VariableModel>(); // Surprisingly, properties can also be of type DataVariable - need to allow both by using the common base class

        /// <summary>
        /// OPC UA: HasComponent references (or of derived reference type) to a DataVariable
        /// </summary>
        public virtual List<DataVariableModel> DataVariables { get; set; } = new List<DataVariableModel>();

        /// <summary>
        /// OPC UA: HasComponent references (or of derived reference types) to an Object
        /// </summary>
        public virtual List<ObjectModel> Objects { get; set; } = new List<ObjectModel>();

        /// <summary>
        /// OPC UA: HasInterface references (or of derivce reference types)
        /// </summary>
        public virtual List<InterfaceModel> Interfaces { get; set; } = new List<InterfaceModel>();

        /// <summary>
        /// TBD - defer for now
        /// OPC UA: HasComponent references (or of derived reference types) to a MethodType
        /// </summary>
        public virtual List<MethodModel> Methods { get; set; } = new List<MethodModel>();
        /// <summary>
        /// OPC UA: GeneratesEvent references (or of derived reference types)
        /// </summary>
        public virtual List<ObjectTypeModel> Events { get; set; } = new List<ObjectTypeModel>();

        public class NodeAndReference
        {
            public virtual NodeModel Node { get; set; }
            public string Reference { get; set; }
        }

        public virtual List<NodeAndReference> OtherReferencedNodes { get; set; } = new List<NodeAndReference>();
        public virtual List<NodeAndReference> OtherReferencingNodes { get; set; } = new List<NodeAndReference>();

        internal virtual bool UpdateIndices(NodeSetModel model, List<NodeModel> updatedNodes)
        {
            if (updatedNodes.Contains(this))
            {
                // break some recursions
                return false;
            }
            updatedNodes.Add(this);
            if (model.ModelUri == this.Namespace)
            {
                model.AllNodesByNodeId.TryAdd(this.NodeId, this);
            }
            foreach (var node in Objects)
            {
                node.UpdateIndices(model, updatedNodes);
            }
            foreach (var node in this.DataVariables)
            {
                node.UpdateIndices(model, updatedNodes);
            }
            foreach (var node in this.Interfaces)
            {
                node.UpdateIndices(model, updatedNodes);
            }
            foreach (var node in this.Methods)
            {
                node.UpdateIndices(model, updatedNodes);
            }
            foreach (var node in this.Properties)
            {
                node.UpdateIndices(model, updatedNodes);
            }
            foreach (var node in this.Events)
            {
                node.UpdateIndices(model, updatedNodes);
            }
            return true;
        }

        public override string ToString()
        {
            return $"{DisplayName?.FirstOrDefault()} ({Namespace}: {NodeId})";
        }
    }

    public abstract class InstanceModelBase : NodeModel
    {
        /// <summary>
        /// Values: Optional, Mandatory, MandatoryPlaceholder, OptionalPlaceholder, ExposesItsArray
        /// </summary>
        public string ModellingRule { get; set; }
        public virtual NodeModel Parent
        {
            get => _parent;
            set
            {
                if (_parent != null && _parent != value)
                {
                    // Changing parent or multiple parents on {this}: new parent {value}, previous parent {_parent}
                    return;
                }
                _parent = value;
            }
        }
        private NodeModel _parent;
    }
    public abstract class InstanceModel<TTypeDefinition> : InstanceModelBase where TTypeDefinition : NodeModel, new()
    {
        public virtual TTypeDefinition TypeDefinition { get; set; }
    }

    public class ObjectModel : InstanceModel<ObjectTypeModel>
    {
        /// <summary>
        /// Not used by the model itself. Captures the many-to-many relationship between NodeModel.Objects and ObjectModel for EF
        /// </summary>
        public virtual List<NodeModel> NodesWithObjects { get; set; } = new List<NodeModel>();
    }

    public abstract class BaseTypeModel : NodeModel
    {
        public bool IsAbstract { get; set; }

        public virtual BaseTypeModel SuperType { get; set; }

        [IgnoreDataMember] // This can contain cycle (and is easily recreated from the SubTypeId)
        public virtual List<BaseTypeModel> SubTypes { get; set; } = new List<BaseTypeModel>();

        public bool HasBaseType(string nodeId)
        {
            var baseType = this;
            do
            {
                if (baseType?.NodeId == nodeId)
                {
                    return true;
                }
                baseType = baseType.SuperType;
            }
            while (baseType != null);
            return false;
        }

        public void RemoveInheritedAttributes(BaseTypeModel superTypeModel)
        {
            while (superTypeModel != null)
            {
                RemoveByBrowseName(Properties, superTypeModel.Properties);
                RemoveByBrowseName(DataVariables, superTypeModel.DataVariables);
                RemoveByBrowseName(Objects, superTypeModel.Objects);
                RemoveByBrowseName(Interfaces, superTypeModel.Interfaces);
                foreach (var uaInterface in superTypeModel.Interfaces)
                {
                    RemoveInheritedAttributes(uaInterface);
                }
                RemoveByBrowseName(Methods, superTypeModel.Methods);
                RemoveByBrowseName(Events, superTypeModel.Events);

                superTypeModel = superTypeModel?.SuperType;
            }
        }

        private void RemoveByBrowseName<T>(List<T> properties, List<T> propertiesToRemove) where T : NodeModel
        {
            foreach (var property in propertiesToRemove)
            {
                properties.RemoveAll(p =>
                    p.GetBrowseName() == property.GetBrowseName()
                    && p.NodeId == property.NodeId
                    );
            }
        }

        internal override bool UpdateIndices(NodeSetModel model, List<NodeModel> updatedNodes)
        {
            var bUpdated = base.UpdateIndices(model, updatedNodes);
            if (bUpdated && SuperType != null && !SuperType.SubTypes.Any(sub => sub.NodeId == this.NodeId))
            {
                SuperType.SubTypes.Add(this);
            }
            return bUpdated;
        }

    }

    public class ObjectTypeModel : BaseTypeModel
    {
        /// Not used by the model itself. Captures the many-to-many relationship between NodeModel.Events and ObjectTypeModel for EF
        public virtual List<NodeModel> NodesWithEvents { get; set; } = new List<NodeModel>();
    }

    public class InterfaceModel : ObjectTypeModel
    {
        /// <summary>
        /// Not used by the model itself. Captures the many-to-many relationship between NodeModel.Interfaces and InterfaceModel for EF
        /// </summary>
        public virtual List<NodeModel> NodesWithInterface { get; set; } = new List<NodeModel>();
    }

    public class VariableModel : InstanceModel<VariableTypeModel>, IVariableDataTypeInfo
    {
        public virtual BaseTypeModel DataType { get; set; }
        /// <summary>
        /// n > 1: the Value is an array with the specified number of dimensions.
        /// OneDimension(1) : The value is an array with one dimension.
        /// OneOrMoreDimensions(0): The value is an array with one or more dimensions.
        /// Scalar(−1): The value is not an array.
        /// Any(−2): The value can be a scalar or an array with any number of dimensions.
        /// ScalarOrOneDimension(−3): The value can be a scalar or a one dimensional array.
        /// </summary>
        public int? ValueRank { get; set; }
        /// <summary>
        /// Comma separated list
        /// </summary>
        public string ArrayDimensions { get; set; }
        /// <summary>
        /// Default value of the variable represented as a JSON-encoded Variant, i.e. {\"Value\":{\"Type\":10,\"Body\":0 }
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Not used by the model itself. Captures the many-to-many relationship between NodeModel.Properties and PropertiesModel for EF
        /// </summary>
        public virtual List<NodeModel> NodesWithProperties { get; set; } = new List<NodeModel>();

        // Engineering units:
        public class EngineeringUnitInfo
        {
            /// <summary>
            /// If only DisplayName is specified, it is assumed to be the DisplayName or the Description of a UNECE unit as specified in https://reference.opcfoundation.org/v104/Core/docs/Part8/5.6.3/, and the referenced http://www.opcfoundation.org/UA/EngineeringUnits/UNECE/UNECE_to_OPCUA.csv
            /// </summary>
            public LocalizedText DisplayName { get; set; }
            public LocalizedText Description { get; set; }
            public string NamespaceUri { get; set; }
            public int? UnitId { get; set; }
        }

        virtual public EngineeringUnitInfo EngineeringUnit { get; set; }
        /// <summary>
        /// NodeId to use for the engineering unit property. A random one can be generated by an exporter if not specified.
        /// </summary>
        public string EngUnitNodeId { get; set; }
        public string EngUnitModellingRule { get; set; }
        public uint? EngUnitAccessLevel { get; set; }
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        /// <summary>
        /// NodeId to use for the EURange property. A random one can be generated by an exporter if not specified.
        /// </summary>
        public string EURangeNodeId { get; set; }
        public string EURangeModellingRule { get; set; }
        public uint? EURangeAccessLevel { get; set; }
        public double? InstrumentMinValue { get; set; }
        public double? InstrumentMaxValue { get; set; }
        public long? EnumValue { get; set; }

        public uint? AccessLevel { get; set; }
        public ushort? AccessRestrictions { get; set; }
        public uint? WriteMask { get; set; }
        public uint? UserWriteMask { get; set; }
        public double? MinimumSamplingInterval { get; set; }
    }

    public class DataVariableModel : VariableModel
    {
        /// <summary>
        /// Not used by the model itself. Captures the many-to-many relationship between NodeModel.DataVariables and DataVariableModel for EF
        /// </summary>
        public virtual List<NodeModel> NodesWithDataVariables { get; set; } = new List<NodeModel>();
    }

    public class PropertyModel : VariableModel
    {
    }

    public class MethodModel : InstanceModel<MethodModel>
    {
        /// <summary>
        /// Not used by the model itself. Captures the many-to-many relationship between NodeModel.Methods and MethodModel for EF
        /// </summary>
        public virtual List<NodeModel> NodesWithMethods { get; set; } = new List<NodeModel>();
    }

    public class ReferenceTypeModel : BaseTypeModel
    {
        /// <summary>
        /// The inverse name for the reference.
        /// </summary>
        public List<LocalizedText> InverseName { get; set; }
        /// <summary>
        /// Whether the reference is symmetric.
        /// </summary>
        public bool Symmetric { get; set; }
    }


    public interface IVariableDataTypeInfo
    {
        BaseTypeModel DataType { get; set; }
        /// <summary>
        /// n > 1: the Value is an array with the specified number of dimensions.
        /// OneDimension(1) : The value is an array with one dimension.
        /// OneOrMoreDimensions(0): The value is an array with one or more dimensions.
        /// Scalar(−1): The value is not an array.
        /// Any(−2): The value can be a scalar or an array with any number of dimensions.
        /// ScalarOrOneDimension(−3): The value can be a scalar or a one dimensional array.
        /// </summary>
        int? ValueRank { get; set; }
        /// <summary>
        /// Comma separated list
        /// </summary>
        string ArrayDimensions { get; set; }
        string Value { get; set; }
    }

    public class VariableTypeModel : BaseTypeModel, IVariableDataTypeInfo
    {
        public virtual BaseTypeModel DataType { get; set; }
        /// <summary>
        /// n > 1: the Value is an array with the specified number of dimensions.
        /// OneDimension(1) : The value is an array with one dimension.
        /// OneOrMoreDimensions(0): The value is an array with one or more dimensions.
        /// Scalar(−1): The value is not an array.
        /// Any(−2): The value can be a scalar or an array with any number of dimensions.
        /// ScalarOrOneDimension(−3): The value can be a scalar or a one dimensional array.
        /// </summary>
        public int? ValueRank { get; set; }
        /// <summary>
        /// Comma separated list
        /// </summary>
        public string ArrayDimensions { get; set; }
        public string Value { get; set; }
    }

    public class DataTypeModel : BaseTypeModel
    {
        public virtual List<StructureField> StructureFields { get; set; }
        public virtual List<UaEnumField> EnumFields { get; set; }
        public bool? IsOptionSet { get; set; }

        public class StructureField
        {
            public string Name { get; set; }
            public virtual BaseTypeModel DataType { get; set; }
            /// <summary>
            /// n > 1: the Value is an array with the specified number of dimensions.
            /// OneDimension(1) : The value is an array with one dimension.
            /// OneOrMoreDimensions(0): The value is an array with one or more dimensions.
            /// Scalar(−1): The value is not an array.
            /// Any(−2): The value can be a scalar or an array with any number of dimensions.
            /// ScalarOrOneDimension(−3): The value can be a scalar or a one dimensional array.
            /// </summary>
            public int? ValueRank { get; set; }
            /// <summary>
            /// Comma separated list
            /// </summary>
            public string ArrayDimensions { get; set; }
            public uint? MaxStringLength { get; set; }
            public virtual List<LocalizedText> Description { get; set; }
            public bool IsOptional { get; set; }
            /// <summary>
            /// Used to preserve field order if stored in a relational database (via EF etc.)
            /// </summary>
            public int FieldOrder { get; set; }
            public override string ToString() => $"{Name}: {DataType} {(IsOptional ? "Optional" : "")}";

        }

        public class UaEnumField
        {
            public string Name { get; set; }
            public virtual List<LocalizedText> DisplayName { get; set; }
            public virtual List<LocalizedText> Description { get; set; }
            public long Value {get; set; }

            public override string ToString() => $"{Name} = {Value}";
        }

        internal override bool UpdateIndices(NodeSetModel model, List<NodeModel> updatedNodes)
        {
            var bUpdated = base.UpdateIndices(model, updatedNodes);
            if (bUpdated && StructureFields?.Any() == true)
            {
                foreach (var field in StructureFields)
                {
                    field.DataType?.UpdateIndices(model, updatedNodes);
                }
            }
            return bUpdated;
        }

    }

}