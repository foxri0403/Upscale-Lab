using System.Security.Cryptography;
using System.Text;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Runtime;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class CognitoEmailVerificationProvider(
    IAmazonCognitoIdentityProvider cognito,
    CognitoOptions options) : IEmailVerificationProvider
{
    private static readonly TimeSpan VerificationCodeLifetime = TimeSpan.FromHours(24);
    private const string InvalidCodeMessage =
        "인증 코드가 올바르지 않거나 만료되었습니다. 새 코드를 요청해 주세요.";
    private const string DeliveryFailureMessage =
        "인증 이메일을 전송할 수 없습니다. 잠시 후 다시 시도해 주세요.";

    public async Task<DateTime> SignUpAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            var response = await cognito.SignUpAsync(new SignUpRequest
            {
                ClientId = options.ClientId,
                SecretHash = CreateSecretHash(email),
                Username = email,
                Password = password,
                UserAttributes =
                [
                    new AttributeType { Name = "email", Value = email }
                ]
            }, cancellationToken);

            if (response.UserConfirmed == true ||
                !string.Equals(response.CodeDeliveryDetails?.DeliveryMedium?.Value, "EMAIL", StringComparison.Ordinal))
            {
                throw new ConfigurationException(
                    "Cognito user pool must require email confirmation and deliver verification codes by email.");
            }

            return DateTime.UtcNow.Add(VerificationCodeLifetime);
        }
        catch (UsernameExistsException)
        {
            throw new ConflictException("이미 사용 중인 이메일입니다.");
        }
        catch (InvalidPasswordException)
        {
            throw new ValidationException(PasswordPolicy.ErrorMessage);
        }
        catch (InvalidParameterException)
        {
            throw new ValidationException("회원가입 정보를 확인해 주세요.");
        }
        catch (CodeDeliveryFailureException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (LimitExceededException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (TooManyRequestsException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (InternalErrorException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (AmazonServiceException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
    }

    public async Task ConfirmSignUpAsync(
        string email,
        string code,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            await cognito.ConfirmSignUpAsync(new ConfirmSignUpRequest
            {
                ClientId = options.ClientId,
                SecretHash = CreateSecretHash(email),
                Username = email,
                ConfirmationCode = code
            }, cancellationToken);
        }
        catch (CodeMismatchException)
        {
            throw new ValidationException(InvalidCodeMessage);
        }
        catch (ExpiredCodeException)
        {
            throw new ValidationException(InvalidCodeMessage);
        }
        catch (NotAuthorizedException)
        {
            throw new ValidationException(InvalidCodeMessage);
        }
        catch (UserNotFoundException)
        {
            throw new ValidationException(InvalidCodeMessage);
        }
        catch (AliasExistsException)
        {
            throw new ConflictException("이미 사용 중인 이메일입니다.");
        }
        catch (TooManyFailedAttemptsException)
        {
            throw new ValidationException(InvalidCodeMessage);
        }
        catch (LimitExceededException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (TooManyRequestsException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (InternalErrorException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (AmazonServiceException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
    }

    public async Task ResendConfirmationCodeAsync(
        string email,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            await cognito.ResendConfirmationCodeAsync(new ResendConfirmationCodeRequest
            {
                ClientId = options.ClientId,
                SecretHash = CreateSecretHash(email),
                Username = email
            }, cancellationToken);
        }
        catch (UserNotFoundException)
        {
            // Keep the public endpoint account-enumeration safe.
        }
        catch (NotAuthorizedException)
        {
            // Cognito reports already-confirmed users here; keep the response indistinguishable.
        }
        catch (CodeDeliveryFailureException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (LimitExceededException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (TooManyRequestsException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (InternalErrorException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
        catch (AmazonServiceException)
        {
            throw new ServiceUnavailableException(DeliveryFailureMessage);
        }
    }

    private string? CreateSecretHash(string username)
    {
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            return null;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.ClientSecret));
        var message = Encoding.UTF8.GetBytes(username + options.ClientId);
        return Convert.ToBase64String(hmac.ComputeHash(message));
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new ConfigurationException("Cognito:ClientId is required.");
        }
    }
}
