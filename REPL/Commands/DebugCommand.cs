﻿using System;
using System.Collections.Generic;
using Mono.Options;
using Vinchuca;
using Vinchuca.Network.Protocol.Peers;

namespace REPL.Commands
{
    class DebugCommand : Command
    {
        private readonly Agent _agent;
        private readonly CommandLineReader _repl;
        public bool ShowHelp { get; set; }

        public DebugCommand(Agent agent, CommandLineReader repl)
            : base("debug", "Allows to perform diagnostic tasks")
        {
            _agent = agent;
            _repl = repl;
            Options = new OptionSet() {
                "usage: debug command",
                "",
                "Execute diagnostic commands.",
                "eg: debug get-peer-list",
                { "help|h|?","Show this message and exit.", v => ShowHelp = v != null }
            };
            _repl.AddAutocompletionWords("debug", "get-peer-list", "clear-peer-list");
        }

        public override int Invoke(IEnumerable<string> args)
        {
            try
            {
                var extra = Options.Parse(args);
                if (ShowHelp)
                {
                    Options.WriteOptionDescriptions(CommandSet.Out);
                    return 0;
                }
                if (extra.Count == 0)
                {
                    Console.WriteLine("commands: Missing required argument `command`.");
                    Console.WriteLine("commands: Use `help debug` for details.");
                    return 1;
                }
                var cmd = extra[0];
                if (cmd == "get-peer-list")
                {

                    foreach (var info in _agent.PeerList)
                    {
                        Console.WriteLine("{0,-54}{1,-26}{2,4}{3,10}", info, 
                            info.LastSeen.ToLocalTime(), 
                            info.Reputation,
                            Convert.ToBase64String(info.EncryptionKey).Substring(0, 8));
                        _repl.AddAutocompletionWords(info.BotId.ToString());
                    }
                    Console.WriteLine();
                }
                else if(cmd == "clear-peer-list")
                {
                    _agent.PeerList.Clear();
                }
                return 0;
            }
            catch (Exception e)
            {
                // _repl.Console.WriteLine("commands: {0}", CommandDemo.Verbosity >= 1 ? e.ToString() : e.Message);
                return 1;
            }
        }
    }
}
