using BusTicketBooking.Dtos.Auth;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusTicketBooking.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    
    public class AuthController : ControllerBase
    {
        private readonly IUserService _users;
        private readonly IPasswordService _passwords;
        private readonly ITokenService _tokens;

        public AuthController(IUserService users, IPasswordService passwords, ITokenService tokens)
        {
            _users = users;
            _passwords = passwords;
            _tokens = tokens;
        }

        /// <summary>
        /// Register a new user (default role: Customer).
        /// </summary>
        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterRequestDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var user = new User
                {
                    Username = dto.Username.Trim(),
                    Email = dto.Email.Trim(),
                    FullName = dto.FullName?.Trim() ?? dto.Username.Trim(),
                    Role = string.IsNullOrWhiteSpace(dto.Role) ? "Customer" : dto.Role.Trim()
                };

                var created = await _users.CreateAsync(user, dto.Password);
                var (token, exp) = _tokens.GenerateAccessToken(created);

                return Created($"/api/users/{created.Id}", new AuthResponseDto
                {
                    AccessToken = token,
                    ExpiresAtUtc = exp,
                    UserId = created.Id.ToString(),
                    Username = created.Username,
                    Email = created.Email,
                    Role = created.Role,
                    FullName = created.FullName
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Login with username + password to receive JWT access token.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _users.FindByUsernameAsync(dto.Username.Trim());
            if (user == null) return Unauthorized("Invalid username or password.");

            if (!_passwords.Verify(user, dto.Password))
                return Unauthorized("Invalid username or password.");

            var (token, exp) = _tokens.GenerateAccessToken(user);

            return Ok(new AuthResponseDto
            {
                AccessToken = token,
                ExpiresAtUtc = exp,
                UserId = user.Id.ToString(),
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                FullName = user.FullName
            });
        }
    }
}