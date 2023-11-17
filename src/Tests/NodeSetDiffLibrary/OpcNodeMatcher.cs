using Org.XmlUnit.Diff;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace NodeSetDiff
{
    internal class OpcNodeMatcher : INodeMatcher
    {
        DefaultNodeMatcher _defaultNodeMatcher;
        private OpcNodeSetXmlUnit _diffHelper;

        public OpcNodeMatcher(OpcNodeSetXmlUnit diffHelper)
        {
            this._diffHelper = diffHelper;
            _defaultNodeMatcher = new DefaultNodeMatcher(new ElementSelector[] { _diffHelper.OpcElementSelector });
        }

        public IEnumerable<KeyValuePair<XmlNode, XmlNode>> Match(IEnumerable<XmlNode> controlNodes, IEnumerable<XmlNode> testNodes)
        {
            if (controlNodes.FirstOrDefault()?.ParentNode.LocalName == "UANodeSet")
            {
                List<KeyValuePair<XmlNode, XmlNode>> matches = new();
                var testRootCache = _diffHelper.TestRootCache;
                foreach(var rootNodeKV in _diffHelper.ControlRootCache)
                {
                    if (testRootCache.TryGetValue(rootNodeKV.Key, out var testNode))
                    {
                        matches.Add(new KeyValuePair<XmlNode, XmlNode>(rootNodeKV.Value, testNode));
                    }
                }
                var remainingControlNodes = controlNodes.Except(matches.Select(m => m.Key)).ToList();
                var remainingTestNodes = testNodes.Except(matches.Select(m => m.Value)).ToList();
                var remainingMatches = _defaultNodeMatcher.Match(remainingControlNodes, remainingTestNodes);
                return remainingMatches.Concat(matches);
            }
            var result = _defaultNodeMatcher.Match(controlNodes, testNodes);
            return result;
        }
    }
}