using Opc.Ua;
using export = Opc.Ua.Export;
using System.Collections.Generic;
using System.Xml;
using System.Linq;
using CESMII.OpcUa.NodeSetModel.Export.Opc;
using System;
using Microsoft.Extensions.Logging;

namespace CESMII.OpcUa.NodeSetModel.Factory.Opc
{
    public static class NodeModelUtils
    {
        public static string GetNodeIdIdentifier(string nodeId)
        {
            return nodeId.Substring(nodeId.LastIndexOf(';') + 1);
        }

        public static string GetNamespaceFromNodeId(string nodeId)
        {
            var parsedNodeId = ExpandedNodeId.Parse(nodeId);
            var namespaceUri = parsedNodeId.NamespaceUri;
            return namespaceUri;
        }

        public static string JsonEncodeVariant(Variant value, bool reencodeExtensionsAsJson = false)
        {
            return JsonEncodeVariant(null, value, null, reencodeExtensionsAsJson = false);
        }
        public static string JsonEncodeVariant(ISystemContext systemContext, Variant value, bool reencodeExtensionsAsJson = false)
        {
            return JsonEncodeVariant(systemContext, value, null, reencodeExtensionsAsJson);
        }
        public static string JsonEncodeVariant(ISystemContext systemContext, Variant value, DataTypeModel dataType, bool reencodeExtensionsAsJson = false)
        {
            ServiceMessageContext context;
            if (systemContext != null)
            {
                context = new ServiceMessageContext { NamespaceUris = systemContext.NamespaceUris, Factory = systemContext.EncodeableFactory };
            }
            else
            {
                context = ServiceMessageContext.GlobalContext;
            }
            if (reencodeExtensionsAsJson)
            {
                if (dataType != null && systemContext.EncodeableFactory is DynamicEncodeableFactory lookupContext)
                {
                    lookupContext.AddEncodingsForDataType(dataType, systemContext.NamespaceUris);
                }

                // Reencode extension objects as JSON 
                if (value.Value is ExtensionObject extObj && extObj.Encoding == ExtensionObjectEncoding.Xml && extObj.Body is XmlElement extXmlBody)
                {
                    var xmlDecoder = new XmlDecoder(extXmlBody, context);
                    var parsedBody = xmlDecoder.ReadExtensionObjectBody(extObj.TypeId);
                    value.Value = new ExtensionObject(extObj.TypeId, parsedBody);
                }
                else if (value.Value is ExtensionObject[] extObjList && extObjList.Any(e => e.Encoding==ExtensionObjectEncoding.Xml && e.Body is XmlElement))
                {
                    var newExtObjList = new ExtensionObject[extObjList.Length];
                    int i = 0;
                    bool bReencoded = false;
                    foreach (var extObj2 in extObjList)
                    {
                        if (extObj2.Encoding == ExtensionObjectEncoding.Xml && extObj2.Body is XmlElement extObj2XmlBody)
                        {
                            var xmlDecoder = new XmlDecoder(extObj2XmlBody, context);
                            var parsedBody = xmlDecoder.ReadExtensionObjectBody(extObj2.TypeId);
                            newExtObjList[i] = new ExtensionObject(extObj2.TypeId, parsedBody);
                            bReencoded = true;
                        }
                        else
                        {
                            newExtObjList[i] = extObj2;
                        }
                        i++;
                    }
                    if (bReencoded)
                    {
                        value.Value = newExtObjList;
                    }
                }
            }
            using (var encoder = new JsonEncoder(context, true))
            {
                encoder.ForceNamespaceUri = true;
                //encoder.ForceNamespaceUriForIndex1 = true;
                encoder.WriteVariant("Value", value);

                var encodedValue = encoder.CloseAndReturnText();
                return encodedValue;
            }
        }

        //private static Dictionary<string, object> ParseStructureValues(XmlElement extXmlBody, int nestingLevel)
        //{
        //    if (nestingLevel > 100)
        //    {
        //        throw new System.Exception("Nested structure of more than 100 levels not supported.");
        //    }
        //    Dictionary<string, object> defaultValues = new Dictionary<string, object>();
        //    foreach (var child in extXmlBody.ChildNodes)
        //    {
        //        if (child is XmlElement elementChild)
        //        {
        //            if (elementChild.ChildNodes.OfType<XmlElement>().Any())
        //            {
        //                defaultValues.Add(elementChild.Name, ParseStructureValues(elementChild, nestingLevel + 1));
        //            }
        //            else
        //            {
        //                defaultValues.Add(elementChild.Name, elementChild.InnerText);
        //            }
        //        }
        //    }
        //    return defaultValues;
        //}

        public static XmlElement JsonDecodeVariant(string jsonVariant, IServiceMessageContext context)
        {
            using (var decoder = new JsonDecoder(jsonVariant, context))
            {
                var value = decoder.ReadVariant("Value");
                var xml = GetVariantAsXML(value, context);
                return xml;
            }
        }

        public static System.Xml.XmlElement GetExtensionObjectAsXML(object extensionBody)
        {
            var extension = new ExtensionObject(extensionBody);
            var context = new ServiceMessageContext();
            var ms = new System.IO.MemoryStream();
            using (var xmlWriter = new System.Xml.XmlTextWriter(ms, System.Text.Encoding.UTF8))
            {
                xmlWriter.WriteStartDocument();

                using (var encoder = new XmlEncoder(new System.Xml.XmlQualifiedName("uax:ExtensionObject", null), xmlWriter, context))
                {
                    encoder.WriteExtensionObject(null, extension);
                    xmlWriter.WriteEndDocument();
                    xmlWriter.Flush();
                }
            }
            var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml.Substring(1));
            var xmlElem = doc.DocumentElement;
            return xmlElem;
        }
        public static System.Xml.XmlElement EncodeAsXML(Action<IEncoder> encode)
        {
            var context = new ServiceMessageContext();
            var ms = new System.IO.MemoryStream();
            using (var xmlWriter = new System.Xml.XmlTextWriter(ms, System.Text.Encoding.UTF8))
            {
                xmlWriter.WriteStartDocument();

                using (var encoder = new XmlEncoder(new System.Xml.XmlQualifiedName("uax:ExtensionObject", null), xmlWriter, context))
                {
                    encode(encoder);
                    xmlWriter.WriteEndDocument();
                    xmlWriter.Flush();
                }
            }
            var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml.Substring(1));
            var xmlElem = doc.DocumentElement;
            return xmlElem;
        }

        public static System.Xml.XmlElement GetVariantAsXML(Variant value, IServiceMessageContext context)
        {
            var ms = new System.IO.MemoryStream();
            using (var xmlWriter = new System.Xml.XmlTextWriter(ms, System.Text.Encoding.UTF8))
            {
                xmlWriter.WriteStartDocument();
                using (var encoder = new XmlEncoder(new System.Xml.XmlQualifiedName("myRoot"/*, "http://opcfoundation.org/UA/2008/02/Types.xsd"*/), xmlWriter, context))
                {
                    encoder.WriteVariant("value", value);
                    xmlWriter.WriteEndDocument();
                    xmlWriter.Flush();
                }
            }
            var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            var doc = new System.Xml.XmlDocument();

            doc.LoadXml(xml.Substring(1));
            var xmlElem = doc.DocumentElement;
            var xmlValue = xmlElem.FirstChild?.FirstChild?.FirstChild as System.Xml.XmlElement;
            return xmlValue;
        }

        public static ServiceMessageContext GetContextWithDynamicEncodeableFactory(DataTypeModel dataType, NamespaceTable namespaces)
        {
            DynamicEncodeableFactory dynamicFactory = new(EncodeableFactory.GlobalFactory);
            dynamicFactory.AddEncodingsForDataType(dataType, namespaces);
            var messageContext = new ServiceMessageContext { Factory = dynamicFactory, NamespaceUris = namespaces };
            return messageContext;
        }

        /// <summary>
        /// Reads a missing nodeset version from a NamespaceVersion object
        /// </summary>
        /// <param name="nodeSet"></param>
        public static void FixupNodesetVersionFromMetadata(export.UANodeSet nodeSet, ILogger logger)
        {
            if (nodeSet?.Models == null)
            {
                return;
            }
            foreach (var model in nodeSet.Models)
            {
                if (string.IsNullOrEmpty(model.Version))
                {
                    var namespaceVersionObject = nodeSet.Items?.FirstOrDefault(n => n is export.UAVariable && n.BrowseName == BrowseNames.NamespaceVersion) as export.UAVariable;
                    var version = namespaceVersionObject?.Value?.InnerText;
                    if (!string.IsNullOrEmpty(version))
                    {
                        model.Version = version;
                        if (logger != null)
                        {
                            logger.LogWarning($"Nodeset {model.ModelUri} did not specify a version, but contained a NamespaceVersion property with value {version}.");
                        }
                    }
                }
            }
        }
    }

}
