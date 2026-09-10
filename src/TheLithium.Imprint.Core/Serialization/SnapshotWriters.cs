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
        Set<string>(static (w, v, _) => w.WriteStringValue(v));
        Set<char>(static (w, v, _) => w.WriteStringValue(v.ToString()));
        Set<bool>(static (w, v, _) => w.WriteBooleanValue(v));
        Set<byte>(static (w, v, _) => w.WriteNumberValue(v));
        Set<sbyte>(static (w, v, _) => w.WriteNumberValue(v));
        Set<short>(static (w, v, _) => w.WriteNumberValue(v));
        Set<ushort>(static (w, v, _) => w.WriteNumberValue(v));
        Set<int>(static (w, v, _) => w.WriteNumberValue(v));
        Set<uint>(static (w, v, _) => w.WriteNumberValue(v));
        Set<long>(static (w, v, _) => w.WriteNumberValue(v));
        Set<ulong>(static (w, v, _) => w.WriteNumberValue(v));
        Set<IntPtr>(static (w, v, _) => w.WriteNumberValue(v.ToInt64()));
        Set<UIntPtr>(static (w, v, _) => w.WriteNumberValue(v.ToUInt64()));
        Set<decimal>(static (w, v, _) => w.WriteNumberValue(v));
        Set<float>(static (w, v, _) => w.WriteNumberValue(v));
        Set<double>(static (w, v, _) => w.WriteNumberValue(v));
        Set<Half>(static (w, v, _) => w.WriteNumberValue((float)v));
        Set<Int128>(static (w, v, _) => w.WriteRawValue(v.ToString(CultureInfo.InvariantCulture)));
        Set<UInt128>(static (w, v, _) => w.WriteRawValue(v.ToString(CultureInfo.InvariantCulture)));
        Set<BigInteger>(static (w, v, _) => w.WriteRawValue(v.ToString(CultureInfo.InvariantCulture)));
        Set<Guid>(static (w, v, _) => w.WriteStringValue(v.ToString("D")));
        Set<DateTime>(static (w, v, _) => w.WriteStringValue(v.ToString("O", CultureInfo.InvariantCulture)));
        Set<DateTimeOffset>(static (w, v, _) => w.WriteStringValue(v.ToString("O", CultureInfo.InvariantCulture)));
        Set<DateOnly>(static (w, v, _) => w.WriteStringValue(v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        Set<TimeOnly>(static (w, v, _) => w.WriteStringValue(v.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)));
        Set<TimeSpan>(static (w, v, _) => w.WriteStringValue(v.ToString("c", CultureInfo.InvariantCulture)));
        Set<Uri>(static (w, v, _) => w.WriteStringValue(v.OriginalString));
        Set<JsonElement>(static (w, v, _) => v.WriteTo(w));
        Set<JsonDocument>(static (w, v, _) => v.RootElement.WriteTo(w));
    }

    private static void Set<T>(SnapshotWriter<T> writer) => Slot<T>.Writer = writer;

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

    public static void Write<T>(Utf8JsonWriter writer, T value, SnapshotWriteContext context)
    {
        using var frame = context.Enter(value);
        if (value is null) { writer.WriteNullValue(); return; }
        var implementation = Volatile.Read(ref Slot<T>.Writer);
        if (implementation is null)
            throw new SnapshotCaptureException($"No static snapshot writer is registered at {context.Path}. " +
                "Use a supported static type, [assembly: SnapshotInclude<YourType>], an explicit writer, or a projection. " +
                "There is no runtime reflection or ToString fallback.");
        try { implementation(writer, value, context); }
        catch (SnapshotException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            throw new SnapshotCaptureException($"Could not capture the value at {context.Path}.", error);
        }
    }
}
