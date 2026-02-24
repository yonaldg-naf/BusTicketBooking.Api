using System.Data;
using BusTicketBooking.Contexts;
using BusTicketBooking.Dtos.Bookings;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using BusTicketBooking.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BusTicketBooking.Services
{
    public class BookingService : IBookingService
    {
        private readonly IRepository<Booking> _bookings;
        private readonly IRepository<BookingPassenger> _passengers;
        private readonly IRepository<Payment> _payments;
        private readonly IRepository<BusSchedule> _schedules;
        private readonly AppDbContext _db;

        public BookingService(
            IRepository<Booking> bookings,
            IRepository<BookingPassenger> passengers,
            IRepository<Payment> payments,
            IRepository<BusSchedule> schedules,
            AppDbContext db)
        {
            _bookings = bookings;
            _passengers = passengers;
            _payments = payments;
            _schedules = schedules;
            _db = db;
        }

        public async Task<BookingResponseDto> CreateAsync(Guid userId, CreateBookingRequestDto dto, CancellationToken ct = default)
        {
            if (dto.Passengers.Count == 0)
                throw new InvalidOperationException("At least one passenger is required.");

            // Validate duplicate seats in request
            var seatSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in dto.Passengers)
            {
                var seat = p.SeatNo.Trim();
                if (!seatSet.Add(seat))
                    throw new InvalidOperationException($"Duplicate seat in request: {seat}");
            }

            // Load schedule with bus/route for pricing & display
            var schedule = await _db.BusSchedules
                .Include(s => s.Bus)
                .Include(s => s.Route)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == dto.ScheduleId, ct)
                ?? throw new InvalidOperationException("Schedule not found.");

            // Price = base price * passenger count (simple v1 rule)
            var total = schedule.BasePrice * dto.Passengers.Count;

            // SERIALIZABLE transaction to avoid concurrent seat conflicts
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            // Check if any requested seat is already taken for this schedule (excluding cancelled bookings)
            var requestedSeats = dto.Passengers.Select(p => p.SeatNo.Trim()).ToList();

            var takenSeats = await _db.BookingPassengers
                .Where(bp => requestedSeats.Contains(bp.SeatNo))
                .Join(_db.Bookings, bp => bp.BookingId, b => b.Id, (bp, b) => new { bp.SeatNo, b.ScheduleId, b.Status })
                .Where(x => x.ScheduleId == dto.ScheduleId && x.Status != BookingStatus.Cancelled)
                .Select(x => x.SeatNo)
                .ToListAsync(ct);

            if (takenSeats.Any())
                throw new InvalidOperationException($"One or more seats are already taken: {string.Join(", ", takenSeats)}");

            // Create booking (Pending)
            var entity = new Booking
            {
                UserId = userId,
                ScheduleId = dto.ScheduleId,
                Status = BookingStatus.Pending,
                TotalAmount = total
            };
            await _bookings.AddAsync(entity, ct);

            // Create passengers
            var passengerEntities = dto.Passengers.Select(p => new BookingPassenger
            {
                BookingId = entity.Id,
                Name = p.Name.Trim(),
                Age = p.Age,
                SeatNo = p.SeatNo.Trim()
            }).ToList();

            await _passengers.AddRangeAsync(passengerEntities, ct);

            // Create payment record (Initiated)
            var payment = new Payment
            {
                BookingId = entity.Id,
                Amount = total,
                Status = PaymentStatus.Initiated,
                ProviderReference = "INIT"
            };
            await _payments.AddAsync(payment, ct);

            await tx.CommitAsync(ct);

            // Load full booking for response
            return await LoadForResponse(entity.Id, ct) ?? throw new InvalidOperationException("Booking created but failed to load.");
        }

        public async Task<IEnumerable<BookingResponseDto>> GetMyAsync(Guid userId, CancellationToken ct = default)
        {
            var data = await _db.Bookings
                .Include(b => b.Passengers)
                .Include(b => b.Payment)
                .Include(b => b.Schedule)!.ThenInclude(s => s!.Bus)
                .Include(b => b.Schedule)!.ThenInclude(s => s!.Route)
                .AsNoTracking()
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreatedAtUtc)
                .ToListAsync(ct);

            return data.Select(Map);
        }

        public async Task<BookingResponseDto?> GetByIdForUserAsync(Guid userId, Guid bookingId, bool allowPrivileged = false, CancellationToken ct = default)
        {
            var e = await _db.Bookings
                .Include(b => b.Passengers)
                .Include(b => b.Payment)
                .Include(b => b.Schedule)!.ThenInclude(s => s!.Bus)
                .Include(b => b.Schedule)!.ThenInclude(s => s!.Route)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

            if (e is null) return null;
            if (!allowPrivileged && e.UserId != userId) return null;

            return Map(e);
        }

        public async Task<bool> CancelAsync(Guid userId, Guid bookingId, bool allowPrivileged = false, CancellationToken ct = default)
        {
            var booking = await _bookings.GetByIdAsync(bookingId, ct);
            if (booking is null) return false;

            if (!allowPrivileged && booking.UserId != userId)
                throw new UnauthorizedAccessException("You cannot cancel a booking you don't own.");

            if (booking.Status == BookingStatus.Cancelled)
                return true; // already cancelled

            booking.Status = BookingStatus.Cancelled;
            booking.UpdatedAtUtc = DateTime.UtcNow;
            await _bookings.UpdateAsync(booking, ct);

            // (Optional) handle refund logic here

            return true;
        }

        public async Task<BookingResponseDto?> PayAsync(Guid userId, Guid bookingId, decimal amount, string providerRef, bool allowPrivileged = false, CancellationToken ct = default)
        {
            // Simple mock payment: set Payment.Success and Booking.Confirmed
            var booking = await _db.Bookings
                .Include(b => b.Payment)
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

            if (booking is null) return null;
            if (!allowPrivileged && booking.UserId != userId) return null;
            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Cannot pay a cancelled booking.");

            // In real world, verify amount matches, call gateway, etc.
            booking.Payment ??= new Payment { BookingId = booking.Id, Amount = booking.TotalAmount };
            booking.Payment.Amount = amount <= 0 ? booking.TotalAmount : amount;
            booking.Payment.Status = PaymentStatus.Success;
            booking.Payment.ProviderReference = string.IsNullOrWhiteSpace(providerRef) ? "MOCK-PAYMENT" : providerRef.Trim();

            booking.Status = BookingStatus.Confirmed;
            booking.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return await LoadForResponse(bookingId, ct);
        }

        private async Task<BookingResponseDto?> LoadForResponse(Guid bookingId, CancellationToken ct)
        {
            var e = await _db.Bookings
                .Include(b => b.Passengers)
                .Include(b => b.Payment)
                .Include(b => b.Schedule)!.ThenInclude(s => s!.Bus)
                .Include(b => b.Schedule)!.ThenInclude(s => s!.Route)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

            return e is null ? null : Map(e);
        }

        private static BookingResponseDto Map(Booking e)
        {
            return new BookingResponseDto
            {
                Id = e.Id,
                UserId = e.UserId,
                ScheduleId = e.ScheduleId,
                Status = e.Status,
                TotalAmount = e.TotalAmount,
                CreatedAtUtc = e.CreatedAtUtc,
                UpdatedAtUtc = e.UpdatedAtUtc,
                BusCode = e.Schedule?.Bus?.Code ?? string.Empty,
                RegistrationNumber = e.Schedule?.Bus?.RegistrationNumber ?? string.Empty,
                RouteCode = e.Schedule?.Route?.RouteCode ?? string.Empty,
                DepartureUtc = e.Schedule?.DepartureUtc ?? default,
                Passengers = e.Passengers.Select(p => new BookingPassengerDto
                {
                    Name = p.Name,
                    Age = p.Age,
                    SeatNo = p.SeatNo
                }).ToList()
            };
        }
    }
}