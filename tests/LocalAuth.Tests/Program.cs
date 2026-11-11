using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LocalAuth.Core;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { Check(true, name); return; }
    throw new Exception("FAIL: expected " + typeof(T).Name + ": " + name);
}

var times = new long[] { 59, 1111111109, 1111111111, 1234567890, 2000000000, 20000000000 };
var vectors = new[]
{
    ("SHA1", "12345678901234567890", new[] { "94287082", "07081804", "14050471", "89005924", "69279037", "65353130" }),
    ("SHA256", "12345678901234567890123456789012", new[] { "46119246", "68084774", "67062674", "91819424", "90698825", "77737706" }),
    ("SHA512", "1234567890123456789012345678901234567890123456789012345678901234", new[] { "90693936", "25091201", "99943326", "93441116", "38618901", "47863826" })
};
foreach (var (algorithm, key, expected) in vectors)
{
    var account = new Account { Issuer = "RFC6238", Secret = Base32.Encode(Encoding.ASCII.GetBytes(key)), Algorithm = algorithm, Digits = 8 }.Normalize();
    for (var i = 0; i < times.Length; i++) Check(Totp.Generate(account, DateTimeOffset.FromUnixTimeSeconds(times[i])) == expected[i], $"RFC6238 {algorithm} at {times[i]}");
}
var sample = new Account { Issuer = "Example", Name = "person@example.test", Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ" }.Normalize();
Check(Totp.Generate(sample, DateTimeOffset.FromUnixTimeSeconds(59)) == "287082", "6-digit truncation");
Check(Totp.Remaining(sample, DateTimeOffset.FromUnixTimeSeconds(60)) == 30, "countdown at boundary");
Check(Totp.Remaining(sample, DateTimeOffset.FromUnixTimeSeconds(89)) == 1, "countdown before rollover");
Check(Totp.Generate(sample, DateTimeOffset.FromUnixTimeSeconds(59)) != Totp.Generate(sample, DateTimeOffset.FromUnixTimeSeconds(60)), "code changes at boundary");
Check(Base32.Normalize("gezd gnbv-gy3tqojqgezdgnbvgy3tqojq====") == sample.Secret, "normalization accepts spaced lowercase secret");
Check(Base32.Encode(Base32.Decode(sample.Secret)) == sample.Secret, "Base32 round trip");
Throws<FormatException>(() => Base32.Decode("ABCDEFG0"), "invalid Base32 character rejected");
Throws<FormatException>(() => Base32.Decode("A"), "invalid Base32 length rejected");
Throws<FormatException>(() => Base32.Decode("ABCDEFGHABCDEFP"), "nonzero Base32 tail rejected");
Throws<FormatException>(() => Base32.Decode("MZXW6"), "short secret rejected");

var parsed = OtpAuth.Parse("otpauth://totp/Example%3Aperson%2Bwork%40example.test?secret=" + sample.Secret + "&issuer=Example&algorithm=SHA256&digits=8&period=60");
Check(parsed.Issuer == "Example" && parsed.Name == "person+work@example.test" && parsed.Algorithm == "SHA256" && parsed.Digits == 8 && parsed.Period == 60, "otpauth parses encoded label and parameters");
Check(OtpAuth.Parse("otpauth://totp/Example:person?secret=" + sample.Secret).Issuer == "Example", "issuer fallback from label");
Throws<FormatException>(() => OtpAuth.Parse("otpauth://hotp/test?secret=" + sample.Secret), "HOTP explicitly rejected");
Throws<FormatException>(() => OtpAuth.Parse("https://example.test/"), "remote URL rejected");
Throws<FormatException>(() => OtpAuth.Parse("otpauth://totp/A:person?issuer=B&secret=" + sample.Secret), "conflicting issuer rejected");
Throws<FormatException>(() => OtpAuth.Parse("otpauth://totp/test?secret=" + sample.Secret + "&digits=7"), "invalid digits rejected");
Throws<FormatException>(() => OtpAuth.Parse("otpauth://totp/test?secret=" + sample.Secret + "&period=0"), "zero period rejected");
Throws<FormatException>(() => OtpAuth.Parse("otpauth://totp/test?secret=" + sample.Secret + "&period=abc"), "nonnumeric period rejected");
Throws<FormatException>(() => OtpAuth.Parse("otpauth://totp/test?secret=" + sample.Secret + "&secret=" + sample.Secret), "duplicate security parameters rejected");
Throws<FormatException>(() => (sample with { Issuer = null! }).Normalize(), "null account field rejected");
Throws<FormatException>(() => new VaultDocument { Accounts = [sample, sample] }.Validate(), "duplicate account ID rejected");

const string password = "correct horse battery staple";
using var vault = VaultCipher.Create(password, new VaultDocument { Accounts = [sample, parsed] });
var sealedBytes = vault.Seal();
var sealedText = Encoding.UTF8.GetString(sealedBytes);
Check(!sealedText.Contains(sample.Secret) && !sealedText.Contains(sample.Name) && !sealedText.Contains(password), "vault contains no plaintext credentials");
using var opened = VaultCipher.Open(sealedBytes, password);
Check(opened.Document.Accounts.SequenceEqual(vault.Document.Accounts), "encrypted vault round trip");
Check(!vault.Seal().SequenceEqual(sealedBytes), "unique nonce on each save");
Throws<CryptographicException>(() => VaultCipher.Open(sealedBytes, "wrong password"), "wrong password rejected");
var tampered = JsonNode.Parse(sealedBytes)!;
var cipher = Convert.FromBase64String(tampered["ciphertext"]!.GetValue<string>());
cipher[0] ^= 1;
tampered["ciphertext"] = Convert.ToBase64String(cipher);
Throws<CryptographicException>(() => VaultCipher.Open(Encoding.UTF8.GetBytes(tampered.ToJsonString()), password), "modified ciphertext rejected");
tampered = JsonNode.Parse(sealedBytes)!;
tampered["iterations"] = 99_999_999;
Throws<FormatException>(() => VaultCipher.Open(Encoding.UTF8.GetBytes(tampered.ToJsonString()), password), "excessive KDF work rejected");
Throws<FormatException>(() => VaultCipher.Open(Encoding.UTF8.GetBytes("not JSON"), password), "malformed backup rejected");
Throws<FormatException>(() => VaultCipher.Create("1234"), "short master password rejected");
var disposable = VaultCipher.Create(password);
disposable.Dispose();
Throws<ObjectDisposedException>(() => disposable.Seal(), "locked vault cannot seal");

var temporary = Path.Combine(Path.GetTempPath(), "LocalAuth-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new VaultStore(Path.Combine(temporary, "accounts.vault"));
    store.Save(vault);
    Check(store.Exists, "first atomic save creates vault");
    var previous = store.Read();
    vault.Document = vault.Document with { Accounts = [sample] };
    store.Save(vault);
    using var current = VaultCipher.Open(store.Read(), password);
    Check(current.Document.Accounts.Count == 1, "subsequent save updates vault");
    Check(File.ReadAllBytes(store.BackupPath).SequenceEqual(previous), "previous encrypted version retained atomically");
    using var recovery = VaultCipher.Open(File.ReadAllBytes(store.BackupPath), password);
    Check(recovery.Document.Accounts.Count == 2, "automatic backup is recoverable");
    Check(Directory.GetFiles(temporary, "*.tmp").Length == 0, "no temporary files remain");
    using var changedPassword = VaultCipher.Create("a different long password", current.Document);
    var changedBytes = changedPassword.Seal();
    Throws<CryptographicException>(() => VaultCipher.Open(changedBytes, password), "old password cannot open rekeyed vault");
    using var rekeyed = VaultCipher.Open(changedBytes, "a different long password");
    Check(rekeyed.Document.Accounts.Count == 1, "password change preserves accounts");
}
finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }


using (var pinVault = VaultCipher.Create("12345", new VaultDocument { Accounts = [sample, new Account { Kind = "Password", Issuer = "Example", Name = "test-user", Secret = " p@ss word! " }] }))
{
    var encrypted = pinVault.Seal();
    Check(!Encoding.UTF8.GetString(encrypted).Contains("p@ss word!"), "stored passwords encrypted");
    using var restored = VaultCipher.Open(encrypted, "12345");
    Check(restored.Document.Version == 2, "password entries upgrade document version");
    Check(restored.Document.Accounts[1].Secret == " p@ss word! ", "password preserves whitespace and punctuation");
    Check(restored.Document.Accounts[1].IsPassword && !restored.Document.Accounts[0].IsPassword, "mixed entry kinds survive reopen");
    Check(!restored.Document.Accounts[1].SameCredential(sample), "different entry kinds are distinct");
    Throws<CryptographicException>(() => VaultCipher.Open(encrypted, "12346"), "wrong five digit PIN rejected");
}
Throws<FormatException>(() => new Account { Kind = "Password", Issuer = "Test", Secret = "" }.Normalize(), "empty stored password rejected");
Throws<FormatException>(() => new Account { Kind = "Other", Issuer = "Test", Secret = "hello" }.Normalize(), "unknown entry kind rejected");

// Moving protection must write the destination first and retain the source on failure.
List<Account> sourceEntries = [sample];
List<Account> destinationEntries = [];
var saveOrder = new List<string>();
EntryTransfer.Move(sample, sourceEntries, destinationEntries,
    entries => { saveOrder.Add("source"); sourceEntries = entries; },
    entries => { saveOrder.Add("destination"); destinationEntries = entries; });
Check(sourceEntries.Count == 0 && destinationEntries.Single() == sample, "moving a credential leaves it only in destination");
Check(saveOrder.SequenceEqual(new[] { "destination", "source" }), "move saves destination before removing source");
sourceEntries = [sample]; destinationEntries = [];
Throws<IOException>(() => EntryTransfer.Move(sample, sourceEntries, destinationEntries,
    entries => sourceEntries = entries, entries => throw new IOException("destination unavailable")), "destination save failure rejects move");
Check(sourceEntries.Single() == sample && destinationEntries.Count == 0, "destination failure preserves original entry");
Throws<IOException>(() => EntryTransfer.Move(sample, sourceEntries, destinationEntries,
    entries => throw new IOException("source unavailable"), entries => destinationEntries = entries), "source save failure rejects move");
Check(sourceEntries.Single() == sample && destinationEntries.Count == 0, "source failure rolls back destination");
var destinationWrites = 0;
Throws<IOException>(() => EntryTransfer.Move(sample, sourceEntries, destinationEntries,
    entries => throw new IOException("source unavailable"), entries =>
    {
        if (++destinationWrites > 1) throw new IOException("rollback unavailable");
        destinationEntries = entries;
    }), "failed rollback is reported");
Check(sourceEntries.Single() == sample && destinationEntries.Single() == sample, "failed rollback retains both copies without losing credential");
Throws<FormatException>(() => EntryTransfer.Move(sample, sourceEntries, destinationEntries,
    _ => throw new Exception("must not write"), _ => throw new Exception("must not write")), "duplicate destination entry rejected before writing");
Throws<FormatException>(() => EntryTransfer.Move(sample, [], [],
    _ => throw new Exception("must not write"), _ => throw new Exception("must not write")), "missing source entry rejected before writing");

Console.WriteLine($"\nAll {passed} checks passed.");
