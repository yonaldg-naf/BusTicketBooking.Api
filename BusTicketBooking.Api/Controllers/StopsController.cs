using BusTicketBooking.Contexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusTicketBooking.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StopsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public StopsController(AppDbContext db) => _db = db;

        /// <summary>List distinct cities (for dropdown/autocomplete)</summary>
        [HttpGet("cities")]
        public async Task<ActionResult<IEnumerable<string>>> GetCities(CancellationToken ct)
        {
            var cities = await _db.Stops.AsNoTracking()
                .Select(s => s.City)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync(ct);

            return Ok(cities);
        }

        /// <summary>List stops; filter by city if provided</summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetStops(
            [FromQuery] string? city = null,
            CancellationToken ct = default)
        {
            var q = _db.Stops.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(city))
                q = q.Where(s => s.City == city.Trim());

            var stops = await q
                .OrderBy(s => s.City).ThenBy(s => s.Name)
                .Select(s => new { s.Id, s.City, s.Name, s.Latitude, s.Longitude })
                .ToListAsync(ct);

            return Ok(stops);
        }
    }
}
