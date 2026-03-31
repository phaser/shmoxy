namespace shmoxy.models;

/// <summary>
/// Represents a single WebSocket frame per RFC 6455.
/// </summary>
public record WebSocketFrame
{
    public bool Fin { get; init; }
    public WebSocketOpcode Opcode { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();
    public bool IsMasked { get; init; }
}

/// <summary>
/// WebSocket frame opcodes as defined in RFC 6455 Section 5.2.
/// </summary>
public enum WebSocketOpcode : byte
{
    Continuation = 0,
    Text = 1,
    Binary = 2,
    Close = 8,
    Ping = 9,
    Pong = 10
}
