using BusTicketBooking.Contexts;
using BusTicketBooking.Dtos.Common;
using BusTicketBooking.Dtos.Schedules;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace BusTicketBooking.Services
{
    public class ScheduleService : IScheduleService
    {
        private readonly IRepository<BusSchedule> _schedules;
        private readonly IRepository<Bus> _buses;
        private readonly IRepository<BusRoute> _routes;
        private readonly AppDbContext _db;

        public ScheduleService(
            IRepository<BusSchedule> schedules,
            IRepository<Bus> buses,
            IRepository<BusRoute> routes,
            AppDbContext db)
        {
            _schedules = schedules;
            _buses = buses;
            _routes = routes;
            _db = db;
        }

        public async Task<ScheduleResponseDto> CreateAsync(CreateScheduleRequestDto dto, CancellationToken ct = default)
        {
            var depUtc = EnsureUtc(dto.DepartureUtc);
            if (depUtc < DateTime.UtcNow.AddMinutes(-1))
                throw new InvalidOperationException("Departure time must be in the future.");

            var bus = await _buses.GetByIdAsync(dto.BusId, ct) ?? throw new InvalidOperationException("Bus not found.");
            var route = await _routes.GetByIdAsync(dto.RouteId, ct) ?? throw new InvalidOperationException("Route not found.");

            var dup = (await _schedules.FindAsync(s => s.BusId == dto.BusId && s.DepartureUtc == depUtc, ct)).Any();
            if (dup) throw new InvalidOperationException("A schedule for this bus at the specified time already exists.");

            var entity = new BusSchedule
            {
                BusId = dto.BusId,
                RouteId = dto.RouteId,
                DepartureUtc = depUtc,
                BasePrice = dto.BasePrice
            };
            entity = await _schedules.AddAsync(entity, ct);
            return Map(entity, bus, route);
        }

        public async Task<IEnumerable<ScheduleResponseDto>> GetAllAsync(CancellationToken ct = default)
        {
            var list = await _db.BusSchedules
                .Include(s => s.Bus)
                .Include(s => s.Route)
                .AsNoTracking()
                .OrderBy(s => s.DepartureUtc)
                .ToListAsync(ct);

            return list.Select(e => Map(e, e.Bus!, e.Route!));
        }

        public async Task<ScheduleResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            var e = await _db.BusSchedules
                .Include(s => s.Bus)
                .Include(s => s.Route)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            return e is null ? null : Map(e, e.Bus!, e.Route!);
        }

        public async Task<ScheduleResponseDto?> UpdateAsync(Guid id, UpdateScheduleRequestDto dto, CancellationToken ct = default)
        {
            var entity = await _schedules.GetByIdAsync(id, ct);
            if (entity is null) return null;

            var depUtc = EnsureUtc(dto.DepartureUtc);
            if (depUtc < DateTime.UtcNow.AddMinutes(-1))
                throw new InvalidOperationException("Departure time must be in the future.");

            var bus = await _buses.GetByIdAsync(dto.BusId, ct) ?? throw new InvalidOperationException("Bus not found.");
            var route = await _routes.GetByIdAsync(dto.RouteId, ct) ?? throw new InvalidOperationException("Route not found.");

            var dup = (await _schedules.FindAsync(s => s.Id != id && s.BusId == dto.BusId && s.DepartureUtc == depUtc, ct)).Any();
            if (dup) throw new InvalidOperationException("A schedule for this bus at the specified time already exists.");

            entity.BusId = dto.BusId;
            entity.RouteId = dto.RouteId;
            entity.DepartureUtc = depUtc;
            entity.BasePrice = dto.BasePrice;
            entity.UpdatedAtUtc = DateTime.UtcNow;

            await _schedules.UpdateAsync(entity, ct);
            return Map(entity, bus, route);
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var e = await _schedules.GetByIdAsync(id, ct);
            if (e is null) return false;
            await _schedules.RemoveAsync(e, ct);
            return true;
        }

        public async Task<PagedResult<ScheduleResponseDto>> SearchAsync(
            Guid fromStopId,
            Guid toStopId,
            DateOnly date,
            PagedRequestDto request,
            CancellationToken ct = default)
        {
            // Base query: same-day schedules (UTC) with route + stops
            var baseQuery = _db.BusSchedules
                .Include(s => s.Bus)
                .Include(s => s.Route)!.ThenInclude(r => r.RouteStops)
                .AsNoTracking()
                .Where(s => DateOnly.FromDateTime(s.DepartureUtc) == date);

            var list = await baseQuery.ToListAsync(ct);

            // Filter by route order (from before to)
            var filtered = list.Where(s =>
            {
                var stops = s.Route!.RouteStops.OrderBy(rs => rs.Order).ToList();
                var fromIdx = stops.FindIndex(rs => rs.StopId == fromStopId);
                var toIdx = stops.FindIndex(rs => rs.StopId == toStopId);
                return fromIdx >= 0 && toIdx >= 0 && fromIdx < toIdx;
            }).Select(s => Map(s, s.Bus!, s.Route!));

            // Sorting
            var sortBy = (request.SortBy ?? "departure").Trim().ToLowerInvariant();
            var desc = request.IsDescending();
            filtered = sortBy switch
            {
                "price" => (desc ? filtered.OrderByDescending(x => x.BasePrice) : filtered.OrderBy(x => x.BasePrice)),
                "buscode" => (desc ? filtered.OrderByDescending(x => x.BusCode) : filtered.OrderBy(x => x.BusCode)),
                "routecode" => (desc ? filtered.OrderByDescending(x => x.RouteCode) : filtered.OrderBy(x => x.RouteCode)),
                _ => (desc ? filtered.OrderByDescending(x => x.DepartureUtc) : filtered.OrderBy(x => x.DepartureUtc)),
            };

            // Paging
            var total = filtered.LongCount();
            var (skip, take) = request.GetSkipTake();
            var pageItems = filtered.Skip(skip).Take(take).ToList();

            return new PagedResult<ScheduleResponseDto>
            {
                Page = request.Page,
                PageSize = request.PageSize,
                TotalCount = total,
                Items = pageItems
            };
        }

        public async Task<SeatAvailabilityResponseDto> GetAvailabilityAsync(Guid scheduleId, CancellationToken ct = default)
        {
            var schedule = await _db.BusSchedules
                .Include(s => s.Bus)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == scheduleId, ct)
                ?? throw new InvalidOperationException("Schedule not found.");

            var totalSeats = schedule.Bus?.TotalSeats ?? 0;
            if (totalSeats <= 0) totalSeats = 40; // fallback

            // all allowed seat labels (v1: "1".."N")
            var allSeats = GenerateNumericSeats(totalSeats);

            // seats taken by bookings that are NOT cancelled
            var bookedSeats = await _db.Bookings
                .Where(b => b.ScheduleId == scheduleId && b.Status != Models.Enums.BookingStatus.Cancelled)
                .SelectMany(b => b.Passengers.Select(p => p.SeatNo))
                .ToListAsync(ct);

            var bookedSet = new HashSet<string>(bookedSeats.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            var available = allSeats.Where(s => !bookedSet.Contains(s)).ToList();

            return new SeatAvailabilityResponseDto
            {
                ScheduleId = scheduleId,
                TotalSeats = totalSeats,
                BookedCount = bookedSet.Count,
                AvailableCount = available.Count,
                BookedSeats = bookedSet.OrderBy(x => ToSeatNumber(x)).ToList(),
                AvailableSeats = available
            };
        }

        private static DateTime EnsureUtc(DateTime dt)
        {
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime()
            };
        }

        private static ScheduleResponseDto Map(BusSchedule e, Bus bus, BusRoute route) => new()
        {
            Id = e.Id,
            BusId = e.BusId,
            RouteId = e.RouteId,
            BusCode = bus.Code,
            RegistrationNumber = bus.RegistrationNumber,
            RouteCode = route.RouteCode,
            DepartureUtc = e.DepartureUtc,
            BasePrice = e.BasePrice,
            CreatedAtUtc = e.CreatedAtUtc,
            UpdatedAtUtc = e.UpdatedAtUtc
        };

        private static List<string> GenerateNumericSeats(int totalSeats)
            => Enumerable.Range(1, totalSeats).Select(i => i.ToString()).ToList();

        private static int ToSeatNumber(string seat)
            => int.TryParse(seat, out var n) ? n : int.MaxValue; // non-numeric at end
    }
}