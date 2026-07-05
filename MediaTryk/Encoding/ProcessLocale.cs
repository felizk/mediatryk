using System.Diagnostics;

namespace MediaTryk.Encoding;

/// <summary>
/// Forces child processes onto a UTF-8 locale. HandBrakeCLI and mkvmerge decode
/// their command-line arguments according to the locale charset; under a non-UTF-8
/// locale (e.g. the C/POSIX locale a container or service commonly inherits) a
/// filename such as "… Senpai ♥ …" is truncated at the first multibyte character,
/// so the tool tries to open a path that doesn't exist and fails. C.UTF-8 is a
/// built-in glibc locale, so it needs no locale generation in the base image.
/// </summary>
public static class ProcessLocale
{
    public static void UseUtf8(ProcessStartInfo startInfo)
    {
        // LC_ALL wins over LANG and every other LC_* setting the parent passed down.
        startInfo.Environment["LC_ALL"] = "C.UTF-8";
        startInfo.Environment["LANG"] = "C.UTF-8";
    }
}
