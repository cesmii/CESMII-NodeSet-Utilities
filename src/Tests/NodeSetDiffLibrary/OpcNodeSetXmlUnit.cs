using Org.XmlUnit.Builder;
using Org.XmlUnit.Diff;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

using Opc.Ua;
using Opc.Ua.Export;
using System.Text;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace NodeSetDiff
{
    public class OpcNodeSetXmlUnit
    {
#pragma warning disable S1075 // URIs should not be hardcoded - these are not URLs representing endpoints, but OPC model identifiers (URIs) that are static and stable
        private const string strUANodeSetSchemaUri = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"; //NOSONAR
        private const string strUATypesSchemaUri = "http://opcfoundation.org/UA/2008/02/Types.xsd"; //NOSONAR
        private const string strOpcNamespaceUri = "http://opcfoundation.org/UA/"; //NOSONAR
#pragma warning restore S1075 // URIs should not be hardcoded

        private readonly Dictionary<string, string> _controlAliases;
        private readonly NamespaceTable _controlNamespaces;
        private readonly Dictionary<string, string> _testAliases;
        private readonly NamespaceTable _testNamespaces;
        private readonly string _controlFile;
        private readonly string _testFile;
        private readonly bool _ignoreRequiredVersion;
        private XDocument _controlDoc;
        private XDocument _testDoc;
        private UANodeSet _controlNodeSet;
        public UANodeSet ControlNodeSet
        {
            get
            {
                if (_controlNodeSet == null)
                {
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(_controlFile));
                    _controlNodeSet = UANodeSet.Read(ms);
                }
                return _controlNodeSet;
            }
        }
        private UANodeSet _testNodeSet;
        public UANodeSet TestNodeSet
        {
            get
            {
                if (_testNodeSet == null)
                {
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(_testFile));
                    _testNodeSet = UANodeSet.Read(ms);
                }
                return _testNodeSet;
            }
        }

        public OpcNodeSetXmlUnit(Dictionary<string, string> controlAliases, NamespaceTable controlNamespaces, Dictionary<string, string> testAliases, NamespaceTable testNamespaces,
            string controlFile, string testFile, bool ignoreRequiredVersion = false)
        {
            this._controlAliases = controlAliases;
            this._controlNamespaces = controlNamespaces;
            this._testAliases = testAliases;
            this._testNamespaces = testNamespaces;
            this._controlFile = controlFile;
            this._testFile = testFile;
            this._ignoreRequiredVersion = ignoreRequiredVersion;
        }
        public ComparisonResult OpcNodeSetDifferenceEvaluator(Comparison c, ComparisonResult outcome)
        {
            if (outcome == ComparisonResult.EQUAL) return outcome;
            if (c.ControlDetails.Target?.Name == "BrowseName")
            {
                if (c.ControlDetails.Target.Value?.Substring(c.ControlDetails.Target.Value.IndexOf(':') + 1)
                   == c.TestDetails.Target.Value?.Substring(c.TestDetails.Target.Value.IndexOf(':') + 1))
                {
                    return ComparisonResult.EQUAL;
                }
                return outcome;
            }
            if (c.Type == ComparisonType.CHILD_NODELIST_SEQUENCE)
            {
                var xPath = Regex.Replace(c.ControlDetails.XPath, "\\[\\d*\\]", "");
                if (new[] {
                    "/UANodeSet",
                    "/UANodeSet/",
                    "/UANodeSet/Aliases/",
                    "/UANodeSet/NamespaceUris/",
                    "/UANodeSet/Models/",
                    "/UANodeSet/Models/Model/",
                    "/UANodeSet/UAVariable/", "/UANodeSet/UAVariableType/", "/UANodeSet/UAObjectType/", "/UANodeSet/UADataType/", "/UANodeSet/UAReferenceType/", "/UANodeSet/UAObject/", "/UANodeSet/UAMethod/",
                    "/UANodeSet/UAVariable/References/", "/UANodeSet/UAVariableType/References/", "/UANodeSet/UAObjectType/References/", "/UANodeSet/UADataType/References/", "/UANodeSet/UAReferenceType/References/", "/UANodeSet/UAObject/References/","/UANodeSet/UAMethod/References/",
                    }
                    .Any(p => xPath.StartsWith(p) && xPath.Substring(p.Length).IndexOf("/") < 0))
                {
                    return ComparisonResult.EQUAL;
                }
                if (xPath.StartsWith("/UANodeSet/UADataType/Definition/Field"))
                {
                    if (c.ControlDetails.Target is XmlElement targetElement)
                    {
                        var browseName = targetElement.ParentNode.ParentNode.Attributes["BrowseName"];
                        if (browseName?.Value?.EndsWith("Mask") == true || browseName?.Value?.Contains("Enum") == true)
                        {
                            // Heuristic to reduce false ordering errors for enumerations and option sets
                            return ComparisonResult.EQUAL;
                        }
                    }
                }
                if (xPath.StartsWith("/UANodeSet/UAVariable/Value/ListOfExtensionObject/ExtensionObject/Body/EnumValueType/"))
                {
                    // Enums get reordered by Enum value which may not match the original XML but does not change the semantics
                    return ComparisonResult.EQUAL;
                }

                if (c.ControlDetails.Target.Name == "Text")
                {
                    if (c.ControlDetails.XPath.Split('/', 4)?[3] == c.TestDetails.XPath.Split('/', 4)?[3])
                    {
                        if (((int)c.ControlDetails.Value) == ((int)c.TestDetails.Value) + 1 && c.ControlDetails.Target.PreviousSibling.LocalName == "Locale" && c.ControlDetails.Target.Value == null)
                        {
                            // treat null locale vs missing locale element as same
                            return ComparisonResult.EQUAL;
                        }
                        //return ComparisonResult.EQUAL;
                    }
                }
                if (xPath.StartsWith("/UANodeSet/UAVariable/Value/ListOfExtensionObject/ExtensionObject"))
                {
                    if (c.ControlDetails.Target is XmlElement targetElement && targetElement["Body", Namespaces.OpcUaXsd]?["EnumValueType", Namespaces.OpcUaXsd] != null)
                    {
                        // Enums get reordered by Enum value which may not match the original XML but does not change the semantics
                        return ComparisonResult.EQUAL;

                    }
                }
                if (xPath.StartsWith("/UANodeSet/UAVariable/Value/ExtensionObject/Body")
                    || xPath.StartsWith("/UANodeSet/UAVariableType/Value/ExtensionObject/Body"))
                {
                    //Structures are not ordered
                    // TODO verify array order
                    return ComparisonResult.EQUAL;
                }
            }
            if (c.Type == ComparisonType.CHILD_NODELIST_LENGTH || c.Type == ComparisonType.ELEMENT_NUM_ATTRIBUTES)
            {
                //if (new[] { "References", "UAVariable", "UAVariableType", "UAObject", "UAObjectType", "UADataType", "UAMethod", "UAReferenceType", "UANodeSet", "Model" }.Contains(c.ControlDetails.Target.Name))
                {
                    return ComparisonResult.EQUAL;
                }
            }
            if (c.Type == ComparisonType.CHILD_LOOKUP)
            {
                if (c.ControlDetails.Target is XmlElement || c.TestDetails.Target is XmlElement)
                {
                    if (c.ControlDetails.Target == null || c.TestDetails.Target == null)
                    {
                        Comparison.Detail detailsNonNull;
                        Comparison.Detail detailsNull;
                        Dictionary<string, string> aliasesNonNull, aliasesNull;
                        NamespaceTable namespacesNonNull, namespacesNull;
                        if (c.ControlDetails.Target == null)
                        {
                            detailsNonNull = c.TestDetails;
                            aliasesNonNull = _testAliases;
                            namespacesNonNull = _testNamespaces;

                            detailsNull = c.ControlDetails;
                            aliasesNull = _controlAliases;
                            namespacesNull = _controlNamespaces;
                        }
                        else
                        {
                            detailsNonNull = c.ControlDetails;
                            aliasesNonNull = _controlAliases;
                            namespacesNonNull = _controlNamespaces;

                            detailsNull = c.TestDetails;
                            aliasesNull = _testAliases;
                            namespacesNull = _testNamespaces;
                        }
                        if (detailsNonNull.Target.NamespaceURI == strUANodeSetSchemaUri || string.IsNullOrEmpty(detailsNonNull.Target.NamespaceURI))
                        {
                            if (detailsNonNull.Target.Name == "References" && detailsNonNull.Target.ChildNodes.Count == 0)
                            {
                                return ComparisonResult.EQUAL;
                            }
                            if (detailsNonNull.Target.Name == "Description" && detailsNonNull.Target.InnerXml == "")
                            {
                                return ComparisonResult.EQUAL;
                            }
                            if (detailsNonNull.Target.Name == "Locale")
                            {
                                return ComparisonResult.EQUAL;
                            }

                            if (detailsNonNull.Target.LocalName == "Reference")
                            {
                                // TODO apply aliases
                                var referencedNodeId = NormalizeNodeId(detailsNonNull.Target.InnerText, aliasesNonNull, namespacesNonNull);
                                var referenceType = NormalizeNodeId(detailsNonNull.Target.Attributes.GetNamedItem("ReferenceType").Value, aliasesNonNull, namespacesNonNull);
                                var isForward = (detailsNonNull.Target.Attributes.GetNamedItem("IsForward")?.Value?.ToLowerInvariant() ?? "true") != "false";
                                var nodeId = NormalizeNodeId(detailsNonNull.Target.ParentNode.ParentNode.Attributes.GetNamedItem("NodeId").Value, aliasesNonNull, namespacesNonNull);

                                // Check for matching reverse references (IsForward true/false)
                                var nullDoc = c.ControlDetails.Target == null ? _controlDoc : _testDoc;

                                var nodes = nullDoc.Root.Descendants().Where(e => NormalizeNodeId(e.Attribute("NodeId")?.Value, aliasesNull, namespacesNull) == referencedNodeId).ToList();
                                foreach (var node in nodes)
                                {
                                    var matchingRefs = node.Descendants().Where(e => e.Name.LocalName == "Reference" && (e?.Attribute("IsForward")?.Value?.ToLowerInvariant() ?? "true") == (isForward ? "false" : "true")
                                        && NormalizeNodeId(e.Value, aliasesNull, namespacesNull) == nodeId
                                        && NormalizeNodeId(e.Attribute("ReferenceType")?.Value, aliasesNull, namespacesNull) == referenceType
                                        ).ToList();
                                    if (matchingRefs.Any())
                                    {
                                        return ComparisonResult.EQUAL;
                                    }
                                }
                            }
                            if (detailsNonNull.Target.LocalName == "Alias")
                            {
                                // Differences in Alias don't change the semantics of the nodeset: ignore
                                return ComparisonResult.EQUAL;
                            }
                            if (detailsNonNull.Target.LocalName == "UAVariable" || detailsNonNull.Target.LocalName == "UAObject")
                            {
                                // Is this node referenced by one of the type system OPC UA nodes: ignore these are deprecated
                                if (IsChildOfTypeSystemNode(detailsNonNull.Target, aliasesNonNull, namespacesNonNull))
                                {
                                    return ComparisonResult.EQUAL;
                                }
                                if (new string[] {

                                    BrowseNames.NamespaceMetadataType, BrowseNames.NamespaceUri, BrowseNames.NamespaceVersion, BrowseNames.NamespacePublicationDate, BrowseNames.IsNamespaceSubset, BrowseNames.StaticNodeIdTypes, BrowseNames.StaticNumericNodeIdRange, BrowseNames.StaticStringNodeIdPattern,
                                    BrowseNames.DefaultJson, BrowseNames.DefaultXml, BrowseNames.DefaultBinary,
                                }
                                    .Contains(detailsNonNull.Target.Attributes?.GetNamedItem("BrowseName")?.Value)
                                    && detailsNonNull.Target.Attributes["NodeId"].Value.Contains(";g="))
                                {
                                    // TODO check for extension indicating generator?
                                    // Metadata or encoding id properties generated by node set model: ignore diff
                                    return ComparisonResult.EQUAL;
                                }
                            }

                        }
                        else if (detailsNonNull.Target.NamespaceURI == strUATypesSchemaUri)
                        {
                            if (detailsNonNull.Target.LocalName == "Locale" || detailsNonNull.Target.LocalName == "Description")
                            {
                                if (string.IsNullOrEmpty(detailsNonNull.Target.InnerText))
                                {
                                    // Consider "" equivalent to null for these elements
                                    return ComparisonResult.EQUAL;
                                }
                            }
                        }
                    }
                }
            }
            if (c.Type == ComparisonType.ATTR_NAME_LOOKUP)
            {
                if (c.ControlDetails.XPath.EndsWith("@Locale"))
                {
                    if (c.ControlDetails.Target.Attributes["Locale"]?.Value == "en"
                    && string.IsNullOrEmpty(c.TestDetails.Target?.Attributes["Locale"]?.Value))
                    {
                        return ComparisonResult.EQUAL;
                    }
                }
                if (outcome == ComparisonResult.DIFFERENT)
                {
                    if (c.ControlDetails.Target.Value == null && c.TestDetails.XPath.EndsWith("@DataType") && IsEqualNodeId("i=24", c.TestDetails.Target.Attributes["DataType"]?.Value.ToString()))
                    {
                        return ComparisonResult.EQUAL;
                    }
                }
            }
            if (c.Type == ComparisonType.ATTR_VALUE)
            {
                if (IsEqualNodeId(c.ControlDetails.Target.Value, c.TestDetails.Target.Value))
                {
                    return ComparisonResult.EQUAL;
                }
                if (c.ControlDetails.Target?.LocalName == "LastModified" || c.TestDetails.Target?.LocalName == "LastModified")
                {
                    return ComparisonResult.EQUAL;
                }
                if (_ignoreRequiredVersion)
                {
                    if (c.ControlDetails.Target?.LocalName == "Version" || c.TestDetails.Target?.LocalName == "Version")
                    {
                        if (c.ControlDetails.XPath.Contains("/RequiredModel[") && c.ControlDetails.Target?.Value?.ToString()?.CompareTo(c.TestDetails.Target?.Value?.ToString()) <= 0)
                        {
                            return ComparisonResult.EQUAL;
                        }
                    }
                    if (c.ControlDetails.Target?.LocalName == "PublicationDate" || c.TestDetails.Target?.LocalName == "PublicationDate")
                    {
                        if (c.ControlDetails.XPath.Contains("/RequiredModel["))
                        {
                            return ComparisonResult.EQUAL;
                        }
                    }
                }
                if (c.ControlDetails.Target?.LocalName == "ArrayDimensions" && c.TestDetails.Target?.LocalName == "ArrayDimensions"
                    && (c.ControlDetails.Target?.InnerXml == "0" || c.TestDetails.Target?.InnerXml == "0"))
                {
                    return ComparisonResult.EQUAL;
                }
            }
            if (c.Type == ComparisonType.TEXT_VALUE)
            {
                if (c.ControlDetails.Target.ParentNode.Name == "Reference")
                {
                    if (IsEqualNodeId(c.ControlDetails.Target.Value, c.TestDetails.Target.Value))
                    {
                        return ComparisonResult.EQUAL;
                    }
                }
                if (outcome != ComparisonResult.EQUAL && c.ControlDetails.Target?.Value?.EndsWith(" ") == true)
                {
                    if (c.ControlDetails.Target?.Value?.TrimEnd() == c.TestDetails.Target?.Value)
                    {
                        return ComparisonResult.EQUAL;
                    }
                }
                if (outcome != ComparisonResult.EQUAL
                && c.ControlDetails?.Target?.ParentNode?.LocalName == "ByteString"
                && c.TestDetails?.Target?.ParentNode?.LocalName == "ByteString"
                && c.ControlDetails?.Target?.Value?.Where(c => !Char.IsWhiteSpace(c))?
                    .SequenceEqual(c.TestDetails?.Target?.Value?.Where(c => !Char.IsWhiteSpace(c))) == true)
                {
                    return ComparisonResult.EQUAL;
                }
                if (outcome != ComparisonResult.EQUAL)
                {
                    if (c.ControlDetails.Target?.Value?.TrimEnd() == c.TestDetails.Target?.Value?.TrimEnd())
                    {
                        return ComparisonResult.EQUAL;
                    }
                }
            }
            if (c.Type == ComparisonType.NAMESPACE_PREFIX)
            {
                // XML namespaces can be different as long as the xml namespace URLs match
                return ComparisonResult.EQUAL;
            }
            return outcome;
        }

        private static bool IsChildOfTypeSystemNode(XmlNode uaNode, Dictionary<string, string> aliases, NamespaceTable namespaces)
        {
            if (uaNode == null) return false;
            var reverseReferences = GetReverseReferences(uaNode);
            if (reverseReferences.Any(n => n.InnerText == "i=93" || n.InnerText == "i=92") == true)
            {
                return true;
            }
            foreach (var reference in reverseReferences)
            {
                var referencedUaNode = uaNode.ParentNode.ChildNodes.Cast<XmlNode>().Where(n => NormalizeNodeId(n.Attributes?.GetNamedItem("NodeId")?.Value, aliases, namespaces) == NormalizeNodeId(reference.InnerText, aliases, namespaces))?.FirstOrDefault();
                if (IsChildOfTypeSystemNode(referencedUaNode, aliases, namespaces))
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<XmlElement> GetReverseReferences(XmlNode uaNode)
        {
            return uaNode.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => n.LocalName == "References").ChildNodes.Cast<XmlNode>().Where(n => n is XmlElement).Cast<XmlElement>().Where(n => n.Attributes.GetNamedItem("IsForward")?.Value?.ToLowerInvariant() == "false");
        }

        public bool OpcElementSelector(XmlElement c, XmlElement t)
        {
            if (_controlDoc == null)
            {
                _controlDoc = XDocument.Parse(c.OwnerDocument.OuterXml);
            }
            if (_testDoc == null)
            {
                _testDoc = XDocument.Parse(t.OwnerDocument.OuterXml);
            }
            if (c.NodeType == XmlNodeType.Element)
            {
                if (c.Name == t.Name || (c.LocalName == t.LocalName && c.NamespaceURI == t.NamespaceURI))
                {
                    switch (c.LocalName)
                    {
                        case "UANodeSet":
                        case "NamespaceUris":
                        case "Models":
                        case "References":
                        case "Aliases":
                        case "DisplayName":
                        case "Documentation":
                        case "Description":
                        case "Value":
                        case "String":
                        case "DateTime":
                        case "Boolean":
                            return true;
                        case "Model":
                        case "Uri":
                            return c.InnerText == t.InnerText;
                        case "RequiredModel":
                            return c.GetAttribute("ModelUri") == t.GetAttribute("ModelUri");
                        case "Alias":
                            return c.GetAttribute("Alias") == t.GetAttribute("Alias");
                        case "Reference":
                            return
                                IsEqualNodeId(c.GetAttribute("ReferenceType"), t.GetAttribute("ReferenceType"))
                                && IsEqualNodeId(c.InnerText, t.InnerText);
                        case "Definition":
                        case "Field":
                            return
                                c.GetAttribute("Name") == t.GetAttribute("Name");
                        case "UAObject":
                        case "UAObjectType":
                        case "UAMethod":
                        case "UAVariableType":
                        case "UAReferenceType":
                        case "UADataType":
                            return IsEqualNodeId(c.GetAttribute("NodeId"), t.GetAttribute("NodeId"));
                        case "UAVariable":
                            {
                                if (IsEqualNodeId(c.GetAttribute("NodeId"), t.GetAttribute("NodeId")))
                                {
                                    return true;
                                }
                                //if (c.GetAttribute("BrowseName") == BrowseNames.NamespaceUri && c.GetAttribute("BrowseName") == t.GetAttribute("BrowseName"))
                                //{
                                //    // Generated meta data property has different GUID on every export: match purely on browsename and ignore nodeid
                                //    if (t.GetAttribute("NodeId").Contains("g="))
                                //    {
                                //        return true;
                                //    }
                                //}
                                return false;
                            }
                        case "ExtensionObject":
                            var cEnumName = c["Body", Namespaces.OpcUaXsd]?["EnumValueType", Namespaces.OpcUaXsd]?["DisplayName", Namespaces.OpcUaXsd]?.InnerText;
                            var tEnumName = t["Body", Namespaces.OpcUaXsd]?["EnumValueType", Namespaces.OpcUaXsd]?["DisplayName", Namespaces.OpcUaXsd]?.InnerText;
                            if (cEnumName != null && cEnumName != tEnumName)
                            {
                                return false;
                            }
                            return true;
                    }
                    return true;
                }
                return false;
            }
            return true;
        }

        private bool IsEqualNodeId(string cNodeId, string tNodeId)
        {
            var bEqual = NormalizeNodeId(cNodeId, _controlAliases, _controlNamespaces) == NormalizeNodeId(tNodeId, _testAliases, _testNamespaces);
            return bEqual;
        }

        private static string NormalizeNodeId(string nodeIdStr, Dictionary<string, string> aliases, NamespaceTable namespaces)
        {
            if (string.IsNullOrEmpty(nodeIdStr)) return nodeIdStr;
            if (aliases.TryGetValue(nodeIdStr, out var value))
            {
                nodeIdStr = value;
            }
            //foreach (var alias in aliases.OrderByDescending(a => a.Key.Length))
            //{
            //    nodeIdStr = nodeIdStr.Replace(alias.Key, alias.Value);
            //}
            try
            {
                var nodeId = ExpandedNodeId.Parse(nodeIdStr, namespaces);
                var exNodeId = new ExpandedNodeId(nodeId, namespaces.GetString(nodeId.NamespaceIndex));
                return exNodeId.ToString();
            }
            catch (ServiceResultException)
            {
                return nodeIdStr;
            }
        }
        public static Diff DiffNodeSetFiles(string controlFileName, string testFileName)
        {
            var controlInfo = LoadNamespaces(controlFileName);
            var testInfo = LoadNamespaces(testFileName);

            var controlNamespaces = controlInfo.Item1;
            var controlAliases = controlInfo.Item2;

            var testNamespaces = testInfo.Item1;
            var testAliases = testInfo.Item2;

            var controlFile = File.ReadAllText(controlFileName);
            var testFile = File.ReadAllText(testFileName);

            controlFile = NormalizeAliasesAndNamespaces(controlFile, controlAliases, controlNamespaces);
            testFile = NormalizeAliasesAndNamespaces(testFile, testAliases, testNamespaces);

            var aliasTestFile = testFile;
            foreach(var alias in controlAliases) 
            {
                aliasTestFile = aliasTestFile
                    .Replace($"ReferenceType=\"{alias.Value}\"", $"ReferenceType=\"{alias.Key}\"")
                    .Replace($"DataType=\"{alias.Value}\"", $"DataType=\"{alias.Key}\"")
                    ;
            }
            GeneratedSortedXmlFile(testFileName, aliasTestFile, controlFile);
            //GeneratedSortedXmlFile(testFileName, aliasTestFile);
            //GeneratedSortedXmlFile(controlFileName, controlFile);

            var diffHelper = new OpcNodeSetXmlUnit(controlAliases, controlNamespaces, testAliases, testNamespaces, controlFile, testFile);

            Diff d = DiffBuilder
                     .Compare(Input.FromString(controlFile))
                     .WithTest(Input.FromString(testFile))
                     .CheckForSimilar()
                     .WithDifferenceEvaluator(diffHelper.OpcNodeSetDifferenceEvaluator)
                     .WithNodeMatcher(new DefaultNodeMatcher(new ElementSelector[] { diffHelper.OpcElementSelector }))
                     .WithAttributeFilter(a =>
                        !((
                            a.LocalName == "ParentNodeId" // ParentNodeId is not part of the address space/information model, ignore entirely
                            || a.LocalName == "UserAccessLevel" // UserAccessLevel is deprecated, and currently not propertly handled by the UA Importer (https://github.com/OPCFoundation/UA-.NETStandard/issues/1918) : ignore entirely
                            )
                        && new[] { "UAObject", "UAVariable", "UAMethod", "UAView", }.Contains(a.OwnerElement.LocalName)))
                     .Build();
            return d;
        }

        private static void GeneratedSortedXmlFile(string fileName, string xml, string controlXml = null)
        {
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsmgr.AddNamespace("ua", "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd");
            var nodeSet = xmlDoc.SelectSingleNode("ua:UANodeSet", nsmgr);

            XmlDocument controlDoc = null;
            XmlNode controlNodeSet = null;
            XmlNamespaceManager controlNsmgr;
            if (controlXml != null)
            {
                controlDoc = new XmlDocument();
                controlDoc.LoadXml(controlXml);
                controlNsmgr = new XmlNamespaceManager(controlDoc.NameTable);
                controlNsmgr.AddNamespace("ua", "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd");
                controlNodeSet = controlDoc.SelectSingleNode("ua:UANodeSet", controlNsmgr);
            }

            var allNodes = new List<XmlNode>();
            var enumerator = nodeSet.ChildNodes.GetEnumerator();
            while (enumerator.MoveNext())
            {
                allNodes.Add((XmlNode)enumerator.Current);
            }
            nodeSet.RemoveAll();
            IEnumerable<XmlNode> orderedNodes;
            if (controlNodeSet != null)
            {
                // Order the nodes in the order of the control nodeset
                var controlEnum = controlNodeSet.ChildNodes.GetEnumerator();
                while (controlEnum.MoveNext())
                {
                    var controlNode = (XmlNode)controlEnum.Current;
                    var controlNodeNodeId = (controlNode as XmlElement)?.GetAttribute("NodeId");
                    if (!string.IsNullOrEmpty(controlNodeNodeId))
                    {
                        var matchingNode = allNodes.FirstOrDefault(n => (n as XmlElement)?.GetAttribute("NodeId") == controlNodeNodeId);
                        if (matchingNode != null)
                        {
                            nodeSet.AppendChild(matchingNode);
                            allNodes.Remove(matchingNode);
                            // Add references in the control nodeset order
                            var referencesNode = matchingNode.SelectSingleNode("ua:References", nsmgr);
                            if (referencesNode != null)
                            {
                                var allReferenceNodes = new List<XmlNode>();
                                var refEnumerator = referencesNode.ChildNodes.GetEnumerator();
                                while (refEnumerator.MoveNext())
                                {
                                    allReferenceNodes.Add((XmlNode)refEnumerator.Current);
                                }
                                referencesNode.RemoveAll();

                                var controlReferencesNode = controlNode.SelectSingleNode("ua:References", nsmgr);
                                if (controlReferencesNode != null)
                                {
                                    var controlRefEnumerator = controlReferencesNode.ChildNodes.GetEnumerator();
                                    while (controlRefEnumerator.MoveNext())
                                    {
                                        var controlRef = (XmlElement)controlRefEnumerator.Current;
                                        var controlRefType = controlRef?.GetAttribute("ReferenceType");
                                        if (controlRefType != null && !string.IsNullOrEmpty(controlRef.InnerText))
                                        {
                                            var matchingRef = allReferenceNodes.FirstOrDefault(r => (r as XmlElement)?.GetAttribute("ReferenceType") == controlRefType && (r as XmlElement)?.InnerText == controlRef.InnerText);
                                            if (matchingRef != null)
                                            {
                                                referencesNode.AppendChild(matchingRef);
                                                allReferenceNodes.Remove(matchingRef);
                                            }
                                        }
                                    }
                                }
                                allReferenceNodes.ForEach(n => referencesNode.AppendChild(n));
                            }
                        }
                    }
                    else if (new string[] { "Models", "Aliases", "NamespaceUris" }.Contains(controlNode.LocalName))
                    {
                        var fallbackNode = allNodes.FirstOrDefault(n => (n as XmlElement)?.LocalName == controlNode.LocalName);
                        if (fallbackNode != null)
                        {
                            nodeSet.AppendChild(fallbackNode);
                            allNodes.Remove(fallbackNode);
                        }
                    }
                }
                // add any remaining nodes not matched to the control nodeset
                allNodes.ForEach(n => nodeSet.AppendChild(n));
            }
            else
            {
                orderedNodes = allNodes.OrderBy(n => (n as XmlElement)?.GetAttribute("NodeId"));
                foreach (var node in orderedNodes)
                {
                    nodeSet.AppendChild(node);
                    if (node is XmlElement elem)
                    {
                        var referencesNode = elem.SelectSingleNode("ua:References", nsmgr);
                        if (referencesNode != null)
                        {
                            var allReferenceNodes = new List<XmlNode>();
                            var refEnumerator = referencesNode.ChildNodes.GetEnumerator();
                            while (refEnumerator.MoveNext())
                            {
                                allReferenceNodes.Add((XmlNode)refEnumerator.Current);
                            }
                            referencesNode.RemoveAll();
                            var orderedReferenceNodes = allReferenceNodes.OrderBy(n => (n as XmlElement)?.GetAttribute("ReferenceType")).ThenBy(n => (n as XmlElement)?.InnerText);
                            foreach (var refNode in orderedReferenceNodes)
                            {
                                referencesNode.AppendChild(refNode);
                            }
                        }
                    }
                }
            }
            xmlDoc.Save(fileName.Replace("NodeSet2.xml", "NodeSet2.sorted.xml"));
        }

        private static string NormalizeAliasesAndNamespaces(string xmlString, Dictionary<string, string> aliases, NamespaceTable namespaces)
        {
            foreach (var alias in aliases)
            {
                //xmlString = xmlString.Replace($"\"{alias.Key}\"", $"\"{alias.Value}\"");
            }

            int i = 0;
            foreach (var ns in namespaces.ToArray())
            {
                xmlString = xmlString.Replace($"\"ns={i};", $"\"nsu={ns};");
                xmlString = xmlString.Replace($">ns={i};", $">nsu={ns};");
                i++;
            }

            return xmlString;
        }

        private static (NamespaceTable, Dictionary<string, string>) LoadNamespaces(string file)
        {
            var namespaces = new NamespaceTable(new[] { strOpcNamespaceUri });
            using (var nodeSetStream = File.OpenRead(file))
            {
                UANodeSet nodeSet = UANodeSet.Read(nodeSetStream);
                var aliases = nodeSet.Aliases?.ToDictionary(a => a.Alias, a => a.Value) ?? new Dictionary<string, string>();
                if (nodeSet.NamespaceUris?.Any() == true)
                {
                    foreach (var ns in nodeSet.NamespaceUris)
                    {
                        namespaces.GetIndexOrAppend(ns);
                    }
                }
                return (namespaces, aliases);
            }
        }
        public static void GenerateDiffSummary(Diff d, out string diffControlStr, out string diffTestStr, out string diffSummaryStr)
        {
            var diffControl = new StringBuilder();
            var diffTest = new StringBuilder();
            var diffSummary = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                ConformanceLevel = ConformanceLevel.Fragment,
            };
            XmlWriter controlWriter = XmlWriter.Create(diffControl, settings);
            XmlWriter testWriter = XmlWriter.Create(diffTest, settings);

            foreach (var diff in d.Differences)
            {
                diffSummary.Append("====>");
                diffSummary.AppendLine(diff.Comparison.ToString() ?? "");
                diffControl.Append("====>");
                diffControl.AppendLine(diff.Comparison.ToString() ?? "");
                diffTest.Append("====>");
                diffTest.AppendLine(diff.Comparison.ToString() ?? "");
                if (new[] { "/", "/UANodeSet[1]" }.Contains(diff.Comparison.ControlDetails.XPath) || diff.Comparison.ControlDetails.Target?.Name == "UANodeSet")
                {

                    continue;
                }
                if (diff.Comparison.ControlDetails.Target != null)
                {
                    diff.Comparison.ControlDetails.Target.WriteTo(controlWriter);
                    //(diff.Comparison.ControlDetails.Target.ParentNode?.ParentNode ?? diff.Comparison.ControlDetails.Target.ParentNode ?? diff.Comparison.ControlDetails.Target).WriteTo(controlWriter);
                    controlWriter.Dispose();
                    controlWriter = XmlWriter.Create(diffControl, settings);
                    diffControl.AppendLine();
                }
                if (diff.Comparison.TestDetails.Target != null)
                {
                    diff.Comparison.TestDetails.Target.WriteTo(testWriter);
                    //(diff.Comparison.TestDetails.Target.ParentNode?.ParentNode ?? diff.Comparison.TestDetails.Target.ParentNode ?? diff.Comparison.TestDetails.Target).WriteTo(testWriter);
                    testWriter.Dispose();
                    testWriter = XmlWriter.Create(diffTest, settings);
                    diffTest.AppendLine();
                }
            }
            diffControlStr = diffControl.ToString();
            diffTestStr = diffTest.ToString();
            diffSummaryStr = diffSummary.ToString();
        }

    }
}

