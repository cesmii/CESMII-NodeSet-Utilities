using System;
using System.Collections.Generic;
using System.Linq;

namespace CESMII.OpcUa.NodeSetModel
{
    public static class NodeSetVersionUtils
    { 
        public static NodeSetModel GetMatchingOrHigherNodeSet(IEnumerable<NodeSetModel> nodeSetsWithSameNamespaceUri, DateTime? publicationDate, string version)
        {
            if (nodeSetsWithSameNamespaceUri.FirstOrDefault()?.ModelUri == "http://opcfoundation.org/UA/")
            {
                // Special versioning rules for core nodesets: only match publication date within version family (1.03, 1.04, 1.05).
                var prefixLength = "0.00".Length;
                string versionPrefix;
                NodeSetModel matchingNodeSet = null;
                if (version?.Length >= prefixLength)
                {
                    versionPrefix = version.Substring(0, prefixLength);
                    var nodeSetsInVersionFamily = nodeSetsWithSameNamespaceUri
                        .Where(n => string.Compare(n.Version.Substring(0, prefixLength), versionPrefix) == 0);
                    matchingNodeSet = GetMatchingOrHigherNodeSetByPublicationDate(nodeSetsInVersionFamily, publicationDate);
                }
                else
                {
                    versionPrefix = null;
                }

                if (matchingNodeSet == null)
                {
                    // no match within version family or no version requested: return the highest available from higher version family
                    matchingNodeSet = nodeSetsWithSameNamespaceUri
                        .Where(n => versionPrefix == null || string.Compare(n.Version.Substring(0, prefixLength), versionPrefix) > 0)
                        .OrderByDescending(n => n.Version.Substring(0, prefixLength))
                        .ThenByDescending(n => n.PublicationDate)
                        .FirstOrDefault();
                }
                return matchingNodeSet;
            }
            else
            {
                return GetMatchingOrHigherNodeSetByPublicationDate(nodeSetsWithSameNamespaceUri, publicationDate);
            }
        }
        public static bool? IsMatchingOrHigherNodeSet(string modelUri, DateTime? modelPublicationDate, string modelVersion, DateTime? publicationDateToMatch, string versionToMatch)
        {
            if (modelUri == "http://opcfoundation.org/UA/")
            {
                if (string.IsNullOrEmpty(versionToMatch))
                {
                    return true;
                }
                // Special versioning rules for core nodesets: only match publication date within version family (1.03, 1.04, 1.05).
                var prefixLength = "0.00".Length;
                if (versionToMatch?.Length >= prefixLength)
                {
                    if (modelVersion.Length < prefixLength)
                    {
                        // Invalid version '{modelVersion}' in OPC UA Core nodeset
                        return null;
                    }
                    var versionPrefix = versionToMatch.Substring(0, prefixLength);
                    var comparison = string.CompareOrdinal(modelVersion.Substring(0, prefixLength), versionPrefix);
                    bool isMatching =  comparison > 0
                    || comparison == 0 &&
                        (publicationDateToMatch == null || modelPublicationDate.Value >= publicationDateToMatch);
                    return isMatching;
                }
                else
                {
                    // Invalid version '{versionToMatch}' for OPC UA Core nodeset
                    return null;
                }
            }
            else
            {
                bool isMatching = (publicationDateToMatch == null || modelPublicationDate == null || modelPublicationDate.Value >= publicationDateToMatch);
                return isMatching;
            }
        }

        private static NodeSetModel GetMatchingOrHigherNodeSetByPublicationDate(IEnumerable<NodeSetModel> nodeSetsWithSameNamespaceUri, DateTime? publicationDate)
        {
            var orderedNodeSets = nodeSetsWithSameNamespaceUri.OrderBy(n => n.PublicationDate);

            if (publicationDate != null && publicationDate.Value != default)
            {
                var matchingNodeSet = orderedNodeSets
                    .FirstOrDefault(nsm => nsm.PublicationDate >= publicationDate);
                return matchingNodeSet;
            }
            else
            {
                var matchingNodeSet = orderedNodeSets
                    .LastOrDefault();
                return matchingNodeSet;
            }
        }

    }
}