using BusTicketBooking.Dtos.Bus;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;

namespace BusTicketBooking.Services
{
    public class BusService : IBusService
    {
        private readonly IRepository<Bus> _buses;

        public BusService(IRepository<Bus> buses)
        {
            _buses = buses;
        }

        public async Task<BusResponseDto> CreateAsync(CreateBusRequestDto dto, CancellationToken ct = default)
        {
            // Enforce uniqueness of Code per Operator
            var exists = (await _buses.FindAsync(
                b => b.OperatorId == dto.OperatorId && b.Code == dto.Code, ct)).Any();
            if (exists) throw new InvalidOperationException("Bus code already exists for this operator.");

            var entity = new Bus
            {
                OperatorId = dto.OperatorId,
                Code = dto.Code.Trim(),
                RegistrationNumber = dto.RegistrationNumber.Trim(),
                BusType = dto.BusType,
                TotalSeats = dto.TotalSeats
            };

            entity = await _buses.AddAsync(entity, ct);
            return Map(entity);
        }

        public async Task<IEnumerable<BusResponseDto>> GetAllAsync(CancellationToken ct = default)
        {
            var list = await _buses.GetAllAsync(ct);
            return list.Select(Map);
        }

        public async Task<BusResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            var entity = await _buses.GetByIdAsync(id, ct);
            return entity == null ? null : Map(entity);
        }

        public async Task<BusResponseDto?> UpdateAsync(Guid id, UpdateBusRequestDto dto, CancellationToken ct = default)
        {
            var entity = await _buses.GetByIdAsync(id, ct);
            if (entity == null) return null;

            entity.RegistrationNumber = dto.RegistrationNumber.Trim();
            entity.BusType = dto.BusType;
            entity.TotalSeats = dto.TotalSeats;
            entity.UpdatedAtUtc = DateTime.UtcNow;

            await _buses.UpdateAsync(entity, ct);
            return Map(entity);
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var entity = await _buses.GetByIdAsync(id, ct);
            if (entity == null) return false;

            await _buses.RemoveAsync(entity, ct);
            return true;
        }

        private static BusResponseDto Map(Bus e) => new()
        {
            Id = e.Id,
            OperatorId = e.OperatorId,
            Code = e.Code,
            RegistrationNumber = e.RegistrationNumber,
            BusType = e.BusType,
            TotalSeats = e.TotalSeats,
            CreatedAtUtc = e.CreatedAtUtc,
            UpdatedAtUtc = e.UpdatedAtUtc
        };
    }
}