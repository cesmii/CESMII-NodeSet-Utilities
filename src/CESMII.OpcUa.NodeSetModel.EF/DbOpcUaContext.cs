/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CESMII.OpcUa.NodeSetModel;
using CESMII.OpcUa.NodeSetModel.Factory.Opc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Export;

namespace CESMII.OpcUa.NodeSetModel.EF
{
    public class DbOpcUaContext : DefaultOpcUaContext
    {
        private DbContext _dbContext;
        private Func<ModelTableEntry, NodeSetModel> _nodeSetFactory;

        public DbOpcUaContext(DbContext appDbContext, ILogger logger, Func<ModelTableEntry, NodeSetModel> nodeSetFactory = null)
            : base(logger)
        {
            this._dbContext = appDbContext;
            this._nodeSetFactory = nodeSetFactory;
        }
        public DbOpcUaContext(DbContext appDbContext, SystemContext systemContext, NodeStateCollection importedNodes, Dictionary<string, NodeSetModel> nodesetModels, ILogger logger, Func<ModelTableEntry, NodeSetModel> nodeSetFactory = null)
            : base (systemContext, importedNodes, nodesetModels, logger)
        {
            this._dbContext = appDbContext;
            this._nodeSetFactory = nodeSetFactory;   
        }

        public override NodeModel GetModelForNode(string nodeId)
        {
            var model = base.GetModelForNode(nodeId);
            if (model != null) return model;

            var uaNamespace = NodeModelUtils.GetNamespaceFromNodeId(nodeId);
            NodeModel nodeModelDb;
            if (_nodesetModels.TryGetValue(uaNamespace, out var nodeSet))
            {
                nodeModelDb = _dbContext.Set<NodeModel>().FirstOrDefault(nm => nm.NodeId == nodeId && nm.NodeSet.ModelUri == nodeSet.ModelUri && nm.NodeSet.PublicationDate == nodeSet.PublicationDate);
                nodeModelDb?.NodeSet.AllNodesByNodeId.Add(nodeModelDb.NodeId, nodeModelDb);
            }
            else
            {
                nodeModelDb = _dbContext.Set<NodeModel>().FirstOrDefault(nm => nm.NodeId == nodeId && nm.NodeSet.ModelUri == uaNamespace);
                nodeSet = GetOrAddNodesetModel(new ModelTableEntry { ModelUri = nodeModelDb.NodeSet.ModelUri, PublicationDate = nodeModelDb.NodeSet.PublicationDate ?? DateTime.MinValue, PublicationDateSpecified = nodeModelDb.NodeSet.PublicationDate != null });
                nodeModelDb?.NodeSet.AllNodesByNodeId.Add(nodeModelDb.NodeId, nodeModelDb);
            }
            return nodeModelDb;
        }

        public override NodeSetModel GetOrAddNodesetModel(ModelTableEntry model, bool createNew = true)
        {
            if (!_nodesetModels.TryGetValue(model.ModelUri, out var nodesetModel))
            {
                var existingNodeSet = GetMatchingOrHigherNodeSetAsync(model.ModelUri, model.PublicationDateSpecified ? model.PublicationDate : null).Result;
                if (existingNodeSet != null)
                {
                    _nodesetModels.Add(existingNodeSet.ModelUri, existingNodeSet);
                    nodesetModel = existingNodeSet;
                }
            }
            if (nodesetModel == null && createNew)
            {
                if (_nodeSetFactory == null)
                {
                    nodesetModel = base.GetOrAddNodesetModel(model);
                    if (nodesetModel.PublicationDate == null)
                    {
                        // Primary Key value can not be null
                        nodesetModel.PublicationDate = DateTime.MinValue;
                    }
                }
                else
                {
                    nodesetModel = _nodeSetFactory.Invoke(model);
                    if (nodesetModel != null)
                    {
                        if (nodesetModel.ModelUri != model.ModelUri)
                        {
                            throw new Exception($"Created mismatching nodeset: expected {model.ModelUri} created {nodesetModel.ModelUri}");
                        }
                        _nodesetModels.Add(nodesetModel.ModelUri, nodesetModel);
                    }
                }
            }
            return nodesetModel;
        }

        public Task<NodeSetModel> GetMatchingOrHigherNodeSetAsync(string modelUri, DateTime? publicationDate)
        {
            return GetMatchingOrHigherNodeSetAsync(_dbContext, modelUri, publicationDate);
        }
        public static Task<NodeSetModel> GetMatchingOrHigherNodeSetAsync(DbContext dbContext, string modelUri, DateTime? publicationDate)
        {
            var matchingNodeSet = dbContext.Set<NodeSetModel>().AsQueryable().Where(nsm => nsm.ModelUri == modelUri && (publicationDate == null || nsm.PublicationDate >= publicationDate)).OrderBy(nsm => nsm.PublicationDate).FirstOrDefaultAsync();
            return matchingNodeSet;
        }
    }
}
