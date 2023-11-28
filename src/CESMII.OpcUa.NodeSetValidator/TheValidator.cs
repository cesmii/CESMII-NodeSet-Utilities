using CESMII.OpcUa.NodeSetImporter;
using CESMII.OpcUa.NodeSetModel;
using CESMII.OpcUa.NodeSetModel.Factory.Opc;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;
using Opc.Ua.Export;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Web;

namespace CESMII.NodeSetValidator
{
    public enum ConnectionState { Disconnected, Connecting, Reconnecting, Connected, Disconnecting }

    public class Issues
    {
        public int Warnings { get; set; }
        public int Errors { get; set; }
        public int Infos { get; set; }

        public void Reset()
        {
            Warnings= 0; Errors = 0; Infos = 0;
        }

        public bool HasIssues { get { return Warnings > 0 || Errors > 0; } }
    }

    public class TheRemoteUAServer
    {
        public TheRemoteUAServer()
        {
            MyServerStates = new TheUAServerStates();
            m_CertificateValidation = new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
        }

        #region Connect/Disconnect/Keep Alive

        public Action<string, int> eventFired;
        public Action<UANodeSetImportResult> eventImportResult;
        public Action eventDisconnectComplete;
        public readonly TheUAServerStates MyServerStates;
        public ISession MyOPCSession;

        private SessionReconnectHandler m_reconnectHandler;
        private ApplicationConfiguration m_configuration;
        private readonly CertificateValidationEventHandler m_CertificateValidation;
        private IUserIdentity UserIdentity { get; set; }
        // Connection State must only be modified while holding this lock!
        private readonly object ConnectionStateLock = new object();
        private readonly object m_reconnectHandlerLock = new object();

        class MyCertificateValidator : CertificateValidator
        {
            private readonly TheRemoteUAServer _server;
            public MyCertificateValidator(TheRemoteUAServer server) : base()
            {
                _server = server;
            }
            public override void Validate(X509Certificate2Collection chain)
            {
                if (_server.MyServerStates.AcceptInvalidCertificate)
                {
                    return;
                }
                base.Validate(chain);
            }
        }


        // Only Connect() must transition from Connecting, as the state is used to prevent double connects
        // Only Disconnect() must transition from Disconnecting, as the state is used to prevent double disconnects
        // Reconnect handlers can only change state from Reconnecting to Connected
        ConnectionState _connectionStatePrivate;
        public ConnectionState ConnectionState
        {
            get
            {
                return _connectionStatePrivate;
            }
            set
            {
                switch (value)
                {
                    case ConnectionState.Connected:
                        MyServerStates.IsConnected = true;
                        MyServerStates.IsReconnecting = false;
                        break;
                    case ConnectionState.Connecting:
                    case ConnectionState.Disconnected:
                    case ConnectionState.Disconnecting:
                        MyServerStates.IsConnected = false;
                        MyServerStates.IsReconnecting = false;
                        break;
                    case ConnectionState.Reconnecting:
                        MyServerStates.IsConnected = false;
                        MyServerStates.IsReconnecting = true;
                        break;
                    default:
                        // Invalid: need to update this is extending the state machine!
                        FireEventLog($"[{MyServerStates.LogAddress}] Set ConnectionState: Invalid connection state {_connectionStatePrivate}", 3);
                        break;
                }
                _connectionStatePrivate = value;
            }
        }

        public string Connect(bool logEssentialOnly, TheUAServerStates pStates)
        {
            MyServerStates.CloneFrom(pStates);
            bool bConnected = false;
            string connectError = "Unknown";
            ConnectionState previousState;
            lock (ConnectionStateLock)
            {
                previousState = ConnectionState;
                if (previousState == ConnectionState.Connected)
                {
                    return "";
                }
                if (previousState == ConnectionState.Connecting)
                {
                    return "Connect already in progress";
                }
                if (previousState == ConnectionState.Reconnecting)
                {
                    return "Reconnect already in progress";
                }
                if (previousState == ConnectionState.Disconnecting)
                {
                    return "Disconnect in progress";
                }

                if (previousState == ConnectionState.Disconnected)
                {
                    ConnectionState = ConnectionState.Connecting;
                }
            }
            if (ConnectionState != ConnectionState.Connecting)
            {
                return "Internal error: invalid connection state"; 
            }

            try
            {
                FireEventLog("Connecting at " + DateTimeOffset.Now.ToString(), 0);

                DoDisconnect(); // Call this just in case some state has not been cleaned up properly on earlier disconnects

                if (MyServerStates.OperationTimeout == 0) 
                {
                    MyServerStates.OperationTimeout = 60000;
                }
                string serverUrl = MyServerStates.Address;
                EndpointDescription endpointDescription = ClientUtils.SelectEndpoint(serverUrl, !MyServerStates.DisableSecurity, MyServerStates.OperationTimeout);
                FireEventLog($"[{MyServerStates.LogAddress}] Selected endpoint", 4);

                m_configuration = new ApplicationConfiguration();
                m_configuration.ApplicationType = ApplicationType.Client;
                m_configuration.ApplicationName = HttpUtility.UrlEncode(MyServerStates.FriendlyName);

                m_configuration.CertificateValidator = new MyCertificateValidator(this);

                var s = new SecurityConfiguration();

                s.ApplicationCertificate = new CertificateIdentifier();
                s.ApplicationCertificate.StoreType = "Directory";

                s.ApplicationCertificate.StorePath = Utils.ReplaceSpecialFolderNames(Path.Combine(Path.Combine(Path.Combine("%CommonApplicationData%", "C-Labs"), "CertificateStores"), "MachineDefault"));
                s.ApplicationCertificate.SubjectName = MyServerStates.AppCertSubjectName;

                s.TrustedPeerCertificates = new CertificateTrustList();
                s.TrustedPeerCertificates.StoreType = "Directory";
                s.TrustedPeerCertificates.StorePath = Utils.ReplaceSpecialFolderNames(Path.Combine(Path.Combine(Path.Combine("%CommonApplicationData%", "C-Labs"), "CertificateStores"), "UA Applications"));

                s.TrustedIssuerCertificates = new CertificateTrustList();
                s.TrustedIssuerCertificates.StoreType = "Directory";
                s.TrustedIssuerCertificates.StorePath = Utils.ReplaceSpecialFolderNames(Path.Combine(Path.Combine(Path.Combine("%CommonApplicationData%", "C-Labs"), "CertificateStores"), "UA Certificate Authorities"));

                s.RejectedCertificateStore = new CertificateStoreIdentifier();
                s.RejectedCertificateStore.StoreType = CertificateStoreType.Directory;
                s.RejectedCertificateStore.StorePath = Utils.ReplaceSpecialFolderNames(Path.Combine(Path.Combine(Path.Combine("%CommonApplicationData%", "C-Labs"), "CertificateStores"), "RejectedCertificates"));

                m_configuration.SecurityConfiguration = s;

                m_configuration.ClientConfiguration = new ClientConfiguration();
                m_configuration.ClientConfiguration.MinSubscriptionLifetime = 10000;
                m_configuration.ClientConfiguration.DefaultSessionTimeout = MyServerStates.SessionTimeout; // 10000;
                m_configuration.ClientConfiguration.EndpointCacheFilePath = "Endpoints.xml";

                m_configuration.TransportQuotas = new TransportQuotas();
                m_configuration.TransportQuotas.OperationTimeout = MyServerStates.OperationTimeout; //10000;
                m_configuration.TransportQuotas.MaxStringLength = 100 * 1024 * 1024; // 50MB - previous 2MB: 2097152;
                m_configuration.TransportQuotas.MaxByteStringLength = 100 * 1024 * 1024; // 100MB 9/22/2016, previous 50MB - previous 2MB: 2097152;
                m_configuration.TransportQuotas.MaxArrayLength = 65535;
                m_configuration.TransportQuotas.MaxMessageSize = 100 * 1024 * 1024; // 50MB - previous 4MB: 4194304;
                m_configuration.TransportQuotas.MaxBufferSize = 65535;
                m_configuration.TransportQuotas.ChannelLifetime = 300000;
                m_configuration.TransportQuotas.SecurityTokenLifetime = 3600000;

                m_configuration.Validate(ApplicationType.Client).Wait();
                m_configuration.CertificateValidator.CertificateValidation += m_CertificateValidation;

                // check the application certificate. Create if it doesn't exist and Opc.Ua.CertificateGenerator.exe is installed
                ApplicationInstance application = new ApplicationInstance();
                application.ApplicationType = ApplicationType.Client;
                application.ApplicationConfiguration = m_configuration;
                if (!MyServerStates.DisableSecurity)
                {
                    application.CheckApplicationInstanceCertificate(true, 0).Wait();
                }

                EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);
                ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

                UserIdentity tIDent = null;
                if (MyServerStates.Anonymous)
                {
                    tIDent = new UserIdentity();
                }
                else if (!string.IsNullOrEmpty(MyServerStates.UserName))
                {
                    tIDent = new UserIdentity(MyServerStates.UserName, MyServerStates.Password);
                }

                string[] preferredLocales;
                if (string.IsNullOrEmpty(MyServerStates.PreferredLocales))
                {
                    preferredLocales = null;
                }
                else
                {
                    preferredLocales = MyServerStates.PreferredLocales.Split(';');
                }
                MyOPCSession = Session.Create(
                    m_configuration,
                    endpoint,
                    false,
                    !MyServerStates.DisableDomainCheck,
                    (String.IsNullOrEmpty(MyServerStates.SessionName)) ? m_configuration.ApplicationName : MyServerStates.SessionName,
                    (uint)MyServerStates.SessionTimeout, 
                    tIDent != null ? tIDent : UserIdentity,
                    preferredLocales).Result;
                if (MyOPCSession.SessionTimeout / 2 < MyServerStates.KeepAliveInterval)
                {
                    FireEventLog(String.Format("[{0}] Adjusting KeepAliveInterval to {1} instead of configured {2} due to server adjustment", MyServerStates.LogAddress, MyOPCSession.SessionTimeout, MyServerStates.KeepAliveInterval), 4);
                }
                MyOPCSession.KeepAliveInterval = Math.Min(MyServerStates.KeepAliveInterval, (int)MyOPCSession.SessionTimeout / 2); 

                MyOPCSession.PublishError += OnSessionPublishError;
                MyOPCSession.Notification += OnSessionNotification;
                MyOPCSession.RenewUserIdentity += OnSessionRenewUserIdentity;
                MyOPCSession.SessionClosing += OnSessionClosing;

                // set up keep alive callback.
                MyOPCSession.KeepAlive += new KeepAliveEventHandler(Session_KeepAlive);

                if (MyOPCSession != null)
                {
                    FireEventLog($"[{MyServerStates.LogAddress}] Connected to server.", 4);
                    MyServerStates.StatusLevel = 1;
                    ConnectionState = ConnectionState.Connected;
                    bConnected = true;
                }
                else
                {
                    FireEventLog($"[{MyServerStates.LogAddress}] Failed to connect to server. Failed to create session.", 3);
                    MyServerStates.StatusLevel = 3;
                    connectError = "Failed to create session.";
                }

                if (!bConnected)
                {
                    DoDisconnect();
                    connectError = "Failed to connect to server";
                }
            }
            catch (Exception e)
            {
                DoDisconnect();
                var sr = (e is ServiceResultException) ? ((ServiceResultException)e).InnerResult : null;
                var message =
                    (sr != null ? sr.ToString() : "") + e.ToString();

                FireEventLog($"[{MyServerStates.LogAddress}] Connect Failed for server. {message}", 3);
                connectError = message;
            }
            finally
            {
                if (bConnected)
                {
                    ConnectionState = ConnectionState.Connected;
                }
                else
                {
                    ConnectionState = ConnectionState.Disconnected;
                }
            }
            return bConnected ? "" : connectError;
        }

        public virtual IUserIdentity OnSessionRenewUserIdentity(ISession session, IUserIdentity identity)
        {
            return identity;
        }

        public virtual void OnSessionClosing(object sender, EventArgs e)
        {
        }

        public virtual void OnSessionNotification(ISession session, NotificationEventArgs e)
        {
        }

        public virtual void OnSessionPublishError(ISession session, PublishErrorEventArgs e)
        {
        }

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        public string Disconnect(bool bDontFireDisconnectEvent, bool logAsError, string disconnectReason)
        {
            ConnectionState previousState;
            lock (ConnectionStateLock)
            {
                previousState = ConnectionState;
                if (previousState == ConnectionState.Disconnecting)
                {
                    return "Disconnect pending";
                }
                if (previousState == ConnectionState.Disconnected)
                {
                    return "";
                }
                if (previousState == ConnectionState.Connecting)
                {
                    return "Connect pending"; // TODO: Should we force the disconnect somehow in this state? Saw (seemingly) permanent connecting state on docker devicegate after laptop sleep
                }
                if (previousState == ConnectionState.Reconnecting || previousState == ConnectionState.Connected)
                {
                    ConnectionState = ConnectionState.Disconnecting;
                }
            }
            if (ConnectionState != ConnectionState.Disconnecting)
            {
                FireEventLog($"[{MyServerStates.LogAddress}] Disconnect: Invalid connection state {previousState}", 3);
                return "Internal error: invalid connection state"; // Someone extended the connection state enum and didn't update these checks
            }

            try
            {
                var lastMessageBeforeDisconnect = string.IsNullOrEmpty(disconnectReason) ? MyServerStates.LastMessage : disconnectReason;
                FireEventLog($"[{MyServerStates.LogAddress}] Disconnecting at {DateTime.Now}. {lastMessageBeforeDisconnect}", 0);

                DoDisconnect();

                ConnectionState = ConnectionState.Disconnected;

                FireEventLog("Disconnected at " + DateTimeOffset.Now.ToString(), 0);
                if (!bDontFireDisconnectEvent)
                {
                    FireEventLog($"[{MyServerStates.LogAddress}] Disconnected from server", 4);
                }
                eventDisconnectComplete?.Invoke();
                MyServerStates.StatusLevel = 0;
                return "";
            }
            catch (Exception e)
            {
                FireEventLog($"[{MyServerStates.LogAddress}] Error disconnecting from server", 3);
                MyServerStates.StatusLevel = 3;
                return "Error disconnecting: " + e.Message;
            }
            finally
            {
                if (ConnectionState == ConnectionState.Disconnecting)
                {
                    ConnectionState = ConnectionState.Disconnected;
                }
            }
        }

        public virtual void DoDisconnect()
        {
            var session = MyOPCSession;
            if (session != null)
            {
                try
                {
                    session.Close();
                }
                catch (Exception e)
                {
                    FireEventLog($"[{MyServerStates.LogAddress}] Exception while closing session on disconnect: Possible session leak: {e.Message}", 3);
                }
                try
                {
                    session.Dispose();
                }
                catch (Exception e)
                {
                    FireEventLog($"[{MyServerStates.LogAddress}] Exception while disposing session on disconnect:  Possible session leak: {e.Message}", 3);
                }
                MyOPCSession = null;
            }

            // stop any reconnect operation.
            lock (m_reconnectHandlerLock)
            {
                if (m_reconnectHandler != null)
                {
                    try
                    {
                        m_reconnectHandler.Dispose();
                    }
                    catch { }
                    finally
                    {
                        m_reconnectHandler = null;
                    }
                }
            }
        }

        /// <summary>
        /// Handles a keep alive event from a session.
        /// </summary>
        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            try
            {
                // check for events from discarded sessions.
                if (!ReferenceEquals(session, MyOPCSession))
                {
                    FireEventLog($"[{MyServerStates.LogAddress}] Received keep alive for old session.", 4);
                    return;
                }

                // start reconnect sequence on communication error.
                if (ServiceResult.IsBad(e.Status) || e.CurrentState == ServerState.CommunicationFault || e.CurrentState == ServerState.Failed)
                {
                    MyServerStates.StatusLevel = 3;
                    var message = $"Communication Error ({e.CurrentState}-{e.Status})";
                    if (MyServerStates.ReconnectPeriod <= 0)
                    {
                        lock (m_reconnectHandlerLock)
                        {
                            try
                            {
                                if (m_reconnectHandler != null)
                                {
                                    m_reconnectHandler.Dispose();
                                    m_reconnectHandler = null;
                                }
                            }
                            catch { }
                        }
                        FireEventLog($"[{MyServerStates.LogAddress}] Communication Error for OPC Server. Disconnecting.", 3);
                        Disconnect(false, true, message); 
                        return;
                    }

                    lock (m_reconnectHandlerLock)
                    {
                        if (m_reconnectHandler == null)
                        {
                            lock (ConnectionStateLock)
                            {
                                if (ConnectionState != ConnectionState.Disconnecting && ConnectionState != ConnectionState.Disconnected)
                                {
                                    ConnectionState = ConnectionState.Reconnecting;
                                }
                            }

                            if (ConnectionState == ConnectionState.Reconnecting)
                            {
                                string fullMessage;
                                if (MyServerStates.ReconnectCount > 0)
                                {
                                    fullMessage = $"{message}: Reconnecting in {MyServerStates.ReconnectPeriod} ms for {MyServerStates.ReconnectCount} attempts";
                                }
                                else
                                {
                                    fullMessage = $"{message}: Reconnecting every {MyServerStates.ReconnectPeriod} ms";
                                }
                                FireEventLog($"[{MyServerStates.LogAddress}] {fullMessage}", 2);

                                MyServerStates.StatusLevel = 2;
                                m_reconnectHandler = new SessionReconnectHandler();
                                m_reconnectHandler.BeginReconnect(MyOPCSession, MyServerStates.ReconnectPeriod, Server_ReconnectComplete); 
                            }
                            else
                            {
                                var fullMessage = $"{message}: Disconnected or disconnecting: Not starting reconnect timer.";
                                FireEventLog($"[{MyServerStates.LogAddress}] {fullMessage}", 4);
                            }
                        }
                        else
                        {
                            var fullMessage = $"{message}: Reconnect already in progress. Not starting another reconnect timer.";
                            FireEventLog($"[{MyServerStates.LogAddress}] {fullMessage}", 2);
                        }
                    }

                    return;
                }
                else
                {
                    lock (m_reconnectHandlerLock)
                    {
                        try
                        {
                            if (m_reconnectHandler != null)
                            {
                                FireEventLog("Stopping reconnect due to keepalive", 4);
                                m_reconnectHandler.Dispose();
                                m_reconnectHandler = null;
                            }
                        }
                        catch { }
                        lock (ConnectionStateLock)
                        {
                            if (ConnectionState == ConnectionState.Reconnecting)
                            {
                                ConnectionState = ConnectionState.Connected;
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                var message = $"Reconnect/Keepalive processing failed with internal error: {exception.Message}. Forcing disconnect/reconnect.";
                FireEventLog($"[{MyServerStates.LogAddress}] {message}", 3);
                if (ConnectionState == ConnectionState.Connected)
                {
                    Disconnect(false, true, message);
                    Connect(false, MyServerStates);
                }
            }
        }

        /// <summary>
        /// Handles a reconnect event complete from the reconnect handler.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e) 
        {
            try
            {
                if (!ReferenceEquals(sender, m_reconnectHandler))
                {
                    FireEventLog("Ignoring reconnect notification for previous handler", 4);
                    return;
                }

                lock (m_reconnectHandlerLock)
                {
                    var newSession = m_reconnectHandler.Session;
                    if (e != null && MyOPCSession != newSession)
                    {
                        FireEventLog("Received new session on reconnect", 4);
                        MyOPCSession = newSession;
                    }
                    try
                    {
                        m_reconnectHandler.Dispose();
                    }
                    catch { }
                    m_reconnectHandler = null;
                    lock (ConnectionStateLock)
                    {
                        if (ConnectionState == ConnectionState.Reconnecting)
                        {
                            ConnectionState = ConnectionState.Connected;
                        }
                    }
                }


                MyServerStates.StatusLevel = 1;
                FireEventLog($"[{MyServerStates.LogAddress}] Reconnected", 5);
            }
            catch (Exception)
            {
                FireEventLog($"[{MyServerStates.LogAddress}] Reconnect Failed - internal error", 3);
                lock (m_reconnectHandlerLock)
                {
                    try
                    {
                        if (m_reconnectHandler != null)
                        {
                            m_reconnectHandler.Dispose();
                        }
                    }
                    catch { }
                    m_reconnectHandler = new SessionReconnectHandler();
                    m_reconnectHandler.BeginReconnect(MyOPCSession, MyServerStates.ReconnectPeriod, Server_ReconnectComplete);
                }
            }
        }

        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        public virtual void CertificateValidator_CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            try
            {
                e.Accept = m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates;

                if (!m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                    e.Accept = MyServerStates.AcceptUntrustedCertificate;
            }
            catch (Exception)
            {
                FireEventLog(String.Format("[{0}] Certificate Verification failed", MyServerStates.LogAddress), 3);
            }
        }
        #endregion

        //Validation starts here

        public virtual UANodeSetImportResult FireEventLog(string text, int severity, int loglevel=0)
        {
            if (loglevel>=MyServerStates.LogLevel)
                eventFired?.Invoke(text, severity);
            return new UANodeSetImportResult { ErrorMessage = text };
        }

        public async Task<UANodeSetImportResult> ValidateServer(TheUAServerStates pStates)
        {

            //Step1: Connect to UA Server if not already connected
            if (ConnectionState != ConnectionState.Connected)
                Connect(false, pStates);

            if (ConnectionState!=ConnectionState.Connected)
                return FireEventLog($"Validation: Could not connect to the server", 3);

            //Step2: find all NamesSpaces used in the Server
            List<string> foundNamespaces = new();
            GatherKnownObjectTypes(foundNamespaces, ObjectIds.ObjectsFolder);
            foreach (var tns in foundNamespaces)
            {
                if (tns == "http://opcfoundation.org/UA/" || tns == "http://opcfoundation.org/UA/Diagnostics")
                    continue;
                FireEventLog($"Validation: NS({tns}) found and will be validated against the server", 4);
            }
            //Step3: Resolve the NameSpaces and download the NodeSets
            var res = await ResolveNamespaces(foundNamespaces);

            //Step4: Import the NodeSets 
            UANodeSetImportResult retres;
            int i = 0;
            List<string> fileList= new List<string>();
            foreach (var tns in res)
            {
                File.WriteAllText($"{MyServerStates.LocalTempPath}/nsdnl{i}.xml", tns);
                fileList.Add($"{MyServerStates.LocalTempPath}/nsdnl{i}.xml");
                i++;
            }
            retres = ImportNewNodeset(fileList);
            if (!string.IsNullOrEmpty(retres?.ErrorMessage))
                return FireEventLog($"Validation: Error during Import of the NodeSets Error: {retres}",1, 3);
            FireEventLog($"The Following NodeSets will be used", 1, 3);
            foreach (var tns in fileList)
            {
                FireEventLog($"{tns}", 3);
            }    

            //Step5: Create the NodeSetModels from the NodeSets
            Dictionary<string, NodeSetModel> NodeSetModels = new();
            if (retres?.Models.Count > 0)
            {
                var allImportedNodes = new NodeStateCollection();
                foreach (var nodesetFile in retres.Models)
                {
                    var imported = new List<NodeState>();
                    ImportNodeset2Xml(nodesetFile, NodeSetModels, allImportedNodes, imported);
                }
            }
            else
            {
                return FireEventLog($"There were no Nodesets Found to import. Validation aborted", 3);
            }


            //Step6: Validate the Server against the Models
            List<ReferenceDescription> foundRef= new List<ReferenceDescription>();
            ValidateObjects(ObjectIds.ObjectsFolder, NodeSetModels, foundRef);

            return retres;
        }

        protected void ImportNodeset2Xml(ModelValue resourcepath, Dictionary<string, NodeSetModel> nodesetModels, NodeStateCollection allImportedNodes, List<NodeState> tout) //, int pass)
        {
            UANodeSet nodeSet = resourcepath.NodeSet;
            var opcContext = new DefaultOpcUaContext(MyOPCSession.SystemContext, allImportedNodes, nodesetModels, new NullLogger(null));
            NodeModelFactoryOpc.LoadNodeSetAsync(opcContext, nodeSet, null, new Dictionary<string, string>(), false, tout);
        }

        public void ValidateObjects(NodeId nodeId, Dictionary<string, NodeSetModel> myModels, List<ReferenceDescription> foundRef)
        {
            BrowseDescription typeNodeToBrowse = new BrowseDescription();
            typeNodeToBrowse.NodeId = nodeId;
            typeNodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            typeNodeToBrowse.NodeClassMask = (int)NodeClass.Object;
            typeNodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            ReferenceDescriptionCollection nodeCollection = ClientUtils.Browse(MyOPCSession, typeNodeToBrowse, true);

            foreach (var node in nodeCollection)
            {
                if (node.TypeDefinition.NamespaceIndex == 0 && node.NodeId.NamespaceIndex == 0)
                    continue; //skip OPC UA Base NodeSet?

                if (node.TypeDefinition.NamespaceIndex != 0)
                {
                    Issues issues = new Issues();
                    ValidateCollection(myModels, node, issues, foundRef);
                }
                ValidateObjects((NodeId)node.NodeId, myModels, foundRef);
            }
        }

        private void ValidateCollection(Dictionary<string, NodeSetModel> myModels, ReferenceDescription node,Issues issues, List<ReferenceDescription> foundRef, NodeModel placeHolderModel = null)
        {
            BrowseDescription typeNodeToBrowse = new BrowseDescription();
            typeNodeToBrowse.NodeId = (NodeId)node.NodeId;
            typeNodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            typeNodeToBrowse.IncludeSubtypes = false;
            typeNodeToBrowse.NodeClassMask =(int)NodeClass.Unspecified;
            typeNodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            bool isnew = placeHolderModel == null;
            ReferenceDescriptionCollection nodeCollection = ClientUtils.Browse(MyOPCSession, typeNodeToBrowse, true);

            var ns = MyOPCSession.NamespaceUris.GetString(node.TypeDefinition.NamespaceIndex);
            var found = myModels.TryGetValue(ns, out var model);
            if (found)
            {
                if (isnew)
                {
                    FireEventLog($"Validation-Start: {node.DisplayName}; ns:{ns} id:{node.TypeDefinition}", 4, 3);
                    foundRef = nodeCollection.ToList();
                    issues.Reset();
                }

                if (placeHolderModel==null)
                    placeHolderModel = model.ObjectTypes.Find(o => ExpandedNodeId.Parse(o.NodeId, MyOPCSession.NamespaceUris) == node.TypeDefinition);
                if (placeHolderModel != null)
                {
                    do
                    {
                        if (placeHolderModel.OtherReferencedNodes.Count > 0)
                        {
                            foreach (var tFolder in placeHolderModel.OtherReferencedNodes.Select(s=>s.Node))
                            {
                                var tNameParts = tFolder.BrowseName.Split(';');
                                var tJustName = tNameParts.Length > 1 ? tNameParts[1] : tNameParts[0];
                                var t = nodeCollection.Find(s => tJustName==s.BrowseName.Name);
                                if (t != null)
                                {
                                    foundRef.Remove(t);
                                    ValidateCollection(myModels, t,issues,foundRef, tFolder);
                                }
                                else
                                {
                                    FireEventLog($"Validation-Error: NodeType {node.TypeDefinition} expected a folder named {tFolder.BrowseName} but was not found in the Servers Node: {node.NodeId}", 3, 3);
                                    issues.Errors++;
                                }
                            }
                        }

                        var uni = placeHolderModel.Properties.Union(placeHolderModel.DataVariables);
                        foreach (var opcProp in uni)
                        {
                            var tNameParts = opcProp.BrowseName.Split(';');
                            var tJustName = tNameParts.Length > 1 ? tNameParts[1] : tNameParts[0];
                            var tNodeToValidate = nodeCollection.Find(d => tJustName==d.BrowseName.Name);
                            if (tNodeToValidate == null)
                            {
                                if (opcProp.ModellingRule.Equals("MandatoryPlaceholder"))
                                {
                                    FireEventLog($"Validation-Error: MandatoryPlaceholder requires: {opcProp.BrowseName} ID:{opcProp.NodeId}. Client is in violation and must implement Type:{opcProp.NodeId}", 3, 3);
                                    issues.Errors++;
                                }
                                else
                                {
                                    FireEventLog($"Validation-Warning: {opcProp.BrowseName} ID:{opcProp.NodeId} is optional and was not found in {node.BrowseName}. Either not implemented of does the node have the wrong Base type?", 2, 2);
                                    issues.Warnings++;
                                }
                            }
                            else
                            {
                                foundRef.Remove(tNodeToValidate);
                                var mynode = MyOPCSession.NodeCache.Find(tNodeToValidate.NodeId);
                                if (mynode?.TypeDefinitionId != null)
                                {
                                    if (ExpandedNodeId.Parse(opcProp.NodeId, MyOPCSession.NamespaceUris) != mynode.TypeDefinitionId)
                                    {
                                        FireEventLog($"Validation-Error: Node:{tNodeToValidate.BrowseName.Name} with ID({tNodeToValidate.NodeId}) is implemented as {NodeId.ToExpandedNodeId(((NodeId)mynode.TypeDefinitionId), MyOPCSession.NamespaceUris)} but should use the correct type: {opcProp.NodeId} ", 3, 3);
                                        issues.Errors++;
                                    }
                                    else
                                    {
                                        FireEventLog($"Validation-Info: {opcProp.BrowseName} is validated!", 2, 1);
                                        issues.Infos++;
                                    }
                                }
                                else
                                {
                                    FireEventLog($"Validation-Error: Node:{tNodeToValidate.BrowseName.Name} with ID({tNodeToValidate.NodeId}) does not have any Type Definition, but the Type NodeSet Requires Type: {opcProp.NodeId}", 3, 3);
                                    issues.Errors++;
                                }
                            }
                        }
                        placeHolderModel = (placeHolderModel as BaseTypeModel)?.SuperType;
                    } while (placeHolderModel != null);
                }
                if (isnew)
                {
                    foreach (var n in foundRef)
                    {
                        FireEventLog($"Validation-Info: Node:{n.BrowseName.Name} with ID({n.NodeId}) is defined in the server but has not type information", 2, 1);
                        issues.Infos++;
                    }
                    if (issues.HasIssues)
                        FireEventLog($"Validation-End: {node.DisplayName} validated with {(issues.Errors>0?$"{issues.Errors} errors":"")} {(issues.Warnings > 0 ? $"{issues.Warnings} warnings" : "")}  --------------", 4, 3);
                }
            }
            else
            {
                FireEventLog($"Validation: {node.DisplayName} has a Type ({node.TypeDefinition}) but is not found in the NodeSet. Is the Type newer than the NodeSet?", 2,2);
                issues.Warnings++;
            }
        }

        UANodeSetImportResult ImportNewNodeset(List<string> pFileNames)
        {
            var fileCache = new UANodeSetFileCache(MyServerStates.LocalCachePath);
            var myCacheManager = new UANodeSetCacheManager(fileCache, MyServerStates.MyCloudLib);
            UANodeSetImportResult resultSet = myCacheManager.ImportNodeSetFiles(pFileNames, false);
            eventImportResult?.Invoke(resultSet);
            foreach (var pFile in pFileNames)
            {
                if (File.Exists(pFile))
                    File.Delete(pFile);
            }
            if (!string.IsNullOrEmpty(resultSet.ErrorMessage))
            {
                if (resultSet?.Models?.Any() == true)
                {
                    foreach (var tmodel in resultSet.Models)
                    {
                        if (tmodel.NewInThisImport && File.Exists(tmodel.FilePath))
                            File.Delete(tmodel.FilePath);
                    }
                }
            }
            return resultSet;
        }

        public void GatherKnownObjectTypes(List<string> allSubTypes, NodeId nodeId)
        {
            BrowseDescription typeNodeToBrowse = new BrowseDescription();
            typeNodeToBrowse.NodeId = nodeId;
            typeNodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            typeNodeToBrowse.NodeClassMask = (int)NodeClass.Object;
            typeNodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            ReferenceDescriptionCollection eventTypes = ClientUtils.Browse(MyOPCSession, typeNodeToBrowse, true);

            foreach (var eventType in eventTypes)
            {
                var ns = MyOPCSession.NamespaceUris.GetString(eventType.TypeDefinition.NamespaceIndex);
                if (!allSubTypes.Contains(ns))
                {
                    FireEventLog($"nid:{eventType.NodeId} ns:{ns} id:{eventType.TypeDefinition}  ", 4);
                    allSubTypes.Add(ns);
                }
                GatherKnownObjectTypes(allSubTypes, (NodeId)eventType.NodeId);
            }
        }

        async Task<IEnumerable<string>> ResolveNamespaces(List<string> foundNS)
        {
            if (MyServerStates.MyCloudLib != null || (!string.IsNullOrEmpty(MyServerStates.CloudLibUID) && !string.IsNullOrEmpty(MyServerStates.CloudLibPWD) && !string.IsNullOrEmpty(MyServerStates.CloudLibEP)))
            {
                try
                {
                    if (MyServerStates.MyCloudLib == null)
                    {
                        FireEventLog($"Login into CloudLib {MyServerStates.CloudLibEP}...", 4);
                        MyServerStates.MyCloudLib = new UANodeSetCloudLibraryResolver(MyServerStates.CloudLibEP, MyServerStates.CloudLibUID, MyServerStates.CloudLibPWD);
                        if (MyServerStates.MyCloudLib == null)
                        {
                            FireEventLog($"Login into CloudLib {MyServerStates.CloudLibEP} was not successful!", 3);
                            return new List<string>();
                        }
                    }
                    List<ModelNameAndVersion> tList = new List<ModelNameAndVersion>();
                    foreach (var ns in foundNS)
                    {
                        tList.Add(new ModelNameAndVersion(new ModelTableEntry { ModelUri = ns }) { PublicationDate = null });
                    }
                    FireEventLog($"Contacting CloudLib {MyServerStates.CloudLibEP} to Resolve {tList.Count} Nodesets...", 4);
                    var retList = await MyServerStates.MyCloudLib.ResolveNodeSetsAsync(tList);
                    return retList;
                }
                catch (Exception)
                {
                    //empty for now
                }
            }
            return new List<string>();
        }
    }

    class NullLogger : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public NullLogger(Action<string,int> peventBlaster)
        {
            eventBlaster = peventBlaster;
        }

        Action<string,int> eventBlaster;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            //intenionally empty
            eventBlaster?.Invoke($"{state}", (int)logLevel);
        }
    }
}
