using System.Security.Cryptography;
using System.Text;
using UploaderCore;

namespace Uploader;

/// <summary>Kimlik bilgisini Windows veri koruma hizmetiyle (DPAPI) şifreleyip kullanıcının yerel klasöründe saklar; yalnızca aynı Windows kullanıcısı çözebilir.</summary>
internal sealed class SecretStore : ICredentialStore
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommonsDiversifiedUploader", "credentials.bin");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("tr.tall.CommonsDiversifiedUploader.commons-auth");

    public string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception) { return null; }
    }

    public void Save(string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        try { File.Delete(FilePath); } catch (Exception) { }
    }
}
