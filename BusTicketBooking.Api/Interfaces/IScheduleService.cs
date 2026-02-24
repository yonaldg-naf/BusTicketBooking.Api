using BusTicketBooking.Dtos.Schedules;

namespace BusTicketBooking.Interfaces
{
    public interface IScheduleService
    {
        Task<ScheduleResponseDto> CreateAsync(CreateScheduleRequestDto dto, CancellationToken ct = default);
        Task<IEnumerable<ScheduleResponseDto>> GetAllAsync(CancellationToken ct = default);
        Task<ScheduleResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<ScheduleResponseDto?> UpdateAsync(Guid id, UpdateScheduleRequestDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

        Task<IEnumerable<ScheduleResponseDto>> SearchAsync(Guid fromStopId, Guid toStopId, DateOnly date, CancellationToken ct = default);
    }
}