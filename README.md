# MagicOnion
[![CircleCI](https://circleci.com/gh/Cysharp/MagicOnion.svg?style=svg)](https://circleci.com/gh/Cysharp/MagicOnion) [![Releases](https://img.shields.io/github/release/Cysharp/MagicOnion.svg)](https://github.com/Cysharp/MagicOnion/releases)

Unified Realtime/API Engine for .NET Core and Unity.

[📖 Table of contents](#-table-of-contents)

## What is it?
MagicOnion is an Realtime Network Engine like [SignalR](https://github.com/aspnet/AspNetCore/tree/master/src/SignalR), [Socket.io](https://socket.io/) and RPC-Web API Framework like any web-framework.

MagicOnion is built on [gRPC](https://grpc.io/) so fast(HTTP/2) and compact(binary) network transport. It does not requires `.proto` and generate unlike plain gRPC. Protocol schema can share a C# interface and classes.

![image](https://user-images.githubusercontent.com/46207/50965239-c4fdb000-1514-11e9-8365-304c776ffd77.png)

> Share interface as schema and request as API Service seems like normal C# code

![image](https://user-images.githubusercontent.com/46207/50965825-7bae6000-1516-11e9-9501-dc91582f4d1b.png)

> StreamingHub realtime service, broadcast data to many connected clients

MagicOnion is for Microservices(communicate between .NET Core Servers like Orleans, ServiceFabric, AMBROSIA), API Service(for WinForms/WPF like WCF, ASP.NET Core MVC), Native Client’s API(for Xamarin, Unity) and Realtime Server that replacement like Socket.io, SignalR, Photon, UNet, etc.

## Quick Start
for .NET 4.6, 4.7 and .NET Standard 2.0(.NET Core) available in NuGet. Unity supports see [Unity client Supports](#unity-client-supports) section. HttpGateway + Swagger Intergarion supports see [Swagger](#swagger) section.

```
Install-Package MagicOnion
```
MagicOnion has two sides, `Service` for like web-api and `StreamingHub` for relatime communication. At first, see define `Service`.

```csharp
using Grpc.Core;
using MagicOnion;
using MagicOnion.Server;
using System;

// define interface as Server/Client IDL.
// implements T : IService<T> and share this type between server and client.
public interface IMyFirstService : IService<IMyFirstService>
{
    // Return type must be `UnaryResult<T>` or `Task<UnaryResult<T>>`.
    // If you can use C# 7.0 or newer, recommend to use `UnaryResult<T>`.
    UnaryResult<int> SumAsync(int x, int y);
}

// implement RPC service to Server Project.
// inehrit ServiceBase<interface>, interface
public class MyFirstService : ServiceBase<IMyFirstService>, IMyFirstService
{
    // You can use async syntax directly.
    public async UnaryResult<int> SumAsync(int x, int y)
    {
        Logger.Debug($"Received:{x}, {y}");

        return x + y;
    }
}
```


and, launch the server.

```csharp
class Program
{
    static void Main(string[] args)
    {
        GrpcEnvironment.SetLogger(new Grpc.Core.Logging.ConsoleLogger());

        // setup MagicOnion and option.
        var service = MagicOnionEngine.BuildServerServiceDefinition(isReturnExceptionStackTraceInErrorDetail: true);

        var server = new global::Grpc.Core.Server
        {
            Services = { service },
            Ports = { new ServerPort("localhost", 12345, ServerCredentials.Insecure) }
        };
        
        // launch gRPC Server.
        server.Start();

        // and wait.
        Console.ReadLine();
    }
}
```

write the client.

```csharp
// standard gRPC channel
var channel = new Channel("localhost", 12345, ChannelCredentials.Insecure);

// get MagicOnion dynamic client proxy
var client = MagicOnionClient.Create<IMyFirstService>(channel);

// call method.
var result = await client.SumAsync(100, 200);
Console.WriteLine("Client Received:" + result);
```

MagicOnion allows primitive, multiple request value. Complex type is serialized by LZ4 Compressed MsgPack by [MessagePack for C#](https://github.com/neuecc/MessagePack-CSharp) so type should follow MessagePack for C# rules.

> for Server Hosting, We recommend to use `MagicOnion.Hosting`, it is easy to host and wait terminate signal, load from config, support DI, etc. see [Server host](#server-host) section.


## 📖 Table of contents

- [What is it?](#what-is-it)
- [Quick Start](#quick-start)
- Fundamentals
    - [StreamingHub](#streaminghub)
    - [Filter](#filter)
    - [ClientFilter](#clientfilter)
    - [ServiceContext and Lifecycle](#servicecontext-and-lifecycle)
    - [ExceptionHandling and StatusCode](#exceptionhandling-and-statuscode)
    - [Group and GroupConfiguration](#group-and-groupconfiguration)
    - [Project Structure](#project-structure)
    - [Dependency Injection](#dependency-injection)
- Client and Server
    - [Unity client supports](#unity-client-supports)
    - [Server Host](#server-host)
        - [Server Host options](#server-host-options)
- Deployment
    - [Host in Docker](#host-in-docker)
    - [SSL/TLS](#ssltls)
- Integrations
    - [Swagger](#swagger)
    - [Telemetry](#telemetry)
- Advanced
    - [MagicOnionOption/Logging](#magiconionoptionlogging)
    - [Raw gRPC APIs](#raw-grpc-apis)
    - [Zero deserialization mapping](#zero-deserialization-mapping)
- [Author Info](#author-info)
- [License](#license)

## Fundamentals
### StreamingHub
StreamingHub is a fully-typed realtime server<->client communication framework.

This sample is for Unity(use Vector3, GameObject, etc) but StreamingHub supports .NET Core, too.

```csharp
// Server -> Client definition
public interface IGamingHubReceiver
{
    // return type shuold be `void` or `Task`, parameters are free.
    void OnJoin(Player player);
    void OnLeave(Player player);
    void OnMove(Player player);
}
 
// Client -> Server definition
// implements `IStreamingHub<TSelf, TReceiver>`  and share this type between server and client.
public interface IGamingHub : IStreamingHub<IGamingHub, IGamingHubReceiver>
{
    // return type shuold be `Task` or `Task<T>`, parameters are free.
    Task<Player[]> JoinAsync(string roomName, string userName, Vector3 position, Quaternion rotation);
    Task LeaveAsync();
    Task MoveAsync(Vector3 position, Quaternion rotation);
}
 
// for example, request object by MessagePack.
[MessagePackObject]
public class Player
{
    [Key(0)]
    public string Name { get; set; }
    [Key(1)]
    public Vector3 Position { get; set; }
    [Key(2)]
    public Quaternion Rotation { get; set; }
}
```

```csharp
// Server implementation
// implements : StreamingHubBase<THub, TReceiver>, THub
public class GamingHub : StreamingHubBase<IGamingHub, IGamingHubReceiver>, IGamingHub
{
    // this class is instantiated per connected so fields are cache area of connection.
    IGroup room;
    Player self;
    IInMemoryStorage<Player> storage;

    public async Task<Player[]> JoinAsync(string roomName, string userName, Vector3 position, Quaternion rotation)
    {
        self = new Player() { Name = userName, Position = position, Rotation = rotation };

        // Group can bundle many connections and it has inmemory-storage so add any type per group. 
        (room, storage) = await Group.AddAsync(roomName, self);

        // Typed Server->Client broadcast.
        Broadcast(room).OnJoin(self);

        return storage.AllValues.ToArray();
    }

    public async Task LeaveAsync()
    {
        await room.RemoveAsync(this.Context);
        Broadcast(room).OnLeave(self);
    }

    public async Task MoveAsync(Vector3 position, Quaternion rotation)
    {
        self.Position = position;
        self.Rotation = rotation;
        Broadcast(room).OnMove(self);
    }

    // You can hook OnConnecting/OnDisconnected by override.
    protected override async ValueTask OnDisconnected()
    {
        // on disconnecting, if automatically removed this connection from group.
        return CompletedTask;
    }
}
```

You can write client like this.

```csharp
public class GamingHubClient : IGamingHubReceiver
{
    Dictionary<string, GameObject> players = new Dictionary<string, GameObject>();
 
    IGamingHub client;
 
    public async Task<GameObject> ConnectAsync(Channel grpcChannel, string roomName, string playerName)
    {
        var client = StreamingHubClient.Connect<IGamingHub, IGamingHubReceiver>(grpcChannel, this);
 
        var roomPlayers = await client.JoinAsync(roomName, playerName, Vector3.zero, Quaternion.identity);
        foreach (var player in roomPlayers)
        {
            (this as IGamingHubReceiver).OnJoin(player);
        }
 
        return players[playerName];
    }
 
    // methods send to server.
 
    public Task LeaveAsync()
    {
        return client.LeaveAsync();
    }
 
    public Task MoveAsync(Vector3 position, Quaternion rotation)
    {
        return client.MoveAsync(position, rotation);
    }
 
    // dispose client-connection before channel.ShutDownAsync is important!
    public Task DisposeAsync()
    {
        return client.DisposeAsync();
    }
 
    // You can watch connection state, use this for retry etc.
    public Task WaitForDisconnect()
    {
        return client.WaitForDisconnect();
    }
 
    // Receivers of message from server.
 
    void IGamingHubReceiver.OnJoin(Player player)
    {
        Debug.Log("Join Player:" + player.Name);
 
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = player.Name;
        cube.transform.SetPositionAndRotation(player.Position, player.Rotation);
        players[player.Name] = cube;
    }
 
    void IGamingHubReceiver.OnLeave(Player player)
    {
        Debug.Log("Leave Player:" + player.Name);
 
        if (players.TryGetValue(player.Name, out var cube))
        {
            GameObject.Destroy(cube);
        }
    }
 
    void IGamingHubReceiver.OnMove(Player player)
    {
        Debug.Log("Move Player:" + player.Name);
 
        if (players.TryGetValue(player.Name, out var cube))
        {
            cube.transform.SetPositionAndRotation(player.Position, player.Rotation);
        }
    }
}
```

### Filter
MagicOnion filter is powerful feature to hook before-after invoke. It is useful than gRPC server interceptor.

![image](https://user-images.githubusercontent.com/46207/50969421-cb465900-1521-11e9-8824-8a34cc52bbe4.png)

```csharp
// You can attach per class/method like [SampleFilter]
// for StreamingHub methods, implement StreamingHubFilterAttribute instead.
public class SampleFilterAttribute : MagicOnionFilterAttribute
{
    public override async ValueTask Invoke(ServiceContext context, Func<ServiceContext, ValueTask> next)
    {
        try
        {
            /* on before */
            await next(context); // next
            /* on after */
        }
        catch
        {
            /* on exception */
            throw;
        }
        finally
        {
            /* on finally */
        }
    }
}
```

Here is example of what kind of filter can be stacked.

![image](https://user-images.githubusercontent.com/46207/50969539-2bd59600-1522-11e9-84ab-15dd85e3dcac.png)

GlobalFilter can attach to MagicOnionOptions.

MagicOnion filters supports [DI](#dependency-injection) by [MagicOnion.Hosting](#server-host).

```csharp
public class MyStreamingHubFilterAttribute : StreamingHubFilterAttribute
{
    private readonly ILogger _logger;

    // the `logger` parameter will be injected at instantiating.
    public MyStreamingHubFilterAttribute(ILogger<MyStreamingHubFilterAttribute> logger)
    {
        _logger = logger;
    }

    public override async ValueTask Invoke(StreamingHubContext context, Func<StreamingHubContext, ValueTask> next)
    {
        _logger.LogInformation($"MyStreamingHubFilter Begin: {context.Path}");
        await next(context);
        _logger.LogInformation($"MyStreamingHubFilter End: {context.Path}");
    }
}
```

Register filters using attributes with constructor injection(you can use `[FromTypeFilter]` and `[FromServiceFilter]`).

```
[FromTypeFilter(typeof(MyFilterAttribute))]
public class MyService : ServiceBase<IMyService>, IMyService
{
    // The filter will instantiate from type.
    [FromTypeFilter(typeof(MySecondFilterAttribute))]
    public UnaryResult<int> Foo()
    {
        return UnaryResult(0);
    }

    // The filter will instantiate from type with some arguments. if the arguments are missing, it will be obtained from `IServiceLocator` 
    [FromTypeFilter(typeof(MyThirdFilterAttribute), Arguments = new object[] { "foo", 987654 })]
    public UnaryResult<int> Bar()
    {
        return UnaryResult(0);
    }

    // The filter instance will be provided via `IServiceLocator`.
    [FromServiceFilter(typeof(MyFourthFilterAttribute))]
    public UnaryResult<int> Baz()
    {
        return UnaryResult(0);
    }
}
```

### ClientFilter
MagicOnion client-filter is a powerful feature to hook before-after invoke. It is useful than gRPC client interceptor.

> Currently only suppots on Unary.

```csharp
// you can attach in MagicOnionClient.Create.
var client = MagicOnionClient.Create<ICalcService>(channel, new IClientFilter[]
{
    new LoggingFilter(),
    new AppendHeaderFilter(),
    new RetryFilter()
});
```

You can create custom client-filter by implements `IClientFilter.SendAsync`.

```csharp
public class IDemoFilter : IClientFilter
{
    public async ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
    {
        try
        {
            /* Before Request, context.MethodPath/CallOptions/Items, etc */

            var response = await next(context); /* Call next filter or method body */

            /* After Request, response.GetStatus/GetTrailers/GetResponseAs<T>, etc */

            return response;
        }
        catch (RpcException ex)
        {
            /* Get gRPC Error Response */
            throw;
        }
        catch (OperationCanceledException ex)
        {
            /* If canceled */
            throw;
        }
        catch (Exception ex)
        {
            /* Other Exception */
            throw;
        }
        finally
        {
            /* Common Finalize */
        }
    }
}
```

Here is the sample filters, you can imagine what you can do.

```csharp
public class AppendHeaderFilter : IClientFilter
{
    public async ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
    {
        // add the common header(like authentcation).
        var header = context.CallOptions.Headers;
        header.Add("x-foo", "abcdefg");
        header.Add("x-bar", "hijklmn");

        return await next(context);
    }
}

public class LoggingFilter : IClientFilter
{
    public async ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
    {
        Console.WriteLine("Request Begin:" + context.MethodPath); // Debug.Log in Unity.

        var sw = Stopwatch.StartNew();
        var response = await next(context);
        sw.Stop();

        Console.WriteLine("Request Completed:" + context.MethodPath + ", Elapsed:" + sw.Elapsed.TotalMilliseconds + "ms");

        return response;
    }
}

public class ResponseHandlingFilter : IClientFilter
{
    public async ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
    {
        var response = await next(context);

        if (context.MethodPath == "ICalc/Sum")
        {
            // You can cast response type.
            var sumResult = await response.GetResponseAs<int>();
            Console.WriteLine("Called Sum, Result:" + sumResult);
        }

        return response;
    }
}

public class MockRequestFilter : IClientFilter
{
    public async ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
    {
        if (context.MethodPath == "ICalc/Sum")
        {
            // don't call next, return mock result.
            return new ResponseContext<int>(9999);
        }

        return await next(context);
    }
}

public class RetryFilter : IClientFilter
{
    public async ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
    {
        Exception lastException = null;
        var retryCount = 0;
        while (retryCount != 3)
        {
            try
            {
                // using same CallOptions so be careful to add duplicate headers or etc.
                return await next(context);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
            retryCount++;
        }

        throw new Exception("Retry failed", lastException);
    }
}

public class EncryptFilter : IClientFilter
{
    public async ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
    {
        context.SetRequestMutator(bytes => Encrypt(bytes));
        context.SetResponseMutator(bytes => Decrypt(bytes));
        
        return await next(context);
    }
}
```

### ServiceContext and Lifecycle
Service/StreamingHub's method or `MagicOnionFilter` can access `this.Context` it is 

| Property | Description |
| --- | --- |
| `ConcurrentDictionary<string, object>` Items | Object storage per request/connection. |
| `Guid` ContextId | Unieuq ID per request(Service)/connection(StreamingHub). |
| `DateTime` Timestamp | Timestamp that request/connection is started time. |
| `Type` ServiceType | Invoked Class. |
| `MethodInfo` MethodInfo | Invoked Method. |
| `ILookup<Type, Attribute> AttributeLookup | Cached Attributes that merged both service and method. |
| `ServerCallContext` CallContext | Raw gRPC Context. |
| `IFormatterResolver` FormatterResolver | Using MessagePack resolver. |
| `IServiceLocator` ServiceLocator | Get the registered service. |

`Items` is useful, for example authentication filter add UserId to Items and take out from service method.

> If using StreamingHub, ServiceContext means per connected context so `Items` is not per method invoke. `StreamingHubContext.Items` supports per streaming hub method request but currently can not take from streaming hub method(only use in StreamingHubFilter). [Issue:#67](https://github.com/Cysharp/MagicOnion/issues/67), it will fix.

MagicOnion supports get current context globaly like HttpContext.Current. `ServiceContext.Current` can get it but it requires `MagicOnionOptions.EnableCurrentContext = true`, default is false.

Lifecycle image of ServiceBase

```
gRPC In(
    var context = new ServiceContext();
    Filter.Invoke(context, 
        var service = new ServiceImpl();
        service.ServiceContext = context;
        service.MethodInvoke(
            /* method impl */
        )
    )
)
```

Lifecycle image of StreamingHub(StreamingHub is inherited from ServiceBase)

```
gRPC In(
    var context = new ServiceContext();
    Filter.Invoke(context, 
        var hub = new StreamingHubImpl();
        hub.ServiceContext = context;
        hub.Connect(
            while (connecting) {
                Streaming In(
                    var streamingHubContext = new StreamingHubContext(context);
                    StreamingHubFilter.Invoke(streamingHubContext,
                        hub.MethodInvoke(
                            /* method impl */
                        )
                    )
                )
            }
        )
    )
)
```

StreamingHub instance is shared while connecting so StreamingHub's field can use cache area of connection.

### ExceptionHandling and StatusCode
If you are return custom status code from server to client, you can use `throw new ReturnStatusException`.

```csharp
public Task SendMessageAsync(string message)
{
    if (message.Contains("foo"))
    {
        //
        throw new ReturnStatusException((Grpc.Core.StatusCode)99, "invalid");
    }

    // ....
```

Client can receive exception as gRPC's `RpcException`. If performance centric to avoid exception throw, you can use raw gRPC CallContext.Status(`ServiceContext.CallCaontext.Status`) and set status directly.

MagicOnion's engine catched exception(except ReturnStatusException), set `StatusCode.Unknown` and client received gRPC's `RpcException`. If `MagicOnionOption.IsReturnExceptionStackTraceInErrorDetail` is true, client can receive StackTrace of server exception, it is very useful for debugging but has critical issue about sercurity so should only to enable debug build.

### Group and GroupConfiguration
StreamingHub's broadcast system is called Group. It can get from StreamingHub impl method, `this.Group`(this.Group type is `HubGroupRepository`, not `IGroup`).

Current connection can add to group by `this.Group.AddAsync(string groupName)`, return value(`IGroup`) is joined group broadcaster so cache to field. It is enable per connection(if disconnected, automaticaly leaved from group). If you want to use some restriction, you can use `TryAddAsync(string groupName, int incluciveLimitCount, bool createIfEmpty)`.

`IGroup` can pass to StreamingHub.`Broadcast`, `BroadcastExceptSelf`, `BroadcastExcept` and calls client proxy.

```csharp
public class ChatHub : StreamingHubBase<IChatHub, IMessageReceiver>, IChatHub
{
    string userName;
    IGroup room;

    public async Task JoinAsync(string userName, string roomName)
    {
        this.userName = userName;
        this.room = await Group.AddAsync(roomName);
    }

    public async Task SendMessageAsync(string message)
    {
        Broadcast(room).OnReceiveMessage(userName, message);
    }
}
```

> GroupRepository is created per StreamingHub type

> If you want to create ServerSide loop and broadcast out of StreamingHub, you can pass Broadcast(room) result but it is unnatural, I'll add support kit of create server-side loop  

Group has in-memory storage, it can store extra data to group member. It can set `Group.AddAsync(string groupName, TStorage data)` instead of standard AddAsync.

Data is can get from `group.GetInMemoryStorage<T>` and can invoke `AllValues`, `Set(Guid connectionId, T Value)`, `Get(Guid connectionId)`.

> StreamingHub's ConnectionId is ServiceContext.ContextId

Default MagicOnion's group is inmemory and using `ImmutableArrayGroup`. This group implementation is tuned for small room, not enter/leave frequently. If large room and enter/leave frequently design, you can use `ConcurrentDictionaryGroup`. It can configure by `GroupConfigurationAttribute` or `MagicOnionOptions.DefaultGroupRepositoryFactory`.

```csharp
// use ***GroupRepositoryFactory type.
[GroupConfiguration(typeof(ConcurrentDictionaryGroupRepositoryFactory))]
public class ChatHub : StreamingHubBase<IChatHub, IMessageReceiver>, IChatHub
{
    // ...
}
```

MagicOnion has distribute system called redis-backplane for group broadcast.

![image](https://user-images.githubusercontent.com/46207/50974777-5f6aed00-152f-11e9-97f3-ba2a0c97f0eb.png)

* Install-Package MagicOnion.Redis

```csharp
// set RedisGroupRepositoryFactory
[GroupConfiguration(typeof(RedisGroupRepositoryFactory))]
public class ...
{
}

// configure ConnectionMultiplexer(StackExchange.Redis) to MagicOnionOption.ServiceLocator
var option = new MagicOnionOption();
option.ServiceLocator.Register(new ConnectionMultiplexer(...));
```

### Project Structure
If creates Server-Client project, I recommend make three projects. `Server`, `ServerDefinition`, `Client`.

![image](https://cloud.githubusercontent.com/assets/46207/21081857/e0f6dfce-c012-11e6-850d-358c5b928a82.png)

ServerDefinition is only defined interface(`IService<>`, `IStreamingHub<,>`)(and some share request/response types).

If debugging, I recommend use [SwitchStartupProject](https://marketplace.visualstudio.com/items?itemName=vs-publisher-141975.SwitchStartupProjectforVS2017) exteinson of VisualStudio and launch both Server and Client.

```json
"MultiProjectConfigurations": {
    "Server + Client": {
        "Projects": {
            "FooService": {},
            "FooClient": {}
        }
    }
}
```

It can step-in/out seamlessly in server and client.

for Unity, you can't share by DLL(because can't share `IServer<>` because it is different reference both Unity and Server). It is slightly complex so we provides sample project and explanation.

see: [samples](https://github.com/Cysharp/MagicOnion/tree/master/samples) page and ReadMe.


### Dependency Injection
You can use DI(constructor injection) by GenericHost.

```csharp
static async Task Main(string[] args)
{
    await MagicOnionHost.CreateDefaultBuilder()
        .ConfigureServices((hostContext, services) =>
        {
            // DI, you can register types on this section.

            // mapping config json to IOption<MyConfig>
            // requires "Microsoft.Extensions.Options.ConfigurationExtensions" package
            services.Configure<MyConfig>(hostContext.Configuration);
        })
        .RunConsoleAsync();
}

public class MyFirstService : ServiceBase<IMyFirstService>, IMyFirstService
{
    IOptions<MyConfig> config;
    ILogger<MyFirstService> logger;

    public MyFirstService(IOptions<MyConfig> config, ILogger<MyFirstService> logger)
    {
        this.config = config;
        this.logger = logger;
    }

    // ...
}
```

## Server and Clients

### Unity client Supports
You can download `MagicOnion.Client.Unity.package` and `moc.zip`(MagicOnionCompiler) in the [releases page](https://github.com/cysharp/MagicOnion/releases). But MagicOnion has no dependency so download gRPC lib from [gRPC daily builds](https://packages.grpc.io/), click Build ID and download `grpc_unity_package.*.*.*-dev.zip`. One more, requires MessagePack for C# for serialization, you can download `MessagePack.Unity.*.*.*.unitypackage` and `mpc.zip`(MessagePackCompiler) from [MessagePack-CSharp/releases](https://github.com/neuecc/MessagePack-CSharp/releases).

MagicOnion only supports `.NET 4.x` runtime and recommend to supports C# 7.0(Unity 2018.3) version. Allow unsafe Code and add `ENABLE_UNSAFE_MSGPACK`, you can use `UnsafeDirectBlitResolver` to extremely fast serialization.

Default MagicOnion's Unity client works well on Unity Editor or not IL2CPP env. But for IL2CPP environment, you need client code generation. `moc` is cross-platform standalone application.

```
moc arguments help:
  -i, --input=VALUE          [required]Input path of analyze csproj
  -o, --output=VALUE         [required]Output path(file) or directory base(in separated mode)
  -u, --unuseunityattr       [optional, default=false]Unuse UnityEngine's RuntimeInitializeOnLoadMethodAttribute on MagicOnionInitializer
  -c, --conditionalsymbol=VALUE [optional, default=empty]conditional compiler symbol
  -n, --namespace=VALUE      [optional, default=MagicOnion]Set namespace root name
  -a, asyncsuffix      [optional, default=false]Use methodName to async suffix
```

Please try it to run iOS/Android etc.

Project structure and code generation sample, see [samples](https://github.com/Cysharp/MagicOnion/tree/master/samples) page and ReadMe.

### Server host
I've recommend to use [.NET Generic Host](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-2.2) to host .NET Core app. `MagicOnion.Hosting` package helps to build to use MagicOnion.

* Install-Package MagicOnion.Hosting

```csharp
// using MagicOnion.Hosting
static async Task Main(string[] args)
{
    // you can use new HostBuilder() instead of CreateDefaultBuilder
    await new HostBuilder()
        .UseMagicOnion()
        .RunConsoleAsync();
}
```

If you can want to load configuration, set logging, etc, see .NET Generic Host documantation.

CreateDefaultBuilder's setup details is same as [MicroBatchFramework](https://github.com/Cysharp/MicroBatchFramework), it is similar as `WebHost.CreateDefaultBuilder` on ASP.NET Core. for the details, see [MicroBatchFramework#configure-configuration](https://github.com/Cysharp/MicroBatchFramework#configure-configuration)


### Server host options
Configure MagicOnion hosting using `Microsoft.Extensions.Options` that align to .NET Core way. In many real use cases, Using setting files (ex. appsettings.json), environment variables, etc ... to configure an application.

For example, We have Production and Development configurations and have some differences about listening ports, certificates and others.

This example makes hosting MagicOnion easier and configurations moved to external files, environment variables. appsettings.json looks like below.

```csharp
{
  "MagicOnion": {
    "Service": {
      "IsReturnExceptionStackTraceInErrorDetail": false
    },
    "ChannelOptions": {
      "grpc.primary_user_agent": "MagicOnion/1.0 (Development)",
      "grpc.max_receive_message_length": 4194304
    },
    "ServerPorts": [
      {
        "Host": "localhost",
        "Port": 12345,
        "UseInsecureConnection": false,
        "ServerCredentials": [
          {
            "CertificatePath": "./server.crt",
            "KeyPath": "./server.key"
          }
        ]
      }
    ]
  }}
```

An application setting files is not required by default. You can simply call UseMagicOnion() then it starts service on localhost:12345 (Insecure connection).

```csharp
class Program
{
   static async Task Main(string[] args)
   {
       await MagicOnionHost.CreateDefaultBuilder()
           .UseMagicOnion()
           .RunConsoleAsync();
   }
}
```

Of course, you can also flexibly configure hosting by code. During configuration, you can access `IHostingEnvironment` / `IConfiguration` instances and configure 
`MagicOnionOptions`.

```csharp
class Program
{
    static async Task Main(string[] args)
    {
        await MagicOnionHost.CreateDefaultBuilder()
            .UseMagicOnion()
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<MagicOnionHostingOptions>(options =>
                {
                    if (hostContext.HostingEnvironment.IsDevelopment())
                    {
                        options.Service.GlobalFilters = new[] { new MyFilterAttribute(null) };
                    }
                    options.ChannelOptions.MaxReceiveMessageLength = 1024 * 1024 * 10;
                });
            })
            .RunConsoleAsync();
    }
}
```

This configuration method supports multiple MagicOnion hosting scenarios.

```json
{
  "MagicOnion": {
    "ServerPorts": [
      {
        "Host": "localhost",
        "Port": 12345,
        "UseInsecureConnection": true
      }
    ]
  },
  "MagicOnion-Management": {
    "ServerPorts": [
      {
        "Host": "localhost",
        "Port": 23456,
        "UseInsecureConnection": true
      }
    ]
  }
}
```

```csharp
class Program
{
    static async Task Main(string[] args)
    {
        await MagicOnionHost.CreateDefaultBuilder()
            .UseMagicOnion(types: new[] { typeof(MyService) })
            .UseMagicOnion(configurationName: "MagicOnion-Management", types: new[] { typeof(ManagementService) })
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<MagicOnionHostingOptions>(options =>
                {
                    options.ChannelOptions.MaxReceiveMessageLength = 1024 * 1024 * 10;
                });
                services.Configure<MagicOnionHostingOptions>("MagicOnion-Management", options =>
                {
                    if (hostContext.HostingEnvironment.IsDevelopment())
                    {
                        options.Service.GlobalFilters = new[] { new MyFilterAttribute(null) };
                    }
                });
            })
            .RunConsoleAsync();
    }
}
```

## Deployment

### Host in Docker
If you hosting the samples on a server, recommend to use container. Add Dockerfile like below.

```dockerfile
FROM mcr.microsoft.com/dotnet/core/sdk:2.2 AS sdk
COPY . ./workspace

RUN dotnet publish ./workspace/samples/ChatApp/ChatApp.Server/ChatApp.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/core/runtime:2.2
COPY --from=sdk /app .
ENTRYPOINT ["dotnet", "ChatApp.Server.dll"]

# Expose ports.
EXPOSE 12345
```

And docker build, send to any container registory.

Here is the sample of deploy AWS [ECR](https://us-east-2.console.aws.amazon.com/ecr/) and [ECS](https://us-east-2.console.aws.amazon.com/ecs) by CircleCI.
```yml
version: 2.1
orbs:
# see: https://circleci.com/orbs/registry/orb/circleci/aws-ecr
# use Environment Variables : AWS_ECR_ACCOUNT_URL
#                             AWS_ACCESS_KEY_ID	
#                             AWS_SECRET_ACCESS_KEY
#                             AWS_REGION  
  aws-ecr: circleci/aws-ecr@4.0.1
# see: https://circleci.com/orbs/registry/orb/circleci/aws-ecs
# use Environment Variables : AWS_ACCESS_KEY_ID	
#                             AWS_SECRET_ACCESS_KEY
#                             AWS_REGION
  aws-ecs: circleci/aws-ecs@0.0.7
workflows:
  build-push:
    jobs:
      - aws-ecr/build_and_push_image:
          repo: sample-magiconion
      - aws-ecs/deploy-service-update:
          requires:
            - aws-ecr/build_and_push_image
          family: 'sample-magiconion-service'
          cluster-name: 'sample-magiconion-cluster'
          container-image-name-updates: 'container=sample-magiconion-service,tag=latest'
          
```

Here is the sample of deploy [Google Cloud Platform(GCP)](https://console.cloud.google.com/) by CircleCI.
```yml
version: 2.1
orbs:
  # see: https://circleci.com/orbs/registry/orb/circleci/gcp-gcr
  # use Environment Variables : GCLOUD_SERVICE_KEY
  #                             GOOGLE_PROJECT_ID
  #                             GOOGLE_COMPUTE_ZONE
    gcp-gcr: circleci/gcp-gcr@0.6.0
workflows:
    build_and_push_image:
        jobs:
            - gcp-gcr/build-and-push-image:
                image: sample-magiconion
                registry-url: asia.gcr.io # other: gcr.io, eu.gcr.io, us.gcr.io
```

Depending on the registration information of each environment and platform, fine tuning may be necessary, so please refer to the platform documentation and customize your own.

### SSL/TLS
As [official gRPC doc](https://grpc.io/docs/guides/auth/) notes gRPC supports SSL/TLS, and MagicOnion also support SSL/TLS. 

> gRPC has SSL/TLS integration and promotes the use of SSL/TLS to authenticate the server, and to encrypt all the data exchanged between the client and the server. Optional mechanisms are available for clients to provide certificates for mutual authentication

I will explain how to setup "SSL/TLS MagicOnion on localhost" with following 4 steps.

* [generate certificate](#generate-certificate)
* [simulate dummy domain on localhost](#simulate-dummy-domain-on-localhost)
* [server configuration](#server-configuration)
* [client configuration](#client-configuration)

Let's use [samples/ChatApp/ChatApp.Server](https://github.com/Cysharp/MagicOnion/tree/master/samples/ChatApp/ChatApp.Server) for server project, and [samples/ChatApp/ChatApp.Unity](https://github.com/Cysharp/MagicOnion/tree/master/samples/ChatApp/ChatApp.Unity) for client project.

#### generate certificate

Certificates are required to establish SSL/TLS with Server/Client channel connection.
Let's use [OpenSSL](https://github.com/openssl/openssl) to create required certificates.

Following command will create 3 files `server.csr`, `server.key` and `server.crt`.
gRPC/MagicOnion Server requires server.crt and server.key, and Client require server.crt.

```shell
# move to your server project
$ cd MagicOnion/samples/ChatApp/ChatApp.Server

# generate certificates
# NOTE: CN=xxxx should match domain name to magic onion server pointing domain name
$ openssl genrsa 2048 > server.key
$ openssl req -new -sha256 -key server.key -out server.csr -subj "/C=JP/ST=Tokyo/L=Tokyo/O=MagicOnion Demo/OU=Dev/CN=*.example.com"
$ openssl x509 -req -in server.csr -signkey server.key -out server.crt -days 7300 -extensions server

# server will use server.crt and server.key, leave generated certificates.

# client will use server.crt, copy certificate to StreamingAssets folder.
$ mkdir ../ChatApp.Unity/Assets/StreamingAssets
$ cp server.crt ../ChatApp.Unity/Assets/StreamingAssets/server.crt
```

Please modify `/C=JP/ST=Tokyo/L=Tokyo/O=MagicOnion Demo/OU=Dev/CN=*.example.com` as you need.
Make sure `CN=xxxx` should match to domain that your MagicOnion Server will recieve request from your client.

> ATTENTION: Make sure **server.key** is very sensitive file, while **server.crt** can be public. DO NOT COPY server.key to your client.

#### simulate dummy domain on localhost

Editting `hosts` file is the simple way to redirect dummy domain request to your localhost.

Let's set your CN to you hosts, example is `dummy.example.com`. 
Open hosts file and add your entry.

```shell
# NOTE: edit hosts to test on localhost
# Windows: (use scoop to install sudo, or open elevated cmd or notepad.)
PS> sudo notepad c:\windows\system32\drivers\etc\hosts
# macos:
$ sudo vim /private/etc/hosts
# Linux:
$ sudo vim /etc/hosts
```

Entry format would be similar to this, please follow to your platform hosts rule.

```shell
127.0.0.1	dummy.example.com
```

After modifying hosts, `ping` to your dummy domain and confirm localhost is responding.

```shell
$ ping dummy.example.com

pinging to dummy.example.com [127.0.0.1] 32 bytes data:
127.0.0.1 response: bytecount =32 time <1ms TTL=128
```

#### server configuration

> NOTE: Server will use **server.crt** and **server.key**, if you didn't copy OpenSSL generated `server.crt` and `server.key`, please back to [generate certificate](#generate-certificate) section and copy them.

Open `samples/ChatApp/ChatApp.Server/ChatApp.Server.csproj` and add folloging lines before `</Project>`.

```xml
  <ItemGroup>
    <Folder Include="LinkFromUnity\" />
  </ItemGroup>

  <!-- FOR SSL/TLS SUPPORT -->
  <ItemGroup>
    <None Update="server.crt">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update="server.key">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```

Open `samples/ChatApp/ChatApp.Server/Program.cs`, there are default Insecure channel definition with `ServerCredentials.Insecure`.
What you need is change this line to use `SslServerCredentials`.

```csharp
new ServerPort("localhost", 12345, ServerCredentials.Insecure))
```

Add following lines before `await MagicOnionHost.CreateDefaultBuilder()`

```csharp
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var certificates = new System.Collections.Generic.List<KeyCertificatePair> { new KeyCertificatePair(System.IO.File.ReadAllText("server.crt"), System.IO.File.ReadAllText("server.key")) };
var credential = new SslServerCredentials(certificates);
```

You may required following using namespaces.

```csharp
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.IO;
```

Replace existing `new ServerPort("localhost", 12345, ServerCredentials.Insecure))` line with following.

```csharp
new ServerPort(config.GetValue<string>("MAGICONION_HOST", "127.0.0.1"), 12345, credential))
```

> NOTE: Replace `localhost` to `127.0.0.1` enforce IPv4 connection.

Debug run server on Visual Studio, any IDE or docker.

```shell
D0729 11:08:21.767387 Grpc.Core.Internal.NativeExtension gRPC native library loaded successfully.
Application started. Press Ctrl+C to shut down.
Hosting environment: Production
```

#### client configuration

> NOTE: Client will use **server.crt**, if you didn't copy OpenSSL generated `server.crt` and `server.key`, please back to [generate certificate](#generate-certificate) section and copy it.

Open `samples/ChatApp/ChatApp.Unity/Assets/ChatComponent.cs`, channel creation is defined as `ChannelCredentials.Insecure` in `InitializeClient()`.
What you need tois change this line to use `SslCredentials`.


```csharp
this.channel = new Channel("localhost", 12345, ChannelCredentials.Insecure);
```

Replace this line to following.

```csharp
var serverCred = new SslCredentials(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "server.crt")));
this.channel = new Channel("dummy.example.com", 12345, serverCred);
```

Play on Unity Editor and confirm Unity MagicOnion Client can connect to MagicOnion Server.

![image](https://user-images.githubusercontent.com/3856350/62017554-1be97f00-b1f2-11e9-9769-70464fe6d425.png)

> NOTE: If there are any trouble establish SSL/TLS connection, Unity Client will show `disconnected server.` log.

## Integrations
### Swagger
MagicOnion has built-in Http1 JSON Gateway and [Swagger](http://swagger.io/) integration for Unary operation. It can execute and debug RPC-API easily.

* Install-Package MagicOnion.HttpGateway

HttpGateway is built on ASP.NET Core. for example, with `Microsoft.AspNetCore.Server.WebListener`.

```csharp
// using MagicOnion.Hosting;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;

class Program
{
    static async Task Main(string[] args)
    {
        // setup MagicOnion hosting.
        var magicOnionHost = MagicOnionHost.CreateDefaultBuilder()
            .UseMagicOnion(
                new MagicOnionOptions(isReturnExceptionStackTraceInErrorDetail: true),
                new ServerPort("localhost", 12345, ServerCredentials.Insecure))
            .UseConsoleLifetime()
            .Build();

        // NuGet: Microsoft.AspNetCore.Server.Kestrel
        var webHost = new WebHostBuilder()
            .ConfigureServices(collection =>
            {
                // Add MagicOnionServiceDefinition for reference from Startup.
                collection.AddSingleton<MagicOnionServiceDefinition>(magicOnionHost.Services.GetService<MagicOnionHostedServiceDefinition>().ServiceDefinition);
            })
            .UseKestrel()
            .UseStartup<Startup>()
            .UseUrls("http://localhost:5432")
            .Build();

        // Run and wait both.
        await Task.WhenAll(webHost.RunAsync(), magicOnionHost.RunAsync());
    }
}

// WebAPI Startup configuration.
public class Startup
{
    // Inject MagicOnionServiceDefinition from DIl
    public void Configure(IApplicationBuilder app, MagicOnionServiceDefinition magicOnion)
    {
        // Optional:Add Summary to Swagger
        // var xmlName = "Sandbox.NetCoreServer.xml";
        // var xmlPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), xmlName);

        // HttpGateway requires two middlewares.
        // One is SwaggerView(MagicOnionSwaggerMiddleware)
        // One is Http1-JSON to gRPC-MagicOnion gateway(MagicOnionHttpGateway)
        app.UseMagicOnionSwagger(magicOnion.MethodHandlers, new SwaggerOptions("MagicOnion.Server", "Swagger Integration Test", "/")
        {
            // XmlDocumentPath = xmlPath
        });
        app.UseMagicOnionHttpGateway(magicOnion.MethodHandlers, new Channel("localhost:12345", ChannelCredentials.Insecure));
    }
}
```

Open `http://localhost:5432`, you can see swagger view.

![image](https://cloud.githubusercontent.com/assets/46207/21295663/6a9d3e28-c59d-11e6-8081-18d14e359567.png)

### Telemetry

You can configure Telemetry for MagicOnion with `MagicOnion.OpenTelemetry` package.
Let's see overview and how to try on localhost.

* overview
* examples of implementation
* try visualization on localhost
* metrics customization
* implement your own metrics

#### overview

MagicOnion.OpenTelemetry is implementation of [open\-telemetry/opentelemetry\-dotnet: OpenTelemetry \.NET SDK](https://github.com/open-telemetry/opentelemetry-dotnet), so you can use any OpenTelemetry exporter, like [Prometheus](https://prometheus.io/), [StackDriver](https://cloud.google.com/stackdriver/pricing), [Zipkin](https://zipkin.io/) and others.

You can collect telemetry and use exporter on MagicOnion Serverside.

#### examples of implementation

What you need to do for Telemetry is followings.

* configure exporter.
* add reference to the MagicOnion.OpenTelemetry.
* configure DI for OpenTelemetry-dotnet.
* configure filters/logger for telemetry.

Let's follow the steps. 

**configure exporeter**

Before implementing exporeters, I've recommend check samples offering on [opentelemetry\-dotnet/samples/Exporters at master · open\-telemetry/opentelemetry\-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet/tree/master/samples/Exporters).

Here's prometheus exporter sample implementation, paste this before `MagicOnionHost.CreateDefaultBuilder()`.
This implementation allow prometheus to collect MagicOnion metrics from http://localhost:9184/metrics.

```csharp
# Program.cs
var exporter = new PrometheusExporter(
    new PrometheusExporterOptions()
    {
        Url = $"http://localhost:9184/metrics/",
    },
    Stats.ViewManager);
exporter.Start();

// await MagicOnionHost.CreateDefaultBuilder(useSimpleConsoleLogger: true)
```

If you are running on any VM, Container or Kubernetes, you can configure exporter host & port by through ConfigurationBuilder.
Following example allow you to change exporter host/port by environment variables `PROMETHEUS_EXPORTER_HOST` & `PROMETHEUS_EXPORTER_PORT`.

```csharp
# Program.cs
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var exporterHost = config.GetValue<string>("PROMETHEUS_EXPORTER_HOST", "localhost");
var exporterPort = config.GetValue<string>("PROMETHEUS_EXPORTER_PORT", "9182");
var exporter = new PrometheusExporter(
    new PrometheusExporterOptions()
    {
        Url = $"http://{exporterHost}:{exporterPort}/metrics/",
    },
    Stats.ViewManager);
exporter.Start();

// await MagicOnionHost.CreateDefaultBuilder(useSimpleConsoleLogger: true)
```

**add reference to the MagicOnion.OpenTelemetry**

Please add [MagicOnion.OpenTelemetry](https://www.nuget.org/packages/MagicOnion.OpenTelemetry) nuget package to your MagicOnion server project.

```shell
dotnet add package MagicOnion.OpenTelemetry
```

You are ready to configure MagicOnion Filter & Logger for OpenTelemetry.

**configure DI for OpenTelemetry-dotnet**

opentelemetry-dotnet requires DI for `ITracer` and `ISampler`.
Make sure register them in DI with MagicOnion Builder.

```csharp
# Program.cs
await MagicOnionHost.CreateDefaultBuilder(useSimpleConsoleLogger: true)
    .ConfigureServices(collection =>
    {
        collection.AddSingleton<ITracer>(Tracing.Tracer);
        collection.AddSingleton<ISampler>(Samplers.AlwaysSample);
    })
    .UseMagicOnion(....)
```

**configure filters/logger for telemetry**

Use `MagicOnionOptions` to register filters and logger for telemetry.
You can collect MagicOnion metrics with `MagicOnionFilter`, MagicOnion.OpenTelemetry offers `OpenTelemetryCollectorFilter` and `OpenTelemetryHubCollectorFilter` for you.
Also register MagicOnionLogger to collect metrics on each hook point prepared on `IMagicOnionLogger`, MagicOnion.OpenTelemetry offers `OpenTelemetryCollectorLogger` for you.

```csharp
await MagicOnionHost.CreateDefaultBuilder(useSimpleConsoleLogger: true)
    .ConfigureServices(collection =>
    {
        collection.AddSingleton<ITracer>(Tracing.Tracer);
        collection.AddSingleton<ISampler>(Samplers.AlwaysSample);
    })
    .UseMagicOnion()
    .ConfigureServices((hostContext, services) =>
    {
        services.Configure<MagicOnionHostingOptions>(options =>
        {
             options.Service.GlobalFilters.Add<OpenTelemetryCollectorFilter>();
             options.Service.GlobalStreamingHubFilters.Add<OpenTelemetryHubCollectorFilter>();
             options.Service.MagicOnionLogger = new OpenTelemetryCollectorLogger(Stats.StatsRecorder, Tags.Tagger, null);
        });
    })
    .RunConsoleAsync();
```

All implementation is done! Let's Debug run Server and confirm you can see metrics on http://localhost:9182/metrics.

![image](https://user-images.githubusercontent.com/3856350/62096698-83213500-b2bf-11e9-88ff-52ef673ac4f5.png)

You may find `MagicOnion_measure_BuildServiceDefinition{MagicOnion_keys_Method="BuildServiceDefinition"}` are collected, and other metrics will shown as #HELP.
They will export when Unary/StreamingHub request is comming.

If you want insert your own tag to default metrics, please add `ITagContext` when register `OpenTelemetryCollectorLogger`.
Following will add verion tag for each metrics.

```csharp
MagicOnionLogger = new OpenTelemetryCollectorLogger(Stats.StatsRecorder, Tags.Tagger, new TagContext(new Dictionary<TagKey, TagValue>
{
    // add version to all default metrics
    { TagKey.Create("version"), TagValue.Create("1.0.0") },
}))
```

Now each metrics contains `version` tag like `MagicOnion_measure_BuildServiceDefinition{MagicOnion_keys_Method="BuildServiceDefinition",version="1.0.0"}`.

#### try visualization on localhost

You can try Prometheus collecter and visualize metrics on Grafana, all these operation can be done by docker-compose.
Please follow the steps.

* Apply above `examples of implementation` settings to the [MagicOnion/samples/ChatApp/ChatApp.Server](https://github.com/Cysharp/MagicOnion/tree/master/samples/ChatApp/ChatApp.Server).

* Copy all items in [MagicOnion/docs/telemetry](https://github.com/Cysharp/MagicOnion/tree/master/docs/telemetry) directory to [MagicOnion/samples/ChatApp].

```shell
# Windows
> xcopy MagicOnion\docs\telemetry MagicOnion\samples\ChatApp /H /E
# Bash
$ cp -rT MagicOnion/docs/telemetry MagicOnion/samples/ChatApp
```

* Build & Launch docker-compose, you are all systems are up and running on your localhost.

```shell
$ cd MagicOnion/samples/ChatApp
$ docker-compose build
$ docker-compose up

Creating network "chatapp_default" with the default driver
Creating alertmanager         ... done
Creating prometheus           ... done
Creating chatapp_magiconion_1 ... done
Creating cAdvisor             ... done
Creating grafana              ... done
```

When you launch docker-compose, followings set of service will launch for you.

* **MagicOnion** stats export on http://localhost:9182/metrics/.
* **cAdvisor** launch on http://localhost:8080.
* **Prometheus** launch on http://localhost:9090.
* **Grafana** launch on http://localhost:3000. (default username: `admin`, password: `admin`)
* **Alertmanager** to notify alert to Slack.
* optional: if you want **node_exporter**, uncomment in `docker-compose.yml` and it launch on http://localhost:9100. make sure host volume is mounted to container.

To configure Grafana dashboard, follow the steps.

* add DataSource: Data Souces> add > Prometheus (prometheus URL will be http://prometheus:9090)
* add Dashboard:
    * **Prometheus 2.0 Stats** dashboard: open Data Source > prometheus > dashboard tab > add Prometheus 2.0 Stats
    * **Docker and Host Monitoring w/ Prometheus** dashboard (cAdvisor): open Dashboard > Manage > Import > https://grafana.com/grafana/dashboards/179
    * **MagicOnion Overview** dashboard (MagicOnion & cAdvisor): open Dashboard > Manage > Import > https://grafana.com/grafana/dashboards/10584
    * optional: **node_exporter 1.8** dashboard: open Dashboard > Manage > Import > https://grafana.com/grafana/dashboards/1860

Now you can observe MagicOnion metrics through Grafana.

![image](https://user-images.githubusercontent.com/3856350/61683238-c58ec300-ad4f-11e9-9057-1cfb9c30cd67.png)

To configure alert eather, modify `prometheus/config/alert.rules` and set slack incoming url on `alertmanager/config.yml`.

#### implement your own metrics

Implement `IMagicOnionLogger` to configure your metrics. You can collect metrics when following callbacks are invoked by filter.

```csharp
namespace MagicOnion.Server
{
    public interface IMagicOnionLogger
    {
        void BeginBuildServiceDefinition();
        void BeginInvokeHubMethod(StreamingHubContext context, ArraySegment<byte> request, Type type);
        void BeginInvokeMethod(ServiceContext context, byte[] request, Type type);
        void EndBuildServiceDefinition(double elapsed);
        void EndInvokeHubMethod(StreamingHubContext context, int responseSize, Type type, double elapsed, bool isErrorOrInterrupted);
        void EndInvokeMethod(ServiceContext context, byte[] response, Type type, double elapsed, bool isErrorOrInterrupted);
        void InvokeHubBroadcast(string groupName, int responseSize, int broadcastGroupCount);
        void ReadFromStream(ServiceContext context, byte[] readData, Type type, bool complete);
        void WriteToStream(ServiceContext context, byte[] writeData, Type type);
    }
}
```

When implement your own metrics, define `IView` and register it `Stats.ViewManager.RegisterView(YOUR_VIEW);`, then send metrics.

There are several way to send metrics.

> Send each metrics each line.

```csharp
statsRecorder.NewMeasureMap().Put(YOUR_METRICS, 1).Record(TagContext);
```

> Put many metrics and send at once: 

```csharp
var map = statsRecorder.NewMeasureMap(); map.Put(YOUR_METRICS, 1);
map.Put(YOUR_METRICS2, 2);
map.Put(YOUR_METRICS3, 10);
if (isErrorOrInterrupted)
{
    map.Put(YOUR_METRICS4, 3);
}

map.Record(TagContext);
```

> create tag scope and set number of metrics.

```csharp
var tagContextBuilder = Tagger.CurrentBuilder.Put(FrontendKey, TagValue.Create("mobile-ios9.3.5"));
using (var scopedTags = tagContextBuilder.BuildScoped())
{
    StatsRecorder.NewMeasureMap().Put(YOUR_METRICS, 1).Record();
    StatsRecorder.NewMeasureMap().Put(YOUR_METRICS2, 2).Record();
    StatsRecorder.NewMeasureMap().Put(YOUR_METRICS3, 10).Record();
}
```

Make sure your View's column, and metrics TagKey is matched. Otherwise none of metrics will shown.

## Advanced
### MagicOnionOption/Logging


`MagicOnionOption` can pass to `MagicOnionEngine.BuildServerServiceDefinition(MagicOnionOptions option)`.

| Property | Description |
| --- | --- |
| `IMagicOnionLogger` MagicOnionLogger | Set the diagnostics info logger. |
| `bool` DisableEmbeddedService | Disable embedded service(ex:heartbeat), default is false. |
| `IList<MagicOnionFilterDescriptor>` GlobalFilters | Global MagicOnion filters. |
| `bool` EnableCurrentContext | Enable ServiceContext.Current option by AsyncLocal, default is false. |
| `IList<StreamingHubFilterDescriptor>` Global StreamingHub filters. | GlobalStreamingHubFilters |
| `IGroupRepositoryFactory` DefaultGroupRepositoryFactory | Default GroupRepository factory for StreamingHub, default is ``. |
| `IServiceLocator` ServiceLocator | Add the extra typed option. |
| `bool` IsReturnExceptionStackTraceInErrorDetail | If true, MagicOnion handles exception ownself and send to message. If false, propagate to gRPC engine. Default is false. |
| `IFormatterResolver` FormatterResolver | MessagePack serialization resolver. Default is used ambient default(MessagePackSerialzier.Default). |

`IMagicOnionLogger` is structured logger of MagicOnion. Implements your custom logging code and append it, default is `NullMagicOnionLogger`(do nothing). MagicOnion has some built in logger, `MagicOnionLogToGrpcLogger` that structured log to string log and send to `GrpcEnvironment.Logger`. `MagicOnionLogToGrpcLoggerWithDataDump` is includes data dump it is useful for debugging(but slightly heavy, recommended to only use debugging). `MagicOnionLogToGrpcLoggerWithNamedDataDump` is more readable than simple WithDataDump logger.

If you want to add many loggers, you can use `CompositeLogger`(for gRPC logging), `CompositeMagicOnionLogger`(for MagicOnion structured logging) to composite many loggers.
### Raw gRPC APIs
MagicOnion can define and use primitive gRPC APIs(ClientStreaming, ServerStreaming, DuplexStreaming). Especially DuplexStreaming is used underlying StreamingHub. If there is no reason, we recommend using StreamingHub.

```csharp
// Definitions
public interface IMyFirstService : IService<IMyFirstService>
{
    UnaryResult<string> SumAsync(int x, int y);
    Task<UnaryResult<string>> SumLegacyTaskAsync(int x, int y);
    Task<ClientStreamingResult<int, string>> ClientStreamingSampleAsync();
    Task<ServerStreamingResult<string>> ServertSreamingSampleAsync(int x, int y, int z);
    Task<DuplexStreamingResult<int, string>> DuplexStreamingSampleAync();
}

// Server
public class MyFirstService : ServiceBase<IMyFirstService>, IMyFirstService
{
    // VisualStudio 2017(C# 7.0), Unity 2018.3 supports return `async UnaryResult` directly
    // I recommend disable async-warning on project level. <NoWarn>1998</NoWarn>
    public async UnaryResult<string> SumAsync(int x, int y)
    {
        Logger.Debug($"Called SumAsync - x:{x} y:{y}");

        return (x + y).ToString();
    }

    // VS2015(C# 6.0), Unity 2018.2 use Task
    public async Task<UnaryResult<string>> SumLegacyTaskAsync(int x, int y)
    {
        Logger.Debug($"Called SumAsync - x:{x} y:{y}");

        // use UnaryResult method.
        return UnaryResult((x + y).ToString());
    }

    public async Task<ClientStreamingResult<int, string>> ClientStreamingSampleAsync()
    {
        Logger.Debug($"Called ClientStreamingSampleAsync");

        // If ClientStreaming, use GetClientStreamingContext.
        var stream = GetClientStreamingContext<int, string>();

        // receive from client asynchronously
        await stream.ForEachAsync(x =>
        {
            Logger.Debug("Client Stream Received:" + x);
        });

        // StreamingContext.Result() for result value.
        return stream.Result("finished");
    }

    public async Task<ServerStreamingResult<string>> ServertSreamingSampleAsync(int x, int y, int z)
    {
        Logger.Debug($"Called ServertSreamingSampleAsync - x:{x} y:{y} z:{z}");

        var stream = GetServerStreamingContext<string>();

        var acc = 0;
        for (int i = 0; i < z; i++)
        {
            acc = acc + x + y;
            await stream.WriteAsync(acc.ToString());
        }

        return stream.Result();
    }

    public async Task<DuplexStreamingResult<int, string>> DuplexStreamingSampleAync()
    {
        Logger.Debug($"Called DuplexStreamingSampleAync");

        // DuplexStreamingContext represents both server and client streaming.
        var stream = GetDuplexStreamingContext<int, string>();

        var waitTask = Task.Run(async () =>
        {
            // ForEachAsync(MoveNext, Current) can receive client streaming.
            await stream.ForEachAsync(x =>
            {
                Logger.Debug($"Duplex Streaming Received:" + x);
            });
        });

        // WriteAsync is ServerStreaming.
        await stream.WriteAsync("test1");
        await stream.WriteAsync("test2");
        await stream.WriteAsync("finish");

        await waitTask;

        return stream.Result();
    }
}
```

Client sample.

```csharp
static async Task UnaryRun(IMyFirstService client)
{
    // await(C# 7.0, Unity 2018.3)
    var vvvvv = await client.SumAsync(10, 20);
    Console.WriteLine("SumAsync:" + vvvvv);
    
    // if use Task<UnaryResult>(Unity 2018.2), use await await
    var vvvv2 = await await client.SumLegacyTaskAsync(10, 20);
}

static async Task ClientStreamRun(IMyFirstService client)
{
    var stream = await client.ClientStreamingSampleAsync();

    for (int i = 0; i < 3; i++)
    {
        await stream.RequestStream.WriteAsync(i);
    }
    await stream.RequestStream.CompleteAsync();

    var response = await stream.ResponseAsync;

    Console.WriteLine("Response:" + response);
}

static async Task ServerStreamRun(IMyFirstService client)
{
    var stream = await client.ServertSreamingSampleAsync(10, 20, 3);

    await stream.ResponseStream.ForEachAsync(x =>
    {
        Console.WriteLine("ServerStream Response:" + x);
    });
}

static async Task DuplexStreamRun(IMyFirstService client)
{
    var stream = await client.DuplexStreamingSampleAync();

    var count = 0;
    await stream.ResponseStream.ForEachAsync(async x =>
    {
        Console.WriteLine("DuplexStream Response:" + x);

        await stream.RequestStream.WriteAsync(count++);
        if (x == "finish")
        {
            await stream.RequestStream.CompleteAsync();
        }
    });
}
```

### Zero deserialization mapping
In RPC, especially in real-time communication involving frequent transmission of data, it is often the serialization process where data is converted before being sent that limits the performance. In MagicOnion, serialization is done by my MessagePack for C#, which is the fastest binary serializer for C#, so it cannot be a limiting factor. Also, in addition to performance, it also provides flexibility regarding data in that variables of any type can be sent as long as they can be serialized by MessagePack for C#.

Also, taking advantage of the fact that both the client and the server run on C# and data stored on internal memory are expected to share the same layout, I added an option to do mapping through memory copy without serialization/deserialization in case of a value-type variable.

Especially in Unity, this is can combinate with `MessagePack.UnityShims` package of NuGet.

```csharp
// It supports standard struct-type variables that are provided by Unity, such as Vector3, and arrays containing them, as well as custom struct-type variables and their arrays.
// I recommend doing this explicitly using [StructLayout(LayoutKind.Explicit)] to accurately match the size.
public struct CustomStruct
{
    public long Id;
    public int Hp;
    public int Mp;
    public byte Status;
}
 
// ---- Register the following code when initializing.
 
// By registering it, T and T[] can be handled using zero deserialization mapping.
UnsafeDirectBlitResolver.Register<CustomStruct>();
 
// The struct-type above as well as Unity-provided struct-types (Vector2, Rect, etc.), and their arrays are registered as standards.
CompositeResolver.RegisterAndSetAsDefault(
    UnsafeDirectBlitResolver.Instance,
    MessagePack.Unity.Extension.UnityBlitResolver.Instance
    );
 
// --- Now the communication will be in the format above when they are used for transmission.
await client.SendAsync(new CustomStruct { Hp = 99 });
```

Nothing needs to be processed here, so it promises the best performance theoretically possible in terms of transmission speed. However, since these struct-type variables need to be copied, I recommend handling everything as ref as a rule when you need to define a large struct-type, or it might slow down the process.

I believe that this can be easily and effectively applied to sending a large number of Transforms, such as an array of Vector3 variables.

## Author Info
This library is mainly developed by Yoshifumi Kawai(a.k.a. neuecc).  
He is the CEO/CTO of Cysharp which is a subsidiary of [Cygames](https://www.cygames.co.jp/en/).  
He is awarding Microsoft MVP for Developer Technologies(C#) since 2011.  
He is known as the creator of [UniRx](https://github.com/neuecc/UniRx/) and [MessagePack for C#](https://github.com/neuecc/MessagePack-CSharp/).

## License
This library is under the MIT License.
