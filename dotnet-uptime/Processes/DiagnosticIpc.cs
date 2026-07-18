
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace MV10.DotnetUptime.Processes;

/// <summary>
/// Queries process information from a .NET runtime via the diagnostic IPC protocol.
/// </summary>
public static class DiagnosticIpc
{
    private static readonly byte[] IpcMagic = Encoding.ASCII.GetBytes("DOTNET_IPC_V1\0");
    private const int HeaderSize = 20;
    private const int GuidSize = 16;
    private const int ConnectTimeoutMs = 5000;

    private const byte CmdSetProcess = 0x04;
    private const byte CmdGetProcessInfo = 0x00;
    private const byte CmdGetProcessInfo2 = 0x04;
    private const byte CmdGetProcessInfo3 = 0x08;
    private const byte ResponseOK = 0x00;

    /// <summary>
    /// Queries the runtime for process information via its diagnostic port.
    /// Tries v3, falls back to v2, then v1. Returns default values on failure.
    /// </summary>
    public static RuntimeProcessInfo GetProcessInfo(int pid)
    {
        try
        {
            byte[] payload;

            payload = SendCommand(pid, CmdGetProcessInfo3);
            if (payload != null) return ParseV3(payload);

            payload = SendCommand(pid, CmdGetProcessInfo2);
            if (payload != null) return ParseV2(payload);

            payload = SendCommand(pid, CmdGetProcessInfo);
            if (payload != null) return ParseV1(payload);
        }
        catch
        {
        }

        return new RuntimeProcessInfo();
    }

    /// <summary>
    /// Sends a parameterless Process command and returns the response payload,
    /// or null on error or unsupported command.
    /// </summary>
    private static byte[] SendCommand(int pid, byte commandId)
    {
        try
        {
            using var stream = Connect(pid);

            byte[] request = new byte[HeaderSize];
            Array.Copy(IpcMagic, request, 14);
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(14), HeaderSize);
            request[16] = CmdSetProcess;
            request[17] = commandId;

            stream.Write(request, 0, request.Length);
            stream.Flush();

            byte[] header = ReadExact(stream, HeaderSize);
            if (header == null) return null;

            if (header[17] != ResponseOK) return null;

            ushort totalSize = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14));
            int payloadSize = totalSize - HeaderSize;
            if (payloadSize <= 0) return null;

            return ReadExact(stream, payloadSize);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Connects to the diagnostic port for a given PID using the platform transport.
    /// </summary>
    private static Stream Connect(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ConnectWindows(pid);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ConnectLinux(pid);

        throw new PlatformNotSupportedException();
    }

    private static Stream ConnectWindows(int pid)
    {
        var pipe = new NamedPipeClientStream(
            ".",
            $"dotnet-diagnostic-{pid}",
            PipeDirection.InOut,
            PipeOptions.None,
            TokenImpersonationLevel.Impersonation);
        pipe.Connect(ConnectTimeoutMs);
        return pipe;
    }

    private static Stream ConnectLinux(int pid)
    {
        var socketPath = FindDiagnosticSocket(pid);
        if (socketPath == null)
            throw new FileNotFoundException($"No diagnostic socket found for PID {pid}");

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>
    /// Locates the diagnostic Unix socket file for a PID, handling
    /// TMPDIR and cross-namespace scenarios.
    /// </summary>
    private static string FindDiagnosticSocket(int pid)
    {
        var tmpDir = ProcessDiscovery.GetProcessTmpDir(pid);
        string searchDir;
        int searchPid;

        if (ProcessDiscovery.TryGetNamespacePid(pid, out int nsPid))
        {
            searchDir = Path.Combine($"/proc/{pid}/root", tmpDir.TrimStart(Path.DirectorySeparatorChar));
            searchPid = nsPid;
        }
        else
        {
            searchDir = tmpDir;
            searchPid = pid;
        }

        return FindSocketInDirectory(searchDir, searchPid)
            ?? (searchDir != Path.GetTempPath() ? FindSocketInDirectory(Path.GetTempPath(), pid) : null);
    }

    private static string FindSocketInDirectory(string directory, int pid)
    {
        try
        {
            return Directory.GetFiles(directory, $"dotnet-diagnostic-{pid}-*-socket")
                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read == 0) return null;
            offset += read;
        }
        return buffer;
    }

    // --- Payload parsing (protocol documented in dotnet/diagnostics ProcessInfo.cs) ---
    //
    // V1: PID(8) + Cookie(16) + CommandLine + OS + Architecture
    // V2: V1 fields + EntrypointAssemblyName + ClrProductVersion
    // V3: Version(4) + V2 fields + [PortableRID if version >= 1]
    //
    // Strings: 4-byte char count (UTF-16, includes null terminator), then char data.

    private static RuntimeProcessInfo ParseCommon(byte[] payload, ref int index)
    {
        var info = new RuntimeProcessInfo();

        index += sizeof(ulong);

        byte[] cookieBytes = new byte[GuidSize];
        Array.Copy(payload, index, cookieBytes, 0, GuidSize);
        info.RuntimeInstanceCookie = new Guid(cookieBytes);
        index += GuidSize;

        SkipIpcString(payload, ref index);
        SkipIpcString(payload, ref index);
        info.ProcessArchitecture = ReadIpcString(payload, ref index);

        return info;
    }

    private static RuntimeProcessInfo ParseV1(byte[] payload)
    {
        int index = 0;
        return ParseCommon(payload, ref index);
    }

    private static RuntimeProcessInfo ParseV2(byte[] payload)
    {
        int index = 0;
        var info = ParseCommon(payload, ref index);
        info.ManagedEntrypointAssemblyName = ReadIpcString(payload, ref index);
        info.ClrProductVersionString = ReadIpcString(payload, ref index);
        return info;
    }

    private static RuntimeProcessInfo ParseV3(byte[] payload)
    {
        int index = 0;

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(index, 4));
        index += sizeof(uint);

        var info = ParseCommon(payload, ref index);
        info.ManagedEntrypointAssemblyName = ReadIpcString(payload, ref index);
        info.ClrProductVersionString = ReadIpcString(payload, ref index);

        if (version >= 1 && index < payload.Length)
            info.PortableRuntimeIdentifier = ReadIpcString(payload, ref index);

        return info;
    }

    /// <summary>
    /// Reads a UTF-16LE string prefixed by a 4-byte character count (includes null terminator).
    /// </summary>
    private static string ReadIpcString(byte[] buffer, ref int index)
    {
        int charCount = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(index, 4));
        index += sizeof(int);

        int byteCount = charCount * sizeof(char);
        var value = Encoding.Unicode.GetString(buffer, index, byteCount).Substring(0, charCount - 1);
        index += byteCount;
        return value;
    }

    private static void SkipIpcString(byte[] buffer, ref int index)
    {
        int charCount = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(index, 4));
        index += sizeof(int);
        index += charCount * sizeof(char);
    }
}
