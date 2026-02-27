using BusTicketBooking.Dtos.Common;
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

        /// <summary>
        /// Search schedules with sorting + pagination.
        /// sortBy: "departure" | "price" | "busCode" | "routeCode"
        /// sortDir: "asc" | "desc"
        /// </summary>
        Task<PagedResult<ScheduleResponseDto>> SearchAsync(
            Guid fromStopId,
            Guid toStopId,
            DateOnly date,
            PagedRequestDto request,
            CancellationToken ct = default);

        /// <summary>
        /// Returns booked vs available seats for a schedule.
        /// </summary>
        Task<SeatAvailabilityResponseDto> GetAvailabilityAsync(Guid scheduleId, CancellationToken ct = default);
    }
}
