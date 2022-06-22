using Opc.Ua;
using ua = Opc.Ua;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CESMII.OpcUa.NodeSetModel.Opc.Extensions
{
    public static class NodeModelOpcExtensions
    {
        public static string GetDisplayNamePath(this InstanceModelBase model)
        {
            return model.GetDisplayNamePath(new List<NodeModel>());
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

        static Dictionary<string, EUInformation> _euInformationByDescription;
        static Dictionary<string, EUInformation> EUInformationByDescription
        {
            get
            {
                if (_euInformationByDescription == null)
                {
                    // Load UNECE units if not already loaded
                    _euInformationByDescription = new Dictionary<string, EUInformation>();
                    try
                    {
                        var euMapping = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(typeof(VariableModel).Assembly.Location), "NodeSets", "UNECE_to_OPCUA.csv"));
                        foreach (var line in euMapping.Skip(1))
                        {
                            //UNECECode,UnitId,DisplayName,Description
                            var parts = line.Split(',');
                            if (parts.Length != 4)
                            {
                                // error
                            }
                            var UNECECode = parts[0].TrimCSV("\"");
                            var UnitId = parts[1].TrimCSV("\"");
                            var DisplayName = parts[2].TrimCSV("\"");
                            var Description = parts[3].TrimCSV("\"");
                            var newEuInfo = new EUInformation(DisplayName, Description, strUNECEUri)
                            {
                                UnitId = int.Parse(UnitId),
                            };
                            if (!_euInformationByDescription.ContainsKey(newEuInfo.Description.Text))
                            {
                                _euInformationByDescription.Add(newEuInfo.Description.Text, newEuInfo);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                return _euInformationByDescription;
            }
        }

        private static string TrimCSV(this string s, string trimChar)
        {
            if (s == null) return null;
            if (s.StartsWith(trimChar))
            {
                s= s.Substring(1);
            }
            if (s.EndsWith(trimChar))
            {
                s = s.Substring(0, s.Length - 1);
            }
            s = s.Replace("\"\"", "\""); // Double quotes => single quote
            return s;
        }

        public static EUInformation GetEUInformation(VariableModel.EngineeringUnitInfo engineeringUnitDescription)
        {
            if (engineeringUnitDescription == null) return null;
            EUInformation euInfo;
            if (!string.IsNullOrEmpty(engineeringUnitDescription.DisplayName?.Text)
                && engineeringUnitDescription.UnitId == null
                && engineeringUnitDescription.Description == null
                && (string.IsNullOrEmpty(engineeringUnitDescription.NamespaceUri) || engineeringUnitDescription.NamespaceUri == strUNECEUri))
            {
                // If we only have a displayname, assume it's a UNECE unit
                if (EUInformationByDescription.TryGetValue(engineeringUnitDescription.DisplayName.Text, out euInfo))
                {
                    return euInfo;
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
                euInfo = new EUInformation(engineeringUnitDescription.DisplayName?.Text, engineeringUnitDescription.Description?.Text, engineeringUnitDescription.NamespaceUri);
                if (engineeringUnitDescription.UnitId != null)
                {
                    euInfo.UnitId = engineeringUnitDescription.UnitId.Value;
                }
                return euInfo;
            }
        }

        public static List<EUInformation> GetUNECEEngineeringUnits()
        {
            var uneceUnits = new List<EUInformation>();
            foreach(var euInfo in EUInformationByDescription.Values)
            {
                uneceUnits.Add(euInfo);
            }
            return uneceUnits;
        }

    }

}