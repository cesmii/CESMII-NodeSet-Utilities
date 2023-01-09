﻿/* Author:      Chris Muench, C-Labs
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

namespace CESMII.OpcUa.NodeSetImporter
{
    //Glossary of Terms:
    //-----------------------------------
    //NodeSet - Container File of one or more Models
    //Model - a unique OPC UA Model identified with a unique NamespaceUri/ModelUri. A model can be spanned across multiple NodeSet (files)
    //Namespace - the unique identifier of a Model (also called ModelUri)
    //UAStandardModel - A Model that has been standardized by the OPC UA Foundation and can be found in the official schema store: https://files.opcfoundation.org/schemas/
    //UANodeSetImporter - Imports one or more OPC UA NodeSets resulting in a "NodeSetImportResult" containing all found Models and a list of missing dependencies

    /// <summary>
    /// Main Importer class importing NodeSets 
    /// </summary>
    public static class UANodeSetImporter
    {
        /// <summary>
        /// Imports NodeSets from Files resolving dependencies using already uploaded NodeSets
        /// </summary>
        /// <param name="NodeSetCacheSystem">This interface can be used to override the default file cache of the Importer, i.e with a Database cache</param>
        /// <param name="previousResults">If null, a new resultset will be created. If not null already uploaded NodeSets can be augmented with New NodeSets referred in the FileNames</param>
        /// <param name="nodeSetFilenames">List of full paths to uploaded NodeSets</param>
        /// <param name="nodeSetStreams">List of streams containing NodeSets</param>
        /// <param name="FailOnExisting">Default behavior is that all Models in NodeSets are returned even if they have been imported before. If set to true, the importer will fail if it has imported a nodeset before and does not cache nodeset if they have missing dependencies</param>
        /// <param name="TenantID">If the import has Multi-Tenant Cache, the tenant ID has to be set here</param>
        /// <returns></returns>
        public static UANodeSetImportResult ImportNodeSetFiles(IUANodeSetCache NodeSetCacheSystem, UANodeSetImportResult previousResults, List<string> nodeSetFilenames, bool FailOnExisting = false, object TenantID = null,
                IUANodeSetResolver nodeSetResolver = null)
        {
            return ImportNodeSets(NodeSetCacheSystem, previousResults, nodeSetFilenames.Select(f => File.ReadAllText(f)), FailOnExisting, TenantID, nodeSetResolver);
        }
        /// <summary>
        /// Imports NodeSets from Files resolving dependencies using already uploaded NodeSets
        /// </summary>
        /// <param name="NodeSetCacheSystem">This interface can be used to override the default file cache of the Importer, i.e with a Database cache</param>
        /// <param name="previousResults">If null, a new resultset will be created. If not null already uploaded NodeSets can be augmented with New NodeSets referred in the FileNames</param>
        /// <param name="nodeSetFilenames">List of full paths to uploaded NodeSets</param>
        /// <param name="nodeSetStreams">List of streams containing NodeSets</param>
        /// <param name="FailOnExisting">Default behavior is that all Models in NodeSets are returned even if they have been imported before. If set to true, the importer will fail if it has imported a nodeset before and does not cache nodeset if they have missing dependencies</param>
        /// <param name="TenantID">If the import has Multi-Tenant Cache, the tenant ID has to be set here</param>
        /// <returns></returns>
        public static UANodeSetImportResult ImportNodeSets(IUANodeSetCache NodeSetCacheSystem, UANodeSetImportResult previousResults, IEnumerable<Stream> nodeSetStreams, bool FailOnExisting = false, object TenantID = null,
                IUANodeSetResolver nodeSetResolver = null)
        {
            return ImportNodeSets(NodeSetCacheSystem, previousResults, nodeSetStreams.Select(s =>
            {
                using (var sr = new StreamReader(s, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }), FailOnExisting, TenantID, nodeSetResolver);
        }
        /// <summary>
        /// Imports NodeSets from Files resolving dependencies using already uploaded NodeSets
        /// </summary>
        /// <param name="NodeSetCacheSystem">This interface can be used to override the default file cache of the Importer, i.e with a Database cache</param>
        /// <param name="previousResults">If null, a new resultset will be created. If not null already uploaded NodeSets can be augmented with New NodeSets referred in the FileNames</param>
        /// <param name="nodeSetFilenames">List of full paths to uploaded NodeSets</param>
        /// <param name="nodeSetStreams">List of streams containing NodeSets</param>
        /// <param name="FailOnExisting">Default behavior is that all Models in NodeSets are returned even if they have been imported before. If set to true, the importer will fail if it has imported a nodeset before and does not cache nodeset if they have missing dependencies</param>
        /// <param name="TenantID">If the import has Multi-Tenant Cache, the tenant ID has to be set here</param>
        /// <returns></returns>
        public static UANodeSetImportResult ImportNodeSets(IUANodeSetCache NodeSetCacheSystem, UANodeSetImportResult previousResults, IEnumerable<string> nodeSetsXml, bool FailOnExisting = false, object TenantID = null,
                IUANodeSetResolver nodeSetResolver = null)
        {
            UANodeSetImportResult results = previousResults;
            if (results == null)
                results = new UANodeSetImportResult();
            if (NodeSetCacheSystem == null)
                NodeSetCacheSystem = new UANodeSetFileCache();
            results.ErrorMessage = "";
            List<ModelNameAndVersion> previousMissingModels = new List<ModelNameAndVersion>();
            try
            {
                bool rerun;
                do
                {
                    rerun = false;
                    bool NewNodeSetFound = false;
                    if (nodeSetsXml != null)
                    {
                        // Must enumerate the nodeSetsXml only once in case the caller creates/loads strings as needed (streams of files)
                        foreach (var nodeSetXml in nodeSetsXml)
                        {
                            var JustFoundNewNodeSet = NodeSetCacheSystem.AddNodeSet(results, nodeSetXml, TenantID, true);
                            NewNodeSetFound |= JustFoundNewNodeSet;
                        }
                        nodeSetsXml = null;
                    }

                    if (!NewNodeSetFound && FailOnExisting)
                    {
                        string names = String.Join(", ", results.Models.Select(m => m.NameVersion));
                        results.ErrorMessage = $"All selected NodeSets or newer versions of them ({names}) have already been imported";
                        return results;
                    }
                    if (results.Models.Count == 0)
                    {
                        results.ErrorMessage = "No Nodesets specified in either nodeSetFilenames or nodeSetStreams";
                        return results;
                    }
                    results.ResolveDependencies();

                    if (results?.MissingModels?.Any() == true)
                    {
                        foreach (var t in results.MissingModels.ToList())
                        {
                            rerun |= NodeSetCacheSystem.GetNodeSet(results, t, TenantID);
                        }
                        results.ResolveDependencies();

                        if (!rerun && results.MissingModels.Any())
                        {
                            // No more cached models were added, but we are still missing models: invoke the resolver if provided
                            if (nodeSetResolver != null && !results.MissingModels.SequenceEqual(previousMissingModels))
                            {
                                previousMissingModels = results.MissingModels.ToList();
                                try
                                {
                                    var newNodeSetsXml = nodeSetResolver.ResolveNodeSetsAsync(results.MissingModels.ToList()).Result;
                                    if (newNodeSetsXml?.Any() == true)
                                    {
                                        nodeSetsXml = newNodeSetsXml;
                                        rerun = true;
                                        continue;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (results.ErrorMessage.Length > 0) results.ErrorMessage += ", ";
                                    results.ErrorMessage += $"Error resolving missing nodesets: {ex.Message}";
                                }
                            }
                            if (results.ErrorMessage.Length > 0) results.ErrorMessage += ", ";
                            results.ErrorMessage += string.Join(",", results.MissingModels);
                        }
                        if (!string.IsNullOrEmpty(results.ErrorMessage))
                        {
                            results.ErrorMessage = $"The following NodeSets are required: " + results.ErrorMessage;
                            //We must delete newly cached models as they need to be imported again into the backend
                            if (FailOnExisting)
                                NodeSetCacheSystem.DeleteNewlyAddedNodeSetsFromCache(results);
                        }
                    }

                    results.Models = results.Models.OrderBy(s => s.Dependencies.Count).ToList();
                } while (rerun && results.MissingModels.Any());
            }
            catch (Exception ex)
            {
                results.ErrorMessage = ex.Message;
            }

            return results;
        }


    }
}
