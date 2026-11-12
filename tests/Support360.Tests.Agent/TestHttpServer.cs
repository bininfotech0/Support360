using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Support360.Agent;

namespace Support360.Tests.Agent;

public sealed class TestHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Action<HttpListenerContext> _handler;
    private readonly Thread _worker;

    public string Url { get; }

    public TestHttpServer(Action<HttpListenerContext> handler)
    {
        _handler = handler;
        var port = FreePort();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _worker = new Thread(AcceptLoop) { IsBackground = true, Name = "test-http-server" };
        _worker.Start();
    }

    public void Dispose()
    {
        try
        {
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void AcceptLoop()
    {
        while (true)
        {
            HttpListenerContext? context;
            try
            {
                context = _listener.GetContext();
            }
            catch (Exception)
            {
                return;
            }
            if (context is null)
            {
                return;
            }
            try
            {
                _handler(context);
            }
            catch (Exception)
            {
                try
                {
                    context.Response.StatusCode = 500;
                }
                catch (ObjectDisposedException)
                {
                }
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch (Exception)
                {
                }
            }
        }
    }

    public static void Respond(HttpListenerContext context, int status)
    {
        context.Response.StatusCode = status;
    }

    public static void RespondJson<T>(HttpListenerContext context, T body)
    {
        RespondRaw(context, 200, JsonSerializer.Serialize(body, AgentJson.Options));
    }

    public static void RespondRaw(HttpListenerContext context, int status, string json)
    {
        var data = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = data.Length;
        context.Response.OutputStream.Write(data);
    }

    public static void RespondRaw(HttpListenerContext context, string json) => RespondRaw(context, 200, json);

    public static T ReadBody<T>(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), AgentJson.Options)
            ?? throw new InvalidOperationException("test request body was empty");
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
