using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace Jorvis.Api.Tests;

/// <summary>
/// A software passkey authenticator: produces the same <c>navigator.credentials.create()</c> and <c>get()</c> results a
/// phone would (ES256, "none" attestation, user verified), so the server's real verification is exercised.
/// </summary>
public sealed class TestAuthenticator : IDisposable
{
    public const string Origin = "http://localhost:5200";
    public const string RelyingParty = "localhost";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _credentialId = RandomNumberGenerator.GetBytes(32);
    private string _userHandle = "";
    private uint _signCount;

    public void Dispose() => _key.Dispose();

    /// <summary>Answers the server's creation options; the result is what the browser posts back.</summary>
    public JsonObject Create(string optionsJson)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var root = options.RootElement;
        var challenge = root.GetProperty("challenge").GetString()!;
        _userHandle = root.GetProperty("user").GetProperty("id").GetString()!;

        var clientData = ClientData("webauthn.create", challenge);
        var authData = AuthenticatorData(flags: 0x45, attested: true);
        var attestationObject = Cbor.Map(
            ("fmt", Cbor.Text("none")),
            ("attStmt", Cbor.Map()),
            ("authData", Cbor.Bytes(authData)));

        return new JsonObject
        {
            ["id"] = Base64UrlTextEncoder.Encode(_credentialId),
            ["rawId"] = Base64UrlTextEncoder.Encode(_credentialId),
            ["type"] = "public-key",
            ["authenticatorAttachment"] = "platform",
            ["clientExtensionResults"] = new JsonObject(),
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Base64UrlTextEncoder.Encode(clientData),
                ["attestationObject"] = Base64UrlTextEncoder.Encode(attestationObject),
                ["transports"] = new JsonArray("internal")
            }
        };
    }

    /// <summary>Answers the server's request options by signing the challenge.</summary>
    public JsonObject Get(string optionsJson)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var challenge = options.RootElement.GetProperty("challenge").GetString()!;

        var clientData = ClientData("webauthn.get", challenge);
        _signCount++;
        var authData = AuthenticatorData(flags: 0x05, attested: false);
        var signature = _key.SignData(
            [.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return new JsonObject
        {
            ["id"] = Base64UrlTextEncoder.Encode(_credentialId),
            ["rawId"] = Base64UrlTextEncoder.Encode(_credentialId),
            ["type"] = "public-key",
            ["authenticatorAttachment"] = "platform",
            ["clientExtensionResults"] = new JsonObject(),
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Base64UrlTextEncoder.Encode(clientData),
                ["authenticatorData"] = Base64UrlTextEncoder.Encode(authData),
                ["signature"] = Base64UrlTextEncoder.Encode(signature),
                ["userHandle"] = _userHandle
            }
        };
    }

    private static byte[] ClientData(string type, string challenge) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin = Origin, crossOrigin = false }));

    private byte[] AuthenticatorData(byte flags, bool attested)
    {
        var data = new List<byte>();
        data.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(RelyingParty)));
        data.Add(flags);
        data.AddRange([(byte)(_signCount >> 24), (byte)(_signCount >> 16), (byte)(_signCount >> 8), (byte)_signCount]);

        if (attested)
        {
            data.AddRange(new byte[16]); // AAGUID: unknown authenticator
            data.AddRange([(byte)(_credentialId.Length >> 8), (byte)_credentialId.Length]);
            data.AddRange(_credentialId);

            var point = _key.ExportParameters(false).Q;
            data.AddRange(Cbor.CoseKeyEs256(point.X!, point.Y!));
        }

        return [.. data];
    }

    /// <summary>Just enough CBOR for an attestation object and a COSE key.</summary>
    private static class Cbor
    {
        public static byte[] Text(string value) => Item(3, Encoding.UTF8.GetBytes(value));

        public static byte[] Bytes(byte[] value) => Item(2, value);

        public static byte[] Int(long value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Map(params (string Key, byte[] Value)[] entries) =>
            [.. Head(5, (ulong)entries.Length), .. entries.SelectMany(e => Text(e.Key).Concat(e.Value))];

        public static byte[] CoseKeyEs256(byte[] x, byte[] y) =>
        [
            .. Head(5, 5),
            .. Int(1), .. Int(2),   // kty: EC2
            .. Int(3), .. Int(-7),  // alg: ES256
            .. Int(-1), .. Int(1),  // crv: P-256
            .. Int(-2), .. Bytes(x),
            .. Int(-3), .. Bytes(y)
        ];

        private static byte[] Item(int major, byte[] payload) => [.. Head(major, (ulong)payload.Length), .. payload];

        private static byte[] Head(int major, ulong value) => value switch
        {
            < 24 => [(byte)((major << 5) | (int)value)],
            < 256 => [(byte)((major << 5) | 24), (byte)value],
            _ => [(byte)((major << 5) | 25), (byte)(value >> 8), (byte)value]
        };
    }
}
