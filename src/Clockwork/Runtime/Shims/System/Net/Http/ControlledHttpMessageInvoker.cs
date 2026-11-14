using System.Net.Http;
using Clockwork.Runtime.Shims;

namespace Clockwork.Shims.System.Net.Http;

/// <summary>
/// Controlled <see cref="HttpMessageInvoker"/> <c>Send</c> and <c>SendAsync</c>. A call through an
/// <see cref="HttpMessageInvoker"/> reference dispatches to <see cref="HttpClient"/>'s override when the receiver is a
/// client, which bounds the request with the client's real-time <see cref="HttpClient.Timeout"/>, so such a call requires
/// an infinite timeout as <see cref="ControlledHttpClient"/> does; any other receiver sends as written. The base calls of
/// an override run <see cref="HttpMessageInvoker"/>'s implementation, which sends through the handler with no timeout.
/// </summary>
public static class ControlledHttpMessageInvoker
{
    private const string Api = "System.Net.Http.HttpMessageInvoker";

    private static readonly Func<HttpMessageInvoker, HttpRequestMessage, CancellationToken, HttpResponseMessage> BaseSendCall =
        NonVirtualCall.Create<Func<HttpMessageInvoker, HttpRequestMessage, CancellationToken, HttpResponseMessage>>(
            typeof(HttpMessageInvoker), nameof(HttpMessageInvoker.Send), typeof(HttpRequestMessage), typeof(CancellationToken));

    private static readonly Func<HttpMessageInvoker, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> BaseSendAsyncCall =
        NonVirtualCall.Create<Func<HttpMessageInvoker, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(
            typeof(HttpMessageInvoker), nameof(HttpMessageInvoker.SendAsync), typeof(HttpRequestMessage), typeof(CancellationToken));

    /// <summary>Controlled <c>HttpMessageInvoker.Send(HttpRequestMessage, CancellationToken)</c>.</summary>
    public static HttpResponseMessage Send(HttpMessageInvoker invoker, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequireInfiniteTimeout(invoker, Api + ".Send");
        return invoker.Send(request, cancellationToken);
    }

    /// <summary>Controlled <c>HttpMessageInvoker.SendAsync(HttpRequestMessage, CancellationToken)</c>.</summary>
    public static Task<HttpResponseMessage> SendAsync(HttpMessageInvoker invoker, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequireInfiniteTimeout(invoker, Api + ".SendAsync");
        return invoker.SendAsync(request, cancellationToken);
    }

    /// <summary>The base call <c>HttpMessageInvoker.Send(HttpRequestMessage, CancellationToken)</c>, from an override.</summary>
    public static HttpResponseMessage BaseSend(HttpMessageInvoker invoker, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(Api + ".Send");
        return BaseSendCall(invoker, request, cancellationToken);
    }

    /// <summary>The base call <c>HttpMessageInvoker.SendAsync(HttpRequestMessage, CancellationToken)</c>, from an override.</summary>
    public static Task<HttpResponseMessage> BaseSendAsync(HttpMessageInvoker invoker, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(Api + ".SendAsync");
        return BaseSendAsyncCall(invoker, request, cancellationToken);
    }

    // A null receiver fails with the NullReferenceException the BCL's instance call does.
    private static void RequireInfiniteTimeout(HttpMessageInvoker invoker, string api)
    {
        if (invoker is HttpClient client)
        {
            ControlledHttpClient.RequireInfiniteTimeoutFor(client, api);
            return;
        }

        SimulationRuntimeDispatch.RequireActiveSimulation(api);
    }
}
