using BusTicketBooking.Dtos.Common;
using BusTicketBooking.Dtos.Schedules;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusTicketBooking.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SchedulesController : ControllerBase
    {
        private readonly IScheduleService _schedules;

        public SchedulesController(IScheduleService schedules)
        {
            _schedules = schedules;
        }

        // Admin/Operator: list all schedules
        [Authorize(Roles = $"{Roles.Admin},{Roles.Operator}")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ScheduleResponseDto>>> GetAll(CancellationToken ct)
        {
            var data = await _schedules.GetAllAsync(ct);
            return Ok(data);
        }

        // Public: get a schedule by Id
        [AllowAnonymous]
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<ScheduleResponseDto>> GetById([FromRoute] Guid id, CancellationToken ct)
        {
            var item = await _schedules.GetByIdAsync(id, ct);
            if (item == null) return NotFound();
            return Ok(item);
        }

        // Admin/Operator: create schedule
        [Authorize(Roles = $"{Roles.Admin},{Roles.Operator}")]
        [HttpPost]
        public async Task<ActionResult<ScheduleResponseDto>> Create([FromBody] CreateScheduleRequestDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await _schedules.CreateAsync(dto, ct);
                return Created($"/api/schedules/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // Admin/Operator: update schedule
        [Authorize(Roles = $"{Roles.Admin},{Roles.Operator}")]
        [HttpPut("{id:guid}")]
        public async Task<ActionResult<ScheduleResponseDto>> Update([FromRoute] Guid id, [FromBody] UpdateScheduleRequestDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var updated = await _schedules.UpdateAsync(id, dto, ct);
                if (updated == null) return NotFound();
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // Admin/Operator: delete schedule
        [Authorize(Roles = $"{Roles.Admin},{Roles.Operator}")]
        [HttpDelete("{id:guid}")]
        public async Task<ActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
        {
            var ok = await _schedules.DeleteAsync(id, ct);
            if (!ok) return NotFound();
            return NoContent();
        }

        /// <summary>
        /// Public: search schedules by fromStopId, toStopId, date (yyyy-MM-dd) with sorting + pagination
        /// Example:
        /// GET /api/schedules/search?fromStopId=...&toStopId=...&date=2026-02-27&page=1&pageSize=10&sortBy=price&sortDir=asc
        /// </summary>
        [AllowAnonymous]
        [HttpGet("search")]
        public async Task<ActionResult<PagedResult<ScheduleResponseDto>>> Search(
            [FromQuery] Guid fromStopId,
            [FromQuery] Guid toStopId,
            [FromQuery] DateTime date,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? sortBy = "departure",
            [FromQuery] string? sortDir = "asc",
            CancellationToken ct = default)
        {
            if (fromStopId == Guid.Empty || toStopId == Guid.Empty)
                return BadRequest(new { message = "fromStopId and toStopId are required." });

            var request = new PagedRequestDto
            {
                Page = page,
                PageSize = pageSize,
                SortBy = sortBy,
                SortDir = sortDir
            };

            var dateOnly = DateOnly.FromDateTime(date);
            var results = await _schedules.SearchAsync(fromStopId, toStopId, dateOnly, request, ct);
            return Ok(results);
        }

        /// <summary>
        /// Public: get seat availability for a schedule (booked vs available)
        /// </summary>
        [AllowAnonymous]
        [HttpGet("{id:guid}/seats")]
        public async Task<ActionResult<SeatAvailabilityResponseDto>> GetSeatAvailability([FromRoute] Guid id, CancellationToken ct)
        {
            try
            {
                var result = await _schedules.GetAvailabilityAsync(id, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}