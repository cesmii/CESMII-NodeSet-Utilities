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
using System.Threading.Tasks;

using CESMII.OpcUa.NodeSetModel.Factory.Opc;
using CESMII.OpcUa.NodeSetModel.Opc.Extensions;
using CESMII.OpcUa.NodeSetImporter;
using Microsoft.Extensions.Logging;
using CESMII.OpcUa.NodeSetModel.Export.Opc;
using Opc.Ua;
using System.Xml.Serialization;
using System.Xml;
using System.Buffers.Text;

namespace CESMII.OpcUa.NodeSetModel
{
    /// <summary>
    /// Main Importer class importing NodeSets 
    /// </summary>
    public class UANodeSetModelImporter
    {
        private readonly IUANodeSetCache _nodeSetCache;
        private readonly IOpcUaContext _opcContext;

        public UANodeSetModelImporter(ILogger logger)
        {
            _opcContext = new DefaultOpcUaContext(logger);
            _nodeSetCache = new UANodeSetFileCache();
        }
        public UANodeSetModelImporter(IOpcUaContext opcContext)
        {
            _opcContext = opcContext;
            _nodeSetCache = new UANodeSetFileCache();
        }
        public UANodeSetModelImporter(IOpcUaContext opcContext, IUANodeSetCache nodeSetCache)
        {
            _opcContext = opcContext;
            _nodeSetCache = nodeSetCache;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nodeSetXML">nodeset to be imported.</param>
        /// <param name="identifier">optional identifier to be attached to the nodeset. For use by the application.</param>
        /// <param name="modelUri">optional modelUri to verify</param>
        /// <returns></returns>
        /// <exception cref="NodeSetResolverException"></exception>
        public async Task<List<NodeSetModel>> ImportNodeSetModelAsync(string nodeSetXML, string identifier = null)
        {
            var resolvedNodeSets = UANodeSetImporter.ImportNodeSets(_nodeSetCache, null, new List<string> { nodeSetXML }, false, null, null);
            if (!string.IsNullOrEmpty(resolvedNodeSets.ErrorMessage))
            {
                throw new NodeSetResolverException($"{resolvedNodeSets.ErrorMessage}");
            }

            List<NodeSetModel> allLoadedNodesetModels = new();

            try
            {
                foreach (var resolvedModel in resolvedNodeSets.Models)
                {
                    if (resolvedModel.NewInThisImport)
                    {
                        var loadedNodesetModels = await NodeModelFactoryOpc.LoadNodeSetAsync(_opcContext,resolvedModel.NodeSet, null, new Dictionary<string, string>(), true);
                        foreach (var nodeSetModel in loadedNodesetModels)
                        {
                            nodeSetModel.Identifier = identifier;
                        }
                        allLoadedNodesetModels.AddRange(loadedNodesetModels);
                    }
                    else
                    {
                        var existingNodeSetModel = _opcContext.GetOrAddNodesetModel(resolvedModel.NodeSet.Models.FirstOrDefault(), false);

                        if (existingNodeSetModel == null)
                        {
                            throw new ArgumentException($"Required NodeSet {existingNodeSetModel} not in database: Inconsistency between file store and db?");
                        }
                        // Get the node state for required models so that UANodeSet.Import works
                        _opcContext.ImportUANodeSet(resolvedModel.NodeSet);
                    }
                }
            }
            catch
            {
                _nodeSetCache.DeleteNewlyAddedNodeSetsFromCache(resolvedNodeSets);
                throw;
            }
            return allLoadedNodesetModels;
        }

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

            NamespaceTable namespaces;
            // Ensure OPC UA model is the first one
            if (exportedNodeSet.NamespaceUris?.Any() == true)
            {
                namespaces = new NamespaceTable(exportedNodeSet.NamespaceUris);
            }
            else
            {
                // Ensure OPC UA model is the first one
                namespaces = new NamespaceTable(new[] { Namespaces.OpcUa });
            }
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
                            PublicationDate = requiredNodeSetModel.PublicationDate != null ? DateTime.SpecifyKind(requiredNodeSetModel.PublicationDate.Value, DateTimeKind.Utc) : default,
                            PublicationDateSpecified = requiredNodeSetModel.PublicationDate != null,
                            RolePermissions = null,
                            AccessRestrictions = 0,
                        };
                        requiredModels.Add(requiredModel);
                    }
                    else
                    {
                        throw new ArgumentException($"Required model {uaNamespace} not found.");
                    }
                }
            }

            var model = new ModelTableEntry
            {
                ModelUri = nodesetModel.ModelUri,
                RequiredModel = requiredModels.ToArray(),
                AccessRestrictions = 0,
                PublicationDate = nodesetModel.PublicationDate != null ? DateTime.SpecifyKind(nodesetModel.PublicationDate.Value, DateTimeKind.Utc) : default,
                PublicationDateSpecified = nodesetModel.PublicationDate != null,
                RolePermissions = null,
                Version = nodesetModel.Version,
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
            var items = new List<UANode>();
            foreach (var node in nodesetModel.AllNodesByNodeId /*.Where(n => n.Value.Namespace == opcNamespace)*/.OrderBy(n => n.Key))
            {
                var result = NodeModelExportOpc.GetUANode(node.Value, namespaces, aliases, nodeIdsUsed);
                items.Add(result.ExportedNode);
                if (result.AdditionalNodes != null)
                {
                    items.AddRange(result.AdditionalNodes);
                }
            }
            return items;
        }


    }
}
