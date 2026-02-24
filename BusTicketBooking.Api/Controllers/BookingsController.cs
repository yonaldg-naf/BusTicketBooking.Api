using System.Security.Claims;
using BusTicketBooking.Dtos.Bookings;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusTicketBooking.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingsController : ControllerBase
    {
        private readonly IBookingService _bookings;

        public BookingsController(IBookingService bookings)
        {
            _bookings = bookings;
        }

        private Guid GetUserIdFromToken()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) // sometimes mapped
                      ?? User.FindFirstValue("sub");                  // explicit JWT 'sub'
            if (Guid.TryParse(sub, out var id)) return id;
            throw new UnauthorizedAccessException("Invalid user id in token.");
        }

        /// <summary>Create a booking (Customer)</summary>
        [Authorize] // any authenticated user
        [HttpPost]
        public async Task<ActionResult<BookingResponseDto>> Create([FromBody] CreateBookingRequestDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = GetUserIdFromToken();

            try
            {
                var created = await _bookings.CreateAsync(userId, dto, ct);
                return Created($"/api/bookings/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        /// <summary>Get my bookings (Customer)</summary>
        [Authorize]
        [HttpGet("my")]
        public async Task<ActionResult<IEnumerable<BookingResponseDto>>> GetMy(CancellationToken ct)
        {
            var userId = GetUserIdFromToken();
            var list = await _bookings.GetMyAsync(userId, ct);
            return Ok(list);
        }

        /// <summary>Get a booking by id (owner; Admin/Operator allowed if you pass allowPrivileged=true in service)</summary>
        [Authorize]
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<BookingResponseDto>> GetById([FromRoute] Guid id, CancellationToken ct)
        {
            var userId = GetUserIdFromToken();
            var item = await _bookings.GetByIdForUserAsync(userId, id, allowPrivileged: User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Operator), ct);
            if (item == null) return NotFound();
            return Ok(item);
        }

        /// <summary>Cancel my booking</summary>
        [Authorize]
        [HttpDelete("{id:guid}")]
        public async Task<ActionResult> Cancel([FromRoute] Guid id, CancellationToken ct)
        {
            var userId = GetUserIdFromToken();
            try
            {
                var ok = await _bookings.CancelAsync(userId, id, allowPrivileged: User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Operator), ct);
                if (!ok) return NotFound();
                return NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        /// <summary>Mock pay a booking (marks Payment=Success, Booking=Confirmed)</summary>
        [Authorize]
        [HttpPost("{id:guid}/pay")]
        public async Task<ActionResult<BookingResponseDto>> Pay([FromRoute] Guid id, [FromBody] PayBookingRequestDto dto, CancellationToken ct)
        {
            var userId = GetUserIdFromToken();
            try
            {
                var updated = await _bookings.PayAsync(userId, id, dto.Amount, dto.ProviderReference, allowPrivileged: User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Operator), ct);
                if (updated == null) return NotFound();
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }
    }
}