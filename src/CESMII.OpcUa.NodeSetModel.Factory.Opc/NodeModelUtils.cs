﻿using Opc.Ua;
using System.Collections.Generic;
using System.Xml;
using System.Linq;
using CESMII.OpcUa.NodeSetModel.Export.Opc;

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

        public static string JsonEncodeVariant(Variant value)
        {
            return JsonEncodeVariant(null, value, null);
        }
        public static string JsonEncodeVariant(ISystemContext systemContext, Variant value)
        {
            return JsonEncodeVariant(systemContext, value, null);
        }
        public static string JsonEncodeVariant(ISystemContext systemContext, Variant value, DataTypeModel dataType)
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

    }
}