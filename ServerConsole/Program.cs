using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Server
{
    private TcpListener listener;
    private CancellationTokenSource cancellationTokenSource;
    private ConcurrentDictionary<int, TcpClient> clients;
    private ConcurrentDictionary<int, string> clientNames;
    private Task sendTask;

    public void Start()
    {
        // Serverı başlat
        IPAddress ipAddress = IPAddress.Parse("127.0.0.1");
        listener = new TcpListener(ipAddress, 8080);
        listener.Start();

        // İstemcilerin bilgilerini depolamak için concurrent sözlükleri oluştur
        clients = new ConcurrentDictionary<int, TcpClient>();
        clientNames = new ConcurrentDictionary<int, string>();

        // Send işlemlerini yürütmek için yeni bir görev oluştur
        sendTask = Task.Run(SendMessagesToClients);

        Console.WriteLine("Server başlatıldı. Bağlantı bekleniyor...");

        // İstemci bağlantılarını kabul etmeye başla
        cancellationTokenSource = new CancellationTokenSource();
        AcceptClientsAsync();
    }

    private async Task AcceptClientsAsync()
    {
        int clientId = 1;

        while (!cancellationTokenSource.Token.IsCancellationRequested)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            clients.TryAdd(clientId, client);
            Thread thread = new Thread(() =>
            {
                 ProcessClient(clientId, client);
            });
            thread.Start();

            Console.WriteLine("Yeni bir istemci bağlandı. ID: " + clientId);

            clientId++;
        }
    }

    private async Task ProcessClient(int clientId, TcpClient client)
    {
        var receiveBuffer = new byte[1024];
        var receivedData = new StringBuilder();

        while (client.Connected && !cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                int bytesRead = await client.GetStream().ReadAsync(receiveBuffer, 0, receiveBuffer.Length);

                if (bytesRead > 0)
                {
                    string receivedText = Encoding.ASCII.GetString(receiveBuffer, 0, bytesRead);
                    receivedData.Append(receivedText);

                    if (receivedText.Contains("<EOF>"))
                    {
                        receivedData.Replace("<EOF>", "");
                        string message = receivedData.ToString();
                        receivedData.Clear();
                        Console.WriteLine("Client " + clientId + " mesaj gönderdi: " + message);

                        // İlk mesajı alırken istemcinin ismini kaydet
                        if (!clientNames.ContainsKey(clientId))
                        {
                            clientNames.TryAdd(clientId, message);
                        }

                        // Tüm istemcilere mesajı gönder
                        await SendMessageToAllClients(message);
                    }
                }
            }
            catch (SocketException)
            {
                // İstemci bağlantısı koptu
                break;
            }
        }

        // İstemci bağlantısı kapatıldı
        client.Close();
        clients.TryRemove(clientId, out _);
        clientNames.TryRemove(clientId, out _);

        Console.WriteLine("Client bağlantısı koptu. ID: " + clientId);
    }

    private async Task SendMessageToAllClients(string message)
    {
        foreach (var clientHandler in clients.Values)
        {
            byte[] data = Encoding.ASCII.GetBytes(message + "<EOF>");
            await clientHandler.GetStream().WriteAsync(data, 0, data.Length);
        }
    }

    private async Task SendMessagesToClients()
    {
        await Task.Delay(100);

        while (!cancellationTokenSource.Token.IsCancellationRequested)
        {
            string message = Console.ReadLine();

            if (string.IsNullOrEmpty(message))
                continue;

            if (message.ToLower() == "exit")
                break;

            await SendMessageToAllClients("Server: " + message);
        }
    }

    public void Stop()
    {
        cancellationTokenSource.Cancel();
        sendTask.Wait();
        listener.Stop();
    }
}

class Program
{
    static void Main()
    {
        Server server = new Server();
        server.Start();

        Console.WriteLine("Çalışmak için ENTER tuşuna basın...");
        Console.ReadLine();

        server.Stop();
    }
}
