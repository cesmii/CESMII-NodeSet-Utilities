﻿using Org.XmlUnit.Builder;
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
        private XDocument _controlDoc;
        private XDocument _testDoc;

        public OpcNodeSetXmlUnit(Dictionary<string, string> controlAliases, NamespaceTable controlNamespaces, Dictionary<string, string> testAliases, NamespaceTable testNamespaces,
            string controlFile, string testFile)
        {
            this._controlAliases = controlAliases;
            this._controlNamespaces = controlNamespaces;
            this._testAliases = testAliases;
            this._testNamespaces = testNamespaces;
            this._controlFile = controlFile;
            this._testFile = testFile;
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
                if (c.ControlDetails.Target.Name == "Text")
                {
                    if (c.ControlDetails.XPath.Split('/', 4)?[3] == c.TestDetails.XPath.Split('/', 4)?[3])
                    {
                        if (((int) c.ControlDetails.Value) == ((int) c.TestDetails.Value)+ 1 &&  c.ControlDetails.Target.PreviousSibling.LocalName=="Locale" && c.ControlDetails.Target.Value == null)
                        {
                            // treat null locale vs missing locale element as same
                            return ComparisonResult.EQUAL;
                        }
                        //return ComparisonResult.EQUAL;
                    }
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
            }
            if (c.Type == ComparisonType.NAMESPACE_PREFIX)
            {
                // XML namespaces can be different as long as the xml namespace URLs match
                return ComparisonResult.EQUAL;
            }
            return outcome;
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
                        case "UAVariable":
                        case "UAVariableType":
                        case "UAReferenceType":
                        case "UADataType":
                            return IsEqualNodeId(c.GetAttribute("NodeId"), t.GetAttribute("NodeId"));
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
            var nodeId = ExpandedNodeId.Parse(nodeIdStr, namespaces);
            var exNodeId = new ExpandedNodeId(nodeId, namespaces.GetString(nodeId.NamespaceIndex));
            return exNodeId.ToString();
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
                foreach (var ns in nodeSet.NamespaceUris)
                {
                    namespaces.GetIndexOrAppend(ns);
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
                    controlWriter.Dispose();
                    controlWriter = XmlWriter.Create(diffControl, settings);
                    diffControl.AppendLine();
                }
                if (diff.Comparison.TestDetails.Target != null)
                {
                    diff.Comparison.TestDetails.Target.WriteTo(testWriter);
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

