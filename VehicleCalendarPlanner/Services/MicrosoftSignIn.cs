using Microsoft.Identity.Client;
using VehicleCalendarPlanner.Core;
#if WINDOWS
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
#endif

namespace VehicleCalendarPlanner.Services;

public sealed class MicrosoftSignIn : IAccessTokenProvider
{
    // Public application registration ID, not a secret.
    public const string ClientId = "4ebb2fae-d55b-4c44-94c0-9c14c325bb6a";
    public const string AndroidRedirectScheme = "msal" + ClientId;
    public const string AndroidRedirectHost = "auth";
    public const string AndroidRedirectUri = AndroidRedirectScheme + "://" + AndroidRedirectHost;
    private static readonly string[] Scopes = ["Calendars.ReadWrite"];
    private readonly IPublicClientApplication _client;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MicrosoftSignIn()
    {
        var builder = PublicClientApplicationBuilder.Create(ClientId)
            .WithAuthority(AadAuthorityAudience.PersonalMicrosoftAccount);
#if ANDROID
        builder = builder.WithRedirectUri(AndroidRedirectUri);
#else
        builder = builder.WithDefaultRedirectUri();
#endif
        _client = builder.Build();
#if WINDOWS
        RegisterEncryptedCache();
#endif
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = (await _client.GetAccountsAsync()).FirstOrDefault();
            if (account is null)
                throw new AuthenticationRequiredException();
            try
            {
                var result = await _client.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken);
                return result.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                throw new AuthenticationRequiredException();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> SignInAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var accounts = await _client.GetAccountsAsync();
            var account = accounts.FirstOrDefault();
            if (account is not null)
            {
                try
                {
                    var silent = await _client.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken);
                    return silent.Account.Username;
                }
                catch (MsalUiRequiredException)
                {
                    // A first-time calendar permission grant needs an interactive sign-in.
                }
            }

            var interactive = _client.AcquireTokenInteractive(Scopes);
#if ANDROID
            interactive = interactive.WithParentActivityOrWindow(
                Platform.CurrentActivity ?? throw new InvalidOperationException(AppResources.AndroidSignInUnavailable));
#elif WINDOWS
            var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow
                ?? throw new InvalidOperationException(AppResources.WindowsSignInUnavailable);
            interactive = interactive.WithParentActivityOrWindow(window.WindowHandle);
#endif
            var result = await interactive.ExecuteAsync(cancellationToken);
            return result.Account.Username;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var account in await _client.GetAccountsAsync())
                await _client.RemoveAsync(account);
        }
        finally
        {
            _gate.Release();
        }
    }

#if WINDOWS
    private void RegisterEncryptedCache()
    {
        var cachePath = Path.Combine(FileSystem.Current.AppDataDirectory, "microsoft-signin.bin");
        _client.UserTokenCache.SetBeforeAccessAsync(async args =>
        {
            if (!File.Exists(cachePath))
                return;
            var encrypted = await File.ReadAllBytesAsync(cachePath);
            var buffer = CryptographicBuffer.CreateFromByteArray(encrypted);
            var plaintext = await new DataProtectionProvider().UnprotectAsync(buffer);
            CryptographicBuffer.CopyToByteArray(plaintext, out var bytes);
            args.TokenCache.DeserializeMsalV3(bytes, shouldClearExistingCache: true);
        });
        _client.UserTokenCache.SetAfterAccessAsync(async args =>
        {
            if (!args.HasStateChanged)
                return;
            var bytes = args.TokenCache.SerializeMsalV3();
            var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
            var protectedBuffer = await new DataProtectionProvider("LOCAL=user").ProtectAsync(buffer);
            CryptographicBuffer.CopyToByteArray(protectedBuffer, out var encrypted);
            var temporary = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, encrypted);
                File.Move(temporary, cachePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        });
    }
#endif
}
