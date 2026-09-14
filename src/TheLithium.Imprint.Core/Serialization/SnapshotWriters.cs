using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace TheLithium.Imprint;

/// <summary>Static generic writer slots. No System.Type keys, runtime member inspection or reflective fallback.</summary>
public static class SnapshotWriters
{
    private static class Slot<T>
    {
        internal static SnapshotWriter<T>? Writer;
    }

    static SnapshotWriters()
    {
        Set<string>(static (writer, value, _) => writer.WriteStringValue(value));
        Set<char>(static (writer, value, _) => writer.WriteStringValue(value.ToString()));
        Set<bool>(static (writer, value, _) => writer.WriteBooleanValue(value));
        Set<byte>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<byte[]>(WriteBytes);
        Set<sbyte>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<short>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<ushort>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<int>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<uint>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<long>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<ulong>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<IntPtr>(static (writer, value, _) => writer.WriteNumberValue(value.ToInt64()));
        Set<UIntPtr>(static (writer, value, _) => writer.WriteNumberValue(value.ToUInt64()));
        Set<decimal>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<float>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<double>(static (writer, value, _) => writer.WriteNumberValue(value));
        Set<Half>(static (writer, value, _) => writer.WriteNumberValue((float)value));
        Set<Int128>(static (writer, value, _) => writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture)));
        Set<UInt128>(static (writer, value, _) => writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture)));
        Set<BigInteger>(static (writer, value, _) => writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture)));
        Set<Guid>(static (writer, value, _) => writer.WriteStringValue(value.ToString("D")));
        Set<DateTime>(static (writer, value, _) => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture)));
        Set<DateTimeOffset>(static (writer, value, _) => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture)));
        Set<DateOnly>(static (writer, value, _) => writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        Set<TimeOnly>(static (writer, value, _) => writer.WriteStringValue(value.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)));
        Set<TimeSpan>(static (writer, value, _) => writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture)));
        Set<Uri>(static (writer, value, _) => writer.WriteStringValue(value.OriginalString));
        Set<JsonElement>(static (writer, value, _) => value.WriteTo(writer));
        Set<JsonDocument>(static (writer, value, _) => value.RootElement.WriteTo(writer));
    }

    private static void Set<T>(SnapshotWriter<T> writer) => Slot<T>.Writer = writer;

    private static void WriteBytes(Utf8JsonWriter writer, byte[] bytes, SnapshotWriteContext context)
    {
        if (context.Representation.ByteArrays == SnapshotByteArrayRepresentation.Base64)
        {
            writer.WriteBase64StringValue(bytes);
            return;
        }
        writer.WriteStartArray();
        for (var index = 0; index < bytes.Length; index++)
        {
            using var path = context.At(index);
            Write(writer, bytes[index], context);
        }
        writer.WriteEndArray();
    }

    /// <summary>Registers an explicit writer. Register during startup, before concurrent tests begin.</summary>
    public static void Register<T>(SnapshotWriter<T> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Interlocked.Exchange(ref Slot<T>.Writer, writer);
    }

    /// <summary>Used by generated module initializers. Explicit registrations always win.</summary>
    public static void TryRegister<T>(SnapshotWriter<T> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Interlocked.CompareExchange(ref Slot<T>.Writer, writer, null);
    }

    /// <summary>Infers anonymous shapes without naming them or reflecting over them.</summary>
    public static void TryRegister<T>(T shape, SnapshotWriter<T> writer) => TryRegister(writer);

    /// <summary>Writes one value through the generated or explicitly registered writer for its static type.</summary>
    /// <typeparam name="T">The static type used to select the writer.</typeparam>
    /// <param name="writer">The open JSON destination.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="context">The active path, cycle, and budget context.</param>
    public static void Write<T>(Utf8JsonWriter writer, T value, SnapshotWriteContext context)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(context);
        using var frame = context.Enter(value);
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        var implementation = Volatile.Read(ref Slot<T>.Writer);
        if (implementation is null)
        {
            throw new SnapshotCaptureException($"No static snapshot writer is registered at {context.Path}. " +
                "Use a supported static type, [assembly: SnapshotInclude<YourType>], an explicit writer, or a projection. " +
                "There is no runtime reflection or ToString fallback.");
        }

        try
        {
            implementation(writer, value, context);
        }
        catch (SnapshotException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            throw new SnapshotCaptureException($"Could not capture the value at {context.Path}.", error);
        }
    }
}
