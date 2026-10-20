namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// The browser's local storage for the session (<c>atcSession</c> in session.js).
/// </summary>
public sealed class BrowserSessionStorage(IJSRuntime js) : ISessionStorage
{
    public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
        => js.InvokeAsync<string?>("atcSession.get", cancellationToken);

    public ValueTask WriteAsync(
        string value,
        CancellationToken cancellationToken)
        => js.InvokeVoidAsync("atcSession.set", cancellationToken, value);

    public ValueTask RemoveIfAsync(
        string expected,
        CancellationToken cancellationToken)
        => js.InvokeVoidAsync("atcSession.clearIf", cancellationToken, expected);
}