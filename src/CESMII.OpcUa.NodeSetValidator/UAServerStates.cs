using CESMII.OpcUa.NodeSetImporter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CESMII.NodeSetValidator
{
    public class TheUAServerStates
    {
        public Action<string, object> eventPropertyChanged;

        public string FriendlyName { get; set; }
        public string PreferredLocales { get; set; }
        public string LogAddress { get; set; }
        public int LogLevel { get; set; }


        #region connection Settings
        public string Address { get; set; }
        public string SessionName { get; set; }
        public int KeepAliveInterval { get; set; }
        public int ReconnectPeriod { get; set; }
        public int SessionTimeout { get; set; }
        public int OperationTimeout { get; set; }
        public int PublishingInterval { get; set; }
        public int DefSampleRate { get; set; }
        public bool DoNotUsePropsOfProps { get; set; }
        #endregion

        #region Security Settings
        public bool Anonymous { get; set; }
        public bool DisableSecurity { get; set; }
        public bool AcceptUntrustedCertificate { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool DisableDomainCheck { get; set; }
        public string AppCertSubjectName { get; set; }
        public bool AcceptInvalidCertificate { get; set; }
        #endregion

        #region Statistics and KPI
        int _ReconnectCount;
        public int ReconnectCount { get { return _ReconnectCount; } set { eventPropertyChanged?.Invoke("ReconnectCount", value); _ReconnectCount = value; } }

        bool _IsConnected;
        public bool IsConnected { get { return _IsConnected; } set { eventPropertyChanged?.Invoke("IsConnected", value); _IsConnected = value; } }

        bool _IsReconnecting;
        public bool IsReconnecting { get { return _IsReconnecting; } set { eventPropertyChanged?.Invoke("IsReconnecting", value); _IsReconnecting = value; } }

        string _LastMessage;
        public string LastMessage { get { return _LastMessage; } set { eventPropertyChanged?.Invoke("LastMessage", value); _LastMessage = value; } }

        int _StatusLevel;
        public int StatusLevel { get { return _StatusLevel; } set { eventPropertyChanged?.Invoke("StatusLevel", value); _StatusLevel = value; } }


        public DateTimeOffset DefHistoryStartTime { get; set; }
        public DateTimeOffset LastDataReceivedTime { get; set; }
        public long DataReceivedCount { get; set; }
        public void IncrementDataReceivedCount()
        {
            DataReceivedCount++;
        }
        #endregion

        #region Advanced Options
        public bool SendStatusCode { get; set; }
        public bool SendOpcDataType { get; set; }
        public bool SendSequenceNumber { get; set; }
        public bool UseLocalTimestamp { get; set; }
        public bool SendServerTimestamp { get; set; }
        public bool SendPicoSeconds { get; set; }
        public bool EnableOPCDataLogging { get; set; }
        public bool UseSequenceNumberForThingSequence { get; set; }
        public bool DoNotWriteArrayElementsAsProperties { get; set; }
        #endregion

        #region cloudlib parameter
        public string CloudLibUID { get; set; } = null;
        public string CloudLibPWD { get; set; } = null;
        public string CloudLibEP { get; set; } = null;

        public UANodeSetCloudLibraryResolver MyCloudLib { get; set; } = null;

        public string LocalCachePath { get; set; } = null;
        public string LocalTempPath { get; set; } = null;
        #endregion

        public string CloneFrom(TheUAServerStates MyValue)
        {
            if (MyValue == this) return null;
            if (MyValue == null) return "ERR: No Value Given";
            StringBuilder ret = new();
            List<PropertyInfo> PropInfoArray = typeof(TheUAServerStates).GetProperties().OrderBy(x => x.Name).ToList();
            foreach (PropertyInfo finfo in PropInfoArray)
            {
                try
                {
                    finfo.SetValue(this, finfo.GetValue(MyValue, null), null);
                }
                catch (Exception e)
                {
                    ret.AppendLine(e.Message);
                }
            }
            return ret.ToString();
        }
    }

}
