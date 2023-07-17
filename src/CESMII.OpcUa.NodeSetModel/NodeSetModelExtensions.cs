using System;
using System.Collections.Generic;
using System.Linq;

namespace CESMII.OpcUa.NodeSetModel
{
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
            var updatedNodes = new HashSet<string>();
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
#if NETSTANDARD2_0
    public static class DictionaryExtensions
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value)
        {
            if (dict.ContainsKey(key)) return false;
            dict.Add(key, value);
            return true;
        }
    }

    internal class HashCode
    {
        public static int Combine<T1, T2, T3>(T1 o1, T2 o2, T3 o3)
        {
            return (o1.GetHashCode() ^ o2.GetHashCode() ^ o3.GetHashCode());
        }
    }

#endif
}