# PoloniexWebSocketsApi
A C# wrapper for the Poloniex WebSockets api

## NuGet package

This package is available on NuGet.org under the name [PoloniexWebSocketsApi](https://www.nuget.org/packages/PoloniexWebSocketsApi).

## Usage example

Check [this JavaScript file](https://poloniex.com/js/plx_exchage.js?v=060617) for api reference.

```csharp
static async Task Main(string[] args)
{
    PoloniexChannel poloniexChannel = new PoloniexChannel();
    
    poloniexChannel.MessageArrived += OnMessageArrived;
    
    await poloniexChannel.ConnectAsync();
    
    await poloniexChannel.SendAsync(new PoloniexCommand() { Channel = 1001, Command = PoloniexCommandType.Subscribe });
    await poloniexChannel.SendAsync(new PoloniexCommand() { Channel = 1002, Command = PoloniexCommandType.Subscribe });
    await poloniexChannel.SendAsync(new PoloniexCommand() { Channel = 1003, Command = PoloniexCommandType.Subscribe });
    await poloniexChannel.SendAsync(new PoloniexCommand() { Channel = TickerSymbol.BTC_XMR, Command = PoloniexCommandType.Subscribe });

    Console.ReadLine();
}

private static void OnMessageArrived(JsonSerializer serializer, object message)
{
    Console.WriteLine(message);
}
```

## I had some problems with official implementation so I recommend using a pure socket client or using an asynchronous overlay ClientWebSocketWrapper
[ClientWebSocketWrapper](https://github.com/mrbighokage/ClientWebSocketWrapper.DotNet)
