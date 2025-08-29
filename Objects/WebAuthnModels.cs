using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Objects;

public sealed record PublicKeyCreationOptionsResponse(
    [property: JsonPropertyName("options")] JsonElement Options
);

public sealed record PublicKeyRequestOptionsResponse(
    [property: JsonPropertyName("options")] JsonElement Options
);

public sealed record WebAuthnCredentialRecord(
    [property: JsonPropertyName("credential")] WebAuthnCredentialDetails Credential
);

public sealed record WebAuthnCredentialDetails(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("credentialId")] string CredentialId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("relyingPartyId")] string RelyingPartyId,
    [property: JsonPropertyName("userId")] Guid UserId
);

public sealed record WebAuthnAttestationCredential(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("response")] WebAuthnAttestationResponse Response,
    [property: JsonPropertyName("type")] string Type = "public-key",
   [property: JsonPropertyName("clientExtensionResults")] WebAuthnClientExtensionResults? ClientExtensionResults = null,
 [property: JsonPropertyName("transports")] string[]? Transports = null
);
public sealed record WebAuthnClientExtensionResults(
    [property: JsonPropertyName("credProps")] WebAuthnCredProps CredProps
);

public sealed record WebAuthnCredProps(
    [property: JsonPropertyName("rk")] bool Rk
);

public sealed record WebAuthnAttestationResponse(
    [property: JsonPropertyName("attestationObject")] string AttestationObject,
    [property: JsonPropertyName("clientDataJSON")] string ClientDataJSON
);

public sealed record WebAuthnAssertionCredential(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("response")] WebAuthnAssertionResponse Response,
    [property: JsonPropertyName("type")] string Type = "public-key",
    [property: JsonPropertyName("clientExtensionResults")] object? ClientExtensionResults = null
);

public sealed record WebAuthnAssertionResponse(
    [property: JsonPropertyName("authenticatorData")] string AuthenticatorData,
    [property: JsonPropertyName("clientDataJSON")] string ClientDataJSON,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("userHandle")] string? UserHandle
);

public sealed record UserLoginResponseSlim(
    [property: JsonPropertyName("user")] UserSlim User,
    [property: JsonPropertyName("token")] string? Token = null,
    [property: JsonPropertyName("refreshToken")] string? RefreshToken = null
);

public sealed record UserSlim(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email
);

// ---- Request DTOs ----
public class WebAuthnRegStartRequest
{
    [JsonPropertyName("userId")] public Guid UserId { get; set; }
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("workflow")] public string Workflow { get; set; } = "general";
}

public class WebAuthnStartRequest
{
    [JsonPropertyName("applicationId")] public Guid ApplicationId { get; set; }
    [JsonPropertyName("workflow")] public string? Workflow { get; set; }
    [JsonPropertyName("userId")] public Guid? UserId { get; set; }
    [JsonPropertyName("loginId")] public string? LoginId { get; set; }
    [JsonPropertyName("loginIdTypes")] public string[]? LoginIdTypes { get; set; }
    [JsonPropertyName("credentialId")] public Guid? CredentialId { get; set; }
    [JsonPropertyName("state")] public object? State { get; set; }
}

public class WebAuthnCompleteRequest
{
    [JsonPropertyName("origin")] public string Origin { get; set; } = string.Empty;
    [JsonPropertyName("rpId")] public string RpId { get; set; } = string.Empty;
    [JsonPropertyName("credential")] public WebAuthnAssertionCredential Credential { get; set; } = default!;
    [JsonPropertyName("twoFactorTrustId")] public string? TwoFactorTrustId { get; set; }
}

public class WebAuthnRegCompleteRequest
{
    [JsonPropertyName("origin")] public string Origin { get; set; } = string.Empty;     // backend fills this in
    [JsonPropertyName("rpId")] public string RpId { get; set; } = string.Empty;         // backend fills this in
    [JsonPropertyName("userId")] public Guid UserId { get; set; }                       // comes from frontend
    [JsonPropertyName("credential")] public WebAuthnAttestationCredential Credential { get; set; } = default!;
}

public class WebAuthnSettings
{
    [JsonPropertyName("rpId")] public string RpId { get; set; } = string.Empty;
    [JsonPropertyName("origin")] public string Origin { get; set; } = string.Empty;
}
