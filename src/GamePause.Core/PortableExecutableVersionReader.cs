using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace GamePause.Core;

public static class PortableExecutableVersionReader
{
    public const long DefaultMaxImageBytes = 64L * 1024 * 1024;

    public static Version ReadAssemblyVersion(Stream source, long maxImageBytes = DefaultMaxImageBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Stream must support read operations.", nameof(source));
        if (maxImageBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxImageBytes));

        using var seekableStream = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var bytesRead = source.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;
            totalBytes += bytesRead;
            if (totalBytes > maxImageBytes)
                throw new InvalidDataException($"PE image exceeds the {maxImageBytes} byte safety limit.");
            seekableStream.Write(buffer, 0, bytesRead);
        }

        seekableStream.Position = 0;
        using var peReader = new PEReader(seekableStream);
        if (!peReader.HasMetadata) throw new InvalidDataException("PE image does not contain assembly metadata.");
        return peReader.GetMetadataReader().GetAssemblyDefinition().Version;
    }
}
