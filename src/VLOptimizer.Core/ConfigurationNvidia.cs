using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace VLOptimizer.Core;

/// <summary>NVDRS_SETTING_V1 DWORD codec from the public NVIDIA ABI (pack 4).
/// No third-party implementation or runtime library is redistributed.</summary>
public static class NvidiaDrsSettingCodec
{
    public const int Size = 12_320;
    public const int SettingIdOffset = 4_100;
    public const int TypeOffset = 4_104;
    public const int PredefinedFlagOffset = 4_112;
    public const int CurrentValueOffset = 8_220;
    public const uint Version = Size | 0x10000;
    public const uint DlssOverrideId = 0x10e41e01;
    public const uint DlssPresetId = 0x10e41df3;
    public const uint LatestPreset = 0x00ffffff;
    public static byte[] Create(uint id, uint value = 0)
    {
        ValidateId(id);
        byte[] data = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(data, Version);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(SettingIdOffset), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(CurrentValueOffset), value);
        return data;
    }
    public static ConfigurationValue Decode(ReadOnlySpan<byte> data, uint expectedId)
    {
        ValidateId(expectedId);
        if (data.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(data) != Version || BinaryPrimitives.ReadUInt32LittleEndian(data[SettingIdOffset..]) != expectedId)
            throw new InvalidDataException("O driver retornou um layout DRS incompatível.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[TypeOffset..]) != 0) throw new InvalidDataException("O ajuste DRS não é um DWORD; valor preservado.");
        // Predefined defaults are restored through RestoreProfileDefaultSetting, rather than
        // converted into a persistent user override with the same numeric value.
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[PredefinedFlagOffset..]) != 0) return ConfigurationValue.Missing(true);
        return ConfigurationValue.DWord(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[CurrentValueOffset..])));
    }
    public static void ValidateId(uint id)
    {
        if (id is not (DlssOverrideId or DlssPresetId)) throw new InvalidDataException("Ajuste NVIDIA fora do conjunto fechado de substituição DLSS-SR.");
    }
}

internal static class ConfigurationNvidiaAccess
{
    private static readonly object gate = new();
    internal static IReadOnlyList<ConfigurationChange> Resolve()
    {
        lock (gate)
        {
            using var driver = new Driver();
            if (!driver.HasRtx()) throw new InvalidOperationException("Substituição DLSS-SR disponível somente com GPU NVIDIA RTX e driver compatível.");
            if (!driver.SupportsSettings()) throw new InvalidOperationException("O driver NVIDIA instalado não anuncia os dois ajustes de substituição DLSS-SR. Atualize pelo instalador oficial da NVIDIA.");
            return new[] { new ConfigurationChange(Target(NvidiaDrsSettingCodec.DlssOverrideId), ConfigurationValue.DWord(1), ConfigurationValue.Missing(true)),
                new ConfigurationChange(Target(NvidiaDrsSettingCodec.DlssPresetId), ConfigurationValue.DWord((int)NvidiaDrsSettingCodec.LatestPreset), ConfigurationValue.Missing(true)) };
        }
    }
    private static ConfigurationTarget Target(uint id) => new(ConfigurationTargetKind.NvidiaDrs, "base-profile", id.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
    private static uint Id(ConfigurationTarget target)
    {
        if (target.Path != "base-profile" || !uint.TryParse(target.Name, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var id)) throw new InvalidDataException("Alvo DRS inválido.");
        NvidiaDrsSettingCodec.ValidateId(id); return id;
    }
    internal static ConfigurationValue Read(ConfigurationTarget target)
    {
        uint id = Id(target);
        lock (gate) { using var driver = new Driver(); return driver.Read(id); }
    }
    internal static void Write(ConfigurationTarget target, ConfigurationValue value)
    {
        uint id = Id(target);
        if (value.Exists && value.Kind != (int)Microsoft.Win32.RegistryValueKind.DWord) throw new InvalidDataException("Somente valores DRS DWORD são aceitos.");
        lock (gate) { using var driver = new Driver(); driver.Write(id, value); }
    }

    private sealed class Driver : IDisposable
    {
        private IntPtr library, session, profile;
        private bool initialized;
        private readonly Query query;
        private readonly SessionCall destroy, save;
        private readonly GetSetting get;
        private readonly SetSetting set;
        private readonly SettingAction restore;
        private readonly PlainCall unload;
        internal Driver()
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("NVAPI DRS exige processo Windows x64.");
            string path = Path.Combine(Environment.SystemDirectory, "nvapi64.dll");
            if (!File.Exists(path)) throw new FileNotFoundException("O componente NVAPI do driver NVIDIA não está instalado.", path);
            library = LoadLibraryEx(path, IntPtr.Zero, 0x00000900); // DLL directory + System32, never the current directory.
            if (library == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "O Windows não carregou o NVAPI instalado no sistema.");
            try
            {
                IntPtr entry = GetProcAddress(library, "nvapi_QueryInterface");
                if (entry == IntPtr.Zero) throw new EntryPointNotFoundException("nvapi_QueryInterface ausente.");
                query = Marshal.GetDelegateForFunctionPointer<Query>(entry);
                unload = Function<PlainCall>(0xd22bdd7e);
                Check(Function<PlainCall>(0x0150e828)(), "Initialize"); initialized = true;
                destroy = Function<SessionCall>(0xdad9cff8); save = Function<SessionCall>(0xfcbc7e14);
                get = Function<GetSetting>(0x73bf8338); set = Function<SetSetting>(0x577dd202); restore = Function<SettingAction>(0x53f0381e);
                Check(Function<CreateSession>(0x0694d52e)(out session), "CreateSession");
                Check(Function<SessionCall>(0x375dbd6b)(session), "LoadSettings");
                Check(Function<GetProfile>(0xda8466a0)(session, out profile), "GetBaseProfile");
            }
            catch { Dispose(); throw; }
        }
        private T Function<T>(uint id) where T : Delegate
        {
            IntPtr address = query(id);
            if (address == IntPtr.Zero) throw new EntryPointNotFoundException($"O driver NVIDIA não fornece a interface 0x{id:X8}.");
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        internal bool HasRtx()
        {
            var handles = new IntPtr[64];
            Check(Function<EnumGpus>(0xe5ac921f)(handles, out uint count), "EnumPhysicalGPUs");
            if (count > handles.Length) throw new InvalidDataException("Quantidade de GPUs NVAPI inválida.");
            var fullName = Function<GpuName>(0xceee8e9f);
            for (int i = 0; i < count; i++)
            {
                byte[] name = new byte[64]; Check(fullName(handles[i], name), "GPU_GetFullName");
                int end = Array.IndexOf(name, (byte)0); if (end < 0) end = name.Length;
                if (Encoding.ASCII.GetString(name, 0, end).Contains("RTX", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        internal bool SupportsSettings()
        {
            uint[] ids = new uint[65_536]; uint count = (uint)ids.Length;
            Check(Function<EnumSettingIds>(0xf020614a)(ids, ref count), "EnumAvailableSettingIds");
            if (count > ids.Length) throw new InvalidDataException("Quantidade de ajustes NVAPI inválida.");
            return ids.AsSpan(0, (int)count).Contains(NvidiaDrsSettingCodec.DlssOverrideId) && ids.AsSpan(0, (int)count).Contains(NvidiaDrsSettingCodec.DlssPresetId);
        }
        internal ConfigurationValue Read(uint id)
        {
            byte[] data = NvidiaDrsSettingCodec.Create(id); IntPtr memory = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, memory, data.Length);
                int status = get(session, profile, id, memory);
                if (status == -160) return ConfigurationValue.Missing(true);
                Check(status, "GetSetting");
                Marshal.Copy(memory, data, 0, data.Length);
                return NvidiaDrsSettingCodec.Decode(data, id);
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        internal void Write(uint id, ConfigurationValue value)
        {
            if (!value.Exists) Check(restore(session, profile, id), "RestoreProfileDefaultSetting");
            else
            {
                uint number = unchecked((uint)JsonSerializer.Deserialize<int>(value.Json));
                byte[] data = NvidiaDrsSettingCodec.Create(id, number); IntPtr memory = Marshal.AllocHGlobal(data.Length);
                try { Marshal.Copy(data, 0, memory, data.Length); Check(set(session, profile, memory), "SetSetting"); }
                finally { Marshal.FreeHGlobal(memory); }
            }
            Check(save(session), "SaveSettings");
            // The engine opens a fresh session for readback; an in-memory session value is insufficient.
        }
        private static void Check(int status, string operation) { if (status != 0) throw new IOException($"NVIDIA NVAPI {operation} recusou a solicitação (status {status}). Nenhum ganho de desempenho foi presumido."); }
        public void Dispose()
        {
            if (session != IntPtr.Zero) { destroy?.Invoke(session); session = IntPtr.Zero; }
            if (initialized) { unload?.Invoke(); initialized = false; }
            if (library != IntPtr.Zero) { FreeLibrary(library); library = IntPtr.Zero; }
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Query(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PlainCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSession(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionCall(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetProfile(IntPtr session, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSetting(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSetting(IntPtr session, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SettingAction(IntPtr session, IntPtr profile, uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumGpus([Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 64)] IntPtr[] handles, out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GpuName(IntPtr gpu, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 64)] byte[] name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumSettingIds([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] uint[] ids, ref uint count);
    }
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);
}
