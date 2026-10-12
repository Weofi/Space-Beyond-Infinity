using System;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Networking
{
    /// <summary>
    /// Static configuration parsed from command-line arguments.
    /// Recognised args: -transport=utp|steam, -ip=127.0.0.1, -port=7777.
    /// Defaults: Steam in standalone builds, UTP in the editor.
    /// </summary>
    public static class TransportConfig
    {
        /// <summary>The transport requested via command line or platform default.</summary>
        public static TransportKind RequestedTransport { get; private set; }

        /// <summary>IP address for UTP connections (default 127.0.0.1).</summary>
        public static string IpAddress { get; private set; }

        /// <summary>Port for UTP connections (default 7777).</summary>
        public static int Port { get; private set; }

        /// <summary>-autohost: host immediately on launch (UTP or Steam per transport).</summary>
        public static bool AutoHost { get; private set; }
        /// <summary>-autojoin=IP: join a UTP host immediately on launch.</summary>
        public static string AutoJoinIp { get; private set; }
        /// <summary>-autostart: host starts the run automatically once N players (-players=N) are connected.</summary>
        public static bool AutoStart { get; private set; }
        public static int AutoStartPlayers { get; private set; } = 1;

        /// <summary>Main-menu override of the requested transport.</summary>
        public static void SetRequested(TransportKind kind) => RequestedTransport = kind;

        static TransportConfig()
        {
            // Platform default: Steam in standalone builds, UTP in editor.
#if UNITY_EDITOR
            RequestedTransport = TransportKind.UTP;
#else
            RequestedTransport = TransportKind.Steam;
#endif
            IpAddress = "127.0.0.1";
            Port = 7777;
            ParseCommandLine();
        }

        static void ParseCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg.StartsWith("-transport=", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = arg.Substring("-transport=".Length);
                        if (Enum.TryParse(val, true, out TransportKind kind))
                            RequestedTransport = kind;
                    }
                    else if (arg.StartsWith("-ip=", StringComparison.OrdinalIgnoreCase))
                    {
                        IpAddress = arg.Substring("-ip=".Length);
                    }
                    else if (arg.StartsWith("-port=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(arg.Substring("-port=".Length), out int port))
                            Port = port;
                    }
                    else if (arg.Equals("-autohost", StringComparison.OrdinalIgnoreCase)) AutoHost = true;
                    else if (arg.StartsWith("-autojoin=", StringComparison.OrdinalIgnoreCase))
                    {
                        AutoJoinIp = arg.Substring("-autojoin=".Length);
                        RequestedTransport = TransportKind.UTP;
                    }
                    else if (arg.Equals("-autostart", StringComparison.OrdinalIgnoreCase)) AutoStart = true;
                    else if (arg.StartsWith("-players=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(arg.Substring("-players=".Length), out int n)) AutoStartPlayers = Mathf.Clamp(n, 1, 4);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TransportConfig] Failed to parse command line: " + e.Message);
            }
        }
    }
}
