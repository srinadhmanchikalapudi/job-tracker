using System.Runtime.InteropServices;
using System.Text;

namespace JobTracker.Core.Ai;

/// <summary>Where the person's API key lives. It is never written to settings.json or any other file of the app.</summary>
public interface ISecretStore
{
    string? Get(string name);
    void Set(string name, string secret);
    void Remove(string name);
}

/// <summary>Windows Credential Manager (the same place Windows keeps saved passwords), for this user on this PC only.</summary>
public sealed class WindowsCredentialStore : ISecretStore
{
    private const uint TypeGeneric = 1;
    private const uint PersistLocalMachine = 2; // stays on this PC, is not copied to other devices

    private const string Prefix = "JobTracker/";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public string? Get(string name)
    {
        if (!CredRead(Prefix + name, TypeGeneric, 0, out var pointer))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                return null;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Set(string name, string secret)
    {
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = TypeGeneric,
                TargetName = Prefix + name,
                Comment = "API key saved by Job Tracker",
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = "JobTracker",
            };
            if (!CredWrite(ref credential, 0))
                throw new IOException($"Windows would not save the key (error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Remove(string name) => CredDelete(Prefix + name, TypeGeneric, 0);
}

/// <summary>For tests.</summary>
public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = [];

    public string? Get(string name) => _secrets.GetValueOrDefault(name);
    public void Set(string name, string secret) => _secrets[name] = secret;
    public void Remove(string name) => _secrets.Remove(name);
}
