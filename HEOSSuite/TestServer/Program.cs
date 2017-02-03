namespace TelnetServer {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;

    public class Client {
        public IPEndPoint remoteEndPoint;
        public DateTime connectedAt;
        public string commandIssued;

        public Client(IPEndPoint _remoteEndPoint, DateTime _connectedAt) {
            remoteEndPoint = _remoteEndPoint;
            connectedAt = _connectedAt;
        }
    }

    class Program {
        private static Socket serverSocket;
        private static byte[] data = new byte[dataSize];
        private static bool newClients = true;
        private const int dataSize = 1024;
        private static Dictionary<Socket, Client> clientList = new Dictionary<Socket, Client>();

        public static void Main(string[] args) {
            int port;
            if(args.Length == 0 || !int.TryParse(args[0], out port)) port = 1255;
            Console.WriteLine("Starting on  port {0}...", port);
            new Thread(new ThreadStart(backgroundThread)) { IsBackground = false }.Start();
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, port);
            serverSocket.Bind(endPoint);
            serverSocket.Listen(0);
            serverSocket.BeginAccept(new AsyncCallback(acceptConnection), serverSocket);
            Console.WriteLine("Listening....");
        }

        private static void backgroundThread() {
            while(true) {
                string input = Console.ReadLine();

                if(input == "clients") {
                    if(clientList.Count == 0) continue;
                    int clientNumber = 0;
                    foreach(KeyValuePair<Socket, Client> client in clientList) {
                        Client currentClient = client.Value;
                        clientNumber++;
                        Console.WriteLine(string.Format("Client #{0} (From: {1}:{2}, Connection time: {3})", clientNumber,
                            currentClient.remoteEndPoint.Address.ToString(), currentClient.remoteEndPoint.Port, currentClient.connectedAt));
                    }
                }

                if(input.StartsWith("kill")) {
                    string[] _Input = input.Split(' ');
                    int clientID = 0;
                    try {
                        if(Int32.TryParse(_Input[1], out clientID) && clientID >= clientList.Keys.Count) {
                            int currentClient = 0;
                            foreach(Socket currentSocket in clientList.Keys.ToArray()) {
                                currentClient++;
                                if(currentClient == clientID) {
                                    currentSocket.Shutdown(SocketShutdown.Both);
                                    currentSocket.Close();
                                    clientList.Remove(currentSocket);
                                    Console.WriteLine("Client has been disconnected and cleared up.");
                                }
                            }
                        } else { Console.WriteLine("Could not kick client: invalid client number specified."); }
                    } catch { Console.WriteLine("Could not kick client: invalid client number specified."); }
                }

                if(input == "killall") {
                    int deletedClients = 0;
                    foreach(Socket currentSocket in clientList.Keys.ToArray()) {
                        currentSocket.Shutdown(SocketShutdown.Both);
                        currentSocket.Close();
                        clientList.Remove(currentSocket);
                        deletedClients++;
                    }

                    Console.WriteLine("{0} clients have been disconnected and cleared up.", deletedClients);
                }

                if(input == "lock") { newClients = false; Console.WriteLine("Refusing new connections."); }
                if(input == "unlock") { newClients = true; Console.WriteLine("Accepting new connections."); }
            }
        }

        private static void acceptConnection(IAsyncResult result) {
            if(!newClients) return;
            Socket oldSocket = (Socket)result.AsyncState;
            Socket newSocket = oldSocket.EndAccept(result);
            Client client = new Client((IPEndPoint)newSocket.RemoteEndPoint, DateTime.Now);
            clientList.Add(newSocket, client);
            Console.WriteLine("Client connected. (From: " + string.Format("{0}:{1}", client.remoteEndPoint.Address.ToString(), client.remoteEndPoint.Port) + ")");
            serverSocket.BeginAccept(new AsyncCallback(acceptConnection), serverSocket);
        }

        private static void sendData(IAsyncResult result) {
            try {
                Socket clientSocket = (Socket)result.AsyncState;
                clientSocket.EndSend(result);
                clientSocket.BeginReceive(data, 0, dataSize, SocketFlags.None, new AsyncCallback(ReceiveData), clientSocket);
            } catch { }
        }

        private static void ReceiveData(IAsyncResult result) {
            try {
                Socket clientSocket = (Socket)result.AsyncState;
                Client client;
                clientList.TryGetValue(clientSocket, out client);
                int received = clientSocket.EndReceive(result);

                if(received == 0) {
                    clientSocket.Close();
                    clientList.Remove(clientSocket);
                    serverSocket.BeginAccept(new AsyncCallback(acceptConnection), serverSocket);
                    Console.WriteLine("Client disconnected. (From: " + string.Format("{0}:{1}", client.remoteEndPoint.Address.ToString(), client.remoteEndPoint.Port) + ")");
                    return;
                }

                Console.WriteLine("Received '{0}' (From: {1}:{2})", BitConverter.ToString(data, 0, received), client.remoteEndPoint.Address.ToString(), client.remoteEndPoint.Port);

                // 0x2E & 0X0D => '.' & LF - why????
                if(data[0] == 0x2E && data[1] == 0x0D && client.commandIssued.Length == 0) {
                    string currentCommand = client.commandIssued;
                    Console.WriteLine(string.Format("Received '{0}' (From: {2}:{3})", currentCommand, client.remoteEndPoint.Address.ToString(), client.remoteEndPoint.Port));
                    client.commandIssued = "";
                    byte[] message = Encoding.ASCII.GetBytes("\u001B[1J\u001B[H" + HandleCommand(clientSocket, currentCommand));
                    clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(sendData), clientSocket);
                    // 0x0D & 0x0A => CR/LF
                } else if(data[0] == 0x0D && data[1] == 0x0A) {
                    string currentCommand = client.commandIssued;
                    Console.WriteLine(string.Format("Received '{0}' (From: {1}:{2}", currentCommand, client.remoteEndPoint.Address.ToString(), client.remoteEndPoint.Port));
                    client.commandIssued = "";
                    byte[] message = Encoding.ASCII.GetBytes("\u001B[1J\u001B[H" + HandleCommand(clientSocket, currentCommand));
                    clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(sendData), clientSocket);
                } else {
                    // 0x08 => backspace character
                    if(data[0] == 0x08) {
                        if(client.commandIssued.Length > 0) {
                            client.commandIssued = client.commandIssued.Substring(0, client.commandIssued.Length - 1);
                            byte[] message = Encoding.ASCII.GetBytes("\u0020\u0008");
                            clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(sendData), clientSocket);
                        } else {
                            clientSocket.BeginReceive(data, 0, dataSize, SocketFlags.None, new AsyncCallback(ReceiveData), clientSocket);
                        }
                    }

                    // 0x7F => delete character
                    else if(data[0] == 0x7F) {
                        clientSocket.BeginReceive(data, 0, dataSize, SocketFlags.None, new AsyncCallback(ReceiveData), clientSocket);
                    } else {
                        string currentCommand = client.commandIssued;
                        client.commandIssued += Encoding.ASCII.GetString(data, 0, received);
                        clientSocket.BeginReceive(data, 0, dataSize, SocketFlags.None, new AsyncCallback(ReceiveData), clientSocket);
                    }
                }
            } catch { }
        }



        private static string HandleCommand(Socket clientSocket, string input) {
            string output = "-- TELNET TEST SERVER --\n\r\n\r";
            byte[] dataInput = Encoding.ASCII.GetBytes(input);
            Client client;
            clientList.TryGetValue(clientSocket, out client);
            if(input == "test") {
                output += "Hello there.\n\r";
            }
            if(input == "getrekt") {
                return output;
            } else {
                output += "Please enter a valid command:\n\r";
            }
            return output;
        }
    }
}
