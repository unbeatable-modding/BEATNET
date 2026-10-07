using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BEATNET;

internal sealed class OnlineException : Exception
{
    internal string Code { get; }
    internal OnlineException(string code) : base(MessageFor(code)) => Code = code;
    private static string MessageFor(string code) => code switch
    {
        "invalid_credentials" => "Username or password is incorrect",
        "username_taken" => "This username is already taken",
        "invalid_username" => "Use a username with 1 to 32 Unicode characters",
        "unauthorized" => "Please log in again",
        "try_again_later" => "Too many requests / try again in a minute",
        "revision_changed" => "This beatmap has a new revision / old scores were removed",
        "beatmap_removed" => "This beatmap is no longer on BEATNET",
        "expired_request" => "Check your computer clock and try again",
        "invalid_region" => "Your game region is not available yet",
        "rating_unavailable" => "Play a difficulty and sync your score before rating",
        "invalid_rating" => "Choose a rating from 1 to 10",
        _ => "The BEATNET server could not complete this request",
    };
}

internal sealed class BeatNetOnlineClient : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly string pinPath;
    private readonly SemaphoreSlim keyLock = new(1);
    private readonly string address;
    private readonly string expectedKey;
    private JObject? publicKey;
    private const string DefaultAddress = "http://92.5.175.72";
    private const string ServerKey = "61bad2a97d926125a644d2470d364ce64dad18b0a99c5912a61b0377e236ecd8";

    internal BeatNetOnlineClient(string dataPath, string address = DefaultAddress, string? expectedKey = null)
    {
        this.address = address;
        this.expectedKey = expectedKey ?? ServerKey;
        pinPath = Path.Combine(dataPath, "BEATNET_data", "server-key.json");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BEATNET/1.0.0");
    }

    internal static string Hash(string value)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }

    internal static string Password(string name, string password)
    {
        if (password.Length < 8 || password.Length > 256)
        {
            throw new InvalidDataException("Use a password with 8 to 256 characters");
        }
        var salt = Encoding.UTF8.GetBytes("BEATNET account v1:" + name.Normalize(NormalizationForm.FormC));
        using var derived = new Rfc2898DeriveBytes(password, salt, 210000, HashAlgorithmName.SHA256);
        return BitConverter.ToString(derived.GetBytes(32)).Replace("-", "").ToLowerInvariant();
    }

    private async Task<JObject> Key(CancellationToken token)
    {
        await keyLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (publicKey != null) { return publicKey; }
            using var response = await http.GetAsync(address + "/api/online/key", token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var value = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var modulus = Convert.FromBase64String((string?)value["modulus"] ?? "");
            var exponent = Convert.FromBase64String((string?)value["exponent"] ?? "");
            if (modulus.Length != 384 || exponent.Length == 0 || exponent.Length > 4)
            {
                throw new InvalidDataException("The server returned an invalid account key");
            }
            if (Hash((string)value["modulus"]! + ":" + (string)value["exponent"]!) != expectedKey)
            {
                throw new InvalidDataException("The BEATNET server key does not match / login was blocked");
            }
            if (File.Exists(pinPath))
            {
                var pinned = JObject.Parse(File.ReadAllText(pinPath));
                if ((string?)pinned["modulus"] != (string?)value["modulus"] || (string?)pinned["exponent"] != (string?)value["exponent"])
                {
                    throw new InvalidDataException("The BEATNET server key changed / login was blocked");
                }
            }
            else { Save(pinPath, value); }
            publicKey = value;
            return value;
        }
        finally { keyLock.Release(); }
    }

    internal async Task<JObject> Send(JObject input, CancellationToken token)
    {
        var serverKey = await Key(token).ConfigureAwait(false);
        var key = new byte[64];
        using var random = RandomNumberGenerator.Create();
        random.GetBytes(key);
        input["requestId"] = Guid.NewGuid().ToString("N");
        input["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var envelope = Encrypt(input, key);
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Convert.FromBase64String((string)serverKey["modulus"]!),
            Exponent = Convert.FromBase64String((string)serverKey["exponent"]!),
        });
        envelope["key"] = Convert.ToBase64String(rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA1));
        using var body = new StringContent(envelope.ToString(Formatting.None), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(address + "/api/online", body, token).ConfigureAwait(false);
        if ((int)response.StatusCode == 429) { throw new OnlineException("try_again_later"); }
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (raw.Length > 1048576) { throw new InvalidDataException("The account response is too large"); }
        var result = Decrypt(JObject.Parse(raw), key);
        Array.Clear(key, 0, key.Length);
        if (result["error"] is JValue error) { throw new OnlineException((string?)error ?? "invalid_request"); }
        return result;
    }

    private static JObject Encrypt(JObject value, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key.Take(32).ToArray();
        aes.GenerateIV();
        using var cipher = aes.CreateEncryptor();
        var plain = Encoding.UTF8.GetBytes(value.ToString(Formatting.None));
        var data = cipher.TransformFinalBlock(plain, 0, plain.Length);
        using var hmac = new HMACSHA256(key.Skip(32).ToArray());
        var mac = hmac.ComputeHash(aes.IV.Concat(data).ToArray());
        return new JObject { ["iv"] = Convert.ToBase64String(aes.IV), ["data"] = Convert.ToBase64String(data), ["mac"] = Convert.ToBase64String(mac) };
    }

    private static JObject Decrypt(JObject value, byte[] key)
    {
        var iv = Convert.FromBase64String((string?)value["iv"] ?? "");
        var data = Convert.FromBase64String((string?)value["data"] ?? "");
        var mac = Convert.FromBase64String((string?)value["mac"] ?? "");
        using var hmac = new HMACSHA256(key.Skip(32).ToArray());
        var expected = hmac.ComputeHash(iv.Concat(data).ToArray());
        var difference = mac.Length ^ expected.Length;
        for (var i = 0; i < Math.Min(mac.Length, expected.Length); i++) { difference |= mac[i] ^ expected[i]; }
        if (difference != 0 || iv.Length != 16) { throw new InvalidDataException("The account response failed its integrity check"); }
        using var aes = Aes.Create();
        aes.Key = key.Take(32).ToArray();
        aes.IV = iv;
        using var cipher = aes.CreateDecryptor();
        return JObject.Parse(Encoding.UTF8.GetString(cipher.TransformFinalBlock(data, 0, data.Length)));
    }

    internal static void Save(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        BeatmapInstaller.CheckParents(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(value));
        if (File.Exists(path)) { File.Replace(temporary, path, null); }
        else { File.Move(temporary, path); }
    }

    public void Dispose() => http.Dispose();
}
