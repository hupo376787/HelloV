using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace HelloV.Desktop;

internal sealed record DirectShowCameraDevice(string Id, string DisplayName, int Index);

internal static class WindowsDirectShowCameraEnumerator
{
    private static readonly Guid SystemDeviceEnumeratorClsid =
        new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");

    private static readonly Guid VideoInputDeviceCategory =
        new("860BB310-5D01-11D0-BD3B-00A0C911CE86");

    private static readonly Guid PropertyBagIid =
        new("55272A00-42CB-11CE-8135-00AA004BB851");

    public static IReadOnlyList<DirectShowCameraDevice> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<DirectShowCameraDevice>();

        object? deviceEnumeratorObject = null;
        IEnumMoniker? monikerEnumerator = null;
        var cameras = new List<DirectShowCameraDevice>();

        try
        {
            var type = Type.GetTypeFromCLSID(SystemDeviceEnumeratorClsid, throwOnError: true)!;
            deviceEnumeratorObject = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("无法创建 DirectShow 设备枚举器。");

            var deviceEnumerator = (ICreateDevEnum)deviceEnumeratorObject;
            var category = VideoInputDeviceCategory;
            var result = deviceEnumerator.CreateClassEnumerator(
                ref category,
                out monikerEnumerator,
                flags: 0);

            // S_FALSE means the category exists but contains no devices.
            if (result != 0 || monikerEnumerator is null)
                return Array.Empty<DirectShowCameraDevice>();

            var monikers = new IMoniker[1];
            var index = 0;
            while (monikerEnumerator.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var moniker = monikers[0];
                try
                {
                    var friendlyName = ReadProperty(moniker, "FriendlyName")
                                       ?? ReadProperty(moniker, "Description")
                                       ?? $"摄像头 {index + 1}";
                    var stableId = ReadProperty(moniker, "DevicePath")
                                   ?? GetDisplayName(moniker)
                                   ?? $"directshow:{index}";

                    cameras.Add(new DirectShowCameraDevice(stableId, friendlyName, index));
                    index++;
                }
                finally
                {
                    ReleaseComObject(moniker);
                    monikers[0] = null!;
                }
            }
        }
        finally
        {
            ReleaseComObject(monikerEnumerator);
            ReleaseComObject(deviceEnumeratorObject);
        }

        // A few drivers publish the same moniker more than once. Keep one entry per device path.
        return cameras
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string? ReadProperty(IMoniker moniker, string propertyName)
    {
        object? propertyBagObject = null;
        try
        {
            var iid = PropertyBagIid;
            moniker.BindToStorage(null!, null!, ref iid, out var boundObject);
            propertyBagObject = boundObject;
            var propertyBag = (IPropertyBag)boundObject;
            return propertyBag.Read(propertyName, out var value, IntPtr.Zero) == 0
                ? value?.ToString()
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(propertyBagObject);
        }
    }

    private static string? GetDisplayName(IMoniker moniker)
    {
        IBindCtx? bindContext = null;
        try
        {
            if (CreateBindCtx(0, out bindContext) != 0 || bindContext is null)
                return null;

            moniker.GetDisplayName(bindContext, null, out var displayName);
            return displayName;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(bindContext);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx bindContext);

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(
            [In] ref Guid deviceClass,
            [Out] out IEnumMoniker? enumMoniker,
            int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read(
            [MarshalAs(UnmanagedType.LPWStr)] string propertyName,
            [MarshalAs(UnmanagedType.Struct)] out object value,
            IntPtr errorLog);

        [PreserveSig]
        int Write(
            [MarshalAs(UnmanagedType.LPWStr)] string propertyName,
            [In, MarshalAs(UnmanagedType.Struct)] ref object value);
    }
}
