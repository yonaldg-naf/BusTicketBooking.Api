using BusTicketBooking.Dtos.Common;
using BusTicketBooking.Dtos.Schedules;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// NEW
using BusTicketBooking.Contexts;
using Microsoft.EntityFrameworkCore;

namespace BusTicketBooking.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SchedulesController : ControllerBase
    {
        private readonly IScheduleService _schedules;

        // NEW: we need DB access to resolve city -> stop(s)
        private readonly AppDbContext _db;

        // UPDATED: inject AppDbContext
        public SchedulesController(IScheduleService schedules, AppDbContext db)
        {
            _schedules = schedules;
            _db = db;
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
        /// Public: search schedules by fromStopId, toStopId, date (yyyy-MM-dd) with sorting + pagination.
        /// Backward-compatible existing endpoint.
        /// Example:
        /// /api/schedules/search?fromStopId=...&toStopId=...&date=2026-03-10&page=1&pageSize=10&sortBy=price&sortDir=asc
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
        /// Public: city-based search.
        /// Example:
        /// /api/schedules/search-by-city?fromCity=Mumbai&toCity=Pune&date=2026-03-10&page=1&pageSize=10
        /// </summary>
        [AllowAnonymous]
        [HttpGet("search-by-city")]
        public async Task<ActionResult<PagedResult<ScheduleResponseDto>>> SearchByCity(
            [FromQuery] string fromCity,
            [FromQuery] string toCity,
            [FromQuery] DateTime date,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? sortBy = "departure",
            [FromQuery] string? sortDir = "asc",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(fromCity) || string.IsNullOrWhiteSpace(toCity))
                return BadRequest(new { message = "fromCity and toCity are required." });

            var fc = fromCity.Trim();
            var tc = toCity.Trim();

            // V1: Exact city match; switch to prefix LIKE for partial match if you want
            var fromStops = await _db.Stops
                .AsNoTracking()
                .Where(s => s.City == fc)
                .OrderBy(s => s.Name)
                .ToListAsync(ct);

            var toStops = await _db.Stops
                .AsNoTracking()
                .Where(s => s.City == tc)
                .OrderBy(s => s.Name)
                .ToListAsync(ct);

            if (fromStops.Count == 0 || toStops.Count == 0)
                return NotFound(new { message = "Could not find one or both cities. Check spelling or seed data." });

            var request = new PagedRequestDto
            {
                Page = page,
                PageSize = pageSize,
                SortBy = sortBy,
                SortDir = sortDir
            };
            var dateOnly = DateOnly.FromDateTime(date);

            // Collect results for all (fromStop, toStop) pairs, de-duplicate by ScheduleId
            var unique = new Dictionary<Guid, ScheduleResponseDto>();
            foreach (var fs in fromStops)
            {
                foreach (var ts in toStops)
                {
                    var pageResult = await _schedules.SearchAsync(fs.Id, ts.Id, dateOnly, request, ct);
                    foreach (var item in pageResult.Items)
                    {
                        unique[item.Id] = item; // last one wins; items are equivalent anyway
                    }
                }
            }

            // Sort + page (same rules)
            var merged = unique.Values.AsEnumerable();
            var sortKey = (request.SortBy ?? "departure").Trim().ToLowerInvariant();
            bool desc = request.IsDescending();

            merged = sortKey switch
            {
                "price" => desc ? merged.OrderByDescending(x => x.BasePrice) : merged.OrderBy(x => x.BasePrice),
                "buscode" => desc ? merged.OrderByDescending(x => x.BusCode) : merged.OrderBy(x => x.BusCode),
                "routecode" => desc ? merged.OrderByDescending(x => x.RouteCode) : merged.OrderBy(x => x.RouteCode),
                _ => desc ? merged.OrderByDescending(x => x.DepartureUtc) : merged.OrderBy(x => x.DepartureUtc),
            };

            var total = merged.LongCount();
            var (skip, take) = request.GetSkipTake();
            var pageItems = merged.Skip(skip).Take(take).ToList();

            return Ok(new PagedResult<ScheduleResponseDto>
            {
                Page = request.Page,
                PageSize = request.PageSize,
                TotalCount = total,
                Items = pageItems
            });
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
