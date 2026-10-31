using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Happyland.Launcher.Core;

public sealed class SkinCredentialStore(string root)
{
    public Task<SkinSession?> ReadAsync(string server, string login, string uuid, CancellationToken ct) => RunAsync(() =>
    {
        var text = Read(Key(server, login, uuid));
        if (text is null) return null;
        var session = JsonSerializer.Deserialize<SkinSession>(text);
        if (session?.Account is null || string.IsNullOrWhiteSpace(session.Account.AccessToken) ||
            string.IsNullOrWhiteSpace(session.Account.Uuid) || string.IsNullOrWhiteSpace(session.ClientToken))
            throw new JsonException("保存的登录状态无效。");
        return session;
    }, ct);

    public Task WriteAsync(string server, string login, SkinSession session, CancellationToken ct) => RunAsync(() =>
    {
        Write(Key(server, login, session.Account.Uuid), JsonSerializer.Serialize(session));
        return true;
    }, ct);

    public Task DeleteAsync(string server, string login, string uuid, CancellationToken ct) => RunAsync(() =>
    {
        Delete(Key(server, login, uuid));
        return true;
    }, ct);

    private string Key(string server, string login, string uuid) => "HAPPYLAND/skin/" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root) + "\0" + server.TrimEnd('/') + "\0" + login + "\0" + uuid)));

    private static async Task<T> RunAsync<T>(Func<T> operation, CancellationToken ct)
    {
        try { return await Task.Run(operation, ct).WaitAsync(ct); }
        catch (Exception e) when (e is Win32Exception or ExternalException or DllNotFoundException or EntryPointNotFoundException)
        { throw new CredentialStorageException("无法访问系统凭据库，请解锁后重试。", e); }
    }

    private static string? Read(string key) => OperatingSystem.IsWindows() ? Windows.Read(key) :
        OperatingSystem.IsMacOS() ? Mac.Read(key) : Linux.Access(key, null, false);
    private static void Write(string key, string value)
    {
        if (OperatingSystem.IsWindows()) Windows.Write(key, value);
        else if (OperatingSystem.IsMacOS()) Mac.Write(key, value);
        else Linux.Access(key, value, false);
    }
    private static void Delete(string key)
    {
        if (OperatingSystem.IsWindows()) Windows.Delete(key);
        else if (OperatingSystem.IsMacOS()) Mac.Delete(key);
        else Linux.Access(key, null, true);
    }

    private static class Windows
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags, Type;
            public string? TargetName, Comment;
            public long LastWritten;
            public uint BlobSize;
            public IntPtr Blob;
            public uint Persist, AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias, UserName;
        }

        public static string? Read(string key)
        {
            if (!CredRead(key, 1, 0, out var pointer))
            {
                var code = Marshal.GetLastWin32Error();
                if (code == 1168) return null;
                throw new Win32Exception(code);
            }
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                var bytes = new byte[credential.BlobSize];
                Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
                try { return Encoding.UTF8.GetString(bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            finally { CredFree(pointer); }
        }

        public static void Write(string key, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new Credential { Type = 1, TargetName = key, UserName = "HAPPYLAND Launcher", BlobSize = (uint)bytes.Length, Blob = blob, Persist = 2 };
                if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                Marshal.FreeCoTaskMem(blob);
            }
        }

        public static void Delete(string key)
        {
            if (!CredDelete(key, 1, 0) && Marshal.GetLastWin32Error() != 1168)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr pointer);
    }

    private static class Mac
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private static readonly byte[] Service = Encoding.UTF8.GetBytes("cc.happyland.launcher");

        private static (int Code, IntPtr Item, string? Value) Find(string key)
        {
            var account = Encoding.UTF8.GetBytes(key);
            var code = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out var length, out var data, out var item);
            try { return (code, item, code == 0 ? Marshal.PtrToStringUTF8(data, (int)length) : null); }
            finally { if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data); }
        }
        public static string? Read(string key)
        {
            var found = Find(key);
            try { if (found.Code == -25300) return null; Check(found.Code); return found.Value; }
            finally { if (found.Item != IntPtr.Zero) CFRelease(found.Item); }
        }
        public static void Write(string key, string value)
        {
            var found = Find(key);
            var item = found.Item;
            var bytes = Encoding.UTF8.GetBytes(value);
            try
            {
                if (found.Code == -25300)
                {
                    var account = Encoding.UTF8.GetBytes(key);
                    Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, (uint)bytes.Length, bytes, out item));
                }
                else { Check(found.Code); Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes)); }
            }
            finally { CryptographicOperations.ZeroMemory(bytes); if (item != IntPtr.Zero) CFRelease(item); }
        }
        public static void Delete(string key)
        {
            var found = Find(key);
            try { if (found.Code == -25300) return; Check(found.Code); Check(SecKeychainItemDelete(found.Item)); }
            finally { if (found.Item != IntPtr.Zero) CFRelease(found.Item); }
        }
        private static void Check(int code) { if (code != 0) throw new ExternalException("Keychain operation failed.", code); }
        [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint passwordLength, out IntPtr password, out IntPtr item);
        [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint passwordLength, byte[] password, out IntPtr item);
        [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);
        [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
        [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);
    }

    private static class Linux
    {
        private const string KeyringLibrary = "libsecret-1.so.0";
        private const string Glib = "libglib-2.0.so.0";
        public static string? Access(string key, string? value, bool delete)
        {
            var library = NativeLibrary.Load(Glib);
            var attributes = g_hash_table_new(NativeLibrary.GetExport(library, "g_str_hash"), NativeLibrary.GetExport(library, "g_str_equal"));
            var pointers = new[] { Marshal.StringToCoTaskMemUTF8("application"), Marshal.StringToCoTaskMemUTF8("cc.happyland.launcher"), Marshal.StringToCoTaskMemUTF8("account"), Marshal.StringToCoTaskMemUTF8(key) };
            try
            {
                g_hash_table_insert(attributes, pointers[0], pointers[1]);
                g_hash_table_insert(attributes, pointers[2], pointers[3]);
                IntPtr error;
                if (delete) secret_password_clearv_sync(IntPtr.Zero, attributes, IntPtr.Zero, out error);
                else if (value is not null)
                {
                    var stored = secret_password_storev_sync(IntPtr.Zero, attributes, null, "HAPPYLAND Launcher", value, IntPtr.Zero, out error);
                    Check(error);
                    if (stored == 0) throw new ExternalException("Secret Service did not save the login state.");
                    return null;
                }
                else
                {
                    var password = secret_password_lookupv_sync(IntPtr.Zero, attributes, IntPtr.Zero, out error);
                    try { Check(error); return Marshal.PtrToStringUTF8(password); }
                    finally { if (password != IntPtr.Zero) secret_password_free(password); }
                }
                Check(error);
                return null;
            }
            finally
            {
                g_hash_table_unref(attributes);
                foreach (var pointer in pointers) Marshal.FreeCoTaskMem(pointer);
                NativeLibrary.Free(library);
            }
        }
        private static void Check(IntPtr error)
        {
            if (error == IntPtr.Zero) return;
            g_error_free(error);
            throw new ExternalException("Secret Service operation failed.");
        }
        [DllImport(Glib)] private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);
        [DllImport(Glib)] private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);
        [DllImport(Glib)] private static extern void g_hash_table_unref(IntPtr table);
        [DllImport(Glib)] private static extern void g_error_free(IntPtr error);
        [DllImport(KeyringLibrary)] private static extern int secret_password_storev_sync(IntPtr schema, IntPtr attributes, [MarshalAs(UnmanagedType.LPUTF8Str)] string? collection, [MarshalAs(UnmanagedType.LPUTF8Str)] string label, [MarshalAs(UnmanagedType.LPUTF8Str)] string password, IntPtr cancellable, out IntPtr error);
        [DllImport(KeyringLibrary)] private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
        [DllImport(KeyringLibrary)] private static extern int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
        [DllImport(KeyringLibrary)] private static extern void secret_password_free(IntPtr password);
    }
}

public sealed class CredentialStorageException(string message, Exception inner) : Exception(message, inner);
