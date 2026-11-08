using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace VLOptimizer.Core;

internal static class ConfigurationDnsAccess
{
    internal static IReadOnlyList<ConfigurationChange> Resolve(string provider, bool includeInactive = false)
    {
        string[] servers = provider switch
        {
            "cloudflare" => ["1.1.1.1", "1.0.0.1"], "google" => ["8.8.8.8", "8.8.4.4"],
            "quad9" => ["9.9.9.9", "149.112.112.112"], "adguard" => ["94.140.14.14", "94.140.15.15"],
            "auto" => [], _ => throw new InvalidDataException("Provedor DNS fora do catálogo.")
        };
        var result = new List<ConfigurationChange>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT) ||
                !nic.Supports(NetworkInterfaceComponent.IPv4) || nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(nic.Id, out var guid)) continue;
            if (!includeInactive && nic.OperationalStatus != OperationalStatus.Up) continue;
            using var connection = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Network\{4d36e972-e325-11ce-bfc1-08002be10318}\" + guid.ToString("B") + @"\Connection");
            if (connection?.GetValue("PnpInstanceID") is not string instance || !instance.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) && !instance.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) continue;
            if (includeInactive)
            {
                try { _ = Read(guid.ToString("D")); }
                catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 50 or 87 or 1168) { continue; }
            }
            var target = new ConfigurationTarget(ConfigurationTargetKind.DnsIpv4, guid.ToString("D"));
            // Name is intentionally empty: all DNS providers conflict on the same interface resource.
            result.Add(new(target, ConfigurationValue.State(new ConfigurationDnsState(servers.Length == 0, servers)), ConfigurationValue.State(new ConfigurationDnsState(true, []))));
        }
        return result;
    }
    internal static ConfigurationValue Read(string interfaceId)
    {
        var guid = Guid.Parse(interfaceId); var settings = new DnsSettings { Version = 1 };
        uint status = GetInterfaceDnsSettings(guid, ref settings);
        if (status != 0) throw new Win32Exception((int)status, $"Não foi possível consultar DNS da interface {guid}.");
        try
        {
            string value = settings.NameServer == IntPtr.Zero ? "" : Marshal.PtrToStringUni(settings.NameServer) ?? "";
            string[] servers = ParseServers(value);
            return ConfigurationValue.State(new ConfigurationDnsState(servers.Length == 0, servers));
        }
        finally { FreeInterfaceDnsSettings(ref settings); }
    }
    internal static void Write(string interfaceId, ConfigurationValue value)
    {
        var desired = JsonSerializer.Deserialize<ConfigurationDnsState>(value.Json) ?? throw new InvalidDataException("Estado DNS ausente.");
        if (desired.Servers.Count > 16 || desired.Automatic && desired.Servers.Count != 0 || !desired.Automatic && desired.Servers.Count == 0) throw new InvalidDataException("Estado DNS inválido.");
        string[] servers = ParseServers(string.Join(',', desired.Servers));
        IntPtr text = Marshal.StringToHGlobalUni(string.Join(',', servers));
        try
        {
            var settings = new DnsSettings { Version = 1, Flags = 2, NameServer = text };
            uint result = SetInterfaceDnsSettings(Guid.Parse(interfaceId), ref settings);
            if (result != 0) throw new Win32Exception((int)result, "O Windows não aceitou a configuração DNS IPv4.");
        }
        finally { Marshal.FreeHGlobal(text); }
    }
    private static string[] ParseServers(string text)
    {
        return text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x =>
        {
            if (!IPAddress.TryParse(x, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) throw new InvalidDataException("A configuração DNS contém um endereço IPv4 inválido ou uma família não suportada; valor preservado.");
            return ip.ToString();
        }).ToArray();
    }
    [StructLayout(LayoutKind.Sequential)] private struct DnsSettings
    {
        public uint Version; public ulong Flags; public IntPtr Domain, NameServer, SearchList;
        public uint RegistrationEnabled, RegisterAdapterName, EnableLlmnr, QueryAdapterName; public IntPtr ProfileNameServer;
    }
    [DllImport("iphlpapi.dll", ExactSpelling = true)] private static extern uint GetInterfaceDnsSettings(Guid interfaceId, ref DnsSettings settings);
    [DllImport("iphlpapi.dll", ExactSpelling = true)] private static extern uint SetInterfaceDnsSettings(Guid interfaceId, ref DnsSettings settings);
    [DllImport("iphlpapi.dll", ExactSpelling = true)] private static extern void FreeInterfaceDnsSettings(ref DnsSettings settings);
}

/// <summary>DEVMODEW offsets are fixed by Win32. Only the display union and mode fields are changed.</summary>
public static class ConfigurationDisplayCodec
{
    public const int Size = 220;
    public static byte[] Empty()
    {
        var bytes = new byte[Size]; BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(68), Size); return bytes;
    }
    public static ConfigurationDisplayMode Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size || BinaryPrimitives.ReadUInt16LittleEndian(bytes[68..]) != Size) throw new InvalidDataException("DEVMODEW incompatível.");
        static uint U(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        return new(U(bytes, 172), U(bytes, 176), U(bytes, 184), U(bytes, 168), U(bytes, 84), unchecked((int)U(bytes, 76)), unchecked((int)U(bytes, 80)), U(bytes, 88));
    }
    public static byte[] Encode(ConfigurationDisplayMode mode)
    {
        if (mode.Width is < 320 or > 32768 || mode.Height is < 200 or > 32768 || mode.BitsPerPixel is not (16 or 24 or 32) || mode.Orientation > 3 || mode.Frequency > 1000 || mode.FixedOutput > 2) throw new InvalidDataException("Modo de vídeo inválido.");
        var bytes = Empty();
        void W(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        W(72, 0x205c00a0); W(172, mode.Width); W(176, mode.Height); W(184, mode.Frequency); W(168, mode.BitsPerPixel); W(84, mode.Orientation); W(76, unchecked((uint)mode.PositionX)); W(80, unchecked((uint)mode.PositionY)); W(88, mode.FixedOutput);
        return bytes;
    }
}

internal static class ConfigurationDisplayAccess
{
    internal static IReadOnlyList<ConfigurationChange> Resolve()
    {
        var device = Primary(); var before = ReadMode(device); bool supported = false;
        for (int index = 0; index < 8192; index++)
        {
            var bytes = ConfigurationDisplayCodec.Empty();
            if (!EnumDisplaySettingsEx(device, index, bytes, 0)) break;
            var mode = ConfigurationDisplayCodec.Decode(bytes);
            if (mode.Width == 1280 && mode.Height == 720 && mode.BitsPerPixel == before.BitsPerPixel && mode.Frequency == before.Frequency) { supported = true; break; }
        }
        if (!supported) throw new InvalidOperationException("1280×720 não está disponível com a frequência e profundidade de cor atuais. Selecione um modo compatível nas Configurações do Windows.");
        var desired = before with { Width = 1280, Height = 720 };
        return [new(new(ConfigurationTargetKind.DisplayMode, device), ConfigurationValue.State(desired), ConfigurationValue.State(before))];
    }
    internal static ConfigurationValue Read(string device) => ConfigurationValue.State(ReadMode(device));
    private static ConfigurationDisplayMode ReadMode(string device)
    {
        ValidateDevice(device); var bytes = ConfigurationDisplayCodec.Empty();
        if (!EnumDisplaySettingsEx(device, -1, bytes, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível consultar o modo de vídeo atual.");
        return ConfigurationDisplayCodec.Decode(bytes);
    }
    internal static void Write(string device, ConfigurationValue value)
    {
        ValidateDevice(device);
        var mode = JsonSerializer.Deserialize<ConfigurationDisplayMode>(value.Json) ?? throw new InvalidDataException("Modo de vídeo ausente.");
        var bytes = ConfigurationDisplayCodec.Encode(mode);
        int test = ChangeDisplaySettingsEx(device, bytes, IntPtr.Zero, 2, IntPtr.Zero);
        if (test != 0) throw new IOException($"O driver recusou o teste do modo de vídeo (DISP_CHANGE={test}).");
        int result = ChangeDisplaySettingsEx(device, bytes, IntPtr.Zero, 0, IntPtr.Zero);
        if (result != 0) throw new IOException($"O driver não aplicou o modo de vídeo (DISP_CHANGE={result}).");
    }
    private static string Primary()
    {
        for (uint index = 0; index < 128; index++)
        {
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.Flags & 5) == 5 && device.Name is not null) return device.Name;
        }
        throw new IOException("Monitor primário ativo não encontrado.");
    }
    private static void ValidateDevice(string value)
    {
        if (value.Length <= 11 || !value.StartsWith(@"\\.\DISPLAY", StringComparison.Ordinal) || !value[11..].All(char.IsDigit)) throw new InvalidDataException("Identificador de monitor inválido.");
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public uint Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Description; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Key;
    }
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettingsEx(string device, int mode, [In, Out, MarshalAs(UnmanagedType.LPArray, SizeConst = ConfigurationDisplayCodec.Size)] byte[] settings, uint flags);
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string device, [In, Out, MarshalAs(UnmanagedType.LPArray, SizeConst = ConfigurationDisplayCodec.Size)] byte[] settings, IntPtr window, uint flags, IntPtr parameter);
}
