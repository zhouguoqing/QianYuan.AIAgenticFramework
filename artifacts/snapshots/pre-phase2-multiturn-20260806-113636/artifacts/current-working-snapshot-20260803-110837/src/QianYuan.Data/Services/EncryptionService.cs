using System.Security.Cryptography;
using System.Text;

namespace QianYuan.Data.Services;

/// <summary>
/// 用于加密和解密敏感数据（如 CLI 凭证）
/// </summary>
public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

/// <summary>
/// 使用 AES 的加密服务实现
/// </summary>
public class AesEncryptionService : IEncryptionService
{
    private readonly string _encryptionKey;

    /// <summary>
    /// 初始化加密服务
    /// </summary>
    /// <param name="encryptionKey">32 字节的加密密钥（Base64 编码）</param>
    public AesEncryptionService(string encryptionKey)
    {
        if (string.IsNullOrEmpty(encryptionKey))
            throw new ArgumentException("Encryption key cannot be empty", nameof(encryptionKey));

        _encryptionKey = encryptionKey;
    }

    /// <summary>
    /// 加密字符串
    /// </summary>
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            byte[] key = Convert.FromBase64String(_encryptionKey);

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    // 先写入 IV，再写入加密数据
                    ms.Write(aes.IV, 0, aes.IV.Length);

                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs))
                    {
                        sw.Write(plainText);
                    }

                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Encryption failed", ex);
        }
    }

    /// <summary>
    /// 解密字符串
    /// </summary>
    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        try
        {
            byte[] key = Convert.FromBase64String(_encryptionKey);
            byte[] buffer = Convert.FromBase64String(cipherText);

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                // 前 16 字节是 IV
                int ivLength = aes.IV.Length;
                byte[] iv = new byte[ivLength];
                Array.Copy(buffer, 0, iv, 0, ivLength);
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream(buffer, ivLength, buffer.Length - ivLength))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs))
                {
                    return sr.ReadToEnd();
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Decryption failed", ex);
        }
    }

    /// <summary>
    /// 生成一个新的随机加密密钥
    /// </summary>
    public static string GenerateEncryptionKey()
    {
        using (var aes = Aes.Create())
        {
            aes.KeySize = 256; // 32 字节
            aes.GenerateKey();
            return Convert.ToBase64String(aes.Key);
        }
    }
}
