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

        // SequenceNumber/IsUpdate
        public object CustomState { get; set; }

        /// <summary>
        /// This is equivalent to ProfileItem of type Class.
        /// </summary>
        public virtual List<ObjectTypeModel> ObjectTypes { get; set; } = new List<ObjectTypeModel>();

        /// <summary>
        /// This is equivalent to ProfileItem of type CustomDataType.
        /// This is used primarily to represent structure type objects (ie MessageCode example). 
        /// This is typically not an instance. 
        /// This could have its own complex properties associated with it.
        /// </summary>
        public virtual List<VariableTypeModel> VariableTypes { get; set; } = new List<VariableTypeModel>();

        /// <summary>
        /// No clean mapping yet.
        /// TBD - Lower priority.
        /// appears to be enumerations
        /// more globally re-usable
        /// TBD - ask JW if we can only use these to be selected and not allow building them out.
        /// </summary>
        public virtual List<DataTypeModel> DataTypes { get; set; } = new List<DataTypeModel>();

        /// <summary>
        /// This is equivalent to ProfileInterface.
        /// </summary>
        public virtual List<InterfaceModel> Interfaces { get; set; } = new List<InterfaceModel>();
        public virtual List<ObjectModel> Objects { get; set; } = new List<ObjectModel>();

        public virtual List<PropertyModel> Properties { get; set; } = new List<PropertyModel>();
        public virtual List<DataVariableModel> DataVariables { get; set; } = new List<DataVariableModel>();

        public virtual List<NodeModel> UnknownNodes { get; set; } = new List<NodeModel>();

        public virtual List<ReferenceTypeModel> ReferenceTypes { get; set; } = new List<ReferenceTypeModel>();

        public Dictionary<string, NodeModel> AllNodesByNodeId { get; } = new Dictionary<string, NodeModel>();
    }
    public static class NodeSetModelExtensions
    { 
        public static void UpdateAllNodes(this NodeSetModel _this)
        {
            _this.AllNodesByNodeId.Clear();
            foreach (var dataType in _this.DataTypes)
            {
                if (!_this.AllNodesByNodeId.TryAdd(dataType.NodeId, dataType))
                {
                    // Duplicate node id!
                }
            }
            foreach (var variableType in _this.VariableTypes)
            {
                _this.AllNodesByNodeId.TryAdd(variableType.NodeId, variableType);
            }
            foreach (var uaInterface in _this.Interfaces)
            {
                _this.AllNodesByNodeId.TryAdd(uaInterface.NodeId, uaInterface);
            }
            foreach (var objectType in _this.ObjectTypes)
            {
                _this.AllNodesByNodeId.TryAdd(objectType.NodeId, objectType);
            }
            foreach (var uaObject in _this.Objects)
            {
                _this.AllNodesByNodeId.TryAdd(uaObject.NodeId, uaObject);
            }
            foreach (var property in _this.Properties)
            {
                _this.AllNodesByNodeId.TryAdd(property.NodeId, property);
            }
            foreach (var dataVariable in _this.DataVariables)
            {
                _this.AllNodesByNodeId.TryAdd(dataVariable.NodeId, dataVariable);
            }
            foreach (var referenceType in _this.ReferenceTypes)
            {
                _this.AllNodesByNodeId.TryAdd(referenceType.NodeId, referenceType);
            }
            foreach (var node in _this.UnknownNodes)
            {
                _this.AllNodesByNodeId.TryAdd(node.NodeId, node);
            }
        }

        public static void UpdateIndices(this NodeSetModel _this)
        {
            _this.AllNodesByNodeId.Clear();
            var updatedNodes = new List<NodeModel>();
            foreach (var dataType in _this.DataTypes)
            {
                dataType.UpdateIndices(_this, updatedNodes);
            }
            foreach (var variableType in _this.VariableTypes)
            {
                variableType.UpdateIndices(_this, updatedNodes);
            }
            foreach (var uaInterface in _this.Interfaces)
            {
                uaInterface.UpdateIndices(_this, updatedNodes);
            }
            foreach (var objectType in _this.ObjectTypes)
            {
                objectType.UpdateIndices(_this, updatedNodes);
            }
            foreach (var property in _this.Properties)
            {
                property.UpdateIndices(_this, updatedNodes);
            }
            foreach (var dataVariable in _this.DataVariables)
            {
                dataVariable.UpdateIndices(_this, updatedNodes);
            }
            foreach (var uaObject in _this.Objects)
            {
                uaObject.UpdateIndices(_this, updatedNodes);
            }
            foreach (var referenceType in _this.ReferenceTypes)
            {
                referenceType.UpdateIndices(_this, updatedNodes);
            }
            foreach (var node in _this.UnknownNodes)
            {
                node.UpdateIndices(_this, updatedNodes);
            }
        }
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
            return BrowseName ?? $"{Namespace}:{DisplayName}";
        }

        public virtual List<LocalizedText> Description { get; set; }
        public string Documentation { get; set; }
        [IgnoreDataMember]
        public string Namespace { get => NodeSet.ModelUri; /*set; */}
        public string NodeId { get; set; }
        public object CustomState { get; set; }
        public virtual List<string> Categories { get; set; }

        public virtual NodeSetModel NodeSet { get; set; }

        public class LocalizedText
        {
            public string Text { get; set; }
            public string Locale { get; set; }

            public static implicit operator LocalizedText(string text) => new LocalizedText { Text = text };
            public static List<LocalizedText> ListFromText (string text) => text != null ? new List<LocalizedText> { new LocalizedText { Text = text } } : new List<LocalizedText>();
            public override string ToString() => Text;
        }

        /// <summary>
        /// This is equivalent to ProfileAttribute except it keeps compositions, variable types elsewhere.
        /// Relatively static (ie. serial number.) over the life of the object instance
        /// </summary>
        public virtual List<VariableModel> Properties { get; set; } = new List<VariableModel>(); // Surprisingly, properties can also be of type DataVariable - need to allow both by using the common base class

        /// <summary>
        /// This is equivalent to ProfileAttribute. More akin to variable types.
        /// More dynamic (ie. RPM, temperature.)
        /// TBD - figure out a way to distinguish between data variables and properties.  
        /// </summary>
        public virtual List<DataVariableModel> DataVariables { get; set; } = new List<DataVariableModel>();

        /// <summary>
        /// This is equivalent to ProfileAttribute.
        /// Sub-systems. More akin to compositions.
        /// (ie. starter, control unit). Complete sub-system that could be used by other profiles. 
        /// The object model has name, description and then ObjectType much like Profile attribute has name, description, Composition(Id). 
        /// </summary>
        public virtual List<ObjectModel> Objects { get; set; } = new List<ObjectModel>();

        /// <summary>
        /// This is equivalent to Interfaces in ProfileItem.
        /// If someone implemented an interface, the objectType should dynamically "get" those properties. 
        /// Essentially, the implementing objectType does not hardcode the properties of the interface. any change to the
        /// interface would automatically be shown to the user on the implementing objectTypes.
        /// </summary>
        public virtual List<InterfaceModel> Interfaces { get; set; } = new List<InterfaceModel>();

        /// <summary>
        /// TBD - defer for now
        /// </summary>
        public virtual List<MethodModel> Methods { get; set; } = new List<MethodModel>();
        /// <summary>
        /// TBD - defer for now
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
        public string ModelingRule { get; set; }
        public virtual NodeModel Parent
        {
            get => _parent;
            set
            {
                if (Parent != null && Parent != value)
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
    }

    public class BaseTypeModel : NodeModel
    {
        public bool IsAbstract { get; set; }
        /// <summary>
        /// This is equivalent to ProfileItem.Parent.
        /// </summary>
        public virtual BaseTypeModel SuperType { get; set; }
        /// <summary>
        /// This is equivalent to ProfileItem.Children.
        /// Dynamically assembled from list of types ids...
        /// Not serialized.
        /// </summary>
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
    }

    public class InterfaceModel : ObjectTypeModel
    {
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
        public string Value { get; set; }

        // Engineering units:
        public class EngineeringUnitInfo
        {
            public LocalizedText DisplayName { get; set; }
            public LocalizedText Description { get; set; }
            public string NamespaceUri { get; set; }
            public int? UnitId { get; set; }
        }

        virtual public EngineeringUnitInfo EngineeringUnit { get; set; }
        public string EngUnitNodeId { get; set; }
        public string EngUnitModelingRule { get; set; }
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public string EURangeNodeId { get; set; }
        public string EURangeModelingRule { get; set; }
        public double? InstrumentMinValue { get; set; }
        public double? InstrumentMaxValue { get; set; }
        public long? EnumValue { get; set; }

        public uint? AccessLevel { get; set; }
        public uint? UserAccessLevel { get; set; }
        public ushort? AccessRestrictions { get; set; }
        public uint? WriteMask { get; set; }
        public uint? UserWriteMask { get; set; }
    }

    public class DataVariableModel : VariableModel
    {
    }

    public class PropertyModel : VariableModel
    {
    }

    public class MethodModel : InstanceModel<MethodModel>
    {
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

#if NETSTANDARD2_0
    static class DictExtensions
    {
        public static bool TryAdd(this Dictionary<string, NodeModel> dict, string key, NodeModel value)
        {
            if (dict.ContainsKey(key)) return false;
            dict.Add(key, value);
            return true;
        }
    }
#endif
}