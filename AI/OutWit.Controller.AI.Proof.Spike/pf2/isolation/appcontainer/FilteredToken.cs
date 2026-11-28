using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using static OutWit.Controller.AI.Proof.Spike.Sandbox.NativeMethods;

namespace OutWit.Controller.AI.Proof.Spike.Sandbox;

/// <summary>
/// A token like the one User Account Control gives an administrator's programs: this process's own
/// token with the Administrators group for deny only, every privilege but change-notify removed, at
/// medium integrity. The AppContainer process is then started from it (CreateProcessAsUser), so the
/// container's low-box token derives from a standard user's token instead of an elevated one (the
/// hosted runner's account is the built-in Administrator; a workstation's user is not).
/// </summary>
internal static class FilteredToken
{
    #region Constants

    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint DISABLE_MAX_PRIVILEGE = 0x1;
    private const uint LUA_TOKEN = 0x4;
    private const int TOKEN_INTEGRITY_LEVEL = 25;
    private const uint SE_GROUP_INTEGRITY = 0x20;
    private const string MEDIUM_INTEGRITY = "S-1-16-8192";

    #endregion

    #region Functions

    /// <summary>
    /// Makes the filtered token.
    /// </summary>
    /// <returns>The token's handle (the caller closes it).</returns>
    /// <exception cref="Win32Exception">A step failed.</exception>
    public static IntPtr Create()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT, out var own))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");
        try
        {
            if (!CreateRestrictedToken(own, LUA_TOKEN | DISABLE_MAX_PRIVILEGE, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, out var filtered))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRestrictedToken");
            SetMediumIntegrity(filtered);
            return filtered;
        }
        finally
        {
            CloseHandle(own);
        }
    }

    /// <summary>
    /// The SID of this process's user, as a string.
    /// </summary>
    /// <returns>The SID.</returns>
    public static string UserSid()
    {
        return System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
    }

    private static void SetMediumIntegrity(IntPtr token)
    {
        if (!ConvertStringSidToSid(MEDIUM_INTEGRITY, out var sid))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertStringSidToSid");
        try
        {
            var size = Marshal.SizeOf<SID_AND_ATTRIBUTES>() + GetLengthSid(sid);
            var label = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(new SID_AND_ATTRIBUTES { Sid = sid, Attributes = SE_GROUP_INTEGRITY }, label, false);
                if (!SetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, label, size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(TokenIntegrityLevel)");
            }
            finally
            {
                Marshal.FreeHGlobal(label);
            }
        }
        finally
        {
            LocalFree(sid);
        }
    }

    #endregion

    #region Native

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CreateRestrictedToken(IntPtr existingToken, uint flags, uint disableSidCount, IntPtr sidsToDisable,
        uint deletePrivilegeCount, IntPtr privilegesToDelete, uint restrictedSidCount, IntPtr sidsToRestrict, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertStringSidToSidW")]
    private static extern bool ConvertStringSidToSid(string text, out IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern int GetLengthSid(IntPtr sid);

    /// <summary>CreateProcessAsUserW with an extended startup information.</summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateProcessAsUserW")]
    public static extern bool CreateProcessAsUser(IntPtr token, string? applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string currentDirectory, ref STARTUPINFOEX startupInfo, out PROCESS_INFORMATION processInformation);

    #endregion
}
