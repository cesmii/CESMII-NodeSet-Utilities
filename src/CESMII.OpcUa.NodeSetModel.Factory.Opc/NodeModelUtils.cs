using Opc.Ua;
using System.IO;
using System.Text;
using System.Xml;

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
            return JsonEncodeVariant(null, value);
        }
        public static string JsonEncodeVariant(ISystemContext systemContext, Variant value)
        {
            ServiceMessageContext context;
            if (systemContext != null)
            {
                context = new ServiceMessageContext { NamespaceUris = systemContext.NamespaceUris, };
            }
            else
            {
                context = ServiceMessageContext.GlobalContext;
            }

            var encoder = new JsonEncoder(context, true);
            encoder.WriteVariant("Value", value, true);

            var encodedValue = encoder.CloseAndReturnText();
            return encodedValue;
        }


        public static XmlElement JsonDecodeVariant(string jsonVariant)
        {
            using (var decoder = new JsonDecoder(jsonVariant, ServiceMessageContext.GlobalContext))
            {
                var value = decoder.ReadVariant("Value");
                var xml = GetVariantAsXML(value);
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
        public static System.Xml.XmlElement GetVariantAsXML(Variant value)
        {
            var context = new ServiceMessageContext();
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

    }
}