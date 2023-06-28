/* Author:      Chris Muench, C-Labs
 * Last Update: 4/8/2022
 * License:     MIT
 * 
 * Some contributions thanks to CESMII – the Smart Manufacturing Institute, 2021
 */

using Opc.Ua.Export;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CESMII.OpcUa.NodeSetModel.Export.Opc;
using CESMII.OpcUa.NodeSetModel.Opc.Extensions;
using Opc.Ua;
using System.Xml.Serialization;
using System.Xml;

namespace CESMII.OpcUa.NodeSetModel
{
    /// <summary>
    /// Exporter helper class
    /// </summary>
    public class UANodeSetModelExporter
    {
        public static string ExportNodeSetAsXml(NodeSetModel nodesetModel, Dictionary<string, NodeSetModel> nodesetModels, Dictionary<string, string> aliases = null)
        {
            var exportedNodeSet = ExportNodeSet(nodesetModel, nodesetModels, aliases);

            string exportedNodeSetXml;
            // .Net6 changed the default to no-identation: https://github.com/dotnet/runtime/issues/64885
            using (var ms = new MemoryStream())
            {
                using (var writer = new StreamWriter(ms, Encoding.UTF8))
                {
                    try
                    {
                        using (var xmlWriter = XmlWriter.Create(writer, new XmlWriterSettings { Indent = true, }))
                        {
                            XmlSerializer serializer = new XmlSerializer(typeof(UANodeSet));
                            serializer.Serialize(xmlWriter, exportedNodeSet);
                        }
                    }
                    finally
                    {
                        writer.Flush();
                    }
                }
                exportedNodeSetXml = Encoding.UTF8.GetString(ms.ToArray());
            }
            return exportedNodeSetXml;
        }
        public static UANodeSet ExportNodeSet(NodeSetModel nodesetModel, Dictionary<string, NodeSetModel> nodesetModels, Dictionary<string, string> aliases = null)
        {
            if (aliases == null)
            {
                aliases = new();
            }
            var exportedNodeSet = new UANodeSet();
            exportedNodeSet.LastModified = DateTime.UtcNow;
            exportedNodeSet.LastModifiedSpecified = true;

            var namespaceUris = nodesetModel.AllNodesByNodeId.Values.Select(v => v.Namespace).Distinct().ToList();

            var requiredModels = new List<ModelTableEntry>();

            // Ensure OPC UA model is the first one (index 0)
            var namespaces = new NamespaceTable(new[] { Namespaces.OpcUa });
            foreach (var nsUri in namespaceUris)
            {
                namespaces.GetIndexOrAppend(nsUri);
            }
            var nodeIdsUsed = new HashSet<string>();
            var items = ExportAllNodes(nodesetModel, aliases, namespaces, nodeIdsUsed);

            // remove unused aliases
            var usedAliases = aliases.Where(pk => nodeIdsUsed.Contains(pk.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

            // Add aliases for all nodeids from other namespaces
            var currentNodeSetNamespaceIndex = namespaces.GetIndex(nodesetModel.ModelUri);
            bool bAliasesAdded = false;
            foreach (var nodeId in nodeIdsUsed)
            {
                var parsedNodeId = NodeId.Parse(nodeId);
                if (parsedNodeId.NamespaceIndex != currentNodeSetNamespaceIndex
                    && !usedAliases.ContainsKey(nodeId))
                {
                    var namespaceUri = namespaces.GetString(parsedNodeId.NamespaceIndex);
                    var nodeIdWithUri = new ExpandedNodeId(parsedNodeId, namespaceUri).ToString();
                    var nodeModel = nodesetModels.Select(nm => nm.Value.AllNodesByNodeId.TryGetValue(nodeIdWithUri, out var model) ? model : null).FirstOrDefault(n => n != null);
                    var displayName = nodeModel?.DisplayName?.FirstOrDefault()?.Text;
                    if (displayName != null && !usedAliases.ContainsValue(displayName))
                    {
                        usedAliases.Add(nodeId, displayName);
                        aliases.Add(nodeId, displayName);
                        bAliasesAdded = true;
                    }
                }
            }

            var aliasList = usedAliases
                .Select(alias => new NodeIdAlias { Alias = alias.Value, Value = alias.Key })
                .OrderBy(kv => kv.Value)
                .ToList();
            exportedNodeSet.Aliases = aliasList.ToArray();

            if (bAliasesAdded)
            {
                // Re-export with new aliases
                items = ExportAllNodes(nodesetModel, aliases, namespaces, null);
            }

            var allNamespaces = namespaces.ToArray();
            if (allNamespaces.Length > 1)
            {
                exportedNodeSet.NamespaceUris = allNamespaces.Where(ns => ns != Namespaces.OpcUa).ToArray();
            }
            else
            {
                exportedNodeSet.NamespaceUris = allNamespaces;
            }
            foreach (var uaNamespace in allNamespaces.Except(namespaceUris))
            {
                if (!requiredModels.Any(m => m.ModelUri == uaNamespace))
                {
                    if (nodesetModels.TryGetValue(uaNamespace, out var requiredNodeSetModel))
                    {
                        var requiredModel = new ModelTableEntry
                        {
                            ModelUri = uaNamespace,
                            Version = requiredNodeSetModel.Version,
                            PublicationDate = requiredNodeSetModel.PublicationDate.GetNormalizedPublicationDate(),
                            PublicationDateSpecified = requiredNodeSetModel.PublicationDate != null,
                            RolePermissions = null,
                            AccessRestrictions = 0,
                        };
                        requiredModels.Add(requiredModel);
                    }
                    else
                    {
                        // The model was not loaded. This can happen if the only reference to the model is in an extension object that only gets parsed but not turned into a node model (Example: onboarding nodeset refernces GDS ns=2;i=1)
                        var requiredModel = new ModelTableEntry
                        {
                            ModelUri = uaNamespace,
                        };
                        requiredModels.Add(requiredModel);
                    }
                }
            }

            var model = new ModelTableEntry
            {
                ModelUri = nodesetModel.ModelUri,
                RequiredModel = requiredModels.ToArray(),
                AccessRestrictions = 0,
                PublicationDate = nodesetModel.PublicationDate.GetNormalizedPublicationDate(),
                PublicationDateSpecified = nodesetModel.PublicationDate != null,
                RolePermissions = null,
                Version = nodesetModel.Version,
                XmlSchemaUri = nodesetModel.XmlSchemaUri != nodesetModel.ModelUri ? nodesetModel.XmlSchemaUri : null
            };
            if (exportedNodeSet.Models != null)
            {
                var models = exportedNodeSet.Models.ToList();
                models.Add(model);
                exportedNodeSet.Models = models.ToArray();
            }
            else
            {
                exportedNodeSet.Models = new ModelTableEntry[] { model };
            }
            if (exportedNodeSet.Items != null)
            {
                var newItems = exportedNodeSet.Items.ToList();
                newItems.AddRange(items);
                exportedNodeSet.Items = newItems.ToArray();
            }
            else
            {
                exportedNodeSet.Items = items.ToArray();
            }
            return exportedNodeSet;
        }

        private static List<UANode> ExportAllNodes(NodeSetModel nodesetModel, Dictionary<string, string> aliases, NamespaceTable namespaces, HashSet<string> nodeIdsUsed)
        {
            var items = new Dictionary<string, UANode>();
            foreach (var nodeModel in nodesetModel.AllNodesByNodeId.Values /*.Where(n => n.Value.Namespace == opcNamespace)*/.OrderBy(n => n.NodeId))
            {
                var result = NodeModelExportOpc.GetUANode(nodeModel, namespaces, aliases, nodeIdsUsed, items);
                if (result.ExportedNode != null)
                {
                    items[result.ExportedNode.NodeId] = result.ExportedNode;
                }
                if (result.AdditionalNodes != null)
                {
                    result.AdditionalNodes.ForEach(n => items[n.NodeId] = n);
                }
            }
            return items.Values.ToList();
        }


    }
}
