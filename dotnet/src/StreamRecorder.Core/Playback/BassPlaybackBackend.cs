using System.Runtime.InteropServices;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Playback;

internal sealed class BassPlaybackBackend : IDisposable
{
    private const uint BassUnicode = 0x80000000;
    private const uint BassStreamBlock = 0x00100000;
    private const uint BassSampleFloat = 0x00000100;
    private const uint BassDeviceEnabled = 1;
    private const int BassConfigNetTimeout = 11;
    private const int BassConfigNetReadTimeout = 37;
    private const int BassConfigNetPlaylist = 21;
    private const int BassNetworkTimeoutMilliseconds = 30000;
    private const int BassNetworkReadTimeoutMilliseconds = 30000;

    private BassNative? native;
    private string? loadedPath;
    private string? initializedDeviceId;

    public bool IsLoaded => native is not null;

    public string? LoadedPath => loadedPath;

    public bool TryLoad(string dllPath, string? aacPluginPath, out string error)
    {
        if (native is null)
        {
            if (!File.Exists(dllPath))
            {
                error = $"Nie znaleziono biblioteki odsłuchu: {dllPath}";
                return false;
            }

            if (!BassNative.TryLoad(dllPath, out var loaded, out error))
            {
                return false;
            }

            native = loaded;
            loadedPath = dllPath;
        }

        if (!string.IsNullOrWhiteSpace(aacPluginPath) && File.Exists(aacPluginPath)
            && !native!.HasPlugin(aacPluginPath!))
        {
            if (!native.TryLoadPlugin(aacPluginPath!, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public IReadOnlyList<AudioOutputDevice> GetDevices(string dllPath, string defaultDeviceName)
    {
        if (!TryLoad(dllPath, null, out _))
        {
            return [AudioOutputDevice.SystemDefault(defaultDeviceName)];
        }

        var result = new List<AudioOutputDevice>
        {
            AudioOutputDevice.SystemDefault(defaultDeviceName),
        };

        foreach (var device in native!.GetDevices())
        {
            result.Add(device);
        }

        return result;
    }

    public void EnsureInitialized(string deviceId)
    {
        if (native is null)
        {
            throw new InvalidOperationException("Backend odsłuchu nie został załadowany.");
        }

        if (native.IsInitialized && string.Equals(initializedDeviceId, deviceId, StringComparison.Ordinal))
        {
            return;
        }

        if (native.IsInitialized)
        {
            native.Free();
        }

        var deviceIndex = native.FindDeviceIndex(deviceId);
        if (!native.Initialize(deviceIndex, BassNetworkTimeoutMilliseconds, BassNetworkReadTimeoutMilliseconds, out var error))
        {
            throw new InvalidOperationException($"Nie udało się uruchomić wyjścia audio: {error}");
        }

        initializedDeviceId = deviceId;
    }

    public uint CreateStream(Station station)
    {
        if (native is null || !native.IsInitialized)
        {
            throw new InvalidOperationException("Wyjście audio nie jest zainicjalizowane.");
        }

        var url = BuildPlaybackUrl(station);
        var stream = native.CreateUrl(url, BassUnicode | BassStreamBlock | BassSampleFloat, out var errorCode);
        if (stream == 0)
        {
            throw new BassPlaybackException("Nie udało się otworzyć strumienia odsłuchu.", errorCode);
        }

        return stream;
    }

    public void Play(uint stream)
    {
        if (native is null || !native.Play(stream))
        {
            var errorCode = native?.GetErrorCode() ?? 0;
            throw new BassPlaybackException("Nie udało się rozpocząć odtwarzania.", errorCode);
        }
    }

    public int GetActiveState(uint stream)
    {
        return native?.GetActiveState(stream) ?? 0;
    }

    public void FreeStream(uint stream)
    {
        native?.StopAndFree(stream);
    }

    public string DescribeError(Exception exception)
    {
        return exception is BassPlaybackException bassException
            ? $"{bassException.Message} (kod BASS: {bassException.ErrorCode})"
            : exception.Message;
    }

    public void Dispose()
    {
        native?.Dispose();
        native = null;
        loadedPath = null;
        initializedDeviceId = null;
    }

    private static string BuildPlaybackUrl(Station station)
    {
        if (station.Credentials is null || string.IsNullOrWhiteSpace(station.Credentials.Username))
        {
            return station.Url;
        }

        var builder = new UriBuilder(station.Url)
        {
            UserName = station.Credentials.Username,
            Password = station.Credentials.Password,
        };
        return builder.Uri.AbsoluteUri;
    }

    private sealed class BassPlaybackException : Exception
    {
        public BassPlaybackException(string message, int errorCode)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public int ErrorCode { get; }
    }

    private sealed class BassNative : IDisposable
    {
        private readonly IntPtr library;
        private readonly BassInitDelegate bassInit;
        private readonly BassFreeDelegate bassFree;
        private readonly BassGetDeviceInfoDelegate bassGetDeviceInfo;
        private readonly BassSetConfigDelegate bassSetConfig;
        private readonly BassStreamCreateUrlDelegate bassStreamCreateUrl;
        private readonly BassChannelPlayDelegate bassChannelPlay;
        private readonly BassChannelIsActiveDelegate bassChannelIsActive;
        private readonly BassChannelStopDelegate bassChannelStop;
        private readonly BassStreamFreeDelegate bassStreamFree;
        private readonly BassErrorGetCodeDelegate bassErrorGetCode;
        private readonly BassPluginLoadDelegate bassPluginLoad;
        private readonly BassPluginFreeDelegate bassPluginFree;
        private readonly Dictionary<string, uint> loadedPlugins = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IntPtr> loadedPluginLibraries = new(StringComparer.OrdinalIgnoreCase);
        private BassStreamCreateUrlDelegate? bassAacStreamCreateUrl;
        private bool disposed;

        private BassNative(
            IntPtr library,
            BassInitDelegate bassInit,
            BassFreeDelegate bassFree,
            BassGetDeviceInfoDelegate bassGetDeviceInfo,
            BassSetConfigDelegate bassSetConfig,
            BassStreamCreateUrlDelegate bassStreamCreateUrl,
            BassChannelPlayDelegate bassChannelPlay,
            BassChannelIsActiveDelegate bassChannelIsActive,
            BassChannelStopDelegate bassChannelStop,
            BassStreamFreeDelegate bassStreamFree,
            BassErrorGetCodeDelegate bassErrorGetCode,
            BassPluginLoadDelegate bassPluginLoad,
            BassPluginFreeDelegate bassPluginFree)
        {
            this.library = library;
            this.bassInit = bassInit;
            this.bassFree = bassFree;
            this.bassGetDeviceInfo = bassGetDeviceInfo;
            this.bassSetConfig = bassSetConfig;
            this.bassStreamCreateUrl = bassStreamCreateUrl;
            this.bassChannelPlay = bassChannelPlay;
            this.bassChannelIsActive = bassChannelIsActive;
            this.bassChannelStop = bassChannelStop;
            this.bassStreamFree = bassStreamFree;
            this.bassErrorGetCode = bassErrorGetCode;
            this.bassPluginLoad = bassPluginLoad;
            this.bassPluginFree = bassPluginFree;
        }

        public bool IsInitialized { get; private set; }

        public bool HasPlugin(string path)
        {
            return loadedPlugins.ContainsKey(Path.GetFullPath(path));
        }

        public bool TryLoadPlugin(string path, out string error)
        {
            var fullPath = Path.GetFullPath(path);
            if (loadedPlugins.ContainsKey(fullPath))
            {
                error = string.Empty;
                return true;
            }

            var pathPointer = Marshal.StringToHGlobalUni(fullPath);
            try
            {
                var handle = bassPluginLoad(pathPointer, BassUnicode);
                if (handle == 0)
                {
                    error = $"Nie udało się załadować dodatku odsłuchu {fullPath} (kod BASS: {GetErrorCode()}).";
                    return false;
                }

                loadedPlugins[fullPath] = handle;
                var pluginLibrary = LoadLibrary(fullPath);
                if (pluginLibrary != IntPtr.Zero)
                {
                    var address = GetProcAddress(pluginLibrary, "BASS_AAC_StreamCreateURL");
                    if (address != IntPtr.Zero)
                    {
                        bassAacStreamCreateUrl = (BassStreamCreateUrlDelegate)(object)Marshal.GetDelegateForFunctionPointer(
                            address,
                            typeof(BassStreamCreateUrlDelegate));
                        loadedPluginLibraries[fullPath] = pluginLibrary;
                    }
                    else
                    {
                        FreeLibrary(pluginLibrary);
                    }
                }

                error = string.Empty;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(pathPointer);
            }
        }

        public static bool TryLoad(string path, out BassNative? result, out string error)
        {
            result = null;
            var library = LoadLibrary(path);
            if (library == IntPtr.Zero)
            {
                error = $"Nie udało się załadować {path} (kod systemu: {Marshal.GetLastWin32Error()}).";
                return false;
            }

            try
            {
                result = new BassNative(
                    library,
                    GetDelegate<BassInitDelegate>(library, "BASS_Init"),
                    GetDelegate<BassFreeDelegate>(library, "BASS_Free"),
                    GetDelegate<BassGetDeviceInfoDelegate>(library, "BASS_GetDeviceInfo"),
                    GetDelegate<BassSetConfigDelegate>(library, "BASS_SetConfig"),
                    GetDelegate<BassStreamCreateUrlDelegate>(library, "BASS_StreamCreateURL"),
                    GetDelegate<BassChannelPlayDelegate>(library, "BASS_ChannelPlay"),
                    GetDelegate<BassChannelIsActiveDelegate>(library, "BASS_ChannelIsActive"),
                    GetDelegate<BassChannelStopDelegate>(library, "BASS_ChannelStop"),
                    GetDelegate<BassStreamFreeDelegate>(library, "BASS_StreamFree"),
                    GetDelegate<BassErrorGetCodeDelegate>(library, "BASS_ErrorGetCode"),
                    GetDelegate<BassPluginLoadDelegate>(library, "BASS_PluginLoad"),
                    GetDelegate<BassPluginFreeDelegate>(library, "BASS_PluginFree"));
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                FreeLibrary(library);
                error = $"Biblioteka BASS nie udostępnia wymaganych funkcji: {exception.Message}";
                return false;
            }
        }

        public IReadOnlyList<AudioOutputDevice> GetDevices()
        {
            var result = new List<AudioOutputDevice>();
            for (var index = 1; index < 128; index++)
            {
                var info = new BassDeviceInfoNative();
                if (!bassGetDeviceInfo(index, ref info))
                {
                    break;
                }

                if ((info.Flags & BassDeviceEnabled) == 0)
                {
                    continue;
                }

                var name = PtrToString(info.Name) ?? $"Urządzenie {index}";
                var driver = PtrToString(info.Driver) ?? string.Empty;
                var id = string.IsNullOrWhiteSpace(driver) ? $"index:{index}" : driver;
                if (result.Any(device => string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase)))
                {
                    id = $"{id}|index:{index}";
                }

                result.Add(new AudioOutputDevice(id, name, driver, index, isSystemDefault: false, isAvailable: true));
            }

            return result;
        }

        public int FindDeviceIndex(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return -1;
            }

            foreach (var device in GetDevices())
            {
                if (string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return device.Index;
                }
            }

            var indexMarker = deviceId.LastIndexOf("|index:", StringComparison.OrdinalIgnoreCase);
            var value = indexMarker >= 0 ? deviceId.Substring(indexMarker + 7) : deviceId;
            return value.StartsWith("index:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value.Substring(6), out var parsed)
                ? parsed
                : -1;
        }

        public bool Initialize(int deviceIndex, int networkTimeout, int networkReadTimeout, out string error)
        {
            if (!bassInit(deviceIndex, 44100, 0, IntPtr.Zero, IntPtr.Zero))
            {
                error = $"BASS_Init zwrócił kod {GetErrorCode()}.";
                return false;
            }

            IsInitialized = true;
            _ = bassSetConfig(BassConfigNetTimeout, networkTimeout);
            _ = bassSetConfig(BassConfigNetReadTimeout, networkReadTimeout);
            _ = bassSetConfig(BassConfigNetPlaylist, 1);
            error = string.Empty;
            return true;
        }

        public uint CreateUrl(string url, uint flags, out int errorCode)
        {
            var urlPointer = Marshal.StringToHGlobalUni(url);
            try
            {
                var stream = bassStreamCreateUrl(urlPointer, 0, flags, IntPtr.Zero, IntPtr.Zero);
                errorCode = GetErrorCode();
                if (stream != 0 || bassAacStreamCreateUrl is null)
                {
                    return stream;
                }

                stream = bassAacStreamCreateUrl(urlPointer, 0, flags, IntPtr.Zero, IntPtr.Zero);
                errorCode = GetErrorCode();
                return stream;
            }
            finally
            {
                Marshal.FreeHGlobal(urlPointer);
            }
        }

        public bool Play(uint stream)
        {
            return bassChannelPlay(stream, restart: false);
        }

        public int GetActiveState(uint stream)
        {
            return bassChannelIsActive(stream);
        }

        public void StopAndFree(uint stream)
        {
            _ = bassChannelStop(stream);
            _ = bassStreamFree(stream);
        }

        public int GetErrorCode()
        {
            return bassErrorGetCode();
        }

        public void Free()
        {
            if (!IsInitialized)
            {
                return;
            }

            _ = bassFree();
            IsInitialized = false;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (var pluginHandle in loadedPlugins.Values)
            {
                _ = bassPluginFree(pluginHandle);
            }

            loadedPlugins.Clear();
            foreach (var pluginLibrary in loadedPluginLibraries.Values)
            {
                FreeLibrary(pluginLibrary);
            }

            loadedPluginLibraries.Clear();
            bassAacStreamCreateUrl = null;
            Free();
            FreeLibrary(library);
        }

        private static T GetDelegate<T>(IntPtr library, string name)
            where T : class
        {
            var address = GetProcAddress(library, name);
            if (address == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException(name);
            }

            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        private static string? PtrToString(IntPtr value)
        {
            return value == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(value);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BassDeviceInfoNative
        {
            public IntPtr Name;
            public IntPtr Driver;
            public uint Flags;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool BassInitDelegate(int device, uint frequency, uint flags, IntPtr window, IntPtr clsid);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool BassFreeDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool BassGetDeviceInfoDelegate(int device, ref BassDeviceInfoNative info);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int BassSetConfigDelegate(int option, int value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint BassStreamCreateUrlDelegate(IntPtr url, uint offset, uint flags, IntPtr downloadProcedure, IntPtr user);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool BassChannelPlayDelegate(uint channel, [MarshalAs(UnmanagedType.Bool)] bool restart);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int BassChannelIsActiveDelegate(uint channel);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool BassChannelStopDelegate(uint channel);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool BassStreamFreeDelegate(uint channel);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int BassErrorGetCodeDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint BassPluginLoadDelegate(IntPtr file, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool BassPluginFreeDelegate(uint handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);
    }
}
