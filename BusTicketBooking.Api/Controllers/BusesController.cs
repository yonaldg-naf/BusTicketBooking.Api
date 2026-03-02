using BusTicketBooking.Dtos.Bus;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using BusTicketBooking.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusTicketBooking.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Operator}")]
    public class BusesController : ControllerBase
    {
        private readonly IBusService _busService;

        public BusesController(IBusService busService)
        {
            _busService = busService;
        }

        // POST: /api/buses
        [HttpPost]
        public async Task<ActionResult<BusResponseDto>> Create([FromBody] CreateBusRequestDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var result = await _busService.CreateAsync(dto, ct);
                return Created($"/api/buses/{result.Id}", result);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // GET: /api/buses
        [HttpGet]
        public async Task<ActionResult<IEnumerable<BusResponseDto>>> GetAll(CancellationToken ct)
        {
            var list = await _busService.GetAllAsync(ct);
            return Ok(list);
        }

        // GET: /api/buses/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<BusResponseDto>> GetById([FromRoute] Guid id, CancellationToken ct)
        {
            var bus = await _busService.GetByIdAsync(id, ct);
            if (bus == null) return NotFound();
            return Ok(bus);
        }

        // PUT: /api/buses/{id}
        [HttpPut("{id:guid}")]
        public async Task<ActionResult<BusResponseDto>> Update([FromRoute] Guid id, [FromBody] UpdateBusRequestDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var updated = await _busService.UpdateAsync(id, dto, ct);
            if (updated == null) return NotFound();
            return Ok(updated);
        }

        // PATCH: /api/buses/{id}/status   <-- added endpoint
        [HttpPatch("{id:guid}/status")]
        public async Task<ActionResult<BusResponseDto>> UpdateStatus([FromRoute] Guid id, [FromBody] UpdateBusStatusRequestDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var updated = await _busService.UpdateStatusAsync(id, dto.Status, ct);
            if (updated == null) return NotFound();

            return Ok(updated);
        }

        // DELETE: /api/buses/{id}
        [HttpDelete("{id:guid}")]
        public async Task<ActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
        {
            var ok = await _busService.DeleteAsync(id, ct);
            if (!ok) return NotFound();
            return NoContent();
        }
    }
}