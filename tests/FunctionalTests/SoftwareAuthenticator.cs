using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Un autenticador WebAuthn de pruebas (ES256, atestación «none», con verificación de usuario) para recorrer las dos ceremonias de passkey
/// sin navegador (ADR 0007, incremento I4). Hace lo que haría el dispositivo: crea la credencial con las opciones del servidor y firma el
/// reto de la entrada. El origen y el dominio son los que se le dicen, para probar también los que no valen.
/// </summary>
internal sealed class SoftwareAuthenticator(string rpId = "localhost", string origin = "http://localhost")
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private uint signCount;

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>Responde a las opciones de creación con la credencial en el JSON que enviaría el navegador.</summary>
    public string Create(string creationOptionsJson, string? challengeOverride = null)
    {
        using var options = JsonDocument.Parse(creationOptionsJson);
        var challenge = challengeOverride ?? options.RootElement.GetProperty("challenge").GetString()!;
        var parameters = key.ExportParameters(false);
        var cose = Cbor.Map(
            (Cbor.Int(1), Cbor.Int(2)), (Cbor.Int(3), Cbor.Int(-7)), (Cbor.Int(-1), Cbor.Int(1)),
            (Cbor.Int(-2), Cbor.Bytes(parameters.Q.X!)), (Cbor.Int(-3), Cbor.Bytes(parameters.Q.Y!)));
        var authData = Concat(
            SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), [0x45], Counter(0), new byte[16],
            [(byte)(CredentialId.Length >> 8), (byte)CredentialId.Length], CredentialId, cose);
        var attestation = Cbor.Map(
            (Cbor.Text("fmt"), Cbor.Text("none")), (Cbor.Text("attStmt"), Cbor.Map()), (Cbor.Text("authData"), Cbor.Bytes(authData)));
        return JsonSerializer.Serialize(new
        {
            id = Base64UrlTextEncoder.Encode(CredentialId),
            rawId = Base64UrlTextEncoder.Encode(CredentialId),
            type = "public-key",
            response = new
            {
                clientDataJSON = Base64UrlTextEncoder.Encode(ClientData("webauthn.create", challenge)),
                attestationObject = Base64UrlTextEncoder.Encode(attestation),
                transports = new[] { "internal" },
            },
            clientExtensionResults = new { },
            authenticatorAttachment = "platform",
        });
    }

    /// <summary>Firma el reto de las opciones de petición. El identificador de usuario es el que el servidor puso al crear la credencial.</summary>
    public string Sign(string requestOptionsJson, string userHandle, string? challengeOverride = null)
    {
        using var options = JsonDocument.Parse(requestOptionsJson);
        var challenge = challengeOverride ?? options.RootElement.GetProperty("challenge").GetString()!;
        var clientData = ClientData("webauthn.get", challenge);
        var authData = Concat(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), [0x05], Counter(++signCount));
        var signature = key.SignData(Concat(authData, SHA256.HashData(clientData)), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.Serialize(new
        {
            id = Base64UrlTextEncoder.Encode(CredentialId),
            rawId = Base64UrlTextEncoder.Encode(CredentialId),
            type = "public-key",
            response = new
            {
                clientDataJSON = Base64UrlTextEncoder.Encode(clientData),
                authenticatorData = Base64UrlTextEncoder.Encode(authData),
                signature = Base64UrlTextEncoder.Encode(signature),
                userHandle,
            },
            clientExtensionResults = new { },
            authenticatorAttachment = "platform",
        });
    }

    /// <summary>El identificador de usuario que el servidor puso en las opciones de creación.</summary>
    public static string UserHandle(string creationOptionsJson)
    {
        using var options = JsonDocument.Parse(creationOptionsJson);
        return options.RootElement.GetProperty("user").GetProperty("id").GetString()!;
    }

    private byte[] ClientData(string type, string challenge) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type, challenge, origin, crossOrigin = false });

    private static byte[] Counter(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    /// <summary>Lo mínimo de CBOR que necesita una atestación: enteros, cadenas de bytes y de texto, y mapas.</summary>
    private static class Cbor
    {
        public static byte[] Int(int value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Bytes(byte[] value) => Concat(Head(2, (ulong)value.Length), value);

        public static byte[] Text(string value) => Concat(Head(3, (ulong)Encoding.UTF8.GetByteCount(value)), Encoding.UTF8.GetBytes(value));

        public static byte[] Map(params (byte[] Key, byte[] Value)[] entries) =>
            Concat(Head(5, (ulong)entries.Length), Concat(entries.SelectMany(entry => new[] { entry.Key, entry.Value }).ToArray()));

        private static byte[] Head(int type, ulong value) => value switch
        {
            < 24 => [(byte)(type << 5 | (int)value)],
            < 256 => [(byte)(type << 5 | 24), (byte)value],
            _ => [(byte)(type << 5 | 25), (byte)(value >> 8), (byte)value],
        };
    }
}
