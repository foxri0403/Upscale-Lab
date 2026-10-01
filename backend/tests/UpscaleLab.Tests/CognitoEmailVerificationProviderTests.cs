using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Runtime;
using UpscaleLab.Application.Common;
using UpscaleLab.Infrastructure.Auth;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class CognitoEmailVerificationProviderTests
{
    [Fact]
    public async Task SignUp_SendsEmailAttributeAndSecretHash()
    {
        var client = new FakeCognitoClient();
        var provider = new CognitoEmailVerificationProvider(client, new CognitoOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        });

        var expiresAt = await provider.SignUpAsync(
            "user@example.com",
            "Correct-horse1!",
            CancellationToken.None);

        Assert.NotNull(client.SignUpRequest);
        Assert.Equal("test-client-id", client.SignUpRequest.ClientId);
        Assert.Equal("user@example.com", client.SignUpRequest.Username);
        Assert.Equal("Correct-horse1!", client.SignUpRequest.Password);
        Assert.False(string.IsNullOrWhiteSpace(client.SignUpRequest.SecretHash));
        Assert.Contains(client.SignUpRequest.UserAttributes,
            attribute => attribute.Name == "email" && attribute.Value == "user@example.com");
        Assert.InRange(expiresAt, DateTime.UtcNow.AddHours(23), DateTime.UtcNow.AddHours(25));
    }

    [Fact]
    public async Task ConfirmSignUp_WhenCodeDoesNotMatch_ReturnsValidationError()
    {
        var client = new FakeCognitoClient
        {
            ConfirmException = new CodeMismatchException("mismatch")
        };
        var provider = new CognitoEmailVerificationProvider(client, new CognitoOptions
        {
            ClientId = "test-client-id"
        });

        await Assert.ThrowsAsync<ValidationException>(() => provider.ConfirmSignUpAsync(
            "user@example.com",
            "123456",
            CancellationToken.None));
    }

    [Fact]
    public async Task ResendConfirmationCode_SendsExpectedRequest()
    {
        var client = new FakeCognitoClient();
        var provider = new CognitoEmailVerificationProvider(client, new CognitoOptions
        {
            ClientId = "test-client-id"
        });

        await provider.ResendConfirmationCodeAsync("user@example.com", CancellationToken.None);

        Assert.NotNull(client.ResendRequest);
        Assert.Equal("test-client-id", client.ResendRequest.ClientId);
        Assert.Equal("user@example.com", client.ResendRequest.Username);
        Assert.Null(client.ResendRequest.SecretHash);
    }

    private sealed class FakeCognitoClient()
        : AmazonCognitoIdentityProviderClient(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public SignUpRequest? SignUpRequest { get; private set; }
        public ConfirmSignUpRequest? ConfirmRequest { get; private set; }
        public ResendConfirmationCodeRequest? ResendRequest { get; private set; }
        public Exception? ConfirmException { get; init; }

        public override Task<SignUpResponse> SignUpAsync(
            SignUpRequest request,
            CancellationToken cancellationToken = default)
        {
            SignUpRequest = request;
            return Task.FromResult(new SignUpResponse
            {
                UserConfirmed = false,
                CodeDeliveryDetails = new CodeDeliveryDetailsType
                {
                    DeliveryMedium = DeliveryMediumType.EMAIL,
                    Destination = "u***@e***"
                }
            });
        }

        public override Task<ConfirmSignUpResponse> ConfirmSignUpAsync(
            ConfirmSignUpRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ConfirmException is not null)
            {
                throw ConfirmException;
            }

            ConfirmRequest = request;
            return Task.FromResult(new ConfirmSignUpResponse());
        }

        public override Task<ResendConfirmationCodeResponse> ResendConfirmationCodeAsync(
            ResendConfirmationCodeRequest request,
            CancellationToken cancellationToken = default)
        {
            ResendRequest = request;
            return Task.FromResult(new ResendConfirmationCodeResponse());
        }
    }
}
