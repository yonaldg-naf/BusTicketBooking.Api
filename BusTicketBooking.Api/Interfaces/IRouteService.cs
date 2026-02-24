using BusTicketBooking.Dtos.Routes;

namespace BusTicketBooking.Interfaces
{
    public interface IRouteService
    {
        Task<RouteResponseDto> CreateAsync(CreateRouteRequestDto dto, CancellationToken ct = default);
        Task<IEnumerable<RouteResponseDto>> GetAllAsync(CancellationToken ct = default);
        Task<RouteResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<RouteResponseDto?> UpdateAsync(Guid id, UpdateRouteRequestDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}