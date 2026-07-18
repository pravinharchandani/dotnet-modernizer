using System;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;

namespace LegacyShop.Core
{
    /// <summary>
    /// Legacy inter-process order channel built on .NET Remoting,
    /// which has no equivalent in modern .NET.
    /// </summary>
    public class RemotingOrderChannel : MarshalByRefObject
    {
        public static void RegisterServer(int port)
        {
            var channel = new TcpChannel(port);
            ChannelServices.RegisterChannel(channel, ensureSecurity: false);
            RemotingConfiguration.RegisterWellKnownServiceType(
                typeof(RemotingOrderChannel),
                "LegacyShop/Orders",
                WellKnownObjectMode.Singleton);
        }

        public static RemotingOrderChannel Connect(string host, int port)
        {
            string url = string.Format("tcp://{0}:{1}/LegacyShop/Orders", host, port);
            return (RemotingOrderChannel)Activator.GetObject(typeof(RemotingOrderChannel), url);
        }

        public override object InitializeLifetimeService()
        {
            return null; // lease never expires
        }

        public string Ping()
        {
            return "pong from " + Environment.MachineName;
        }
    }
}
