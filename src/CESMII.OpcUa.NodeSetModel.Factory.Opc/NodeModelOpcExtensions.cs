using Opc.Ua;
using ua = Opc.Ua;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NotVisualBasic.FileIO;
using System.Reflection;
using Opc.Ua.Export;
using System;

namespace CESMII.OpcUa.NodeSetModel.Opc.Extensions
{
    public static class NodeModelOpcExtensions
    {
        public static string GetDisplayNamePath(this InstanceModelBase model)
        {
            return model.GetDisplayNamePath(new List<NodeModel>());
        }
        public static DateTime GetNormalizedPublicationDate(this ModelTableEntry model)
        {
            return model.PublicationDateSpecified ? DateTime.SpecifyKind(model.PublicationDate, DateTimeKind.Utc) : default;
        }
        public static DateTime GetNormalizedPublicationDate(this DateTime? publicationDate)
        {
            return publicationDate != null ? DateTime.SpecifyKind(publicationDate.Value, DateTimeKind.Utc) : default;
        }
        public static string GetDisplayNamePath(this InstanceModelBase model, List<NodeModel> nodesVisited)
        {
            if (nodesVisited.Contains(model))
            {
                return "(cycle)";
            }
            nodesVisited.Add(model);
            if (model.Parent is InstanceModelBase parent)
            {
                return $"{parent.GetDisplayNamePath(nodesVisited)}.{model.DisplayName.FirstOrDefault()?.Text}";
            }
            return model.DisplayName.FirstOrDefault()?.Text;
        }
        internal static void SetEngineeringUnits(this VariableModel model, EUInformation euInfo)
        {
            model.EngineeringUnit = new VariableModel.EngineeringUnitInfo
            {
                DisplayName = euInfo.DisplayName?.ToModelSingle(),
                Description = euInfo.Description?.ToModelSingle(),
                NamespaceUri = euInfo.NamespaceUri,
                UnitId = euInfo.UnitId,
            };
        }

        internal static void SetRange(this VariableModel model, ua.Range euRange)
        {
            model.MinValue = euRange.Low;
            model.MaxValue = euRange.High;
        }
        internal static void SetInstrumentRange(this VariableModel model, ua.Range range)
        {
            model.InstrumentMinValue = range.Low;
            model.InstrumentMaxValue = range.High;
        }

        private const string strUNECEUri = "http://www.opcfoundation.org/UA/units/un/cefact";

        static List<EUInformation> _UNECEEngineeringUnits;
        public static List<EUInformation> UNECEEngineeringUnits
        {
            get
            {
                if (_UNECEEngineeringUnits == null)
                {
                    // Load UNECE units if not already loaded
                    _UNECEEngineeringUnits = new List<EUInformation>();
                    var fileName = Path.Combine(Path.GetDirectoryName(typeof(VariableModel).Assembly.Location), "NodeSets", "UNECE_to_OPCUA.csv");
                    Stream fileStream;
                    if (File.Exists(fileName))
                    {
                        fileStream = File.OpenRead(fileName);
                    }
                    else
                    {
                        fileStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CESMII.OpcUa.NodeSetModel.Factory.Opc.NodeSets.UNECE_to_OPCUA.csv");
                    }
                    var parser = new CsvTextFieldParser(fileStream);
                    if (!parser.EndOfData)
                    {
                        var headerFields = parser.ReadFields();
                    }
                    while (!parser.EndOfData)
                    {
                        var parts = parser.ReadFields();
                        if (parts.Length != 4)
                        {
                            // error
                        }
                        var UNECECode = parts[0];
                        var UnitId = parts[1];
                        var DisplayName = parts[2];
                        var Description = parts[3];
                        var newEuInfo = new EUInformation(DisplayName, Description, strUNECEUri)
                        {
                            UnitId = int.Parse(UnitId),
                        };
                        _UNECEEngineeringUnits.Add(newEuInfo);
                    }
                }

                return _UNECEEngineeringUnits;
            }
        }

        static Dictionary<string, List<EUInformation>> _euInformationByDescription;
        static Dictionary<string, List<EUInformation>> EUInformationByDescription
        {
            get
            {
                if (_euInformationByDescription == null)
                {
                    _euInformationByDescription = new Dictionary<string, List<EUInformation>>();
                    foreach (var aEuInformation in UNECEEngineeringUnits)
                    {
                        if (!_euInformationByDescription.ContainsKey(aEuInformation.Description.Text))
                        {
                            _euInformationByDescription.Add(aEuInformation.Description.Text, new List<EUInformation> { aEuInformation });
                        }
                        else
                        {
                            _euInformationByDescription[aEuInformation.DisplayName.Text].Add(aEuInformation);
                        }
                    }
                }
                return _euInformationByDescription;
            }
        }

        static Dictionary<string, List<EUInformation>> _euInformationByDisplayName;
        static Dictionary<string, List<EUInformation>> EUInformationByDisplayName
        {
            get
            {
                if (_euInformationByDisplayName == null)
                {
                    _euInformationByDisplayName = new Dictionary<string, List<EUInformation>>();
                    foreach (var aEuInformation in UNECEEngineeringUnits)
                    {
                        if (!_euInformationByDisplayName.ContainsKey(aEuInformation.DisplayName.Text))
                        {
                            _euInformationByDisplayName.Add(aEuInformation.DisplayName.Text, new List<EUInformation> { aEuInformation });
                        }
                        else
                        {
                            _euInformationByDisplayName[aEuInformation.DisplayName.Text].Add(aEuInformation);
                        }
                    }
                }
                return _euInformationByDisplayName;
            }
        }

        public static EUInformation GetEUInformation(VariableModel.EngineeringUnitInfo engineeringUnitDescription)
        {
            if (engineeringUnitDescription == null) return null;

            List<EUInformation> euInfoList;
            if (!string.IsNullOrEmpty(engineeringUnitDescription.DisplayName?.Text)
                && engineeringUnitDescription.UnitId == null
                && engineeringUnitDescription.Description == null
                && (string.IsNullOrEmpty(engineeringUnitDescription.NamespaceUri) || engineeringUnitDescription.NamespaceUri == strUNECEUri))
            {
                // If we only have a displayname, assume it's a UNECE unit
                // Try to lookup engineering unit by description
                if (EUInformationByDescription.TryGetValue(engineeringUnitDescription.DisplayName.Text, out euInfoList))
                {
                    return euInfoList.FirstOrDefault();
                }
                // Try to lookup engineering unit by display name
                else if (EUInformationByDisplayName.TryGetValue(engineeringUnitDescription.DisplayName.Text, out euInfoList))
                {
                    return euInfoList.FirstOrDefault();
                }
                else
                {
                    // No unit found: just use the displayname
                    return new EUInformation(engineeringUnitDescription.DisplayName.Text, engineeringUnitDescription.DisplayName.Text, null);
                }
            }
            else
            {
                // Custom EUInfo: use what was specified without further validation
                EUInformation euInfo = new EUInformation(engineeringUnitDescription.DisplayName?.Text, engineeringUnitDescription.Description?.Text, engineeringUnitDescription.NamespaceUri);
                if (engineeringUnitDescription.UnitId != null)
                {
                    euInfo.UnitId = engineeringUnitDescription.UnitId.Value;
                }
                return euInfo;
            }
        }

        /// <summary>
        /// Updates or creates the object of type NamespaceMetaDataType as described in https://reference.opcfoundation.org/Core/Part5/v105/docs/6.3.13
        /// </summary>
        /// <param name="_this"></param>
        public static void UpdateNamespaceMetaData(this NodeSetModel _this, NodeSetModel opcUaModel)
        {
            var metaDataTypeNodeId = new ExpandedNodeId(ObjectTypeIds.NamespaceMetadataType, Namespaces.OpcUa);
            var metadataObjects = _this.Objects.Where(o => o.TypeDefinition.HasBaseType(metaDataTypeNodeId.ToString())).ToList();
            var metadataObject = metadataObjects.FirstOrDefault();
            if (metadataObject == null)
            {
                metadataObject = new ObjectModel
                {
                    NodeSet = _this,
                    NodeId = new ExpandedNodeId(Guid.NewGuid(), _this.ModelUri).ToString(),
                    DisplayName = new ua.LocalizedText(_this.ModelUri).ToModel(),
                    BrowseName = $"{Namespaces.OpcUa};{nameof(ObjectTypeIds.NamespaceMetadataType)}",
                    Properties = new List<VariableModel>(),
                    OtherReferencingNodes = new List<NodeModel.NodeAndReference>
                    {
                        new NodeModel.NodeAndReference
                        {
                             ReferenceType = opcUaModel.ReferenceTypes.FirstOrDefault(rt => rt.NodeId == new ExpandedNodeId(ReferenceTypeIds.HasComponent, Namespaces.OpcUa).ToString()),
                             Node = opcUaModel.Objects.FirstOrDefault(o => o.NodeId == new ExpandedNodeId(ObjectIds.Server, Namespaces.OpcUa).ToString()),
                        }
                    }
                };
                _this.Objects.Add(metadataObject);
            }
            CreateOrReplaceProperty(_this, metadataObject, nameof(NamespaceMetadataState.NamespaceUri), _this.ModelUri, true);
            CreateOrReplaceProperty(_this, metadataObject, nameof(NamespaceMetadataState.NamespacePublicationDate), _this.PublicationDate?.ToString(), true);
            CreateOrReplaceProperty(_this, metadataObject, nameof(NamespaceMetadataState.NamespaceVersion), _this.Version, true);

            // Only create if not already authored
            CreateOrReplaceProperty(_this, metadataObject, nameof(NamespaceMetadataState.IsNamespaceSubset), "false", false);
            CreateOrReplaceProperty(_this, metadataObject, nameof(NamespaceMetadataState.StaticNodeIdTypes), null, false);
            CreateOrReplaceProperty(_this, metadataObject, nameof(NamespaceMetadataState.StaticNumericNodeIdRange), null, false);
            CreateOrReplaceProperty(_this, metadataObject, nameof(NamespaceMetadataState.StaticStringNodeIdPattern), null, false);
        }

        private static void CreateOrReplaceProperty(NodeSetModel _this, ObjectModel metadataObject, string browseName, string value, bool replaceIfExists)
        {
            string qualifiedBrowseName = $"{Namespaces.OpcUa};{browseName}";
            var previousProp = metadataObject.Properties.FirstOrDefault(p => p.BrowseName == $"{Namespaces.OpcUa};{browseName}");
            if (replaceIfExists || previousProp == null)
            {
                var encodedValue = $"{{\"Value\":{{\"Type\":12,\"Body\":\"{value}\"}}}}";
                if (previousProp != null)
                {
                    previousProp.Value = encodedValue;
                }
                else
                {
                    metadataObject.Properties.Add(new PropertyModel
                    {
                        NodeSet = _this,
                        NodeId = new ExpandedNodeId(Guid.NewGuid(), _this.ModelUri).ToString(),
                        BrowseName = $"{Namespaces.OpcUa};{browseName}",
                        DisplayName = new ua.LocalizedText(browseName).ToModel(),
                        Value = encodedValue,
                    });
                }
            }
        }
    }

}