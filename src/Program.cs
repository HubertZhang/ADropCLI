using Makaretu.Dns;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using ADrop;
using ADrop.Proto;
using System.Text;
using System.IO;

namespace ADrop.CLI
{

    class Program
    {

        public static int Main(string[] args)
        {
            var rootCommand = new RootCommand
            {
                Description = "ADrop Client"
            };

            var receiveCommand = new Command("receive") {
                new Option<String>(new string[]{"-n", "--device-name"}, "name of this device"),
            };
            receiveCommand.Handler = CommandHandler.Create<ReceiveParameters>(Receive);
            rootCommand.AddCommand(receiveCommand);

            var sendCommand = new Command("send"){
                new Option<String>(new string[]{"-t", "--text"}, "text to send"),
                new Option<System.IO.FileInfo[]>(new string[]{"-f", "--files"}, "file to send").ExistingOnly()
            };

            sendCommand.Handler = CommandHandler.Create<SendParameters>(Send);
            rootCommand.AddCommand(sendCommand);

            var scanallCommand = new Command("scanall");
            scanallCommand.Handler = CommandHandler.Create(Scan);
            rootCommand.AddCommand(scanallCommand);

            var scanCommand = new Command("scan");
            scanCommand.Handler = CommandHandler.Create(Scan);
            rootCommand.AddCommand(scanCommand);

            return rootCommand.InvokeAsync(args).Result;
        }

        public class SendParameters
        {
            public String text { get; set; }
            public FileInfo[] files { get; set; }
        }

        public static async Task<int> Send(SendParameters param)
        {
            if (param.text == null && param.files == null)
            {
                Console.WriteLine("One of text and files must be specified");
                return -1;
            }
            Console.WriteLine("Scanning...");
            var instances = new HashSet<DomainName>();
            using (var sd = new ServiceDiscovery())
            {
                sd.ServiceInstanceDiscovered += (s, e) =>
                {
                    if (!instances.Contains(e.ServiceInstanceName))
                    {
                        Console.WriteLine($"Found '{e.ServiceInstanceName}'");
                        instances.Add(e.ServiceInstanceName);
                    }
                };
                sd.QueryServiceInstances(ADropDomainName);
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10.0));
            }

            if (instances.Count() == 0)
            {
                Console.WriteLine("Cannot find another endpoint...");
                return 0;
            }
            var instancesList = instances.Select(s =>
            {
                return s.ToString();
            }).ToList();
            var index = DisplayMenu("Select an endpoint", instancesList);
            if (index == -1)
            {
                Console.WriteLine("Invalid input...");
                return -1;
            }
            var server = instancesList[index];

            SRVRecord s;
            try
            {
                var source = new CancellationTokenSource(5000);
                s = await ADrop.Util.Resolve(server, source.Token);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"Failed to resolve {server}, timeout");
                return 0;
            }
            if (s == null)
            {
                Console.WriteLine($"Failed to resolve {server}, SRVRecord is empty");
                return 0;
            }

            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork,
                                    SocketType.Stream,
                                    ProtocolType.Tcp))
                {
                    socket.Connect(s.Target.ToString(), s.Port);
                    using (var sender = new ADrop.Sender(socket))
                    {

                        var files = new List<MetaInfo.Types.FileInfo>();
                        if (param.text != null)
                        {
                            files.Add(new MetaInfo.Types.FileInfo
                            {
                                FileType = "text/plain"
                            });
                        }
                        if (param.files != null)
                        {
                            foreach (var file in param.files)
                            {
                                files.Add(new MetaInfo.Types.FileInfo
                                {
                                    FileType = MimeTypes.MimeTypeMap.GetMimeType(file.Name)
                                });
                            }
                        }
                        var meta = new MetaInfo
                        {
                            FileInfos = { files }
                        };
                        sender.SendMetadata(meta);
                        var action = sender.WaitForConfirmation();
                        switch (action.Type)
                        {
                            case ADrop.Proto.Action.Types.ActionType.Accepted:
                                if (param.text != null)
                                {
                                    Console.WriteLine("Sending text...");
                                    sender.SendData(Encoding.UTF8.GetBytes(param.text));
                                }
                                if (param.files != null)
                                {
                                    foreach (var file in param.files)
                                    {
                                        Console.WriteLine($"Sending file {file.Name} ...");
                                        using (var stream = file.OpenRead())
                                        {
                                            sender.SendData(stream);
                                        }
                                    }
                                }
                                Console.WriteLine("Request completed");
                                break;
                            default:
                                Console.WriteLine("Request is rejected.");
                                break;
                        }
                    }
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine(e);
            }
            return 0;
        }
        public static String ADropDomainName = "_adrop._tcp";
        static public int DisplayMenu(String title, IEnumerable<String> options)
        {
            Console.WriteLine(title);
            Console.WriteLine();
            for (int i = 0; i < options.Count(); i++)
            {
                Console.WriteLine($"{i + 1}. {options.ElementAt(i)}");
            }
            Console.Write("Input:");
            var resultLine = Console.ReadLine() ?? "0";
            try
            {
                var result = Convert.ToInt32(resultLine);
                if (result <= 0 || result > options.Count())
                {
                    return -1;
                }
                return result - 1;
            }
            catch
            {
                return -1;
            }


        }
        public class ReceiveParameters
        {
            public String deviceName { get; set; }
        }

        public static async Task Receive(ReceiveParameters param)
        {
            if (param.deviceName == null)
            {
                param.deviceName = Environment.MachineName;
            }
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork,
                                    SocketType.Stream,
                                    ProtocolType.Tcp))
                {
                    using (var sd = new ServiceDiscovery())
                    {
                        var endPoint = new IPEndPoint(IPAddress.Any, 0);
                        socket.Bind(endPoint);
                        var port = ((IPEndPoint)socket.LocalEndPoint).Port;
                        var serviceProfile = new ServiceProfile(param.deviceName, ADropDomainName, (ushort)port);
                        sd.Advertise(serviceProfile);
                        socket.Listen(1);
                        var incoming = socket.Accept();
                        Console.WriteLine($"{incoming.RemoteEndPoint} connect, receiving metadata.");
                        using (var receiver = new Receiver(incoming))
                        {


                            var meta = receiver.ReadMetadata();
                            Console.WriteLine($"MetaInfo: {meta.FileInfos.Count()} files with type:\n{meta.FileInfos}");

                            receiver.SendAction(Proto.Action.Types.ActionType.Accepted);

                            for (int i = 0; i < meta.FileInfos.Count(); i++)
                            {
                                var mimeType = meta.FileInfos[i].FileType;
                                var result = await receiver.ReadFile();
                                if (mimeType == "text/plain")
                                {
                                    Console.WriteLine($"received text: \n{Encoding.UTF8.GetString(result)}");
                                }
                                else
                                {
                                    var extension = MimeTypes.MimeTypeMap.GetExtension(mimeType) ?? ".dat";
                                    File.WriteAllBytes($"{i}{extension}", result);
                                }
                            }
                        }
                    }
                    return;
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine(e);
            }

            return;
        }

        public static void Scan()
        {
            var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            mdns.NetworkInterfaceDiscovered += (s, e) =>
            {
                foreach (var nic in e.NetworkInterfaces)
                {
                    Console.WriteLine($"NIC '{nic.Name}'");
                }

                // Ask for the name of all services.
                sd.QueryAllServices();
            };

            sd.ServiceDiscovered += (s, serviceName) =>
            {
                Console.WriteLine($"service '{serviceName}'");

                // Ask for the name of instances of the service.
                mdns.SendQuery(serviceName, type: DnsType.PTR);
            };

            sd.ServiceInstanceDiscovered += (s, e) =>
            {
                Console.WriteLine($"service instance '{e.ServiceInstanceName}'");

                // Ask for the service instance details.
                mdns.SendQuery(e.ServiceInstanceName, type: DnsType.SRV);
            };

            mdns.AnswerReceived += (s, e) =>
            {
                // Is this an answer to a service instance details?
                var servers = e.Message.Answers.OfType<SRVRecord>();
                foreach (var server in servers)
                {
                    Console.WriteLine($"host '{server.Target}' for '{server.Name}'");

                    // Ask for the host IP addresses.
                    mdns.SendQuery(server.Target, type: DnsType.A);
                    mdns.SendQuery(server.Target, type: DnsType.AAAA);
                }

                // Is this an answer to host addresses?
                var addresses = e.Message.Answers.OfType<AddressRecord>();
                foreach (var address in addresses)
                {
                    Console.WriteLine($"host '{address.Name}' at {address.Address}");
                }

            };

            try
            {
                mdns.Start();
                Console.ReadLine();
            }
            finally
            {
                sd.Dispose();
                mdns.Stop();
            }
        }
    }
}