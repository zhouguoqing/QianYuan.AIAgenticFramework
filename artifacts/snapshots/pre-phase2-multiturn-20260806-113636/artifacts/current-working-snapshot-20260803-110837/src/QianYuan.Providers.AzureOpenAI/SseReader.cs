using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;

namespace QianYuan.Providers.AzureOpenAI;

/// <summary>
/// Minimal Server-Sent Events parser. Yields raw "data:" payloads (one per event).
/// An event with payload "[DONE]" signals end of stream.
/// </summary>
internal static class SseReader
{
    public static async IAsyncEnumerable<string> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var reader = PipeReader.Create(stream);
        var dataBuilder = new StringBuilder();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            ReadResult result;
            try { result = await reader.ReadAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { yield break; }

            var buffer = result.Buffer;
            while (TryReadLine(ref buffer, out var line))
            {
                if (line.Length == 0)
                {
                    if (dataBuilder.Length > 0)
                    {
                        yield return dataBuilder.ToString();
                        dataBuilder.Clear();
                    }
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var payload = line["data:".Length..].TrimStart();
                    if (dataBuilder.Length > 0) dataBuilder.Append('\n');
                    dataBuilder.Append(payload);
                }
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                if (dataBuilder.Length > 0) yield return dataBuilder.ToString();
                yield break;
            }
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out string line)
    {
        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out ReadOnlySequence<byte> seq, (byte)'\n', advancePastDelimiter: true))
        {
            line = string.Empty;
            return false;
        }

        var bytes = seq.ToArray();
        var len = bytes.Length;
        if (len > 0 && bytes[len - 1] == (byte)'\r') len--;
        line = Encoding.UTF8.GetString(bytes, 0, len);
        buffer = buffer.Slice(reader.Position);
        return true;
    }
}
