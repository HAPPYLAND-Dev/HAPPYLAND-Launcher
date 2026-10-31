namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

/// <summary>
/// Surfaced from <see cref="IMicrosoftAuthService"/> when sign-in falls into the OAuth
/// device-code branch (no embedded WebView available). The view-model shows the user
/// the <see cref="Message"/> + opens <see cref="VerificationUrl"/> in the default browser.
/// </summary>
public sealed record MicrosoftDeviceCodeInfo
{
    /// <summary>The short alphanumeric code the user types into the verification page.</summary>
    public required string UserCode { get; init; }

    /// <summary>Where the user enters the code (typically <c>https://microsoft.com/devicelogin</c>).</summary>
    public required string VerificationUrl { get; init; }

    /// <summary>Full human-readable message from Microsoft, e.g. "To sign in, use a web browser to open ...".</summary>
    public required string Message { get; init; }
}
