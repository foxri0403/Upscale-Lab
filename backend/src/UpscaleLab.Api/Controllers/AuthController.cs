using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Auth;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RegisterResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.RegisterAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [AllowAnonymous]
    [HttpPost("verify-email")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> VerifyEmail(
        VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await authService.VerifyEmailAsync(request, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("resend-verification")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MessageResponse>> ResendVerificationEmail(
        ResendVerificationEmailRequest request,
        CancellationToken cancellationToken)
    {
        await authService.ResendVerificationEmailAsync(request, cancellationToken);
        return Accepted(new MessageResponse("인증 메일 요청이 접수되었습니다."));
    }

    [AllowAnonymous]
    [HttpPost("find-id")]
    [ProducesResponseType<FindIdResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FindIdResponse>> FindId(
        FindIdRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await authService.FindIdAsync(request, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MessageResponse>> ForgotPassword(
        PasswordResetStartRequest request,
        CancellationToken cancellationToken)
    {
        await authService.StartPasswordResetAsync(request, cancellationToken);
        return Accepted(new MessageResponse(
            "일치하는 계정이 있다면 등록된 이메일로 재설정 코드를 전송했습니다."));
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MessageResponse>> ResetPassword(
        PasswordResetConfirmRequest request,
        CancellationToken cancellationToken)
    {
        await authService.ConfirmPasswordResetAsync(request, cancellationToken);
        return Ok(new MessageResponse("비밀번호가 변경되었습니다."));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await authService.LoginAsync(request, cancellationToken));
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        return Ok(await authService.GetMeAsync(User.GetRequiredUserId(), cancellationToken));
    }
}
