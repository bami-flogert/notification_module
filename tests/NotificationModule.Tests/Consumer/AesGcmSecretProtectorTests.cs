using System.Text;
using NotificationModule.Consumer.Secrets;

namespace NotificationModule.Tests.Consumer;

public sealed class AesGcmSecretProtectorTests
{
    [Fact]
    public void Encrypt_decrypt_round_trips_plaintext()
    {
        var protector = new AesGcmSecretProtector(TestDb.CreateConfiguration());
        var plaintext = Encoding.UTF8.GetBytes("""{"apiKey":"test-key"}""");

        var (ciphertext, nonce) = protector.Encrypt(plaintext);
        var decrypted = protector.Decrypt(ciphertext, nonce);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Constructor_throws_when_master_key_is_not_32_bytes()
    {
        var config = TestDb.CreateConfiguration(values =>
        {
            values["Secrets:MasterKeyBase64"] = Convert.ToBase64String(new byte[16]);
        });

        Assert.Throws<InvalidOperationException>(() => new AesGcmSecretProtector(config));
    }
}
