using System.Buffers.Binary;
using System.Text;

namespace DbConnector.Protocol;

/// <summary>Mensagem do protocolo do PostgreSQL (v3): tipo de 1 byte e corpo; o tamanho vai no fio.</summary>
public readonly record struct PgMessage(byte Type, byte[] Body)
{
    public byte[] ToBytes()
    {
        var bytes = new byte[5 + Body.Length];
        bytes[0] = Type;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(1), Body.Length + 4);
        Body.CopyTo(bytes, 5);
        return bytes;
    }
}

/// <summary>Leitura e escrita das mensagens. Limite de 64 MB por mensagem (o servidor tem o mesmo teto prático).</summary>
public static class PgWire
{
    public const int SslRequestCode = 80877103;
    public const int GssEncRequestCode = 80877104;
    public const int CancelRequestCode = 80877102;
    public const int ProtocolVersion3 = 196608;
    private const int MaxMessageLength = 64 * 1024 * 1024;

    /// <summary>Primeira mensagem da conexão (sem byte de tipo): o código e o resto.</summary>
    public static async Task<(int Code, byte[] Body)?> ReadStartupAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct)) return null;
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 8 || length > 10_000) throw new InvalidDataException($"startup com tamanho inválido ({length})");
        var rest = new byte[length - 4];
        if (!await ReadExactAsync(stream, rest, ct)) return null;
        return (BinaryPrimitives.ReadInt32BigEndian(rest), rest[4..]);
    }

    public static async Task<PgMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[5];
        if (!await ReadExactAsync(stream, header, ct)) return null;
        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
        if (length < 4 || length > MaxMessageLength) throw new InvalidDataException($"mensagem '{(char)header[0]}' com tamanho inválido ({length})");
        var body = new byte[length - 4];
        if (!await ReadExactAsync(stream, body, ct)) return null;
        return new PgMessage(header[0], body);
    }

    public static async Task WriteAsync(Stream stream, PgMessage message, CancellationToken ct)
    {
        await stream.WriteAsync(message.ToBytes(), ct);
        await stream.FlushAsync(ct);
    }

    public static byte[] Startup(int code, byte[] body)
    {
        var bytes = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(bytes, bytes.Length);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), code);
        body.CopyTo(bytes, 8);
        return bytes;
    }

    /// <summary>Parâmetros do StartupMessage (nome\0valor\0...\0).</summary>
    public static Dictionary<string, string> ParseStartupParameters(byte[] body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var reader = new PgReader(body);
        while (reader.Remaining > 0)
        {
            var name = reader.CString();
            if (name.Length == 0) break;
            result[name] = reader.CString();
        }
        return result;
    }

    public static byte[] StartupParameters(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var buffer = new MemoryStream();
        foreach (var (name, value) in parameters)
        {
            WriteCString(buffer, name);
            WriteCString(buffer, value);
        }
        buffer.WriteByte(0);
        return buffer.ToArray();
    }

    public static PgMessage Authentication(int code, byte[]? data = null)
    {
        var body = new byte[4 + (data?.Length ?? 0)];
        BinaryPrimitives.WriteInt32BigEndian(body, code);
        data?.CopyTo(body, 4);
        return new PgMessage((byte)'R', body);
    }

    /// <summary>ErrorResponse (severidade FATAL ou ERROR, SQLSTATE e mensagem em pt-BR).</summary>
    public static PgMessage Error(string sqlState, string message, string severity = "FATAL")
    {
        var buffer = new MemoryStream();
        foreach (var (field, value) in new[] { ('S', severity), ('V', severity), ('C', sqlState), ('M', message) })
        {
            buffer.WriteByte((byte)field);
            WriteCString(buffer, value);
        }
        buffer.WriteByte(0);
        return new PgMessage((byte)'E', buffer.ToArray());
    }

    /// <summary>Campos de um ErrorResponse/NoticeResponse (C = SQLSTATE, M = mensagem...).</summary>
    public static Dictionary<char, string> ParseErrorFields(byte[] body)
    {
        var fields = new Dictionary<char, string>();
        var reader = new PgReader(body);
        while (reader.Remaining > 0)
        {
            var field = reader.Byte();
            if (field == 0) break;
            fields[(char)field] = reader.CString();
        }
        return fields;
    }

    public static void WriteCString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes);
        stream.WriteByte(0);
    }

    public static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) return offset == 0 ? false : throw new EndOfStreamException("conexão encerrada no meio de uma mensagem");
            offset += read;
        }
        return true;
    }
}

/// <summary>Leitor sequencial do corpo de uma mensagem.</summary>
public ref struct PgReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position;

    public readonly int Remaining => _data.Length - _position;

    public byte Byte() => _data[_position++];

    public short Int16()
    {
        var value = BinaryPrimitives.ReadInt16BigEndian(_data[_position..]);
        _position += 2;
        return value;
    }

    public int Int32()
    {
        var value = BinaryPrimitives.ReadInt32BigEndian(_data[_position..]);
        _position += 4;
        return value;
    }

    public ReadOnlySpan<byte> Bytes(int count)
    {
        var value = _data.Slice(_position, count);
        _position += count;
        return value;
    }

    public string CString()
    {
        var end = _data[_position..].IndexOf((byte)0);
        if (end < 0) throw new InvalidDataException("string sem terminador");
        var value = Encoding.UTF8.GetString(_data.Slice(_position, end));
        _position += end + 1;
        return value;
    }
}
