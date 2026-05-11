using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace Client.App.Win;

internal static class FontBootstrapper
{
    private const int WmFontChange = 0x001D;
    private const int HwndBroadcast = 0xFFFF;
    private const int SmtoAbortIfHung = 0x0002;
    private const uint FrPrivate = 0x10;

    private static readonly FontAsset[] Fonts =
    [
        new("BitcountPropDouble-Regular.ttf", ["Bitcount Prop Double", "Bitcount Prop Double Open Default", "Bitcount Prop Double Open Defau"]),
        new("Sekuya-Regular.ttf", ["Sekuya"]),
        new("BadScript-Regular.ttf", ["Bad Script"])
    ];

    private static readonly FontAsset[] ObsoleteFonts =
    [
        new("BitcountGridDouble-Regular.ttf", ["Bitcount Grid Double", "Bitcount Grid Double Open Default Upright"])
    ];

    public static string UserFontsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "Windows",
        "Fonts");

    public static string PixelFontPath => Path.Combine(UserFontsDirectory, "BitcountPropDouble-Regular.ttf");

    public static void EnsureInstalled()
    {
        Directory.CreateDirectory(UserFontsDirectory);
        RemoveObsoleteFonts();

        var changed = false;
        foreach (var font in Fonts)
        {
            var target = Path.Combine(UserFontsDirectory, font.FileName);
            if (WriteFontResource(font.FileName, target))
            {
                changed = true;
            }

            RegisterUserFont(font, target);
            _ = AddFontResource(target);
            _ = AddFontResourceEx(target, 0, IntPtr.Zero);
            _ = AddFontResourceEx(target, FrPrivate, IntPtr.Zero);
        }

        if (changed)
        {
            _ = SendMessageTimeout(
                new IntPtr(HwndBroadcast),
                WmFontChange,
                IntPtr.Zero,
                IntPtr.Zero,
                SmtoAbortIfHung,
                1000,
                out _);
        }
    }

    public static System.Windows.Media.FontFamily CreatePixelFontFamily()
    {
        var fileUri = new Uri(PixelFontPath).AbsoluteUri;

        return new System.Windows.Media.FontFamily(
            $"{fileUri}#Bitcount Prop Double," +
            $"{fileUri}#Bitcount Prop Double Open Default," +
            $"{fileUri}#Bitcount Prop Double Open Defau," +
            "Bitcount Prop Double," +
            "Bitcount Prop Double Open Default," +
            "Bitcount Prop Double Open Defau," +
            "./Assets/Fonts/#Bitcount Prop Double," +
            "./Assets/Fonts/#Bitcount Prop Double Open Default," +
            "./Assets/Fonts/#Bitcount Prop Double Open Defau");
    }

    private static bool WriteFontResource(string fileName, string target)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri($"/Assets/Fonts/{fileName}", UriKind.Relative));
        if (resource is null)
        {
            return false;
        }

        using var stream = resource.Stream;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();

        if (File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(bytes))
        {
            return false;
        }

        File.WriteAllBytes(target, bytes);
        return true;
    }

    private static void RegisterUserFont(FontAsset font, string target)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts");
        foreach (var familyName in font.FamilyNames)
        {
            key?.SetValue($"{familyName} (TrueType)", target, RegistryValueKind.String);
        }
    }

    private static void RemoveObsoleteFonts()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts");
        foreach (var font in ObsoleteFonts)
        {
            foreach (var familyName in font.FamilyNames)
            {
                key?.DeleteValue($"{familyName} (TrueType)", false);
            }

            var target = Path.Combine(UserFontsDirectory, font.FileName);
            if (File.Exists(target))
            {
                try
                {
                    File.Delete(target);
                }
                catch (IOException)
                {
                    // The old font can stay locked until the previous app process exits.
                }
                catch (UnauthorizedAccessException)
                {
                    // Per-user font cleanup is best-effort; the new font is still registered.
                }
            }
        }
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResource(string lpszFilename);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string name, uint fl, IntPtr res);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        int flags,
        int timeout,
        out IntPtr result);

    private sealed record FontAsset(string FileName, string[] FamilyNames);
}
