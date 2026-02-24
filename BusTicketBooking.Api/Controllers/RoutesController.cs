using BusTicketBooking.Dtos.Routes;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusTicketBooking.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Operator}")]
    public class RoutesController : ControllerBase
    {
        private readonly IRouteService _routes;

        public RoutesController(IRouteService routes)
        {
            _routes = routes;
        }

        // POST /api/routes
        [HttpPost]
        public async Task<ActionResult<RouteResponseDto>> Create([FromBody] CreateRouteRequestDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _routes.CreateAsync(dto, ct);
                return Created($"/api/routes/{result.Id}", result);
            }
            catch (InvalidOperationException ex)
            {
                // Duplicate RouteCode / stop validation errors show as 409 to signal conflict/invalid
                return Conflict(new { message = ex.Message });
            }
        }

        // GET /api/routes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RouteResponseDto>>> GetAll(CancellationToken ct)
        {
            var data = await _routes.GetAllAsync(ct);
            return Ok(data);
        }

        // GET /api/routes/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<RouteResponseDto>> GetById([FromRoute] Guid id, CancellationToken ct)
        {
            var route = await _routes.GetByIdAsync(id, ct);
            if (route == null) return NotFound();
            return Ok(route);
        }

        // PUT /api/routes/{id}
        [HttpPut("{id:guid}")]
        public async Task<ActionResult<RouteResponseDto>> Update([FromRoute] Guid id, [FromBody] UpdateRouteRequestDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var updated = await _routes.UpdateAsync(id, dto, ct);
                if (updated == null) return NotFound();
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // DELETE /api/routes/{id}
        [HttpDelete("{id:guid}")]
        public async Task<ActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
        {
            var ok = await _routes.DeleteAsync(id, ct);
            if (!ok) return NotFound();
            return NoContent();
        }
    }
}